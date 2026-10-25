using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NippleTether.Logic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace NippleTether
{
    /// <summary>
    /// The level's colliders for the chains (<see cref="IObstacles"/>): a joint's sphere is tested against the colliders it
    /// overlaps (<c>Physics.OverlapSphereNonAlloc</c>, triggers ignored) and pushed out with <c>Physics.ComputePenetration</c>
    /// against a probe sphere collider (kept far away and a trigger, so it never touches anything itself). Skipped: her own
    /// colliders, NPCs, UI, and the levels' invisible player blockers (layer <c>PlayerBlocker</c>, or named so - in StationFront
    /// they sit on <c>Level</c>, api-notes level-geometry.md).
    /// </summary>
    internal sealed class EnvironmentProbe : IObstacles
    {
        private readonly GameObject _host;
        private readonly SphereCollider _probe;
        private readonly Il2CppReferenceArray<Collider> _hits = new Il2CppReferenceArray<Collider>(16);
        private readonly Dictionary<IntPtr, bool> _skip = new Dictionary<IntPtr, bool>();
        private readonly int _mask;
        public Transform Player;
        public int Queries, Pushes;            // diagnostics, per frame (reset by the caller)

        public EnvironmentProbe()
        {
            _host = new GameObject("NippleTether.Probe");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.transform.position = new Vector3(0f, -10000f, 0f);
            int ignore = LayerMask.NameToLayer("Ignore Raycast");
            if (ignore >= 0) _host.layer = ignore;
            _probe = _host.AddComponent<SphereCollider>();
            _probe.isTrigger = true;
            _probe.radius = 0.01f;
            int excluded = 0;
            foreach (string name in new[] { "Player", "PlayerFace", "PlayerBlocker", "UI", "Npc", "Ignore Raycast", "CensorFaceCamera", "LensFlareBlocker" })
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0) excluded |= 1 << layer;
            }
            _mask = ~excluded;
        }

        public void Destroy()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
        }

        public bool PushOut(ref NVector3 p, float radius, out NVector3 normal)
        {
            normal = NVector3.UnitY;
            Vector3 at = new Vector3(p.X, p.Y, p.Z);
            Queries++;
            int n = Physics.OverlapSphereNonAlloc(at, radius, _hits, _mask, QueryTriggerInteraction.Ignore);
            if (n <= 0) return false;
            _probe.radius = radius;
            bool moved = false;
            Vector3 way = Vector3.zero;
            for (int i = 0; i < n && i < _hits.Length; i++)
            {
                var c = _hits[i];
                if (c == null || Skip(c)) continue;
                Transform t = c.transform;
                if (!Physics.ComputePenetration(_probe, at, Quaternion.identity, c, t.position, t.rotation, out Vector3 dir, out float dist)) continue;
                if (!(dist > 0f)) continue;
                at += dir * dist;
                way += dir * dist;
                moved = true;
            }
            if (!moved) return false;
            Pushes++;
            p = new NVector3(at.x, at.y, at.z);
            float len = way.magnitude;
            normal = len > 1e-9f ? new NVector3(way.x / len, way.y / len, way.z / len) : NVector3.UnitY;
            return true;
        }

        private bool Skip(Collider c)
        {
            if (c.Pointer == _probe.Pointer) return true;
            if (Player != null && c.transform.IsChildOf(Player)) return true;
            if (_skip.TryGetValue(c.Pointer, out bool skip)) return skip;
            skip = false;
            for (Transform t = c.transform; t != null && !skip; t = t.parent)
                skip = t.name.IndexOf("PlayerBlocker", StringComparison.OrdinalIgnoreCase) >= 0;
            if (_skip.Count > 4096) _skip.Clear(); // map changes leave dead entries behind
            _skip[c.Pointer] = skip;
            return skip;
        }
    }
}
