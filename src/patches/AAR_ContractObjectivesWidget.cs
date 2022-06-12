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
                Flareup flareup = Utilities.currentExtendedContract() as Flareup;
                Contract contract = Traverse.Create(__instance).Field("theContract").GetValue<Contract>();

                if (flareup == null || flareup.currentContractName != contract.Name) {
                    return;
                }

                Settings s = WIIC.settings;

                int bonus = flareup.type == "Attack" ? s.attackBonusPerHalfSkull : s.raidBonusPerHalfSkull;
                int bonusMoney = bonus * contract.Difficulty;
                int bonusSalvage = flareup.type == "Attack" ? s.attackBonusSalvage : s.raidBonusSalvage;
                string loss = Utilities.forcesToString(flareup.currentContractForceLoss);
                string objectiveString = Strings.T("{0} takes {1} point loss in Flareup\n¢{2:n0} bonus, {3} additional salvage", flareup.target.FactionDef.ShortName, loss, bonusMoney, bonusSalvage);
                WIIC.modLog.Debug?.Write(objectiveString);

                bool won = contract.State == Contract.ContractState.Complete;
                if ((flareup.employer == flareup.attacker && won) || (flareup.employer == flareup.target && !won)) {
                    flareup.defenderStrength -= flareup.currentContractForceLoss;
                    WIIC.modLog.Debug?.Write($"defenderStrength -= {flareup.currentContractForceLoss}");
                } else {
                    flareup.attackerStrength -= flareup.currentContractForceLoss;
                    WIIC.modLog.Debug?.Write($"attackerStrength -= {flareup.currentContractForceLoss}");
                }

                MissionObjectiveResult objective = new MissionObjectiveResult(objectiveString, GUID, false, true, ObjectiveStatus.Ignored, false);
                Traverse.Create(__instance).Method("AddObjective", objective).GetValue();

                WIIC.modLog.Info?.Write($"MoneyResults from ARR: {contract.MoneyResults}, funds: {WIIC.sim.Funds}");

                flareup.playerDrops += 1;
                flareup.currentContractForceLoss = 0;
                flareup.currentContractName = "";
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
