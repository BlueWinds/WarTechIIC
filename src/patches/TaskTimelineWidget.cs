using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(TaskTimelineWidget), "RemoveEntry")]
    public static class TaskTimelineWidget_RemoveEntry_Patch {
        static bool Prefix(WorkOrderEntry entry) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract?.workOrder == entry || extendedContract?.extraWorkOrder == entry) {
                    return false;
                }

                foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.workOrder == entry)) {
                    return false;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(TaskTimelineWidget), "RegenerateEntries")]
    public static class TaskTimelineWidget_RegenerateEntries_Patch {
        static void Postfix(TaskTimelineWidget __instance) {
            WIIC.l.Log("TaskTimelineWidget.RegenerateEntries");

            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract?.workOrder != null) {
                    __instance.AddEntry(extendedContract.workOrder, false);
                }
                if (extendedContract?.extraWorkOrder != null) {
                    __instance.AddEntry(extendedContract.extraWorkOrder, false);
                }

                foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.workOrder != null)) {
                    __instance.AddEntry(ac.workOrder, false);
                }

                __instance.RefreshEntries();
            }
            catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(TaskTimelineWidget), "OnTaskDetailsClicked")]
    public static class TaskTimelineWidget_OnTaskDetailsClicked_Patch {
        static void Postfix(TaskManagementElement element) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract != null && (element.Entry.ID == "nextflareupContract" || element.Entry.ID == "extendedContractComplete")) {
                    WIIC.sim.SetTimeMoving(false);
                    PauseNotification.Show($"{extendedContract.type} Details", extendedContract.getDescription(), WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), "", true, null);
                    return;
                }

                if (element.Entry.ID == "campaignContract") {
                    WIIC.l.Log($"Sent to command center from task timeline widget");
                    WIIC.sim.SetTimeMoving(false);
                    WIIC.sim.RoomManager.SetQueuedUIActivationID(DropshipMenuType.Contract, DropshipLocation.CMD_CENTER, true);
                    WIIC.sim.RoomManager.ForceShipRoomChangeOfRoom(DropshipLocation.CMD_CENTER);
                    return;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
