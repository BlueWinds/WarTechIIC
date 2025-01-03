using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using BattleTech;
using BattleTech.Save;
using BattleTech.Save.Test;

namespace WarTechIIC {
    [HarmonyPatch(typeof(GameInstanceSave), "PreSerialization")]
    public static class GameInstanceSave_PreSerialization_Patch {
        public static void Prefix(GameInstanceSave __instance) {
            if (__instance.serializePassOne) {
                return;
            }

            WIIC.l.Log($"Saving {WIIC.extendedContracts.Keys.Count} extended contracts and {WIIC.systemControl.Keys.Count} system control tags");

            string saves = "";

            try {
                foreach (ExtendedContract extendedContract in WIIC.extendedContracts.Values) {
                    saves += "\n    " + extendedContract.Serialize();
                    extendedContract.location.Tags.Add(extendedContract.Serialize());
                }
                foreach (KeyValuePair<string, string> control in WIIC.systemControl) {
                    WIIC.sim.GetSystemById(control.Key).Tags.Add(control.Value);
                }
            } catch (Exception e) {
                WIIC.l.Log(saves);
                WIIC.l.LogException(e);
                saves = "";
            }

            WIIC.l.Log(saves);
        }
    }

    [HarmonyPatch(typeof(GameInstanceSave), "PostSerialization")]
    public static class GameInstanceSave_PostSerialization_Patch {
        public static void Prefix(GameInstanceSave __instance) {
            if (!__instance.serializePassOne) {
                return;
            }

            WIIC.l.Log("Clearing extendedContract system tags post-save");

            try {
                foreach (ExtendedContract extendedContract in WIIC.extendedContracts.Values) {
                    List<string> tagList = extendedContract.location.Tags.ToList().FindAll(t => t.StartsWith("WIIC:"));
                    foreach (string tag in tagList) {
                        extendedContract.location.Tags.Remove(tag);
                    }
                }
                foreach (KeyValuePair<string, string> control in WIIC.systemControl) {
                    WIIC.sim.GetSystemById(control.Key).Tags.Remove(control.Value);
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
