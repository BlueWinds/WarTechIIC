using System;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    [HarmonyPatch(typeof(Contract), "CompleteContract")]
    public static class Contract_CompleteContract {
        public static void Postfix(Contract __instance, MissionResult result, bool isGoodFaithEffort) {
            try {
                Settings s = WIIC.settings;
                WIIC.modLog.Debug?.Write($"Contract complete: {__instance.Name}, override: {__instance.Override.ID}");

                Flareup flareup = Utilities.currentFlareup();
                if (flareup == null || __instance.Name != flareup.currentContractName) {
                    return;
                }

                int newCost = __instance.MoneyResults +s.flareupMissionBonusPerHalfSkull * __instance.Difficulty;
                Traverse.Create(__instance).Property("MoneyResults").SetValue(newCost);

                WIIC.modLog.Info?.Write($"Flareup contract complete. Employer: {flareup.employer.Name}, Attacker: {flareup.attacker.Name}, force loss: {flareup.currentContractForceLoss}");
                WIIC.modLog.Debug?.Write($"CurMaxContracts: {flareup.location.CurMaxContracts}, CurMaxBreadcrumbs: {flareup.location.CurMaxBreadcrumbs}, InitialContractsFetched: {flareup.location.InitialContractsFetched}, SystemContracts: {flareup.location.SystemContracts.Count}, SystemBreadcrumbs: {flareup.location.SystemBreadcrumbs.Count}");
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
