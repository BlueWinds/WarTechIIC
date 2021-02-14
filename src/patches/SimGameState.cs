using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using Harmony;
using BattleTech;
using BattleTech.UI;
using BattleTech.Save;
using BattleTech.Save.Test;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameState), "Init")]
    public static class SimGameState_InitPatch {
        [HarmonyPostfix]
        public static void OnInit(SimGameState __instance) {
            try {
                WIIC.sim = __instance;
                WIIC.modLog.Debug?.Write("Clearing Flareups for new SimGameState");
                WIIC.flareups.Clear();

                if (WIIC.settings.setActiveFactionsForAllSystems) {
                    foreach (StarSystem system in __instance.StarSystems) {
                        Utilities.setActiveFactions(system);
                    }
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "Rehydrate")]
    public static class SimGameState_RehydratePatch {
        [HarmonyPostfix]
        public static void LoadFlareups(GameInstanceSave gameInstanceSave, SimGameState __instance) {
            try {
                WIIC.modLog.Debug?.Write("Loading Flareups");
                WIIC.sim = __instance;
                WIIC.flareups.Clear();
                ColourfulFlashPoints.Main.clearMapMarkers();
                foreach (StarSystem system in __instance.StarSystems) {
                    foreach (string tag in system.Tags) {
                        if (Flareup.isSerializedFlareup(tag)) {
                            WIIC.modLog.Debug?.Write($"    {tag}");
                            Flareup flareup = Flareup.Deserialize(tag, __instance);
                            WIIC.flareups[system.ID] = flareup;
                            flareup.addToMap();
                        }
                    }
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "Dehydrate")]
    public static class SimGameState_DehydratePatch {
        [HarmonyPrefix]
        public static void SaveFlareups() {
            WIIC.modLog.Debug?.Write("Saving active flareups in system tags");

            try {
                foreach (Flareup flareup in WIIC.flareups.Values) {
                    WIIC.modLog.Debug?.Write($"    {flareup.Serialize()}");
                    flareup.location.Tags.Add(flareup.Serialize());
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
    public static class SimGameStateOnDayPassedPatch {
        private static FieldInfo _getTimelineWidget = AccessTools.Field(typeof(SGRoomManager), "timelineWidget");
        private static FieldInfo _getActiveItems = AccessTools.Field(typeof(TaskTimelineWidget), "ActiveItems");

        private static void Prefix() {
            try {
                var timelineWidget = (TaskTimelineWidget)_getTimelineWidget.GetValue(WIIC.sim.RoomManager);
                var activeItems = (Dictionary<WorkOrderEntry, TaskManagementElement>)_getActiveItems.GetValue(timelineWidget);

                ColourfulFlashPoints.Main.clearMapMarkers();

                // ToList is used to make a copy because we may need to remove elements as we're iterating
                foreach (Flareup flareup in WIIC.flareups.Values.ToList()) {
                    bool finished = flareup.passDay();
                    if (finished) {
                        WIIC.flareups.Remove(flareup.location.Name);
                    } else {
                        flareup.addToMap();
                        if (activeItems.TryGetValue(flareup.workOrder, out var taskManagementElement)) {
                            taskManagementElement.UpdateItem(0);
                        }
                    }
                }

                WhoAndWhere.checkForNewFlareup();

                WIIC.sim.RoomManager.RefreshTimeline(false);
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
                WIIC.modLog.Error?.Write(e.InnerException);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "FinishCompleteBreadcrumbProcess")]
    public static class SimGameState_FinishCompleteBreadcrumbProcessPatch {
        [HarmonyPrefix]
        public static bool ParticipateInFlareupPrefix(SimGameState __instance, out bool __state) {
            __state = false;
            try {
                if (__instance.ActiveTravelContract.Override.ID == "wiic_help_attacker") {
                    WIIC.modLog.Debug?.Write($"Added company tag for helping attacker");
                    __instance.CompanyTags.Add("WIIC_helping_attacker");
                    __state = true;
                } else if (__instance.ActiveTravelContract.Override.ID == "wiic_help_defender") {
                    WIIC.modLog.Debug?.Write($"Added company tag for helping defender");
                    __instance.CompanyTags.Add("WIIC_helping_defender");
                    __state = true;
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
            return true;
        }

        [HarmonyPostfix]
        public static void ParticipateInFlareupPostfix(SimGameState __instance, bool __state) {
            try {
                // __state is used to tell the postfix (from the prefix) that we're in the middle of accepting a flareup
                if (!__state) {
                    return;
                }

                Flareup flareup = Utilities.currentFlareup();
                if (flareup == null) {
                    return;
                }

                // Clean up the opposite-side travel contract, if it exists
                flareup.removeParticipationContracts();

                // When the player arrives, we start the flareup the next day - it's only fair not to make them wait around. :)
                flareup.countdown = 0;
                flareup.daysUntilMission = 1;

                WIIC.modLog.Info?.Write($"Player embarked on flareup at {flareup.location.Name}.");

                __instance.SetSimRoomState(DropshipLocation.SHIP);
                __instance.RoomManager.AddWorkQueueEntry(flareup.workOrder);
                __instance.RoomManager.RefreshTimeline(false);
                __instance.Update();
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "SetCurrentSystem")]
    public static class SimGameState_SetCurrentSystem_Patch {
        [HarmonyPrefix]
        public static void SetCurrentSystemPrefix(StarSystem system, bool force = false, bool timeSkip = false) {
            try {
                if (WIIC.flareups.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    // Clean up participation contracts for the system we've just left
                    Flareup prevFlareup = WIIC.flareups[WIIC.sim.CurSystem.ID];
                    prevFlareup.removeParticipationContracts();
                }

                if (WIIC.flareups.ContainsKey(system.ID)) {
                    // Create new participation contracts for the system we're entering
                    Flareup flareup = WIIC.flareups[system.ID];
                    flareup.spawnParticipationContracts();
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "CompleteLanceConfigurationPrep")]
    public static class SimGameState_CompleteLanceConfigurationPrep_Patch {
        public static void Postfix() {
            try {
                Flareup flareup = Utilities.currentFlareup();
                WIIC.modLog.Debug?.Write($"CompleteLanceConfigurationPrep. selectedContract: {WIIC.sim.SelectedContract.Name}, flareupContract: {flareup.currentContractName}");
                if (flareup != null && WIIC.sim.SelectedContract.Name == flareup.currentContractName) {
                    WIIC.modLog.Debug?.Write($"Hiding nav drawer from CompleteLanceConfigurationPrep.");
                    SGLeftNavDrawer leftDrawer = (SGLeftNavDrawer)AccessTools.Field(typeof(SGRoomManager), "LeftDrawerWidget").GetValue(WIIC.sim.RoomManager);
                    leftDrawer.Visible = false;
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}