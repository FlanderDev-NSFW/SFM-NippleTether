using System;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace NippleTether
{
    /// <summary>
    /// BreastPhysics' API (<c>BreastPhysics.BreastApi</c>), found at runtime: with that mod on, taut chains hold the nipples in its
    /// simulation. Without it, or while it is switched off, the tether turns the breasts itself (<see cref="Logic.BreastPull"/>).
    /// </summary>
    internal static class BreastLink
    {
        public const string Guid = "FlanderDev.SFM.BreastPhysics";
        private const string Owner = "NippleTether";

        private static Action<string, int, Vector3, float, Vector3, float> _hold;
        private static Action<string, int> _release;
        private static Func<int, Vector3> _offset;
        private static Func<bool> _active;
        private static Func<int, Vector3, float> _give;

        public static bool Active => _hold != null && _active();

        public static void Connect()
        {
            if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(Guid, out var info) || info.Instance == null)
            {
                Plugin.Logger.LogInfo("[SFM-ACCEPT] breasts: BreastPhysics is not installed; the tether pulls them itself");
                return;
            }
            try
            {
                Type api = info.Instance.GetType().Assembly.GetType("BreastPhysics.BreastApi", true);
                T Get<T>(string name) where T : Delegate => (T)api.GetMethod(name).CreateDelegate(typeof(T));
                _release = Get<Action<string, int>>("Release");
                _offset = Get<Func<int, Vector3>>("Offset");
                _active = Get<Func<bool>>("IsActive");
                _give = Get<Func<int, Vector3, float>>("Give");
                _hold = Get<Action<string, int, Vector3, float, Vector3, float>>("HoldWithin");
                Plugin.Logger.LogInfo("[SFM-ACCEPT] breasts: chains pull through BreastPhysics " + info.Metadata.Version);
            }
            catch (Exception e)
            {
                _hold = null;
                Plugin.Logger.LogWarning("BreastPhysics does not fit this version, the tether pulls the breasts itself: " + e.Message);
            }
        }

        /// <summary>The nipple no further than a length from a point, but drawn no further than <paramref name="most"/> from rest.</summary>
        public static void Hold(int side, Vector3 point, float length, Vector3 at, float most) => _hold?.Invoke(Owner, side, point, length, at, most);

        public static void Release(int side) => _release?.Invoke(Owner, side);

        /// <summary>How much nearer to a point the breast can bring its nipple than at rest (m).</summary>
        /// <param name="to">The point from where the chain would start with the breast at rest (world).</param>
        public static float Give(int side, Vector3 to) => Active ? _give(side, to) : 0f;

        /// <summary>How far the nipple is drawn from its resting place now (world).</summary>
        public static Vector3 Offset(int side) => Active ? _offset(side) : Vector3.zero;
    }
}
