using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Player.Scripts.Other;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.Sound;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using NipplePiercings.Logic;
using NippleTether.Logic;
using RestraintHub;
using SfmKit.Game;
using SfmKit.Pure;
using UnityEngine;
using UnityEngine.InputSystem;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using Collider = NipplePiercings.Logic.Collider;

namespace NippleTether
{
    /// <summary>
    /// The tether: a chain from each nipple (a ring through it, on the game's piercing point <c>ChikubiPiasController</c> and the
    /// nipple's tip found on her body bake, as NipplePiercings) to one point in the air that has no gravity and goes through
    /// anything. The chains hang under gravity and lie on her body and the level (<see cref="TetherChain"/>,
    /// <see cref="EnvironmentProbe"/>). When a chain is held longer than it is, the breast is turned towards it (its root bone
    /// <c>BreastRotateL/R</c>, in LateUpdate, put back next frame), then she is moved along (<c>CharacterController.Move</c>)
    /// (<see cref="BreastPull"/>). Selected in the restraint list: tap F to move the point yourself (WASD, up / down keys; she
    /// stands still) or give her back control; hold F to struggle free; + / - let the chains out and take them in.
    /// Bridge diagnostics: <c>game get NippleTether.TetherController::Status</c>.
    /// </summary>
    public sealed class TetherController : MonoBehaviour
    {
        public const int RingItemValue = 9500;
        public const float UnitMetres = 0.065f / 2.67f;          // NipplePiercings: metres per model unit at Size 1, height 1
        public const float ReferenceNipple = 2.2385f / 1.3125f;  // NipplePiercings: piercing point's scale per unit of height
        public const float BakeInterval = 2f;
        internal static PiercingSet Set;
        internal static TetherController Instance;
        public static string Status = "off";
        /// <summary>The player moves the point (true) or her (false). Bridge: <c>game call NippleTether.TetherController TogglePoint</c>.</summary>
        public static bool PointMode;

        internal readonly LockMachine Machine = new LockMachine();

        private sealed class Side
        {
            public string Name;
            public Transform Anchor;          // PiasPosition
            public Transform Breast;          // Breast_L / R (its parent BreastRotateL / R is turned)
            public int ApexVertex = -1;
            public Vector3 ApexLocal;
            public bool HasApex;
            public TetherChain Chain;
            public readonly BreastPull Pull = new BreastPull();
            public NVector3 Hook;
            // The breast turn written in LateUpdate, put back in the next Update.
            public bool Written;
            public Quaternion Saved, WrittenRot;
            public Vector3 SavedScale, WrittenScale;
            public float Angle, Stretch = 1f; // diagnostics
            public float NippleSize = -1f;    // the piercing point's scale while the breast is not stretched (it scales with it)
        }

        private readonly Side[] _sides = { new Side { Name = "L" }, new Side { Name = "R" } };
        private TetherRig _rig;
        private EnvironmentProbe _probe;
        private Animator _anim;
        private Transform _avatar;
        private SkinnedMeshRenderer _body;
        private Mesh _bake;
        private CharacterController _cc;
        private Camera.CameraCallback _preCull;
        private int _placedFrame = -1;
        private float _nextSearch, _nextBake, _length, _target;
        private NVector3 _point;
        private bool _needPoint = true;
        private Vector3 _lastPlayer;
        private Vector3 _heldAt;                // where she stood after the last LateUpdate
        private bool _hasHeldAt;
        private NVector3 _held;                 // diagnostics: last correction
        private float _dragged;                 // diagnostics: metres of her steps undone since put on
        private float _heldFor;                 // s the + / - key has been held
        private readonly List<Collider> _colliders = new List<Collider>();
        private float _pitch, _linkScale, _thickness;
        private static int _failures;

        public TetherController(IntPtr pointer) : base(pointer) { }

        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake() => Instance = this;

