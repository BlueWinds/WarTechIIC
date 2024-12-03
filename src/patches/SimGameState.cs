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
                WIIC.l.Log("Clearing Extended Contracts for new SimGameState");
                WIIC.extendedContracts.Clear();
                WIIC.systemControl.Clear();
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "Rehydrate")]
    public static class SimGameState_RehydratePatch {
        public static void Postfix(GameInstanceSave gameInstanceSave, SimGameState __instance) {
            try {
                WIIC.sim = __instance;
                WIIC.l.Log($"Player currently at {__instance.CurSystem.ID}. Loading Extended Contracts.");

                WIIC.extendedContracts.Clear();
                __instance.CompanyTags.Add("WIIC_enabled");

                foreach (StarSystem system in __instance.StarSystems) {
                    // If one tag fails to load we don't want to break all those that come afterwards.
                    try {
                        string tag = system.Tags.ToList().Find(ExtendedContract.isSerializedExtendedContract);
                        if (tag != null) {
                            system.Tags.Remove(tag);
                            WIIC.l.Log($"    {tag}");
                            ExtendedContract extendedContract = ExtendedContract.Deserialize(tag);
                            WIIC.extendedContracts[system.ID] = extendedContract;
                        }

                        tag = system.Tags.ToList().Find(Utilities.isControlTag);
                        if (tag != null) {
                            system.Tags.Remove(tag);
                            WIIC.systemControl[system.ID] = tag;
                        }
                    } catch (Exception e) {
                        WIIC.l.LogException(e);
                    }
                }

                WIIC.l.Log($"Loaded {WIIC.extendedContracts.Keys.Count} extended contracts and {WIIC.systemControl.Keys.Count} system control tags");
                Utilities.redrawMap();
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "_OnAttachUXComplete")]
    public static class SimGameState_OnAttachUXComplete_Patch {
        public static void Postfix(SimGameState __instance) {
            try {
                ExtendedContract current = Utilities.currentExtendedContract();
                if (current == null || String.IsNullOrEmpty(current.currentContractName)) {
                    WIIC.l.Log($"_OnAttachUXComplete: No current contract, or current contract not mid-drop. current={current}");
                    return;
                }

                StarSystem system = WIIC.sim.CurSystem;
                Contract contract = system.SystemContracts.Find(c => c.Name == current.currentContractName);
                if (contract == null) {
                    WIIC.l.LogError($"_OnAttachUXComplete: currentContract {current.currentContractName} was not found among {system.SystemContracts.Count} contracts in {system.Name}");
                }

                WIIC.l.Log($"_OnAttachUXComplete: Loaded game, launching currentContract {current.currentContractName}.");
                current.launchContract(current.currentEntry, contract);
            } catch (Exception e) {
                WIIC.l.LogException(e);
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
                    string toast = $"{attacker.factionDef.CapitalizedName} {action}{s} {defender.FactionDef.Name} at {newFlareup.location.Name}";
                    WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(toast));
                }

                Utilities.redrawMap();

                WIIC.sim.RoomManager.RefreshTimeline(false);
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnNewQuarterBegin")]
    public static class SimGameStateOnNewQuarterBeginPatch {
        private static void Postfix() {
            try {
                // If we're in the middle of initializing a new career no need to do anything.
                if (WIIC.sim != null) {
                    WIIC.l.Log($"Refreshing contracts in current system at start of month");
                    WIIC.sim.CurSystem.ResetContracts();
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "FinishCompleteBreadcrumbProcess")]
    public static class SimGameState_FinishCompleteBreadcrumbProcessPatch {
        public static void Prefix(SimGameState __instance, out string __state) {
            __state = null;
            try {
                __state = __instance.ActiveTravelContract.Override.ID;
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }

        public static void Postfix(SimGameState __instance, string __state) {
            try {
                if (String.IsNullOrEmpty(__state)) {
                    WIIC.l.Log($"Breadcrumb complete, not a travel contract.");
                    return;
                }

                WIIC.l.Log($"Breadcrumb complete {WIIC.sim.CurSystem.ID} - {__state}. GetType={__state.GetType()}");
                if (WIIC.extendedContracts.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    ExtendedContract extendedContract = WIIC.extendedContracts[WIIC.sim.CurSystem.ID];
                    WIIC.l.Log($"Type: {extendedContract.extendedType.name}, looking for {extendedContract.extendedType.hireContract}{(String.IsNullOrEmpty(extendedContract.extendedType.targetHireContract) ? "" : (" or " + extendedContract.extendedType.targetHireContract))}");
                    if (__state == extendedContract.extendedType.hireContract || __state == extendedContract.extendedType.targetHireContract) {
                        extendedContract.acceptContract(__state);

                        __instance.ClearBreadcrumb();
                        WIIC.sim.RoomManager.ShipRoom.TimePlayPause.UpdateLaunchContractButton();
                    }
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "SetCurrentSystem")]
    public static class SimGameState_SetCurrentSystem_Patch {
        [HarmonyPrefix]
        public static void SetCurrentSystemPrefix(StarSystem system, bool force = false, bool timeSkip = false) {
            try {
                WIIC.l.Log($"Entering system {system.ID} from {WIIC.sim.CurSystem.ID}");

                WhoAndWhere.clearLocationCache();
                if (WIIC.extendedContracts.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    WIIC.extendedContracts[WIIC.sim.CurSystem.ID].onLeaveSystem();
                }

                if (WIIC.extendedContracts.ContainsKey(system.ID)) {
                    WIIC.extendedContracts[system.ID].onEnterSystem();
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "CompleteLanceConfigurationPrep")]
    public static class SimGameState_CompleteLanceConfigurationPrep_Patch {
        public static void Postfix() {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                WIIC.l.Log($"CompleteLanceConfigurationPrep. selectedContract: {WIIC.sim.SelectedContract.Name}, currentContractName: {(extendedContract != null ? extendedContract.currentContractName : null)}");
                if (extendedContract != null && WIIC.sim.SelectedContract.Name == extendedContract.currentContractName) {
                    WIIC.l.Log($"Hiding nav drawer from CompleteLanceConfigurationPrep.");
                    WIIC.sim.RoomManager.LeftDrawerWidget.Visible = false;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "ContractUserMeetsReputation")]
    public static class SimGameState_ContractUserMeetsReputation_Patch {
        public static void Postfix(ref bool __result, Contract c) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract != null && extendedContract.extendedType.blockOtherContracts && c.Name != extendedContract.currentContractName) {
                    WIIC.l.Log($"Marking as insufficent reputation because blockOtherContracts");
                    __result = false;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SGContractsWidget), "OnContractAccepted")]
    [HarmonyPatch(new Type[] { typeof(bool) })]
    public static class SGContractsWidget_OnContractAccepted_Patch {
        public static bool Prefix(SGContractsWidget __instance, bool skipCombat) {
            try {
                WIIC.l.Log($"OnContractAccepted Prefix - Blocking CU's prefix, because it is breaking things for some users? If you understand the purpose of CustomUnits.SGContractsWidget_OnContractAccepted, reach out to BloodyDoves or BlueWinds.");
                originalImplementation(__instance, skipCombat);

                return false;
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }
        public static void originalImplementation(SGContractsWidget __instance, bool skipCombat) {
            if (__instance.Sim.HasTravelContract) {
                if (__instance.Sim.IsContractOurArrivedAtTravelContract(__instance.SelectedContract)) {
                    __instance.Sim.PrepareBreadcrumb(__instance.Sim.ActiveTravelContract);
                    return;
                }

                __instance.Sim.CreateBreakContractWarning(delegate {
                    __instance.OnTravelContractWarningAccepted(skipCombat);
                }, __instance.OnTravelContractWarningCancelled);

                __instance.uiManager.SetFaderColor(__instance.uiManager.UILookAndColorConstants.PopupBackfill, UIManagerFader.FadePosition.FadeInBack, UIManagerRootType.PopupRoot);
                return;
            }
            float num = 0f;
            float num2 = 0f;
            if (__instance.SelectedContract.Override != null && !__instance.SelectedContract.CanNegotiate) {
                num = __instance.SelectedContract.Override.negotiatedSalary;
                num2 = __instance.SelectedContract.Override.negotiatedSalvage;
            } else {
                num = __instance.NegPaymentSlider.Value / __instance.NegPaymentSlider.ValueMax;
                num2 = __instance.NegSalvageSlider.Value / __instance.NegSalvageSlider.ValueMax;
            }
            __instance.SelectedContract.SetNegotiatedValues(num, num2);
            __instance.contractAccepted(skipCombat);
        }
    }
}
