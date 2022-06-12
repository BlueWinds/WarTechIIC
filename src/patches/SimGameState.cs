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
                WIIC.readFromJson("WIIC_systemControl.json", false);
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
                WIIC.modLog.Info?.Write("Player currently at {__instance.CurSystem.ID}. Loading Flareups.");

                WIIC.flareups.Clear();
                WIIC.sim.CompanyTags.Add("WIIC_enabled");

                WIIC.readFromJson("WIIC_ephemeralSystemControl.json", true);

                foreach (StarSystem system in __instance.StarSystems) {
                    string tag = system.Tags.ToList().Find(ExtendedContract.isSerializedExtendedContract);
                    if (tag != null) {
                        system.Tags.Remove(tag);

                        WIIC.modLog.Debug?.Write($"Extended Contract for {system.ID}: {tag}");
                        ExtendedContract extendedContract = ExtendedContract.Deserialize(tag);
                        WIIC.extendedContracts[system.ID] = extendedContract;
                    }

                    tag = system.Tags.ToList().Find(Flareup.isSerializedFlareup);
                    if (tag != null) {
                        system.Tags.Remove(tag);

                        WIIC.modLog.Debug?.Write($"Flareup for {system.ID}: {tag}");
                        Flareup flareup = Flareup.Deserialize(tag);
                        WIIC.flareups[system.ID] = flareup;
                    }

                    tag = system.Tags.ToList().Find(Utilities.isControlTag);
                    if (tag != null) {
                        system.Tags.Remove(tag);
                        WIIC.systemControl[system.ID] = tag;
                    }
                }

                WIIC.modLog.Debug?.Write($"Loaded {WIIC.flareups.Keys.Count} flareups, {WIIC.extendedContracts.Keys.Count} extended contracts and {WIIC.systemControl.Keys.Count} system control tags");
                Utilities.redrawMap();


                // Just some temporary cleanup
                WIIC.modLog.Error?.Write($"If this makes it into the release version, poke BlueWinds.");
                WIIC.removeGlobalContract("wiic_help_attacker");
                WIIC.removeGlobalContract("wiic_help_defender");
                WIIC.removeGlobalContract("wiic_raid_attacker");
                WIIC.removeGlobalContract("wiic_raid_defender");
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
                        Utilities.cleanupFlareupSystem(flareup.location);
                    } else {
                        if (activeItems.TryGetValue(flareup.workOrder, out var taskManagementElement)) {
                            taskManagementElement.UpdateItem(0);
                        }
                    }
                }

                // ToList is used to make a copy because we may need to remove elements as we're iterating
                foreach (ExtendedContract extendedContract in WIIC.extendedContracts.Values.ToList()) {
                    bool finished = extendedContract.passDay();
                    if (finished) {
                        WIIC.extendedContracts.Remove(extendedContract.locationID);
                    } else {
                        if (activeItems.TryGetValue(extendedContract.workOrder, out var taskManagementElement)) {
                            taskManagementElement.UpdateItem(0);
                        }
                    }
                }

                bool newFlareup = WhoAndWhere.checkForNewFlareup();
                if (!newFlareup) {
                    WhoAndWhere.checkForNewExtendedContract();
                }

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
                // If we're in the middle of initializing a new career no need to do anything.
                if (WIIC.sim != null) {
                    WIIC.modLog.Info?.Write($"Refreshing contracts in current system at start of month");
                    WIIC.sim.CurSystem.ResetContracts();
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "FinishCompleteBreadcrumbProcess")]
    public static class SimGameState_FinishCompleteBreadcrumbProcessPatch {
        [HarmonyPrefix]
        public static bool ParticipateInContractPrefix(SimGameState __instance, out string __state) {
            __state = null;
            try {
                __state = __instance.ActiveTravelContract.Override.ID;
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }

            return true;
        }

        [HarmonyPostfix]
        public static void ParticipateInContractPostfix(SimGameState __instance, string __state) {
            try {
                if (WIIC.flareups.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    Flareup flareup = WIIC.flareups[WIIC.sim.CurSystem.ID];
                    if (__state == "wiic_help_defender" || __state == "wiic_raid_defender" || __state == "wiic_helping_defender" || __state == "Wiic_helping_attacker") {
                        __instance.ClearBreadcrumb();
                        flareup.acceptContract(__state);
                    }
                }

                if (WIIC.extendedContracts.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    ExtendedContract extendedContract = WIIC.extendedContracts[WIIC.sim.CurSystem.ID];
                    if (__state == extendedContract.extendedType.hireContract) {
                        __instance.ClearBreadcrumb();
                        extendedContract.acceptContract(__state);
                    }
                }
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
                    prevFlareup.removeParticipationContract();
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
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                WIIC.modLog.Debug?.Write($"CompleteLanceConfigurationPrep. selectedContract: {WIIC.sim.SelectedContract.Name}, currentContractName: {(extendedContract != null ? extendedContract.currentContractName : null)}");
                if (extendedContract != null && WIIC.sim.SelectedContract.Name == extendedContract.currentContractName) {
                    WIIC.modLog.Debug?.Write($"Hiding nav drawer from CompleteLanceConfigurationPrep.");
                    SGLeftNavDrawer leftDrawer = (SGLeftNavDrawer)AccessTools.Field(typeof(SGRoomManager), "LeftDrawerWidget").GetValue(WIIC.sim.RoomManager);
                    leftDrawer.Visible = false;
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