        private void Start()
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(OnPreCull));
            Camera.onPreCull = Camera.onPreCull == null
                ? _preCull
                : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
            _length = _target = Plugin.LengthCm.Value / 100f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_preCull != null && Camera.onPreCull != null)
                Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCull)?.Cast<Camera.CameraCallback>();
            _rig?.Destroy();
            _probe?.Destroy();
            if (_bake != null) UnityEngine.Object.Destroy(_bake);
        }

        // --- every frame ---------------------------------------------------------------------------------------------------

        private void Update()
        {
            try { RestoreBreasts(); }
            catch (Exception e) { Fail("restoring the breasts", e); }
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            var player = PlayerController.Instance;
            if (player == null || GameState.GameStateData == null) return;
            if ((_sides[0].Anchor == null || _sides[1].Anchor == null || _anim == null) && Time.time >= _nextSearch)
            {
                _nextSearch = Time.time + 1f;
                FindBody();
            }
            float dt = Time.deltaTime;

            // F: a tap hands the point over (Restraint.OnTap); holding it longer is the struggle (StruggleHeld, from the hub).
            bool locked = Machine.State == LockState.Locked;
            Machine.AttemptSeconds = Plugin.AttemptSeconds.Value;
            Machine.BaseChance = Plugin.SuccessChance.Value;
            switch (Machine.Tick(dt, Plugin.Entry.StruggleHeld && locked, UnityEngine.Random.value))
            {
                case LockMachine.Event.Locked:
                    GameUi.PlaySe(InGameSoundManager.SeType.AttachHandcuffs);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: locked, chains " + Cm(_length) + " cm");
                    break;
                case LockMachine.Event.AttemptFailed:
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: failed");
                    break;
                case LockMachine.Event.Unlocking:
                    GameUi.PlaySe(InGameSoundManager.SeType.Unlock);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: success");
                    break;
                case LockMachine.Event.Unlocked:
                    TakenOff();
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] release: off");
                    break;
            }
            if (!Machine.Worn) { PointMode = false; return; }

            Length(dt);

            // Teleports and map changes: the point comes along, put in front of her again.
            Vector3 at = player.transform.position;
            if ((at - _lastPlayer).sqrMagnitude > 4f) _needPoint = true;
            _lastPlayer = at;

            if (PointMode) MovePoint(dt);
        }

        /// <summary>+ / -: a press lets out / takes in one step; held, the chains run on smoothly at <c>LengthSpeed</c>
        /// (RestraintHub's <c>HeldStep</c>; the hub's repeated steps came every 0.08 s). Stored in the config on release.</summary>
        [HideFromIl2Cpp]
        private void Length(float dt)
        {
            int step = Plugin.Entry.TakeStep(); // the first press (its repeats are replaced by the smooth run)
            int held = Plugin.Entry.HeldStep;
            float min = Plugin.MinLengthCm.Value / 100f, max = Math.Max(min, Plugin.MaxLengthCm.Value / 100f);
            float before = _target;
            if (held == 0)
            {
                if (_heldFor > 0f)
                {
                    Plugin.LengthCm.Value = (float)Math.Round(_target * 100f, 1); // the player's action: stored
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] length: target " + Cm(_target) + " cm");
                }
                _heldFor = 0f;
            }
            else
            {
                if (_heldFor == 0f && step != 0) _target += step * Plugin.StepCm.Value / 100f;
                _heldFor = Math.Max(_heldFor + dt, 1e-4f);
                if (_heldFor > 0.4f) _target += held * Plugin.LengthSpeed.Value * dt;
            }
            _target = Math.Clamp(_target, min, max);
            // Taken in no further than she can follow without being pulled: the chains stop where the breasts are drawn out
            // all the way (she is not dragged; a shorter chain would only stretch).
            if (_target < before)
            {
                float need = 0f;
                foreach (var s in _sides)
                    if (s.Chain != null) need = Math.Max(need, NVector3.Distance(s.Hook, _point) - MaxStretch);
                _target = Math.Max(_target, Math.Min(before, need));
            }
            if (step != 0 && Math.Abs(_target - before) < 1e-6f)
                Hub.Message(step > 0 ? "The chains are let out all the way." : "The chains are as short as they go.");
            _length = MoveTowards(_length, _target, Plugin.LengthSpeed.Value * dt);
        }

        [HideFromIl2Cpp]
        private void MovePoint(float dt)
        {
            if (Hub.MenuOpen) return;
            var kb = Keyboard.current;
            if (kb == null || _needPoint) return;
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            float y = (Held(kb, Plugin.UpKey.Value) ? 1f : 0f) - (Held(kb, Plugin.DownKey.Value) ? 1f : 0f);
            if (x == 0f && y == 0f && z == 0f) return;
            var cam = Camera.main;
            NVector3 fwd = cam != null ? N(cam.transform.forward) : NVector3.UnitZ;
            float speed = Plugin.PointSpeed.Value * (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? 2.5f : 1f);
            NVector3 to = _point + PointControl.Move(new NVector3(x, y, z), fwd, speed, dt);
            NVector3 centre = NippleMiddle();
            // Never further than the chains reach: the point does not drag her (it used to, 0.25 m beyond).
            float reach = _length + MaxStretch;
            _point = PointControl.Clamp(_point, to, centre, reach, reach);
        }

        private static bool Held(Keyboard kb, string name)
        {
            if (string.IsNullOrEmpty(name) || !Enum.TryParse(name, true, out Key key) || key == Key.None) return false;
            try { return kb[key].isPressed; }
            catch (Exception) { return false; }
        }

        /// <summary>The breasts drawn towards their chains and her held back, written after the animator (LateUpdate) so it
        /// shows on the skinned body; from the pulls the chains measured in the last frame's render.</summary>
        private void LateUpdate()
        {
            try
            {
                if (!Machine.Worn) { _hasHeldAt = false; return; }
                int debug = Plugin.DebugDisable.Value;
                if ((debug & 1) == 0)
                    foreach (var s in _sides) TurnBreast(s);
                HoldBack((debug & 2) == 0);
            }
            catch (Exception e) { Fail("pulling her", e); }
        }

        /// <summary>Undo the part of her step this frame that takes a nipple out of its chain's reach
        /// (<see cref="PointControl.Hold"/>); she is never moved otherwise.</summary>
        [HideFromIl2Cpp]
        private void HoldBack(bool on)
        {
            var player = PlayerController.Instance;
            if (player == null || _cc == null) { _hasHeldAt = false; return; }
            Vector3 pos = player.transform.position;
            NVector3 moved = _hasHeldAt ? N(pos - _heldAt) : NVector3.Zero;
            _held = NVector3.Zero;
            if (on && _cc.enabled && !_needPoint && moved.Length() < 1f) // not across a teleport
            {
                float reach = _length + MaxStretch;
                foreach (var s in _sides)
                {
                    if (s.Chain == null) continue;
                    NVector3 c = PointControl.Hold(s.Hook, _point, reach, moved);
                    if (c.LengthSquared() > _held.LengthSquared()) _held = c;
                }
                if (_held.LengthSquared() > 1e-10f)
                {
                    _cc.Move(U(_held));
                    _dragged += _held.Length();
                }
            }
            _heldAt = player.transform.position;
            _hasHeldAt = true;
        }

        [HideFromIl2Cpp]
        private void TurnBreast(Side s)
        {
            if (s.Anchor == null || s.Breast == null || s.Breast.parent == null) return;
            if (s.Pull.Offset.LengthSquared() < 1e-8f) { s.Angle = 0f; s.Stretch = 1f; return; }
            Transform root = s.Breast.parent; // BreastRotateL / R
            Vector3 pivot = root.position;
            Vector3 r = s.Anchor.position - pivot, t = r + U(s.Pull.Offset);
            s.Saved = root.localRotation;
            s.SavedScale = root.localScale;
            // Drawn out: the breast is stretched along the root's local axis that points at the nipple (z, cos 0.94 on the first
            // save [observed 2026-10-26]) by a share of the pull, then turned the rest of the way. Turning alone already put the
            // nipple where the chain holds it, so the breast itself showed little of the pull [user, 2026-10-26].
            float len = r.magnitude;
            s.Stretch = 1f + Math.Min(Plugin.MaxElongation.Value, Plugin.Elongation.Value * s.Pull.Offset.Length() / Math.Max(len, 1e-4f));
            if (s.Stretch > 1.0001f)
            {
                Vector3 local = root.InverseTransformDirection(r);
                int axis = Math.Abs(local.x) > Math.Abs(local.y) ? (Math.Abs(local.x) > Math.Abs(local.z) ? 0 : 2) : (Math.Abs(local.y) > Math.Abs(local.z) ? 1 : 2);
                Vector3 scale = s.SavedScale;
                scale[axis] *= s.Stretch;
                root.localScale = scale;
                r = s.Anchor.position - pivot; // the stretched nipple
            }
            Quaternion q = Quaternion.RotateTowards(Quaternion.identity, Quaternion.FromToRotation(r, t), Plugin.MaxAngle.Value);
            root.rotation = q * root.rotation;
            s.WrittenRot = root.localRotation;
            s.WrittenScale = root.localScale;
            s.Written = true;
            s.Angle = Quaternion.Angle(Quaternion.identity, q);
        }

        /// <summary>Put the breasts' root bones back as the game left them (unless the game wrote them itself since).</summary>
        [HideFromIl2Cpp]
        private void RestoreBreasts()
        {
            foreach (var s in _sides)
            {
                if (!s.Written) continue;
                s.Written = false;
                Transform root = s.Breast == null ? null : s.Breast.parent;
                if (root == null) continue;
                if (Quaternion.Angle(root.localRotation, s.WrittenRot) < 0.01f) root.localRotation = s.Saved;
                if ((root.localScale - s.WrittenScale).sqrMagnitude < 1e-10f) root.localScale = s.SavedScale;
            }
        }

        // --- before rendering: the chains --------------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        private void OnPreCull(Camera cam)
        {
            if (_placedFrame == Time.frameCount) return;
            _placedFrame = Time.frameCount;
            try
            {
                bool show = Machine.Worn && _anim != null && _sides[0].Anchor != null && _sides[1].Anchor != null
                            && _sides[0].Breast != null && _sides[0].Breast.gameObject.activeInHierarchy; // PiasPosition itself is inactive for a while after a load
                if (!show)
                {
                    _rig?.SetVisible(false);
                    if (!Machine.Worn) Status = "off";
                    return;
                }
                if (Time.time >= _nextBake)
                {
                    _nextBake = Time.time + BakeInterval;
                    BakeApexes();
                }
                Place();
            }
            catch (Exception e) { Fail("placing the chains", e); }
        }

        private float Height => _avatar != null ? _avatar.lossyScale.y : 1f;
        private float MaxStretch => Plugin.MaxStretchCm.Value / 100f * Height;

        [HideFromIl2Cpp]
        private void Place()
        {
            int debug = Plugin.DebugDisable.Value;
            _rig ??= new TetherRig(Set, _sides[0].Anchor.gameObject.layer);
            _probe ??= new EnvironmentProbe();
            var player = PlayerController.Instance;
            _probe.Player = player == null ? null : player.transform;
            _probe.Queries = _probe.Pushes = 0;
            if (_pitch <= 0f) LinkSize();
            if (_needPoint) PlacePoint();
            BodyColliders((debug & 4) == 0 && Plugin.BodyCollision.Value);
            bool[] worn = PiercingsLink.Worn();
            var sb = new StringBuilder();
            for (int i = 0; i < 2; i++)
            {
                var s = _sides[i];
                bool ring = (debug & 16) == 0 && !worn[i];
                PlaceRing(i, s, ring);
                s.Chain ??= new TetherChain(_pitch, _thickness, _length);
                var c = s.Chain;
                c.SetLength(_length);
                c.Damping = Plugin.Damping.Value;
                c.Gravity = new NVector3(0f, -9.81f * Plugin.Gravity.Value, 0f);
                c.Friction = Plugin.Friction.Value;
                c.Colliders.Clear();
                c.Colliders.AddRange(_colliders);
                c.Environment = (debug & 8) == 0 && Plugin.LevelCollision.Value ? _probe : null;
                c.Update(Time.deltaTime, s.Hook, _point);
                _rig.PlaceLinks(i, c, _linkScale);
                s.Pull.MaxStretch = MaxStretch;
                s.Pull.Tolerance = 0.004f + 0.005f * _length;
                s.Pull.Update(Time.deltaTime, c.Stretch, c.PullDirection);
                sb.Append(s.Name).Append(": ").Append(c.Segments).Append(" links, stretch ").Append(Cm(c.Stretch))
                  .Append(" cm, breast ").Append(Cm(s.Pull.Offset.Length())).Append(" cm / ").Append(s.Angle.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(" deg x").Append(s.Stretch.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(", level ").Append(c.EnvironmentContacts).Append(ring ? "" : ", no ring").Append("; ");
            }
            _rig.PlacePoint(_point, PointRotation(), _linkScale * 1.6f);
            _rig.SetVisible(true);
            NVector3 mid = NippleMiddle();
            sb.Append("point ").Append(PointMode ? "held by the player" : "fixed").Append(" ").Append(Cm(NVector3.Distance(mid, _point))).Append(" cm away, ")
              .Append(Cm(_point.Y - mid.Y)).Append(" cm up; length ").Append(Cm(_length)).Append(" (target ").Append(Cm(_target)).Append(") cm; held back ")
              .Append(Cm(_held.Length())).Append(" cm last frame, ").Append(Cm(_dragged)).Append(" cm in all; probes ").Append(_probe.Queries)
              .Append(", pushed ").Append(_probe.Pushes);
            Status = sb.ToString();
        }

        /// <summary>The ring through the nipple (NipplePiercings' "ring", sized to the nipple, through it a little behind the tip),
        /// swung about the pierced point towards its chain; the chain hangs from its bottom. Without the ring (a NipplePiercings
        /// piercing is worn there, or Debug 16) the chain starts at the pierced point.</summary>
        [HideFromIl2Cpp]
        private void PlaceRing(int i, Side s, bool visible)
        {
            Transform a = s.Anchor;
            if (!s.Written || s.NippleSize < 0f) s.NippleSize = a.lossyScale.z; // the stretch scales the point too
            float nipple = s.NippleSize / ReferenceNipple;
            float scale = UnitMetres * nipple * Plugin.NippleFit.Value;
            NVector3 forward = N(a.forward);
            NVector3 tip = N(s.HasApex ? a.TransformPoint(s.ApexLocal) : a.position);
            NVector3 pivot = tip - forward * (Plugin.DepthCm.Value / 100f * nipple);
            var ar = a.rotation;
            var rot = new NQuaternion(ar.x, ar.y, ar.z, ar.w);
            var bone = _rig.RingBone;
            NVector3 restDir = PendantBone.ToNipple(bone.Tail) - PendantBone.ToNipple(bone.Head);
            NVector3 hookLocal = restDir; // the ring's bottom
            if (restDir.LengthSquared() > 1e-12f && s.Chain != null && s.Chain.Count > 2)
            {
                NVector3 from = NVector3.Normalize(NVector3.Transform(restDir, rot));
                NVector3 to = s.Chain.Joint(Math.Min(3, s.Chain.Count - 1)) - pivot;
                if (to.LengthSquared() > 1e-10f) rot = NQuaternion.Normalize(FromTo(from, NVector3.Normalize(to)) * rot);
            }
            s.Hook = visible ? pivot + NVector3.Transform(hookLocal * scale, rot) : pivot;
            _rig.PlaceRing(i, visible, pivot, rot, scale);
        }

        /// <summary>The hoop at the point hangs upright, facing her.</summary>
        [HideFromIl2Cpp]
        private NQuaternion PointRotation()
        {
            NVector3 toHer = NippleMiddle() - _point;
            toHer.Y = 0f;
            if (toHer.LengthSquared() < 1e-8f) return NQuaternion.Identity;
            // The model's front (nipple frame +z) towards her, up stays up.
            return SaggingChain.LookRotation(NVector3.Normalize(toHer), NVector3.UnitY);
        }

        [HideFromIl2Cpp]
        private NVector3 NippleMiddle()
        {
            if (_sides[0].Anchor == null || _sides[1].Anchor == null) return _point;
            return (N(_sides[0].Anchor.position) + N(_sides[1].Anchor.position)) / 2f;
        }

        [HideFromIl2Cpp]
        private void PlacePoint()
        {
            var player = PlayerController.Instance;
            if (player == null) return;
            NVector3 fwd = N(player.transform.forward);
            fwd.Y = 0f;
            fwd = fwd.LengthSquared() < 1e-8f ? NVector3.UnitZ : NVector3.Normalize(fwd);
            _point = NippleMiddle() + fwd * Plugin.StartDistance.Value;
            _needPoint = false;
            foreach (var s in _sides) { s.Chain?.Reset(); s.Pull.Reset(); }
            Plugin.Logger.LogInfo("[SFM-ACCEPT] point: placed " + Cm(Plugin.StartDistance.Value) + " cm in front of her at " + _point);
        }

        /// <summary>The link's size: NipplePiercings' link (its pitch = inner length) scaled to <c>Chain.LinkCm</c>.</summary>
        [HideFromIl2Cpp]
        private void LinkSize()
        {
            var bone = Set.Get("link").Bones[0];
            float lx = float.MaxValue, hx = float.MinValue, ly = float.MaxValue, hy = float.MinValue;
            foreach (var p in bone.Positions) { lx = Math.Min(lx, p.X); hx = Math.Max(hx, p.X); ly = Math.Min(ly, p.Y); hy = Math.Max(hy, p.Y); }
            float wire = hy - ly, inner = hx - lx - 2f * wire;
            _pitch = Plugin.LinkCm.Value / 100f;
            _linkScale = _pitch / Math.Max(inner, 1e-4f);
            _thickness = Math.Max(0.003f, wire * 0.5f * _linkScale);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] chain: links of " + Cm(_pitch) + " cm, " + Cm(2f * _thickness) + " cm thick (model scale " + _linkScale.ToString("0.0000", CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>
        /// Her body for the chains: torso (hips - chest - neck), head, arms, legs as capsules and spheres sized from her bone
        /// lengths, and a sphere behind each nipple for the breasts (as NipplePiercings). Shares of bone lengths [estimated
        /// 2026-10-25 from the first save: hips - neck 0.41 m, torso ~0.22 m deep; arms as NipplePiercings].
        /// </summary>
        [HideFromIl2Cpp]
        private void BodyColliders(bool on)
        {
            _colliders.Clear();
            if (!on || _anim == null) return;
            Transform hips = Bone(HumanBodyBones.Hips), chest = Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine),
                      neck = Bone(HumanBodyBones.Neck), head = Bone(HumanBodyBones.Head);
            if (hips != null && chest != null && neck != null)
            {
                float spine = Vector3.Distance(hips.position, neck.position);
                _colliders.Add(Collider.Capsule(N(hips.position), N(chest.position), 0.27f * spine));
                _colliders.Add(Collider.Capsule(N(chest.position), N(neck.position), 0.22f * spine));
                if (head != null)
                {
                    Vector3 up = (head.position - neck.position).normalized;
                    _colliders.Add(Collider.Sphere(N(head.position + up * 0.2f * spine), 0.24f * spine));
                }
            }
            Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.10f);
            Limb(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.08f);
            Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.10f);
            Limb(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.08f);
            Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.22f);
            Limb(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.12f);
            Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.22f);
            Limb(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.12f);
            foreach (var s in _sides)
            {
                if (s.Anchor == null || s.Breast == null) continue;
                float radius = 0.5f * Vector3.Distance(s.Anchor.position, s.Breast.position);
                _colliders.Add(Collider.Sphere(N(s.Anchor.position - s.Anchor.forward * radius), radius));
            }
        }

        // Plain methods, not local functions (the class injector cannot convert a local function's closure parameter).
        [HideFromIl2Cpp]
        private void Limb(HumanBodyBones a, HumanBodyBones b, float share)
        {
            Transform ta = Bone(a), tb = Bone(b);
            if (ta == null || tb == null) return;
            _colliders.Add(Collider.Capsule(N(ta.position), N(tb.position), share * Vector3.Distance(ta.position, tb.position)));
        }

        [HideFromIl2Cpp]
        private Transform Bone(HumanBodyBones b) => _anim == null ? null : _anim.GetBoneTransform(b);

        // --- her body ------------------------------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        private void FindBody()
        {
            var player = PlayerController.Instance;
            var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
            if (anim == null) return;
            _anim = anim;
            _avatar = anim.transform;
            _cc = player.GetComponent<CharacterController>();
            _body = null;
            foreach (var r in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name == "Selestia_body") { _body = r; break; }
            _nextBake = 0f;
            foreach (var c in anim.GetComponentsInChildren<ChikubiPiasController>(true))
            {
                Transform t = c.transform, parent = t.parent;
                if (parent == null) continue;
                int i = parent.name.EndsWith("_L", StringComparison.Ordinal) ? 0 : parent.name.EndsWith("_R", StringComparison.Ordinal) ? 1 : -1;
                if (i < 0 || _sides[i].Anchor == t) continue;
                _sides[i].Anchor = t;
                _sides[i].Breast = parent;
                _sides[i].ApexVertex = -1;
                _sides[i].HasApex = false;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] anchor: " + _sides[i].Name + " nipple under " + parent.name + " / " + (parent.parent == null ? "-" : parent.parent.name));
            }
        }

        /// <summary>The nipples' tips from her drawn body at every bake (as NipplePiercings: <see cref="NippleApex"/>, near the
        /// piercing point and its axis, furthest along its +z), kept in the point's frame between bakes.</summary>
        [HideFromIl2Cpp]
        private void BakeApexes()
        {
            if (_body == null) return;
            if (_bake == null) _bake = new Mesh { name = "NippleTether.Bake", hideFlags = HideFlags.DontUnloadUnusedAsset };
            _body.BakeMesh(_bake, true);
            var verts = _bake.vertices;
            Transform t = _body.transform;
            Vector3 pos = t.position, ls = t.lossyScale;
            Quaternion rot = t.rotation;
            var world = new NVector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) world[i] = N(pos + rot * Vector3.Scale(ls, verts[i]));
            foreach (var s in _sides)
            {
                if (s.Anchor == null) continue;
                // While the tether stretches the breast its scale grows with it: search with the unstretched nipple size.
                float scale = s.NippleSize > 0f ? s.NippleSize : s.Anchor.lossyScale.z;
                s.ApexVertex = NippleApex.Find(world, N(s.Anchor.position), N(s.Anchor.forward), NippleApex.Radius(scale));
                if (s.ApexVertex < 0) { s.HasApex = false; continue; }
                s.ApexLocal = s.Anchor.InverseTransformPoint(U(world[s.ApexVertex]));
                s.HasApex = true;
            }
        }

        // --- on and off --------------------------------------------------------------------------------------------------------

        /// <summary>The hub calls this (Restraint.OnChosen) when the player chooses the entry while it is off.</summary>
        [HideFromIl2Cpp]
        internal void PutOn()
        {
            if (_sides[0].Anchor == null || _sides[1].Anchor == null) FindBody();
            if (_sides[0].Anchor == null || _sides[1].Anchor == null)
            {
                Hub.Message("Can't use the " + Plugin.RingLabel.Value + " now.");
                return;
            }
            if (!Machine.RequestLock()) return;
            _length = _target = Math.Clamp(Plugin.LengthCm.Value / 100f, Plugin.MinLengthCm.Value / 100f, Math.Max(Plugin.MinLengthCm.Value, Plugin.MaxLengthCm.Value) / 100f);
            NewChains();
            GameUi.PlaySe(InGameSoundManager.SeType.CommonEquip);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] ring: put on, closing");
        }

        [HideFromIl2Cpp]
        private void NewChains()
        {
            _needPoint = true;
            _pitch = 0f;            // link size re-read (config)
            _dragged = 0f;
            PointMode = false;
            foreach (var s in _sides) { s.Chain = null; s.Pull.Reset(); }
        }

        [HideFromIl2Cpp]
        private void TakenOff()
        {
            PointMode = false;
            foreach (var s in _sides) { s.Chain = null; s.Pull.Reset(); }
            _rig?.SetVisible(false);
        }

        [HideFromIl2Cpp]
        private void TogglePointMode()
        {
            PointMode = !PointMode;
            Hub.Message(PointMode
                ? "You hold the chains' end: WASD moves it, " + Plugin.UpKey.Value + " / " + Plugin.DownKey.Value + " up and down. Tap F to give her back control."
                : "The chains' end stays where it is. Tap F to move it again.");
            Plugin.Logger.LogInfo("[SFM-ACCEPT] control: " + (PointMode ? "point" : "player"));
        }

        /// <summary>RestraintHub's Restraint.OnTap: F tapped while the tether is selected.</summary>
        [HideFromIl2Cpp]
        internal void OnTap()
        {
            if (Machine.State == LockState.Locked) TogglePointMode();
        }

        /// <summary>Diagnostics: the F tap.</summary>
        public static string TogglePoint()
        {
            if (Instance == null || Instance.Machine.State != LockState.Locked) return "not locked";
            Instance.TogglePointMode();
            return PointMode ? "point" : "player";
        }

        /// <summary>Diagnostics: put it on as the ring entry would (skips the hub's conflict check).</summary>
        public static string PutOnNow()
        {
            if (Instance == null) return "no controller";
            Instance.PutOn();
            return Instance.Machine.State.ToString();
        }

        /// <summary>Diagnostics: move the point by x,y,z metres (world).</summary>
        public static string MovePointBy(Vector3 by)
        {
            if (Instance == null) return "no controller";
            Instance._point += N(by);
            return Instance._point.ToString();
        }

        /// <summary>While the player holds the point she stands still: her moves are cleared (RestraintHub's OnInput, after
        /// the hub's own blocking). The up / down keys' game actions too (Space: jump / coat, C: crouch).</summary>
        [HideFromIl2Cpp]
        internal void OnInput(InputData i)
        {
            if (!PointMode || i == null) return;
            i.Move = Vector3.zero;
            i.JustMove = false;
            i.Jump = false;
            i.Dash = false;
            i.JustCrouch = false;
            i.MoveForwardOneHand = false;
            var kb = Keyboard.current;
            if (kb != null && (Held(kb, Plugin.UpKey.Value) || Held(kb, Plugin.DownKey.Value)))
            {
                // Space is also "Wear Coat": it closed her coat while raising the point [observed 2026-10-25].
                i.JustCloseClothesA = false;
                i.JustCloseClothesB = false;
                i.JustOpenClothesA = false;
                i.JustOpenClothesB = false;
            }
        }

        // --- save data (RestraintHub's OnSave / OnLoad / OnNewGame) ----------------------------------------------------------

        internal const string SaveKeyLocked = "NippleTether.Locked";
        internal const string SaveKeyChance = "NippleTether.Chance";
        internal const string SaveKeyShortcuts = "NippleTether.Shortcuts"; // written by the hub

        [HideFromIl2Cpp]
        internal void OnSave(SaveSlot slot)
        {
            bool locked = Machine.Worn && Machine.State != LockState.Opening;
            slot.Save(SaveKeyLocked, locked);
            slot.Save(SaveKeyChance, (double)Machine.Chance);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] save: locked=" + locked + " path=" + slot.Path);
        }

        [HideFromIl2Cpp]
        internal void OnLoad(SaveSlot slot)
        {
            bool locked = slot.Load(SaveKeyLocked, false); // default when the save has no key (older save)
            double chance = slot.Load(SaveKeyChance, -1.0);
            Machine.Restore(locked, (float)Math.Max(chance, 0.0));
            _length = _target = Plugin.LengthCm.Value / 100f;
            NewChains();
            Plugin.Logger.LogInfo("[SFM-ACCEPT] load: locked=" + locked + " path=" + slot.Path);
        }

        /// <summary>A new game or the title screen (the hub noticed the game data replaced without a load): off.</summary>
        [HideFromIl2Cpp]
        internal void OnNewGame()
        {
            if (Machine.Worn) Plugin.Logger.LogInfo("game data replaced without a load: off");
            Machine.Reset();
            TakenOff();
        }

        // --- helpers ---------------------------------------------------------------------------------------------------------

        private static float MoveTowards(float from, float to, float max) =>
            Math.Abs(to - from) <= max ? to : from + Math.Sign(to - from) * max;

        private static NQuaternion FromTo(NVector3 from, NVector3 to)
        {
            float d = NVector3.Dot(from, to);
            if (d > 0.999999f) return NQuaternion.Identity;
            NVector3 axis = NVector3.Cross(from, to);
            if (axis.LengthSquared() < 1e-12f)
            {
                axis = NVector3.Cross(from, NVector3.UnitX);
                if (axis.LengthSquared() < 1e-12f) axis = NVector3.Cross(from, NVector3.UnitY);
            }
            return NQuaternion.CreateFromAxisAngle(NVector3.Normalize(axis), (float)Math.Acos(Math.Clamp(d, -1f, 1f)));
        }

        internal static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
        internal static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);
        private static string Cm(float m) => (m * 100f).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
