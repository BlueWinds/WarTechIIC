using System;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGLeftNavDrawer), "SetCollapsed")]
    public static class SGLeftNavDrawer_SetCollapsed_Patch {
        public static bool Prefix(ref bool isCollapsed) {
            try {
                if (WIIC.sim.SelectedContract == null) {
                    return true;
                }

                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract == null) {
                    return true;
                }

                if (WIIC.sim.SelectedContract.Name == extendedContract.currentContractName && !isCollapsed) {
                    WIIC.modLog.Debug?.Write($"SGLeftNavDrawer.SetCollapsed -> blocking because selectedContract");
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
