using System;
using Harmony;
using BattleTech;
using BattleTech.Framework;

namespace WarTechIIC {
    [HarmonyPatch(typeof(Contract))]
    [HarmonyPatch(MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof(string), typeof(string), typeof(string), typeof(ContractTypeValue), typeof(GameInstance), typeof(ContractOverride), typeof(GameContext), typeof(bool), typeof(int), typeof(int), typeof(int?) })]
    public static class Contract_Constructor_Patch {
        public static void Postfix(Contract __instance) {
            // Make contract values available under RES_OBJ
            // eg: "Contract name: {RES_OBJ.Name} is a {RES_OBJ.ContractTypeValue.FriendlyName} contract"
            __instance.GameContext.SetObject(GameContextObjectTagEnum.ResultObject, __instance);
        }
    }

    [HarmonyPatch(typeof(Contract), "CompleteContract")]
    public static class Contract_CompleteContract_Patch {
        public static void Postfix(Contract __instance, MissionResult result, bool isGoodFaithEffort) {
            try {
                Settings s = WIIC.settings;
                WIIC.modLog.Debug?.Write($"Contract complete: {__instance.Name}, override: {__instance.Override.ID}");

                Attack flareup = Utilities.currentExtendedContract() as Attack;
                if (flareup == null || __instance.Name != flareup.currentContractName) {
                    return;
                }

                int bonus = flareup.type == "Attack" ? s.attackBonusPerHalfSkull : s.raidBonusPerHalfSkull;
                int newCost = __instance.MoneyResults + bonus * __instance.Difficulty;
                WIIC.modLog.Info?.Write($"{flareup.type} contract complete, adding bonus. old money: {__instance.MoneyResults}, new: {newCost}, funds: {WIIC.sim.Funds}");

                Traverse.Create(__instance).Property("MoneyResults").SetValue(newCost);
                WIIC.modLog.Info?.Write($"Reading it back after setting: {__instance.MoneyResults}");

                bonus = flareup.type == "Attack" ? s.attackBonusSalvage : s.raidBonusSalvage;
                WIIC.modLog.Info?.Write($"Adding salvage. FinalSalvageCount: {__instance.FinalSalvageCount}, bonus: {bonus}");
                Traverse.Create(__instance).Property("FinalSalvageCount").SetValue(__instance.FinalSalvageCount + bonus);
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
