using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using NipplePiercings.Logic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace NippleTether
{
    /// <summary>
    /// What is drawn: the two chains' links (a pool per chain, grown as the chain is let out, the rest hidden), a ring through
    /// each nipple and the hoop at the point - NipplePiercings' "link", "ring" and "hoop" models, one object per piece, placed in
    /// the world every frame before rendering (not parented to her). Light silver on a copy of the game's handcuff material,
    /// as NipplePiercings' metal.
    /// </summary>
    internal sealed class TetherRig
    {
        private readonly GameObject _root;
        private readonly int _layer;
        private readonly Material _metal;
        private readonly Mesh _link, _ring, _hoop;
        private readonly List<Transform>[] _links = { new List<Transform>(), new List<Transform>() };
        private readonly int[] _shown = new int[2];
        private readonly Transform[] _rings = new Transform[2];
        private readonly Transform _anchor;

        /// <summary>The ring model's bone the ring hangs on (its head = where the wire passes through the nipple).</summary>
        public readonly PendantBone RingBone;
        public readonly PendantBone HoopBone;

        public TetherRig(PiercingSet set, int layer)
        {
            _layer = layer;
            _root = new GameObject("NippleTether");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.layer = layer;
            _metal = MakeMetal();
            var link = set.Get("link") ?? throw new InvalidOperationException("no chain link model");
            var ring = set.Get("ring") ?? throw new InvalidOperationException("no ring model");
            var hoop = set.Get("hoop") ?? throw new InvalidOperationException("no hoop model");
            _link = MakeMesh(link.Bones[0], "link", centred: true);
            RingBone = ring.Bones[ring.Bones.Count - 1];
            HoopBone = hoop.Bones[hoop.Bones.Count - 1];
            _ring = MakeMesh(RingBone, "ring", centred: false);
            _hoop = MakeMesh(HoopBone, "hoop", centred: false);
            for (int s = 0; s < 2; s++) _rings[s] = Piece("ring." + (s == 0 ? "L" : "R"), _ring);
            _anchor = Piece("point", _hoop);
            _root.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        /// <summary>Chain <paramref name="chain"/>'s links where its joints are, <paramref name="scale"/> metres per model unit.</summary>
        public void PlaceLinks(int chain, Logic.TetherChain c, float scale)
        {
            var pool = _links[chain];
            int need = c.Segments;
            while (pool.Count < need) pool.Add(Piece("link." + (chain == 0 ? "L" : "R") + pool.Count.ToString("000"), _link));
            for (int k = 0; k < need; k++)
            {
                var (centre, rot) = c.Link(k);
                Set(pool[k], centre, rot, scale);
                if (k >= _shown[chain]) pool[k].gameObject.SetActive(true);
            }
            for (int k = need; k < _shown[chain]; k++) pool[k].gameObject.SetActive(false);
            _shown[chain] = need;
        }

        public void PlaceRing(int side, bool visible, NVector3 at, NQuaternion rot, float scale)
        {
            var t = _rings[side];
            if (t.gameObject.activeSelf != visible) t.gameObject.SetActive(visible);
            if (visible) Set(t, at, rot, scale);
        }

        public void PlacePoint(NVector3 at, NQuaternion rot, float scale) => Set(_anchor, at, rot, scale);

        private static void Set(Transform t, NVector3 at, NQuaternion rot, float scale)
        {
            t.SetPositionAndRotation(new Vector3(at.X, at.Y, at.Z), new Quaternion(rot.X, rot.Y, rot.Z, rot.W));
            t.localScale = Vector3.one * scale;
        }

        private Transform Piece(string name, Mesh mesh)
        {
            var g = new GameObject("NippleTether." + name);
            g.layer = _layer;
            g.transform.SetParent(_root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            var mats = new Material[mesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = _metal;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return g.transform;
        }

        /// <summary>A piece's mesh in the nipple's frame (NipplePiercings' axes), origin at its bone's head - or at its centre
        /// for the link (TetherChain.Link places links by their centres).</summary>
        private static Mesh MakeMesh(PendantBone bone, string name, bool centred)
        {
            var (pos, nrm, subs) = bone.ToLocal();
            if (centred)
            {
                NVector3 centre = NVector3.Zero;
                foreach (var p in bone.Positions) centre += PendantBone.ToNipple(p);
                centre /= Math.Max(1, bone.Positions.Length);
                for (int i = 0; i < pos.Length; i++) pos[i] = PendantBone.ToNipple(bone.Positions[i]) - centre;
            }
            var mesh = new Mesh { name = "NippleTether." + name };
            var v = new Vector3[pos.Length];
            var n = new Vector3[nrm.Length];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector3(pos[i].X, pos[i].Y, pos[i].Z);
            for (int i = 0; i < n.Length; i++) n[i] = new Vector3(nrm[i].X, nrm[i].Y, nrm[i].Z);
            mesh.vertices = v;
            mesh.normals = n;
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s].Indices, s);
            mesh.RecalculateBounds();
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset; // survives map changes (UnloadUnusedAssets)
            return mesh;
        }

        /// <summary>Light silver on a copy of the game's handcuff material (toon shader, lit and outlined like the game's items),
        /// with a third of its outline and a rim shine - NipplePiercings' metal.</summary>
        private static Material MakeMetal()
        {
            Material source = null;
            try
            {
                foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>()))
                {
                    var m = o.TryCast<Material>();
                    if (m != null && m.name != null && m.name.StartsWith("tejyou", StringComparison.Ordinal)) { source = m; break; }
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("handcuff material lookup failed: " + e.Message); }
            Material mat;
            if (source != null) mat = new Material(source);
            else
            {
                Plugin.Logger.LogWarning("handcuff material not found; using a toon/standard shader");
                Shader shader = Shader.Find("UnityChanToonShader/Toon_DoubleShadeWithFeather");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
            }
            mat.name = "NippleTether.Metal";
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var c = new Color(0.80f, 0.82f, 0.86f, 1f);
            foreach (string tex in new[] { "_MainTex", "_BaseMap", "_1st_ShadeMap", "_2nd_ShadeMap" })
                if (mat.HasProperty(tex)) mat.SetTexture(tex, Texture2D.whiteTexture);
            Set(mat, "_BaseColor", c);
            Set(mat, "_Color", c);
            Set(mat, "_1st_ShadeColor", new Color(c.r * 0.75f, c.g * 0.75f, c.b * 0.75f, 1f));
            Set(mat, "_2nd_ShadeColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, 1f));
            Set(mat, "_RimLight", 1f);
            Set(mat, "_RimLightColor", Color.white);
            Set(mat, "_RimLight_Power", 0.25f);
            Set(mat, "_HighColor", new Color(0.45f, 0.45f, 0.45f, 1f));
            Set(mat, "_HighColor_Power", 0.15f);
            if (mat.HasProperty("_Outline_Width")) mat.SetFloat("_Outline_Width", mat.GetFloat("_Outline_Width") * 0.35f);
            return mat;
        }

        private static void Set(Material m, string name, float v) { if (m.HasProperty(name)) m.SetFloat(name, v); }
        private static void Set(Material m, string name, Color v) { if (m.HasProperty(name)) m.SetColor(name, v); }
    }
}
