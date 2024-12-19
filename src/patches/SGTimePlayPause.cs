using System;
using Harmony;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGTimePlayPause), "ReceiveButtonPress")]
    public static class SGTimePlayPause_ReceiveButtonPress_Patch {
        [HarmonyPrefix]
        static void Prefix(ref string button) {
            try {
                WIIC.activeCampaigns.TryGetValue(WIIC.sim.CurSystem.ID, out ActiveCampaign ac);
                WIIC.l.Log($"SGTimePlayPause_ReceiveButtonPress_Patch: ac={ac} entryCountdown={ac?.entryCountdown} button={button}");

                if (ac?.entryCountdown == 0 && ac.currentEntry.contract != null && button == "ToggleTime") {
                    WIIC.l.Log($"    Overriding original \"ToggleTime\" button with \"LaunchContract\".");
                    button = "LaunchContract";
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
