using System;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    [HarmonyPatch(typeof(Contract), "CompleteContract")]
    public static class Contract_CompleteContract {
        public static void Postfix(Contract __instance, MissionResult result, bool isGoodFaithEffort) {
            try {
                Settings s = WIIC.settings;

                Flareup flareup = Utilities.currentFlareup();
                if (flareup == null || __instance.Name != flareup.currentContractName) {
                    return;
                }

                int newCost = __instance.MoneyResults +s.flareupMissionBonusPerHalfSkull * __instance.Difficulty;
                Traverse.Create(__instance).Property("MoneyResults").SetValue(newCost);

                if (flareup.employer == flareup.attacker) {
                    flareup.defenderStrength -= flareup.currentContractForceLoss;
                } else {
                    flareup.attackerStrength -= flareup.currentContractForceLoss;
                }

                ContractManager.employer = null;
                ContractManager.target = null;
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
