using System;
using Harmony;
using BattleTech;
using BattleTech.Framework;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGContractsWidget), "HandleEscapeKeypress")]
    public static class SGContractsWidget_HandleEscapeKeypress_Patch {
        public static bool Prefix(SGContractsWidget __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract != null && __instance.SelectedContract.Name == extendedContract.currentContractName) {
                    WIIC.l.Log($"Blocking HandleEscapeKeypress. selected: {__instance.SelectedContract.Name}, selectedContract: {__instance.SelectedContract.Name}, currentContractName: {extendedContract.currentContractName}");
                    return false;
                }
            }
            catch (Exception e) {
                WIIC.l.LogException(e);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(SGContractsWidget), "NegotiateContract")]
    public static class SGContractsWidget_NegotiateContract_Patch {
        public static void Postfix(SGContractsWidget __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract != null && __instance.SelectedContract.Name == extendedContract.currentContractName) {
                    WIIC.l.Log($"Hiding widgets for NegotiateContract. selectedContract: {__instance.SelectedContract.Name}, currentContractName: {extendedContract.currentContractName}");

                    __instance.NegotiateTitleBackButton.SetState(ButtonState.Disabled);
                    WIIC.sim.RoomManager.LeftDrawerWidget.gameObject.SetActive(false);
                }
            }
            catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(SGContractsWidget), "GetContractComparePriority")]
    public static class SGContractsWidget_GetContractComparePriority_Patch {
        static bool Prefix(SGContractsWidget __instance, ref int __result, Contract contract) {
            try {
                int difficulty = contract.Override.GetUIDifficulty();
                if (WIIC.sim.ContractUserMeetsReputation(contract)) {
                    if (contract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignRestoration) {
                        __result = 0;
                    } else if (contract.Override.contractDisplayStyle == ContractDisplayStyle.HeavyMetalFlashpointCampaign) {
                        __result = 1;
                    } else if (contract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignStory) {
                        __result = 2;
                    } else if (contract.TargetSystem.Replace("starsystemdef_", "").Equals(WIIC.sim.CurSystem.Name)) {
                        __result = difficulty + 2;
                    } else {
                        __result = difficulty + 12;
                    }
                } else {
                    __result = difficulty + 22;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return false;
        }
    }
}
