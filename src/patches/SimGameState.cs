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
using HBS;
using HBS.Collections;
using UnityEngine.Events;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameState), "Init")]
    public static class SimGameState_InitPatch {
        public static void Postfix(SimGameState __instance) {
            try {
                WIIC.sim = __instance;
                WIIC.l.Log("Clearing Campaings and Extended Contracts for new SimGameState");
                WIIC.extendedContracts.Clear();
                WIIC.activeCampaigns.Clear();
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
                WIIC.l.Log($"Player currently at {__instance.CurSystem.ID}. Loading Campaigns and Extended Contracts.");

                WIIC.extendedContracts.Clear();
                WIIC.activeCampaigns.Clear();
                __instance.CompanyTags.Add("WIIC_enabled");

                foreach (string tag in __instance.CompanyTags.ToList().Where(ActiveCampaign.isSerializedCampaign)) {
                    WIIC.l.Log($"    {tag}");
                    __instance.CompanyTags.Remove(tag);
                    ActiveCampaign ac = ActiveCampaign.Deserialize(tag);
                    WIIC.activeCampaigns.Add(ac);
                }

                foreach (StarSystem system in __instance.StarSystems) {
                    // If one tag fails to load we don't want to break all those that come afterwards.
                    try {
                        foreach (string tag in system.Tags.ToList()) {
                            if (!tag.StartsWith("WIIC")) {
                                continue;
                            }
                            system.Tags.Remove(tag);

                            if (ExtendedContract.isSerializedExtendedContract(tag)) {
                                WIIC.l.Log($"    {tag}");
                                ExtendedContract extendedContract = ExtendedContract.Deserialize(tag);
                                WIIC.extendedContracts[system.ID] = extendedContract;
                            } else if (Utilities.isControlTag(tag)) {
                                WIIC.systemControl[system.ID] = tag;
                            // Ages ago, we chose WIICDoNotAttack as a tag; make sure it doesn't get munged.
                            } else if (!WIIC.settings.systemAggressionByTag.ContainsKey(tag)) {
                                WIIC.l.Log($"    {tag}");
                                WIIC.l.LogError($"    WIIC tag of unknown providence? Removing so it doesn't clutter the starmap, but confused.");
                            }
                        }
                    } catch (Exception e) {
                        WIIC.l.LogException(e);
                    }
                }

                WIIC.l.Log($"Loaded {WIIC.activeCampaigns.Count} campaigns, {WIIC.extendedContracts.Keys.Count} extended contracts and {WIIC.systemControl.Keys.Count} system control tags");
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
                StarSystem system = WIIC.sim.CurSystem;
                ExtendedContract ec = Utilities.currentExtendedContract();
                WIIC.l.Log($"_OnAttachUXComplete: Loaded SimGame. system={system.ID} ec.type={ec?.type}");

                if (!String.IsNullOrEmpty(ec?.currentContractName)) {
                    Contract contract = system.SystemContracts.Find(c => c.Override.ID == ec.currentContractName);

                    if (contract == null) {
                        WIIC.l.LogError($"    EC currentContract {ec.currentContractName} was not found among {system.SystemContracts.Count} contracts in {system.ID}. Something is wrong.");
                        return;
                    }

                    WIIC.l.Log($"    Launching EC currentContract {ec.currentContractName}.");
                    ec.launchContract(ec.currentEntry, contract);
                    return;
                }

                foreach (ActiveCampaign ac in WIIC.activeCampaigns.ToArray()) {
                    if (ac.currentEntry.contract?.immediate == true && WIIC.sim.GlobalContracts.Exists(c => c.Override.ID == ac.currentEntry.contract.id)) {
                        WIIC.l.Log($"    ActiveCampaign loaded and we're pre-drop in an immediate contract.");
                        Utilities.sendToCommandCenter(true);
                        return;
                    }

                    if (
                        (ac.currentEntry.contract != null && !WIIC.sim.GlobalContracts.Exists(c => c.Override.ID == ac.currentEntry.contract.id))
                        || ac.currentEntry.@event != null
                    ) {
                        WIIC.l.Log($"    ActiveCampaign loaded and we're in a post-contract or post-event save; running entryComplete() to trigger next step.");
                        ac.entryComplete();
                        return;
                    }
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "ResolveCompleteContract")]
    public static class SimGameState_ResolveCompleteContract_Patch {
        public static void Prefix(SimGameState __instance, out string __state) {
            __state = __instance.CompletedContract.Override.ID;
        }

        public static void Postfix(SimGameState __instance, string __state) {
            try {
                WIIC.l.Log($"ResolveCompleteContract: CompletedContract={__state}");

                // Re-enable the left drawer, in case we've come in from an `immediate` campaign mission.
                WIIC.sim.RoomManager.LeftDrawerWidget.gameObject.SetActive(true);

                foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.currentEntry.contract?.id == __state).ToArray()) {
                    WIIC.l.Log($"    ActiveCampaign contract; running entryComplete().");
                    ac.entryComplete();
                    return;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
    public static class SimGameState_OnDayPassed_Patch {
        private static void Postfix() {
            try {
                Dictionary<WorkOrderEntry, TaskManagementElement> activeItems = WIIC.sim.RoomManager.timelineWidget.ActiveItems;

                Utilities.slowDownFloaties();
                ColourfulFlashPoints.Main.clearMapMarkers();

                // ToList is used to make a copy because we may need to remove elements as we're iterating
                foreach (ExtendedContract extendedContract in WIIC.extendedContracts.Values.ToList()) {
                    bool finished = extendedContract.passDay();
                    if (finished) {
                        extendedContract.removeParticipationContracts();
                        WIIC.extendedContracts.Remove(extendedContract.locationID);
                        Utilities.cleanupSystem(extendedContract.location);
                    }
                }

                ExtendedContract current = Utilities.currentExtendedContract();
                TaskManagementElement taskManagementElement;
                if (current?.workOrder != null && activeItems.TryGetValue(current.workOrder, out taskManagementElement)) {
                    taskManagementElement.UpdateItem(0);
                }

                if (current?.extraWorkOrder != null && activeItems.TryGetValue(current.extraWorkOrder, out taskManagementElement)) {
                    taskManagementElement.UpdateItem(0);
                    taskManagementElement.UpdateTaskInfo();
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

                // If an active campaign is currently counting down towards a contract,
                // pause time passing when the counter reaches 0.
                foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.entryCountdown != null).ToArray()) {
                    ac.entryCountdown--;
                    if (ac.workOrder != null && activeItems.TryGetValue(ac.workOrder, out taskManagementElement)) {
                        taskManagementElement.UpdateItem(0);
                        taskManagementElement.UpdateTaskInfo();
                    }

                    if (ac.entryCountdown <= 0) {
                        WIIC.sim.SetTimeMoving(false);
                        if (ac.currentEntry.wait != null) {
                            ac.entryComplete();
                        }
                    }
                }
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
                WIIC.l.Log($"CompleteLanceConfigurationPrep. selectedContract: {WIIC.sim.SelectedContract.Override.ID}, currentContractName: {(extendedContract != null ? extendedContract.currentContractName : null)}");
                if (extendedContract != null && WIIC.sim.SelectedContract.Override.ID == extendedContract.currentContractName) {
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
                if (Utilities.shouldBlockContract(c)) {
                    __result = false;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "GetFlashpointInSystem")]
    public static class SimGameState_GetFlashpointInSystem_Patch {
        public static bool Prefix(ref Flashpoint __result, StarSystem theSystem) {
            try {
                foreach (ActiveCampaign ac in WIIC.activeCampaigns) {
                    WIIC.l.Log($"SimGameState_GetFlashpointInSystem_Patch. theSystem.ID={theSystem.ID} ac={ac} currentFakeFlashpoint={ac.currentFakeFlashpoint}");

                    if (ac.currentEntry.fakeFlashpoint?.at == theSystem.ID) {
                        __result = ac.currentFakeFlashpoint;
                        return false;
                    }
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(SGFlashpointEmployerInfoPanel), "RefreshWidget")]
    public static class SG_SGFlashpointEmployerInfoPanel_RefreshWidget_Patch {
        public static void Prefix(SGFlashpointEmployerInfoPanel __instance) {
            try {
                WIIC.l.Log($"SG_SGFlashpointEmployerInfoPanel_RefreshWidget_Patch - EmployerFaction={__instance.EmployerFaction}");
                WIIC.l.Log($"    DoesGainReputation={__instance.EmployerFaction?.DoesGainReputation}");
                WIIC.l.Log($"    EmployerFactionDef={__instance.EmployerFactionDef}");
                WIIC.l.Log($"    Name={__instance.EmployerFactionDef?.Name}");
                WIIC.l.Log($"    HasFactionDef={__instance.HasFactionDef}");
                WIIC.l.Log($"    theFlashpoint={__instance.theFlashpoint}");
                WIIC.l.Log($"    theFlashpoint.EmployerValue={__instance.theFlashpoint?.EmployerValue}");
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SimGameState), "SetActiveFlashpoint")]
    public static class SimGameState_SetActiveFlashpoint_Patch {
        public static bool Prefix(Flashpoint fp, SimGameState __instance) {
            try {
                WIIC.l.Log($"SimGameState_SetActiveFlashpoint_Patch begin");
                if (fp is null || __instance is null) {
                    WIIC.l.Log($"SimGameState_SetActiveFlashpoint_Patch - null exit. fp is null={fp is null}, __instance is null={__instance is null}");
                    return true;
                }
                WIIC.l.Log($"SimGameState_SetActiveFlashpoint_Patch. fp={fp} fp.GUID={fp?.GUID}, CurSystem.ID={__instance?.CurSystem?.ID}");
                foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.currentFakeFlashpoint == fp).ToArray()) {
                    WIIC.l.Log($"    ac={ac}");
                    ac.entryComplete();
                    __instance.RoomManager.NavRoom.RefreshData();
                    return false;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnEventDismissed")]
    public static class SimGameState_OnEventDismissed_Patch {
        public static void Postfix(SimGameInterruptManager.EventPopupEntry entry) {
            try {
                SimGameEventDef eventDef = entry.parameters[0] as SimGameEventDef;
                string id = eventDef?.Description?.Id;
                WIIC.l.Log($"SimGameState_OnEventDismissed_Patch: {id} dismissed.");

                if (postContractEvent()) { return; }
                if (campaignEntryEvent(id)) { return; }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }

        public static bool postContractEvent() {
            Contract contract = AAR_ContractObjectivesWidget_Init.contract;
            if (contract == null) {
                WIIC.l.Log($"    Not a postContractEvent.");
                return false;
            }

            WIIC.l.Log($"    postContractEvent {contract.Name} dismissed, proceeding with AAR.");

            // Clear out the module we created, so that it doesn't confuse things when the simgame starts up again
            SGEventPanel eventPopup = LazySingletonBehavior<UIManager>.Instance.GetOrCreatePopupModule<SGEventPanel>();
            eventPopup.gameObject.transform.SetParent(AAR_ContractObjectivesWidget_Init.oldParent);
            eventPopup.gameObject.SetActive(false);

            AAR_ContractResults_Screen screen = AAR_ContractObjectivesWidget_Init.centerPanel.transform.parent.parent.gameObject.GetComponent<AAR_ContractResults_Screen>();

            AAR_ContractObjectivesWidget_Init.centerPanel = null;
            AAR_ContractObjectivesWidget_Init.contract = null;
            AAR_ContractObjectivesWidget_Init.oldParent = null;

            screen.missionResultParent.AdvanceAARState();
            return true;
        }

        public static bool campaignEntryEvent(string eventId) {
            foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.currentEntry.@event?.id == eventId).ToArray()) {
                ac.entryComplete();
                return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(SimGameState), "OnVideoComplete")]
    public static class SimGameState_OnVideoComplete_Patch {
        public static void Postfix(string videoName) {
            try {
                WIIC.l.Log($"SimGameState_OnVideoComplete_Patch: video={videoName}.");
                foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => $"Video/{ac.currentEntry.video}" == videoName).ToArray()) {
                    WIIC.l.Log($"SimGameState_OnVideoComplete_Patch: node={ac.node} nodeIndex={ac.nodeIndex}");
                    ac.entryComplete();
                    return;
                }

                WIIC.l.Log($"    Not a campaign video.");
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
