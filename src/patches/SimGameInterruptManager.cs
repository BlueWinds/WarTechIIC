using System;
using Harmony;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameInterruptManager), "IsOpen", MethodType.Getter)]
    public static class SimGameInterruptManager_IsOpen_Patch {
        public static void Postfix(ref bool __result) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                if (extendedContract != null && extendedContract.currentContractName != "") {
                    __result = true;
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
