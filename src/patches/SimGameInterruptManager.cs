using System;
using System.Collections.Generic;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameInterruptManager), "PopupClose")]
    public static class SimGameInterruptManager_PopupClose_Patch {
        public static void Postfix(SimGameInterruptManager.Entry entry) {
            try {
                SimGameInterruptManager.RewardsPopupEntry reward = entry as SimGameInterruptManager.RewardsPopupEntry;
                if (entry == null) {
                    return;
                }

                ActiveCampaign ac = WIIC.activeCampaigns[WIIC.sim.CurSystem.ID];
                WIIC.l.Log($"SimGameInterruptManager_PopupClose_Patch: ac={ac} node={ac?.node} nodeIndex={ac?.nodeIndex}");

                if (ac?.currentEntry?.reward == null) {
                    WIIC.l.Log($"    Rewards popup, but not a campaign loootbox.");
                    return;
                }

                ac.entryComplete();
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
