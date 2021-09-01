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
                Flareup flareup = Utilities.currentFlareup();
                if (flareup != null && __instance.SelectedContract.Name == flareup.currentContractName) {
                    WIIC.modLog.Debug?.Write($"Blocking HandleEscapeKeypress. selected: {__instance.SelectedContract.Name}, selectedContract: {__instance.SelectedContract.Name}, flareupContract: {flareup.currentContractName}");
                    return false;
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(SGContractsWidget), "NegotiateContract")]
    public static class SGContractsWidget_NegotiateContract_Patch {
        public static void Postfix(SGContractsWidget __instance) {
            try {
                Flareup flareup = Utilities.currentFlareup();
                if (flareup != null && __instance.SelectedContract.Name == flareup.currentContractName) {
                    WIIC.modLog.Debug?.Write($"Hiding widgets for NegotiateContract. selectedContract: {__instance.SelectedContract.Name}, flareupContract: {flareup.currentContractName}");

                    HBSButton backButton = (HBSButton)AccessTools.Field(typeof(SGContractsWidget), "NegotiateTitleBackButton").GetValue(__instance);
                    backButton.SetState(ButtonState.Disabled);

                    SGLeftNavDrawer leftDrawer = (SGLeftNavDrawer)AccessTools.Field(typeof(SGRoomManager), "LeftDrawerWidget").GetValue(WIIC.sim.RoomManager);
                    leftDrawer.gameObject.SetActive(false);
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
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
                    } else if (contract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignStory) {
                        __result = 1;
                    } else if (contract.TargetSystem.Replace("starsystemdef_", "").Equals(WIIC.sim.CurSystem.Name)) {
                        __result = difficulty + 1;
                    } else {
                        __result = difficulty + 11;
                    }
                } else {
                    __result = difficulty + 21;
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }

            return false;
        }
    }
}
