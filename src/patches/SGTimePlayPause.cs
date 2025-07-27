using System;
using Harmony;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGTimePlayPause), "SetTimeMoving")]
    public static class SGTimePlayPause_SetTimeMoving_Patch {
        [HarmonyPrefix]
        static bool Prefix(bool isPlaying, SGTimePlayPause __instance) {
            try {
                if (!isPlaying || __instance.TimeMoving) {
                    return true;
                }

                ExtendedContract ec = Utilities.currentExtendedContract();

                if (ec?.currentContractName != null) {
                    WIIC.l.Log($"SGTimePlayPause_SetTimeMoving_Patch: ec.type={ec.type}, currentContractName={ec.currentContractName}, isPlaying={isPlaying}");
                    WIIC.l.Log($"    Overriding original SetTimeMoving and sending player to the command center.");
                    Utilities.sendToCommandCenter();
                    return false;
                }

                foreach (ActiveCampaign ac in WIIC.activeCampaigns) {
                    WIIC.l.Log($"SGTimePlayPause_SetTimeMoving_Patch: entryCountdown={ac.entryCountdown}, isPlaying={isPlaying}");

                    if (ac.currentEntry.contract?.withinDays != null && (ac.entryCountdown == 0 || ac.entryCountdown == null)) {
                        WIIC.l.Log($"    Overriding original SetTimeMoving and sending player to the command center.");

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
