using System;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameConversationManager), "EndConversation")]
    public static class SimGameConversationManager_EndConversation_Patch {
        public static void Postfix() {
            try {
                foreach (ActiveCampaign ac in WIIC.activeCampaigns) {
                    WIIC.l.Log($"SimGameConversationManager_EndConversation_Patch: ac={ac}");

                    if (ac?.currentEntry.conversation != null) {
                        WIIC.l.Log($"    Conversation complete!");
                        ac.currentEntry.conversation?.characters.reset();
                        ac.entryComplete();
                    }
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
