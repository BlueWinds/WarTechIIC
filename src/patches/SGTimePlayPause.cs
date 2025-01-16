using System;
using Harmony;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGTimePlayPause), "ReceiveButtonPress")]
    public static class SGTimePlayPause_ReceiveButtonPress_Patch {
        [HarmonyPrefix]
        static bool Prefix(ref string button) {
            try {
                foreach (ActiveCampaign ac in WIIC.activeCampaigns) {
                    WIIC.l.Log($"SGTimePlayPause_ReceiveButtonPress_Patch: entryCountdown={ac.entryCountdown} button={button}");

                    if (ac.currentEntry.contract?.withinDays != null && (ac.entryCountdown == 0 || ac.entryCountdown == null)) {
                        WIIC.l.Log($"    Overriding original \"{button}\" button and sending player to the command center.");

                        Utilities.sendToCommandCenter();
                        return false;
                    }
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }
    }
}
