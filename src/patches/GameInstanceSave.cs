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
        [HarmonyPrefix]
        public static void SaveFlareups() {
            WIIC.modLog.Debug?.Write($"Saving {WIIC.flareups.Keys.Count} flareups and {WIIC.systemControl.Keys.Count} system control tags");

            try {
                foreach (Flareup flareup in WIIC.flareups.Values) {
                    WIIC.modLog.Debug?.Write(flareup.Serialize());
                    flareup.location.Tags.Add(flareup.Serialize());
                }
                foreach (KeyValuePair<string, string> control in WIIC.systemControl) {
                    WIIC.sim.GetSystemById(control.Key).Tags.Add(control.Value);
                }
                WIIC.serializeToJson();
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(GameInstanceSave), "PostSerialization")]
    public static class GameInstanceSave_PostSerialization_Patch {
        [HarmonyPostfix]
        public static void RemoveFlareupTags() {
            WIIC.modLog.Debug?.Write("Clearing flareup system tags post-save");

            try {
                foreach (Flareup flareup in WIIC.flareups.Values) {
                    List<string> tagList = flareup.location.Tags.ToList().FindAll(t => t.StartsWith("WIIC:"));
                    foreach (string tag in tagList) {
                        flareup.location.Tags.Remove(tag);
                    }
                }
                foreach (KeyValuePair<string, string> control in WIIC.systemControl) {
                    WIIC.sim.GetSystemById(control.Key).Tags.Remove(control.Value);
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
