using System;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace NippleTether
{
    /// <summary>
    /// Optional: whether NipplePiercings (if installed) has a piercing on each nipple - then the tether draws no ring of its own
    /// there and its chain starts at the pierced point. Read by reflection from that mod's config entries
    /// (<c>NipplePiercings.Plugin.Enabled / Left / Right</c>), once a second; any mismatch turns it off with one warning.
    /// </summary>
    internal static class PiercingsLink
    {
        private const string Guid = "com.local.nipplepiercings";
        private static readonly bool[] _worn = new bool[2];
        private static float _next;
        private static bool _broken, _found;
        private static FieldInfo _enabled, _left, _right;

        public static bool[] Worn()
        {
            if (_broken || Time.time < _next) return _worn;
            _next = Time.time + 1f;
            try
            {
                if (!_found)
                {
                    _found = true;
                    if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(Guid, out var info) || info.Instance == null) { _broken = true; return _worn; }
                    var type = info.Instance.GetType();
                    _enabled = type.GetField("Enabled", BindingFlags.Public | BindingFlags.Static);
                    _left = type.GetField("Left", BindingFlags.Public | BindingFlags.Static);
                    _right = type.GetField("Right", BindingFlags.Public | BindingFlags.Static);
                    if (_enabled == null || _left == null || _right == null)
                    {
                        Plugin.Logger.LogWarning("NipplePiercings " + info.Metadata.Version + " found, but not its Enabled / Left / Right config: the tether's rings are always drawn");
                        _broken = true;
                        return _worn;
                    }
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] piercings: NipplePiercings " + info.Metadata.Version + " found; its piercings replace the tether's rings");
                }
                bool on = Value(_enabled) is bool b && b;
                _worn[0] = on && Worn(Value(_left));
                _worn[1] = on && Worn(Value(_right));
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("reading NipplePiercings' config failed, the tether's rings are always drawn: " + e.Message);
                _broken = true;
                _worn[0] = _worn[1] = false;
            }
            return _worn;
        }

        private static object Value(FieldInfo f) => (f.GetValue(null) as BepInEx.Configuration.ConfigEntryBase)?.BoxedValue;

        private static bool Worn(object id) => id is string s && s.Length > 0 && !string.Equals(s, "none", StringComparison.OrdinalIgnoreCase);
    }
}
