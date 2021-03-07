using System;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameInterruptManager), "CouldDisplay")]
    public static class SimGameInterruptManager_CouldDisplay_Patch {
        public static void Postfix(ref bool __result) {
            try {
                Flareup flareup = Utilities.currentFlareup();
                if (flareup != null && flareup.currentContractName != "") {
                    __result = false;
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
