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

                string moneyString = "";

                if (WIIC.settings.flareupMissionBonusPerHalfSkull > 0) {
                    int bonusMoney = WIIC.settings.flareupMissionBonusPerHalfSkull * contract.Difficulty;

                    moneyString = string.Format("\n¢{0:n0} bonus", bonusMoney);
                }

                string objectiveString = Strings.T("{0} takes {1} point loss in Flareup{2}", flareup.target.FactionDef.ShortName, flareup.currentContractForceLoss, moneyString);
                WIIC.modLog.Debug?.Write(objectiveString);

                MissionObjectiveResult objective = new MissionObjectiveResult(objectiveString, GUID, false, true, ObjectiveStatus.Succeeded, false);
                Traverse.Create(__instance).Method("AddObjective", objective).GetValue();

                flareup.currentContractForceLoss = 0;
                flareup.currentContractName = "";
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
