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
                Contract contract = __instance.theContract;
                ExtendedContract ec = Utilities.currentExtendedContract();

                WIIC.l.Log($"AAR_ContractObjectivesWidget_FillInObjectives: ec={ec}, currentContractName={ec.currentContractName}, Override.ID={contract.Override.ID}");

                // Player not working here
                if (ec == null || ec.currentContractName != contract.Override.ID) {
                    return;
                }

                Attack attack = ec as Attack;
                if (attack == null) {
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

                if (won) {
                    attack.playerDrops += 2;
                }
                attack.currentContractForceLoss = null;
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
                ExtendedContract ec = Utilities.currentExtendedContract();
                WIIC.l.Log($"AAR_ContractObjectivesWidget_Init: ID={__instance.theContract.Override.ID}, ec={ec}");

                string eventId = null;

                if (ec?.currentContractName == __instance.theContract.Override.ID) {
                    eventId = ec.currentEntry.postContractEvent;
                    WIIC.l.Log($"    ec eventId={eventId}");
                }

                foreach (ActiveCampaign ac in WIIC.activeCampaigns) {
                    if (ac.currentEntry.contract?.postContractEvent != null) {
                        eventId = ac.currentEntry.contract.postContractEvent;
                        WIIC.l.Log($"    ac eventId={eventId}");
                    }
                }

                if (eventId == null) {
                    return;
                }

                contract = __instance.theContract;
                WIIC.l.Log($"    Displaying {eventId} instead of normal contract results.");

                centerPanel = __instance.gameObject.transform.parent.gameObject;
                GameObject representation = centerPanel.transform.parent.gameObject;
                GameObject contractResultsText = representation.FindFirstChildNamed("contractResultsText");
                GameObject nextButton = representation.FindFirstChildNamed("buttonPanel (1)");

                centerPanel.SetActive(false);
                contractResultsText.SetActive(false);
                nextButton.SetActive(false);

                __instance.simState.DataManager.SimGameEventDefs.TryGet(eventId, out SimGameEventDef eventDef);
                if (eventDef.Scope != EventScope.Company && eventDef.Scope != EventScope.StarSystem) {
                    WIIC.l.LogError($"AAR_ContractObjectivesWidget_Init: event {eventId} is not Company or StarSystem scope; Scope {eventDef.Scope} is not supported.");
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
}
