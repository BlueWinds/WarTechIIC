using System;
using Harmony;
using Localize;
using BattleTech;
using BattleTech.Framework;
using BattleTech.UI;
using UnityEngine;
using HBS;
using HBS.Extensions;

namespace WarTechIIC {
    [HarmonyPatch(typeof(AAR_ContractObjectivesWidget), "FillInObjectives")]
    public static class AAR_ContractObjectivesWidget_FillInObjectives {
        private static string GUID = "7facf07a-626d-4a3b-a1ec-b29a35ff1ac0";
        private static void Postfix(AAR_ContractObjectivesWidget __instance) {
            try {
                Attack extendedContract = Utilities.currentExtendedContract() as Attack;
                Contract contract = __instance.theContract;

                if (extendedContract == null || extendedContract.currentContractName != contract.Name) {
                    return;
                }

                Settings s = WIIC.settings;

                int bonus = extendedContract.type == "Attack" ? s.attackBonusPerHalfSkull : s.raidBonusPerHalfSkull;
                int bonusMoney = bonus * contract.Difficulty;
                int bonusSalvage = extendedContract.type == "Attack" ? s.attackBonusSalvage : s.raidBonusSalvage;
                string loss = Utilities.forcesToString(extendedContract.currentContractForceLoss);
                string objectiveString = Strings.T("{0} takes {1} point loss in Flareup\n¢{2:n0} bonus, {3} additional salvage", extendedContract.target.FactionDef.ShortName, loss, bonusMoney, bonusSalvage);
                WIIC.modLog.Debug?.Write(objectiveString);

                bool won = contract.State == Contract.ContractState.Complete;
                if ((extendedContract.employer == extendedContract.attacker && won) || (extendedContract.employer == extendedContract.target && !won)) {
                    extendedContract.defenderStrength -= extendedContract.currentContractForceLoss;
                    WIIC.modLog.Debug?.Write($"defenderStrength -= {extendedContract.currentContractForceLoss}");
                } else {
                    extendedContract.attackerStrength -= extendedContract.currentContractForceLoss;
                    WIIC.modLog.Debug?.Write($"attackerStrength -= {extendedContract.currentContractForceLoss}");
                }

                MissionObjectiveResult objective = new MissionObjectiveResult(objectiveString, GUID, false, true, ObjectiveStatus.Ignored, false);
                __instance.AddObjective(objective);

                WIIC.modLog.Info?.Write($"MoneyResults from ARR: {contract.MoneyResults}, funds: {WIIC.sim.Funds}");

                extendedContract.playerDrops += 1;
                extendedContract.currentContractForceLoss = 0;
                extendedContract.currentContractName = "";
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(AAR_ContractObjectivesWidget), "Init")]
    public static class AAR_ContractObjectivesWidget_Init {
        internal static Contract contract;
        internal static GameObject centerPanel;

