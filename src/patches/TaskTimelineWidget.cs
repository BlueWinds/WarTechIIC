using System;
using System.Collections.Generic;
using Harmony;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(TaskTimelineWidget), "RemoveEntry")]
    public static class TaskTimelineWidget_RemoveEntry_Patch {
        static bool Prefix(WorkOrderEntry entry) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract != null && extendedContract.workOrder == entry || extendedContract.extraWorkOrder == entry) {
                    return false;
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TaskTimelineWidget), "RegenerateEntries")]
    public static class TaskTimelineWidget_RegenerateEntries_Patch {
        static void Postfix(TaskTimelineWidget __instance) {
            WIIC.modLog.Debug?.Write("TaskTimelineWidget.RegenerateEntries");

            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract == null) {
                    return;
                }

                __instance.AddEntry(extendedContract.workOrder, false);
                if (extendedContract.extraWorkOrder != null) {
                    __instance.AddEntry(extendedContract.extraWorkOrder, false);
                }
                __instance.RefreshEntries();
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(TaskTimelineWidget), "OnTaskDetailsClicked")]
    public static class TaskTimelineWidget_OnTaskDetailsClicked_Patch {
        static void Postfix(TaskManagementElement element) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract == null || (element.Entry.ID != "nextflareupContract" && element.Entry.ID != "extendedContractComplete")) {
                    return;
                }

                WIIC.sim.SetTimeMoving(false);
                PauseNotification.Show($"{extendedContract.type} Details", extendedContract.getDescription(), WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), "", true, null);
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
