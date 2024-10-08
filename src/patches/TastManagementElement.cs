using System;
using System.Collections.Generic;
using Harmony;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(TaskManagementElement), "UpdateTaskInfo")]
    public static class TaskManagementElement_UpdateTaskInfo_Patch {
        static void Postfix(TaskManagementElement __instance) {
            try {
                var entry = __instance.entry;
                if (entry.Type == WorkOrderType.NotificationGeneric && (entry.ID == "extendedContractComplete" || entry.ID == "extendedContractExtra")) {
                    var extended = Utilities.currentExtendedContract();
                    WIIC.modLog.Trace?.Write($"TaskManagementElement_UpdateTaskInfo_Patch setting icon to {extended.employer.factionDef.Name}");
                    __instance.SetIcon(extended.employer.factionDef.GetSprite());
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
