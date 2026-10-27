using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using NipplePiercings.Logic;
using RestraintHub;

namespace NippleTether
{
    /// <summary>
    /// Two chains from her nipples to one point in the air. A restraint on RestraintHub: put on from the ring, selected in the
    /// list a tap on F hands the point to the player (WASD, up / down keys) and back, holding F struggles free, + / - let the
    /// chains out and take them in. Taut chains draw her breasts out and hold her back (<see cref="TetherController"/>).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Hub.Guid, "0.3.0")] // OnTap, HeldStep, OnSave / OnLoad / OnNewGame, MenuOpen
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "com.local.nippletether";
        public const string PluginName = "NippleTether";
        public const string PluginVersion = "0.3.2";
        internal const string ModelResource = "NippleTether.assets.piercings.npd";

        internal static ManualLogSource Logger;
        internal static Restraint Entry;
        // public: tunable live with the bridge (game set)
        public static ConfigEntry<string> RingLabel, UpKey, DownKey;
        public static ConfigEntry<float> LengthCm, StepCm, LengthSpeed, MinLengthCm, MaxLengthCm, LinkCm, Damping, Gravity, Friction;
        public static ConfigEntry<float> PointSpeed, StartDistance, MaxStretchCm, MaxAngle, Elongation, MaxElongation, NippleFit, DepthCm, AttemptSeconds, SuccessChance;
        public static ConfigEntry<bool> BodyCollision, LevelCollision;
        public static ConfigEntry<int> DebugDisable;

