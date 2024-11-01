using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Harmony;
using BattleTech;
using BattleTech.UI;
using BattleTech.Framework;
using BattleTech.Save;
using BattleTech.Save.Test;
using Localize;
using HBS.Collections;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameState), "Init")]
    public static class SimGameState_InitPatch {
        public static void Postfix(SimGameState __instance) {
            try {
                WIIC.sim = __instance;
                WIIC.modLog.Debug?.Write("Clearing Extended Contracts for new SimGameState");
                WIIC.extendedContracts.Clear();
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
        public static void Postfix(GameInstanceSave gameInstanceSave, SimGameState __instance) {
            try {
                WIIC.sim = __instance;
                WIIC.modLog.Info?.Write($"Player currently at {__instance.CurSystem.ID}. Loading Extended Contracts.");

                WIIC.extendedContracts.Clear();
                __instance.CompanyTags.Add("WIIC_enabled");

                WIIC.readFromJson("WIIC_ephemeralSystemControl.json", true);

                foreach (StarSystem system in __instance.StarSystems) {
                    // If one tag fails to load we don't want to break all those that come afterwards.
                    try {
                        string tag = system.Tags.ToList().Find(ExtendedContract.isSerializedExtendedContract);
                        if (tag != null) {
                            system.Tags.Remove(tag);
                            WIIC.modLog.Debug?.Write($"    {tag}");
                            ExtendedContract extendedContract = ExtendedContract.Deserialize(tag);
                            WIIC.extendedContracts[system.ID] = extendedContract;
                        }

                        tag = system.Tags.ToList().Find(Utilities.isControlTag);
                        if (tag != null) {
                            system.Tags.Remove(tag);
                            WIIC.systemControl[system.ID] = tag;
                        }
                    } catch (Exception e) {
                        WIIC.modLog.Error?.Write(e);
                    }
                }

                WIIC.modLog.Debug?.Write($"Loaded {WIIC.extendedContracts.Keys.Count} extended contracts and {WIIC.systemControl.Keys.Count} system control tags");
                Utilities.redrawMap();
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
    public static class SimGameStateOnDayPassedPatch {
        private static void Postfix() {
            try {
                var activeItems = WIIC.sim.RoomManager.timelineWidget.ActiveItems;

                Utilities.slowDownFloaties();
                ColourfulFlashPoints.Main.clearMapMarkers();

                // ToList is used to make a copy because we may need to remove elements as we're iterating
                foreach (ExtendedContract extendedContract in WIIC.extendedContracts.Values.ToList()) {
                    bool finished = extendedContract.passDay();
                    if (finished) {
                        WIIC.extendedContracts.Remove(extendedContract.locationID);
                        Utilities.cleanupSystem(extendedContract.location);
                    }
                }

                ExtendedContract current = Utilities.currentExtendedContract();
                if (current != null) {
                    if (activeItems.TryGetValue(current.workOrder, out var taskManagementElement)) {
                        taskManagementElement.UpdateItem(0);
                    }
                    if (current.extraWorkOrder != null && activeItems.TryGetValue(current.extraWorkOrder, out taskManagementElement)) {
                        taskManagementElement.UpdateItem(0);
                        taskManagementElement.UpdateTaskInfo();
                    }
                }

                Attack newFlareup = WhoAndWhere.checkForNewFlareup();
                if (newFlareup == null) {
                    WhoAndWhere.checkForNewExtendedContract();
                } else {
                    FactionValue attacker = newFlareup.attacker;
                    FactionValue defender = newFlareup.defender;
                    string action = newFlareup.type == "Attack" ? "invade" : "raid";
                    string s = SimGameState_ApplySimGameEventResult_Patch.anS(attacker);
                    string toast = $"{attacker.factionDef.CapitalizedName} {action}{s} {defender.factionDef.Name} at {newFlareup.location.Name}";
                    WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(toast));
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
        public static bool Prefix(SimGameState __instance, out string __state) {
            __state = null;
            try {
                __state = __instance.ActiveTravelContract.Override.ID;
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }

            return true;
        }

        public static void Postfix(SimGameState __instance, string __state) {
            try {
                WIIC.modLog.Debug?.Write($"Breadcrumb complete {WIIC.sim.CurSystem.ID} - {__state}");
                if (WIIC.extendedContracts.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    ExtendedContract extendedContract = WIIC.extendedContracts[WIIC.sim.CurSystem.ID];
                    WIIC.modLog.Debug?.Write($"Type: {extendedContract.extendedType.name}, looking for {extendedContract.extendedType.hireContract}{(String.IsNullOrEmpty(extendedContract.extendedType.targetHireContract) ? "" : (" or " + extendedContract.extendedType.targetHireContract))}");
                    if (__state == extendedContract.extendedType.hireContract || __state == extendedContract.extendedType.targetHireContract) {
                        extendedContract.acceptContract(__state);

                        __instance.ClearBreadcrumb();
                        WIIC.sim.RoomManager.ShipRoom.TimePlayPause.UpdateLaunchContractButton();
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

                WhoAndWhere.clearLocationCache();
                if (WIIC.extendedContracts.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    WIIC.extendedContracts[WIIC.sim.CurSystem.ID].onLeaveSystem();
                }

                if (WIIC.extendedContracts.ContainsKey(system.ID)) {
                    WIIC.extendedContracts[system.ID].onEnterSystem();
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
                    WIIC.sim.RoomManager.LeftDrawerWidget.Visible = false;
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "ContractUserMeetsReputation")]
    public static class SimGameState_ContractUserMeetsReputation_Patch {
        public static void Postfix(ref bool __result, Contract c) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract != null && extendedContract.extendedType.blockOtherContracts && c.Name != extendedContract.currentContractName) {
                    WIIC.modLog.Debug?.Write($"Marking as insufficent reputation because blockOtherContracts");
                    __result = false;
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
