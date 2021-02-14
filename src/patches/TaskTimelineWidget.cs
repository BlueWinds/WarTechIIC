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
                Flareup flareup = Utilities.currentFlareup();
                if (flareup != null && flareup.workOrder == entry) {
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
                Flareup flareup = Utilities.currentFlareup();
                if (flareup == null) {
                    return;
                }

                __instance.AddEntry(flareup.workOrder, false);
                __instance.RefreshEntries();
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