        public override void Load()
        {
            Logger = Log;
            RingLabel = Config.Bind("General", "RingLabel", "Nipple Tether", "Name in the ring menu and the restraint list.");
            LengthCm = Config.Bind("Chain", "LengthCm", 100f, new ConfigDescription(
                "Length of each chain (cm; + / - change it while the tether is selected, and store it here).", new AcceptableValueRange<float>(10f, 400f)));
            StepCm = Config.Bind("Chain", "StepCm", 2f, new ConfigDescription(
                "How much a press of + / - lets out or takes in (cm; held, the chains run on at LengthSpeed).", new AcceptableValueRange<float>(0.5f, 20f)));
            LengthSpeed = Config.Bind("Chain", "LengthSpeed", 0.4f, new ConfigDescription(
                "How fast the chains run out and in while + / - is held (m/s; the links run out of and into the point's hoop).", new AcceptableValueRange<float>(0.05f, 3f)));
            MinLengthCm = Config.Bind("Chain", "MinLengthCm", 15f, new ConfigDescription("Shortest length + / - reach (cm).", new AcceptableValueRange<float>(5f, 100f)));
            MaxLengthCm = Config.Bind("Chain", "MaxLengthCm", 300f, new ConfigDescription("Longest length + / - reach (cm).", new AcceptableValueRange<float>(20f, 400f)));
            LinkCm = Config.Bind("Chain", "LinkCm", 1.5f, new ConfigDescription(
                "Size of a link (cm from one link's joint to the next; NipplePiercings' chain link scaled up; applies when put on).",
                new AcceptableValueRange<float>(0.5f, 5f)));
            Damping = Config.Bind("Chain", "Damping", 1f, new ConfigDescription(
                "How quickly a swing dies down (share of the speed lost per second).", new AcceptableValueRange<float>(0f, 20f)));
            Gravity = Config.Bind("Chain", "Gravity", 1f, new ConfigDescription("Gravity on the chains relative to the real one.", new AcceptableValueRange<float>(0f, 3f)));
            Friction = Config.Bind("Chain", "Friction", 0.6f, new ConfigDescription(
                "Share of its sliding speed a chain loses per frame where it lies on or against the level.", new AcceptableValueRange<float>(0f, 1f)));
            BodyCollision = Config.Bind("Chain", "BodyCollision", true, "The chains stay out of her body (torso, head, arms, legs, breasts).");
            LevelCollision = Config.Bind("Chain", "LevelCollision", true, "The chains stay out of the level (floor, walls, furniture).");
            PointSpeed = Config.Bind("Point", "Speed", 0.8f, new ConfigDescription(
                "How fast the point moves while the player holds it (m/s; Shift: 2.5 x).", new AcceptableValueRange<float>(0.1f, 5f)));
            UpKey = Config.Bind("Point", "UpKey", "Space", "Key that raises the point while the player holds it (Input System key name).");
            DownKey = Config.Bind("Point", "DownKey", "C", "Key that lowers the point while the player holds it (Input System key name).");
            StartDistance = Config.Bind("Point", "StartDistance", 0.6f, new ConfigDescription(
                "Where the point is put when the tether is put on: this far in front of her, at the nipples' height (m).", new AcceptableValueRange<float>(0.1f, 3f)));
            MaxStretchCm = Config.Bind("Breasts", "MaxStretchCm", 4f, new ConfigDescription(
                "How far a taut chain draws a nipple out before it pulls her along (cm at height 1, scaled with her height).", new AcceptableValueRange<float>(0f, 15f)));
            MaxAngle = Config.Bind("Breasts", "MaxAngle", 30f, new ConfigDescription(
                "How far a breast turns towards its chain at most (degrees).", new AcceptableValueRange<float>(0f, 80f)));
            Elongation = Config.Bind("Breasts", "Elongation", 1.5f, new ConfigDescription(
                "How much a taut chain stretches the breast towards it (per unit of pull relative to the breast's length; 0 = the breast is only turned).",
                new AcceptableValueRange<float>(0f, 5f)));
            MaxElongation = Config.Bind("Breasts", "MaxElongation", 0.5f, new ConfigDescription(
                "How far a breast is stretched at most (0.5 = to 1.5 times its length).", new AcceptableValueRange<float>(0f, 1.5f)));
            NippleFit = Config.Bind("Nipple", "NippleFit", 1.7f, new ConfigDescription(
                "Size of the ring through each nipple relative to the nipple (as NipplePiercings' rings).", new AcceptableValueRange<float>(0.3f, 4f)));
            DepthCm = Config.Bind("Nipple", "DepthCm", 0.45f, new ConfigDescription(
                "How far behind the nipple's tip the ring goes through (cm for the first save's nipples; scales with the nipple).", new AcceptableValueRange<float>(0f, 2f)));
            AttemptSeconds = Config.Bind("Unlock", "AttemptSeconds", 3f, new ConfigDescription(
                "Seconds of holding F per attempt to struggle free (a short tap of F hands the point over instead).", new AcceptableValueRange<float>(0.5f, 30f)));
            SuccessChance = Config.Bind("Unlock", "SuccessChance", 1f, new ConfigDescription(
                "Chance that an attempt frees her (1 = the first; failures add 0.02, like the game's handcuffs).", new AcceptableValueRange<float>(0.01f, 1f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0,
                "Bit mask for tests (live): 1 = breasts not pulled, 2 = she is not held back, 4 = no body collisions, 8 = no level collisions, 16 = no nipple rings.");

            try
            {
                using Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(ModelResource)
                                 ?? throw new FileNotFoundException("embedded resource " + ModelResource + " missing");
                TetherController.Set = PiercingSet.Read(s);
            }
            catch (Exception e)
            {
                Logger.LogError("chain models could not be read, the mod is disabled: " + e.Message);
                return;
            }
            ClassInjector.RegisterTypeInIl2Cpp<TetherController>();
            AddComponent<TetherController>();
            Entry = new Restraint
            {
                Id = "NippleTether",
                RingValue = TetherController.RingItemValue,
                Label = () => RingLabel.Value,
                ShortcutSaveKey = TetherController.SaveKeyShortcuts,
                Holds = BodyParts.Spot,   // it holds her near a place and moves her
                IsWorn = () => TetherController.Instance != null && TetherController.Instance.Machine.Worn,
                Blocks = () => RestraintHub.Blocks.None,
                OnChosen = () => { var c = TetherController.Instance; if (c != null) c.PutOn(); },
                Attempt = () => TetherController.Instance == null ? 0f : TetherController.Instance.Machine.Attempt,
                OnInput = i => { var c = TetherController.Instance; if (c != null) c.OnInput(i); },
                OnTap = () => TetherController.Instance?.OnTap(),
                OnSave = slot => TetherController.Instance?.OnSave(slot),
                OnLoad = slot => TetherController.Instance?.OnLoad(slot),
                OnNewGame = () => TetherController.Instance?.OnNewGame(),
                Controls = "+/- length, tap F: point",
            };
            Hub.Register(Entry);
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready (" + TetherController.Set.Models.Count + " models)");
        }
    }
}
