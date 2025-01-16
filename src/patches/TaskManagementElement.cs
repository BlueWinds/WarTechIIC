using System;
using System.Collections.Generic;
using System.Linq;
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
                Sprite sprite = null;

                if (entry.ID == "extendedContractComplete" || entry.ID == "extendedContractExtra") {
                    ExtendedContract extended = Utilities.currentExtendedContract();
                    sprite = extended.employer.FactionDef.GetSprite();
                } else if (entry.ID == "campaignContract") {
                    foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.currentEntry.contract?.forcedDays != null)) {
                        string employer = ac.currentEntry.contract.employer;
                        sprite = WIIC.sim.GetFactionDef(employer).GetSprite();
                    }

                    if (__instance.entry.IsCostPaid()) {
                        __instance.daysText.SetText("0 Days");
                    }
                } else if (entry.ID == "campaignWait") {
                    foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.currentEntry.wait?.sprite != null)) {
                        sprite = WIIC.sim.DataManager.SpriteCache.GetSprite(ac.currentEntry.wait.sprite);
                    }
                }

                if (sprite != null) {
                    __instance.SetIcon(sprite);
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
