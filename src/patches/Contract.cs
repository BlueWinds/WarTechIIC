using System;
using System.Linq;
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
                WIIC.l.Log($"Contract complete: {__instance.Name}, override: {__instance.Override.ID}");

                ExtendedContract current = Utilities.currentExtendedContract() as Attack;
                if (current == null) {
                    return;
                }

                if (__instance.Name != current.currentContractName) {
                    current.currentContractName = null;
                    return;
                }

                int bonus = current.type == "Attack" ? s.attackBonusPerHalfSkull : s.raidBonusPerHalfSkull;
                WIIC.l.Log($"{current.type} contract complete, adding bonus {bonus} * {__instance.Difficulty}");

                __instance.MoneyResults += bonus * __instance.Difficulty;
                WIIC.l.Log($"Reading it back after setting: {__instance.MoneyResults}");

                bonus = current.type == "Attack" ? s.attackBonusSalvage : s.raidBonusSalvage;
                WIIC.l.Log($"Adding salvage. FinalSalvageCount: {__instance.FinalSalvageCount}, bonus: {bonus}");
                __instance.FinalSalvageCount += bonus;

            }
            catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(Contract), "OnDayPassed")]
    public static class Contract_OnDayPassed_Patch {
        public static void Postfix(Contract __instance, ref bool __result) {
            try {
                if (__instance.UsingExpiration) {
                    foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => __instance.Override.ID == ac.currentEntry.contract.id)) {
                        WIIC.l.Log($"Contract_OnDayPassed_Patch - ensurring {__instance.Override.ID} is not removed because it belongs to {ac.campaign}");
                        __result = false;
                    }
                }
            }
            catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
