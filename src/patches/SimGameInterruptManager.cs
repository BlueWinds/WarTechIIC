using System;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameInterruptManager), "IsOpen", MethodType.Getter)]
    public static class SimGameInterruptManager_IsOpen_Patch {
        public static void Postfix(ref bool __result) {
            try {
                Flareup flareup = Utilities.currentFlareup();
                if (flareup != null && flareup.currentContractName != "") {
                    __result = true;
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
