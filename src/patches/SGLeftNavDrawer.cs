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

                if (WIIC.sim.SelectedContract.Override.ID == extendedContract?.currentContractName && !isCollapsed) {
                    WIIC.l.Log($"SGLeftNavDrawer.SetCollapsed -> blocking because selectedContract");
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
