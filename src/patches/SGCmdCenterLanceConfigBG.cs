using System;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGCmdCenterLanceConfigBG), "ShowLanceConfiguratorScreen")]
    public static class SGCmdCenterLanceConfigBG_ShowLanceConfiguratorScreen_Patch {
        public static void Postfix(SGCmdCenterLanceConfigBG __instance) {
            try {
                Flareup flareup = Utilities.currentFlareup();
                if (flareup == null || WIIC.sim.SelectedContract == null) {
                    return;
                }

                WIIC.modLog.Debug?.Write($"SGCmdCenterLanceConfigBG.ShowLanceConfiguratorScreen. selectedContract: {WIIC.sim.SelectedContract.Name}, flareup: {flareup}");
                if (WIIC.sim.SelectedContract.Name == flareup.currentContractName) {
                    WIIC.modLog.Debug?.Write($"Hiding nav drawer from ShowLanceConfiguratorScreen.");
                    SGLeftNavDrawer leftDrawer = (SGLeftNavDrawer)AccessTools.Field(typeof(SGRoomManager), "LeftDrawerWidget").GetValue(WIIC.sim.RoomManager);
                    leftDrawer.gameObject.SetActive(false);
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }

    [HarmonyPatch(typeof(SGCmdCenterLanceConfigBG), "HandleEscapeKeypress")]
    public static class SGCmdCenterLanceConfigBG_HandleEscapeKeypress_Patch {
        public static bool Prefix(SGCmdCenterLanceConfigBG __instance) {
            try {
                Flareup flareup = Utilities.currentFlareup();
                WIIC.modLog.Debug?.Write($"SGCmdCenterLanceConfigBG.HandleEscapeKeypress. selectedContract: {WIIC.sim.SelectedContract.Name}, flareup: {flareup}");
                if (flareup != null && WIIC.sim.SelectedContract.Name == flareup.currentContractName) {
                    return false;
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
            return true;
        }
    }
}
