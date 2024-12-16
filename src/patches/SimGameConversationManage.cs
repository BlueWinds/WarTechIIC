using System;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameConversationManager), "EndConversation")]
    public static class SimGameConversationManager_EndConversation_Patch {
        public static void Postfix() {
            try {
                WIIC.activeCampaigns.TryGetValue(WIIC.sim.CurSystem.ID, out ActiveCampaign ac);
                WIIC.l.Log($"SimGameConversationManager_EndConversation_Patch: ac={ac}");

                if (ac?.currentEntry?.conversation != null) {
                    WIIC.l.Log($"    Conversation complete!");
                    ac.entryComplete();
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
