using System;
using System.Collections.Generic;
using Harmony;
using BattleTech;
using BattleTech.UI;
using UnityEngine;

namespace WarTechIIC {
    [HarmonyPatch(typeof(TaskManagementElement), "UpdateTaskInfo")]
    public static class TaskManagementElement_UpdateTaskInfo_Patch {
        static void Postfix(TaskManagementElement __instance) {
            try {
                WorkOrderEntry entry = __instance.entry;
                if (entry.ID == "extendedContractComplete" || entry.ID == "extendedContractExtra") {
                    ExtendedContract extended = Utilities.currentExtendedContract();
                    __instance.SetIcon(extended.employer.FactionDef.GetSprite());
                }

                if (entry.ID == "campaignContract") {
                    WIIC.activeCampaigns.TryGetValue(WIIC.sim.CurSystem.ID, out ActiveCampaign ac);
                    string employer = ac?.currentEntry.contract.employer;
                    Sprite sprite = WIIC.sim.GetFactionDef(employer).GetSprite();
                    __instance.SetIcon(sprite);
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
