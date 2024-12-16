using System;
using System.Collections.Generic;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameInterruptManager), "PopupClosed")]
    public static class SimGameInterruptManager_PopupClosed_Patch {
        public static void Postfix(SimGameInterruptManager.Entry popup) {
            try {
                SimGameInterruptManager.RewardsPopupEntry reward = popup as SimGameInterruptManager.RewardsPopupEntry;
                if (reward == null) {
                    return;
                }

                WIIC.activeCampaigns.TryGetValue(WIIC.sim.CurSystem.ID, out ActiveCampaign ac);
                WIIC.l.Log($"SimGameInterruptManager_PopupClosed_Patch: ac={ac} node={ac?.node} nodeIndex={ac?.nodeIndex}");

                if (ac?.currentEntry.reward == null) {
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
