using System;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    [HarmonyPatch(typeof(Contract), "CompleteContract")]
    public static class Contract_CompleteContract_Patch {
        public static void Postfix(Contract __instance, MissionResult result, bool isGoodFaithEffort) {
            try {
                Settings s = WIIC.settings;
                WIIC.modLog.Debug?.Write($"Contract complete: {__instance.Name}, override: {__instance.Override.ID}");

                Flareup flareup = Utilities.currentFlareup();
                if (flareup == null || __instance.Name != flareup.currentContractName) {
                    return;
                }

                int bonus = flareup.type == "Attack" ? s.attackBonusPerHalfSkull : s.raidBonusPerHalfSkull;
                int newCost = __instance.MoneyResults + bonus * __instance.Difficulty;
                WIIC.modLog.Info?.Write($"{flareup.type} contract complete, adding bonus. old money: {__instance.MoneyResults}, new: {newCost}, funds: {WIIC.sim.Funds}");

                Traverse.Create(__instance).Property("MoneyResults").SetValue(newCost);
                WIIC.modLog.Info?.Write($"Reading it back after setting: {__instance.MoneyResults}");

                bonus = flareup.type == "Attack" ? s.attackBonusSalvage : s.raidBonusSalvage;
                WIIC.modLog.Info?.Write($"Addng salvage. FinalSalvageCount: {__instance.FinalSalvageCount}, bonus: {bonus}");
                Traverse.Create(__instance).Property("FinalSalvageCount").SetValue(__instance.FinalSalvageCount + bonus);
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
