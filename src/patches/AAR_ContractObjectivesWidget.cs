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
                Flareup flareup = Utilities.currentFlareup();
                Contract contract = Traverse.Create(__instance).Field("theContract").GetValue<Contract>();

                if (flareup == null || flareup.currentContractName != contract.Name) {
                    return;
                }

                int bonusMoney = WIIC.settings.flareupMissionBonusPerHalfSkull * contract.Difficulty;
                string loss = Utilities.forcesToString(flareup.currentContractForceLoss);
                string objectiveString = Strings.T("{0} takes {1} point loss in Flareup\n¢{2:n0} bonus", flareup.target.FactionDef.ShortName, loss, bonusMoney);
                WIIC.modLog.Debug?.Write(objectiveString);

                if (flareup.employer == flareup.attacker) {
                    flareup.defenderStrength -= flareup.currentContractForceLoss;
                    WIIC.modLog.Debug?.Write($"defenderStrength -= {flareup.currentContractForceLoss}");
                } else {
                    flareup.attackerStrength -= flareup.currentContractForceLoss;
                    WIIC.modLog.Debug?.Write($"attackerStrength -= {flareup.currentContractForceLoss}");
                }

                MissionObjectiveResult objective = new MissionObjectiveResult(objectiveString, GUID, false, true, ObjectiveStatus.Succeeded, false);
                Traverse.Create(__instance).Method("AddObjective", objective).GetValue();
                WIIC.modLog.Info?.Write($"MoneyResults from ARR: {contract.MoneyResults}, funds: {WIIC.sim.Funds}");

                flareup.currentContractForceLoss = 0;
                flareup.currentContractName = "";
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
