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

                int newCost = __instance.MoneyResults + s.flareupMissionBonusPerHalfSkull * __instance.Difficulty;
                WIIC.modLog.Info?.Write($"Flareup contract complete, adding bonus. old money: {__instance.MoneyResults}, new: {newCost}, funds: {WIIC.sim.Funds}");

                Traverse.Create(__instance).Property("MoneyResults").SetValue(newCost);
                WIIC.modLog.Info?.Write($"Reading it back after setting: {__instance.MoneyResults}");

                ContractManager.employer = null;
                ContractManager.target = null;
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
