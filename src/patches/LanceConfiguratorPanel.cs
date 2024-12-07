using System;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
   [HarmonyPatch(typeof(LanceConfiguratorPanel), "OnCancelClicked")]
    public static class LanceConfiguratorPanel_OnCancelClicked_Patch {
        public static bool Prefix(SGCmdCenterLanceConfigBG __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                WIIC.l.Log($"LanceConfiguratorPanel.OnCancelClicked. selectedContract: {WIIC.sim.SelectedContract.Name}, extendedContract: {extendedContract}");
                if (extendedContract != null && WIIC.sim.SelectedContract.Name == extendedContract.currentContractName) {
                    return false;
                }
            }
            catch (Exception e) {
                WIIC.l.LogException(e);
            }
            return true;
        }
    }
}
