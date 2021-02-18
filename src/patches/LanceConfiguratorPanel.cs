using System;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
   [HarmonyPatch(typeof(LanceConfiguratorPanel), "OnCancelClicked")]
    public static class LanceConfiguratorPanel_OnCancelClicked_Patch {
        public static bool Prefix(SGCmdCenterLanceConfigBG __instance) {
            try {
                Flareup flareup = Utilities.currentFlareup();
                WIIC.modLog.Debug?.Write($"LanceConfiguratorPanel.OnCancelClicked. selectedContract: {WIIC.sim.SelectedContract.Name}, flareup: {flareup}");
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