        private static void Postfix(AAR_ContractObjectivesWidget __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                contract = __instance.theContract;

                if (extendedContract == null) {
                    WIIC.modLog.Info?.Write($"AAR_ContractObjectivesWidget_Init: No current extended contract. Doing nothing.");
                    return;
                }

                if (extendedContract.currentContractName != contract.Name || String.IsNullOrEmpty(extendedContract.currentEntry)) {
                    WIIC.modLog.Info?.Write($"AAR_ContractObjectivesWidget_Init: {contract.Name} not from current extended contract. Doing nothing.");
                    return;
                }

                string eventId = extendedContract.extendedType.entries[extendedContract.currentEntry].postContractEvent;

                if (String.IsNullOrEmpty(eventId)) {
                    WIIC.modLog.Info?.Write($"AAR_ContractObjectivesWidget_Init: {contract.Name} is from current Extended contract, but entry {extendedContract.currentEntry} has no postContractEvent. Doing nothing.");
                    return;
                }

                WIIC.modLog.Info?.Write($"AAR_ContractObjectivesWidget_Init: {contract.Name} is from current Extended contract. Displaying {eventId} instead of normal contract results.");


                centerPanel = __instance.gameObject.transform.parent.gameObject;
                GameObject representation = centerPanel.transform.parent.gameObject;
                GameObject contractResultsText = representation.FindFirstChildNamed("contractResultsText");
                GameObject nextButton = representation.FindFirstChildNamed("buttonPanel (1)");

                centerPanel.SetActive(false);
                contractResultsText.SetActive(false);
                nextButton.SetActive(false);

                __instance.simState.DataManager.SimGameEventDefs.TryGet(eventId, out SimGameEventDef eventDef);
                if (eventDef.Scope != EventScope.Company && eventDef.Scope != EventScope.StarSystem) {
                    WIIC.modLog.Error?.Write($"AAR_ContractObjectivesWidget_Init: event {eventId} is not Company or StarSystem scope; other scopes are not supported.");
                    return;
                }

                SimGameEventTracker eventTracker = new SimGameEventTracker();
                eventTracker.Init(new[] { EventScope.Company }, 0, 0, SimGameEventDef.SimEventType.NORMAL, WIIC.sim);
                SimGameInterruptManager.EventPopupEntry entry = new SimGameInterruptManager.EventPopupEntry(eventDef, eventDef.Scope, eventTracker);

                SGEventPanel eventPopup = LazySingletonBehavior<UIManager>.Instance.GetOrCreatePopupModule<SGEventPanel>();
                eventPopup.Init(__instance.simState);
                eventPopup.gameObject.transform.SetParent(representation.transform);
                eventPopup.SetEvent(eventDef, eventDef.Scope, eventTracker, entry);
                eventPopup.gameObject.SetActive(true);
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }


    [HarmonyPatch(typeof(SimGameState), "OnEventDismissed")]
    public static class SimGameState_OnEventDismissed_Patch {
        public static void Postfix(SimGameInterruptManager.EventPopupEntry entry) {
            try {
                Contract contract = AAR_ContractObjectivesWidget_Init.contract;

                if (contract == null) {
                    WIIC.modLog.Info?.Write($"SimGameState_OnEventDismissed_Patch: entry dismissed, is not a postContractEvent. Doing nothing.");
                    return;
                }

                WIIC.modLog.Info?.Write($"SimGameState_OnEventDismissed_Patch: entry {contract.Name} dismissed, proceeding with AAR.");

                // Clear out the module we created, so that it doesn't confuse things when the simgame starts up again
                SGEventPanel eventPopup = LazySingletonBehavior<UIManager>.Instance.GetOrCreatePopupModule<SGEventPanel>();
                LazySingletonBehavior<UIManager>.Instance.RemoveModule(eventPopup);

                AAR_ContractResults_Screen screen = AAR_ContractObjectivesWidget_Init.centerPanel.transform.parent.parent.gameObject.GetComponent<AAR_ContractResults_Screen>();

                AAR_ContractObjectivesWidget_Init.centerPanel = null;
                AAR_ContractObjectivesWidget_Init.contract = null;

                screen.missionResultParent.AdvanceAARState();
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}

// BattleTech.UI.SGRoomController_Ship.InitWidgets ->
//   eventPopup = LazySingletonBehavior<UIManager>.Instance.GetOrCreatePopupModule<SGEventPanel>();
//   eventPopup.Init(simState);
//
// "Contract Results" -> hide
//   UIManager/UIRoot/uixPrfScrn_AA_AfterActionReport-Screen_v2(Clone)/Representation/contractResultsText
//
// centerPanel -> hide
//   UIManager/UIRoot/uixPrfScrn_AA_AfterActionReport-Screen_v2(Clone)/Representation/centerPanel
//
// AAR_ContractObjectivesWidget -> gameObject -> parent = centerPanel
//
// centerPanel -> parent -> contractResultsText
//
// centerPanel -> parent = Representation -> add event popup?
