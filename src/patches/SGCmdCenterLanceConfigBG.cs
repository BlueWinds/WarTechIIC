using System;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGCmdCenterLanceConfigBG), "HandleEscapeKeypress")]
    public static class SGCmdCenterLanceConfigBG_HandleEscapeKeypress_Patch {
        public static bool Prefix(SGCmdCenterLanceConfigBG __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                WIIC.l.Log($"SGCmdCenterLanceConfigBG.HandleEscapeKeypress. selectedContract: {WIIC.sim.SelectedContract.Name}, extendedContract: {extendedContract}");
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
