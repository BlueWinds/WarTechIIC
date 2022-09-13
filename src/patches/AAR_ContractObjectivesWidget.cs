using System;
using Harmony;
using Localize;
using BattleTech;
using BattleTech.Framework;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(AAR_ContractObjectivesWidget), "FillInObjectives")]
    public static class AAR_ContractObjectivesWidget_FillInObjectives {
        private static string GUID = "7facf07a-626d-4a3b-a1ec-b29a35ff1ac0";
        private static void Postfix(AAR_ContractObjectivesWidget __instance) {
            try {
                Attack extendedContract = Utilities.currentExtendedContract() as Attack;
                Contract contract = Traverse.Create(__instance).Field("theContract").GetValue<Contract>();

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
                Traverse.Create(__instance).Method("AddObjective", objective).GetValue();

                WIIC.modLog.Info?.Write($"MoneyResults from ARR: {contract.MoneyResults}, funds: {WIIC.sim.Funds}");

                extendedContract.playerDrops += 1;
                extendedContract.currentContractForceLoss = 0;
                extendedContract.currentContractName = "";
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
