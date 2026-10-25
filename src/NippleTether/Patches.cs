using System;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;
using SfmKit.Pure;

namespace NippleTether
{
    /// <summary>
    /// The tether's save data: whether it is on and the struggle chance, as two extra keys in the game's save slot (ignored
    /// by the game without the mod); the hub saves the number shortcuts. Every hook fails open.
    /// </summary>
    internal static class Patches
    {
        public const string SaveKeyLocked = "NippleTether.Locked";
        public const string SaveKeyChance = "NippleTether.Chance";
        public const string SaveKeyShortcuts = "NippleTether.Shortcuts"; // written by the hub

        public static void Apply(Harmony harmony)
        {
            var me = typeof(Patches);
            harmony.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save)), postfix: new HarmonyMethod(me, nameof(SavePostfix)));
            harmony.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Load)), postfix: new HarmonyMethod(me, nameof(LoadPostfix)));
        }

        // Written right after SaveManager.Save into the slot it just wrote (SaveRapper.currentDataPath); the system file (s.sd)
        // is skipped. A hook on SaveRapper.AfterProcess never ran [observed 2026-10-11, SpreaderBar].
        private static void SavePostfix()
        {
            try
            {
                var c = TetherController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                SaveRapper.Save(SaveKeyLocked, c.Machine.Worn && c.Machine.State != LockState.Opening);
                SaveRapper.Save(SaveKeyChance, (double)c.Machine.Chance);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: locked=" + c.Machine.Worn + " path=" + SaveRapper.currentDataPath);
            }
            catch (Exception e) { TetherController.Fail("save hook", e); }
        }

        private static void LoadPostfix()
        {
            try
            {
                var c = TetherController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                bool locked = SaveRapper.Load(SaveKeyLocked, false); // default when the save has no key (older save)
                double chance = SaveRapper.Load(SaveKeyChance, -1.0);
                c.OnSaveLoaded(locked, (float)Math.Max(chance, 0.0));
                Plugin.Logger.LogInfo("[SFM-ACCEPT] load: locked=" + locked + " path=" + SaveRapper.currentDataPath);
            }
            catch (Exception e) { TetherController.Fail("load hook", e); }
        }
    }
}
