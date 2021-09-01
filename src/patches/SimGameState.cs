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
using Localize;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameState), "Init")]
    public static class SimGameState_InitPatch {
        public static void Postfix(SimGameState __instance) {
            try {
                WIIC.sim = __instance;
                WIIC.modLog.Debug?.Write("Clearing Flareups for new SimGameState");
                WIIC.flareups.Clear();
                WIIC.systemControl.Clear();
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnCareerModeStart")]
    public static class SimGameState_OnCareerModeStartPatch {
        public static void Postfix(SimGameState __instance) {
            try {
                WIIC.readFromJson();
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
                WIIC.sim = __instance;
                WIIC.modLog.Debug?.Write("Loading Flareups");
                WIIC.flareups.Clear();
                WIIC.sim.CompanyTags.Add("WIIC_enabled");

                foreach (StarSystem system in __instance.StarSystems) {
                    string tag = system.Tags.ToList().Find(Flareup.isSerializedFlareup);
                    if (tag != null) {
                        system.Tags.Remove(tag);

                        Flareup flareup = Flareup.Deserialize(tag, __instance);
                        WIIC.flareups[system.ID] = flareup;
                    }

                    tag = system.Tags.ToList().Find(Utilities.isControlTag);
                    if (tag != null) {
                        system.Tags.Remove(tag);
                        WIIC.systemControl[system.ID] = tag;
                    }
                }

                WIIC.modLog.Debug?.Write($"Loaded {WIIC.flareups.Keys.Count} flareups and {WIIC.systemControl.Keys.Count} system control tags");
                Utilities.redrawMap();
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

                Utilities.slowDownFloaties();
                ColourfulFlashPoints.Main.clearMapMarkers();

                // ToList is used to make a copy because we may need to remove elements as we're iterating
                foreach (Flareup flareup in WIIC.flareups.Values.ToList()) {
                    bool finished = flareup.passDay();
                    if (finished) {
                        WIIC.cleanupSystem(flareup.location);
                    } else {
                        if (activeItems.TryGetValue(flareup.workOrder, out var taskManagementElement)) {
                            taskManagementElement.UpdateItem(0);
                        }
                    }
                }

                WhoAndWhere.checkForNewFlareup();

                if (Utilities.deferredToasts.Count > 0) {
                    foreach (var toast in Utilities.deferredToasts) {
                        WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(toast));
                    }
                    Utilities.deferredToasts.Clear();
                }

                Utilities.redrawMap();

                WIIC.sim.RoomManager.RefreshTimeline(false);
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnNewQuarterBegin")]
    public static class SimGameStateOnNewQuarterBeginPatch {
        private static void Postfix() {
            try {
                WIIC.modLog.Debug?.Write($"Refreshing contracts in current system at start of month");
                WIIC.sim.CurSystem.ResetContracts();
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "FinishCompleteBreadcrumbProcess")]
    public static class SimGameState_FinishCompleteBreadcrumbProcessPatch {
        [HarmonyPrefix]
        public static bool ParticipateInFlareupPrefix(SimGameState __instance, out bool __state) {
            __state = false;
            try {
                string id = __instance.ActiveTravelContract.Override.ID;
                if (id == "wiic_help_attacker" || id == "wiic_raid_attacker") {
                    WIIC.modLog.Debug?.Write($"Added company tag for helping attacker");
                    __instance.CompanyTags.Add("WIIC_helping_attacker");
                    __state = true;
                } else if (id == "wiic_help_defender" || id == "wiic_raid_defender") {
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
                __instance.ClearBreadcrumb();
                flareup.removeParticipationContracts();

                // When the player arrives, we start the flareup the next day - it's only fair not to make them wait around. :)
                flareup.countdown = 0;
                flareup.daysUntilMission = 1;

                WIIC.modLog.Info?.Write($"Player embarked on flareup at {flareup.location.Name}.");

                __instance.SetSimRoomState(DropshipLocation.SHIP);
                __instance.RoomManager.AddWorkQueueEntry(flareup.workOrder);
                __instance.RoomManager.RefreshTimeline(false);
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
                WIIC.modLog.Debug?.Write($"Entering system {system.ID} from {WIIC.sim.CurSystem.ID}");

                if (WIIC.flareups.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    WIIC.modLog.Debug?.Write($"Found flareup from previous system, cleaning up contracts");
                    Flareup prevFlareup = WIIC.flareups[WIIC.sim.CurSystem.ID];
                    prevFlareup.removeParticipationContracts();
                }

                if (WIIC.flareups.ContainsKey(system.ID)) {
                    WIIC.modLog.Debug?.Write($"Found flareup for new system, adding contracts");
                    WIIC.flareups[system.ID].spawnParticipationContracts();
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
                WIIC.modLog.Debug?.Write($"CompleteLanceConfigurationPrep. selectedContract: {WIIC.sim.SelectedContract.Name}, flareupContract: {(flareup != null ? flareup.currentContractName : null)}");
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

    [HarmonyPatch(typeof(SimGameState), "BuildSimGameStatsResults")]
    public static class SimGameState_BuildSimGameStatsResults_Patch {
        public static void Prefix(ref SimGameStat[] stats) {
            try {
                stats = stats.Where(s => !s.name.StartsWith("WIIC")).ToArray();
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
