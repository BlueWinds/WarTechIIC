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
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                Contract contract = __instance.theContract;
                // Player not working here
                if (extendedContract == null || extendedContract.currentContractName != contract.Name) {
                    return;
                }

                Attack attack = extendedContract as Attack;
                if (attack == null) {
                    extendedContract.currentContractName = null;
                    return;
                }

                Settings s = WIIC.settings;
                int bonus = attack.type == "Attack" ? s.attackBonusPerHalfSkull : s.raidBonusPerHalfSkull;
                int bonusMoney = bonus * contract.Difficulty;
                int bonusSalvage = attack.type == "Attack" ? s.attackBonusSalvage : s.raidBonusSalvage;

                int loss = attack.currentContractForceLoss ?? 0;
                string lossString = Utilities.forcesToString(loss);
                string objectiveString = Strings.T("{0} takes {1} point loss in Flareup\n¢{2:n0} bonus, {3} additional salvage", attack.target.FactionDef.CapitalizedShortName, lossString, bonusMoney, bonusSalvage);
                WIIC.l.Log(objectiveString);

                bool won = contract.State == Contract.ContractState.Complete;
                if ((attack.employer == attack.attacker && won) || (attack.employer == attack.target && !won)) {
                    attack.defenderStrength -= loss;
                    WIIC.l.Log($"defenderStrength -= {loss}");
                } else {
                    attack.attackerStrength -= loss;
                    WIIC.l.Log($"attackerStrength -= {loss}");
                }

                MissionObjectiveResult objective = new MissionObjectiveResult(objectiveString, GUID, false, true, ObjectiveStatus.Ignored, false);
                __instance.AddObjective(objective);

                WIIC.l.Log($"MoneyResults from ARR: {contract.MoneyResults}, funds: {WIIC.sim.Funds}");

                if (attack.playerDrops == null) {
                    attack.playerDrops = 0;
                }
                attack.playerDrops += 1;
                attack.currentContractForceLoss = null;
                attack.currentContractName = null;
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(AAR_ContractObjectivesWidget), "Init")]
    public static class AAR_ContractObjectivesWidget_Init {
        internal static Contract contract;
        internal static Transform oldParent;
        internal static GameObject centerPanel;

        private static void Postfix(AAR_ContractObjectivesWidget __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();

                if (extendedContract == null) {
                    WIIC.l.Log($"AAR_ContractObjectivesWidget_Init: No current extended contract. Doing nothing.");
                    return;
                }

                if (extendedContract.currentContractName != __instance.theContract.Name || extendedContract.currentEntry == null) {
                    WIIC.l.Log($"AAR_ContractObjectivesWidget_Init: {__instance.theContract.Name} not from current extended contract. Doing nothing.");
                    return;
                }

                string eventId = extendedContract.currentEntry.postContractEvent;

                if (String.IsNullOrEmpty(eventId)) {
                    WIIC.l.Log($"AAR_ContractObjectivesWidget_Init: {__instance.theContract.Name} is from current Extended contract, but entry {extendedContract.currentEntry} has no postContractEvent. Doing nothing.");
                    return;
                }

                contract = __instance.theContract;
                WIIC.l.Log($"AAR_ContractObjectivesWidget_Init: {contract.Name} is from current Extended contract. Displaying {eventId} instead of normal contract results.");

                centerPanel = __instance.gameObject.transform.parent.gameObject;
                GameObject representation = centerPanel.transform.parent.gameObject;
                GameObject contractResultsText = representation.FindFirstChildNamed("contractResultsText");
                GameObject nextButton = representation.FindFirstChildNamed("buttonPanel (1)");

                centerPanel.SetActive(false);
                contractResultsText.SetActive(false);
                nextButton.SetActive(false);

                __instance.simState.DataManager.SimGameEventDefs.TryGet(eventId, out SimGameEventDef eventDef);
                if (eventDef.Scope != EventScope.Company && eventDef.Scope != EventScope.StarSystem) {
                    WIIC.l.LogError($"AAR_ContractObjectivesWidget_Init: event {eventId} is not Company or StarSystem scope; other scopes are not supported.");
                    return;
                }

                SimGameEventTracker eventTracker = new SimGameEventTracker();
                eventTracker.Init(new[] { EventScope.Company }, 0, 0, SimGameEventDef.SimEventType.NORMAL, WIIC.sim);
                SimGameInterruptManager.EventPopupEntry entry = new SimGameInterruptManager.EventPopupEntry(eventDef, eventDef.Scope, eventTracker);

                SGEventPanel eventPopup = LazySingletonBehavior<UIManager>.Instance.GetOrCreatePopupModule<SGEventPanel>();
                eventPopup.Init(__instance.simState);

                oldParent = eventPopup.gameObject.transform.parent;
                eventPopup.gameObject.transform.SetParent(representation.transform);
                eventPopup.SetEvent(eventDef, eventDef.Scope, eventTracker, entry);
                eventPopup.gameObject.SetActive(true);
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }


    [HarmonyPatch(typeof(SimGameState), "OnEventDismissed")]
    public static class SimGameState_OnEventDismissed_Patch {
        public static void Postfix(SimGameInterruptManager.EventPopupEntry entry) {
            try {
                Contract contract = AAR_ContractObjectivesWidget_Init.contract;

                if (contract == null) {
                    WIIC.l.Log($"SimGameState_OnEventDismissed_Patch: entry dismissed, is not a postContractEvent. Doing nothing.");
                    return;
                }

                WIIC.l.Log($"SimGameState_OnEventDismissed_Patch: entry {contract.Name} dismissed, proceeding with AAR.");

                // Clear out the module we created, so that it doesn't confuse things when the simgame starts up again
                SGEventPanel eventPopup = LazySingletonBehavior<UIManager>.Instance.GetOrCreatePopupModule<SGEventPanel>();
                eventPopup.gameObject.transform.SetParent(AAR_ContractObjectivesWidget_Init.oldParent);
                eventPopup.gameObject.SetActive(false);

                AAR_ContractResults_Screen screen = AAR_ContractObjectivesWidget_Init.centerPanel.transform.parent.parent.gameObject.GetComponent<AAR_ContractResults_Screen>();

                AAR_ContractObjectivesWidget_Init.centerPanel = null;
                AAR_ContractObjectivesWidget_Init.contract = null;
                AAR_ContractObjectivesWidget_Init.oldParent = null;

                screen.missionResultParent.AdvanceAARState();
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
