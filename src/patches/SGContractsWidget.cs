using System;
using Harmony;
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
                    backButton.SetState(ButtonState.Unavailable);

                    SGLeftNavDrawer leftDrawer = (SGLeftNavDrawer)AccessTools.Field(typeof(SGRoomManager), "LeftDrawerWidget").GetValue(WIIC.sim.RoomManager);
                    leftDrawer.gameObject.SetActive(false);
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
