using System;
using Harmony;
using Localize;
using BattleTech;
using BattleTech.Framework;
using BattleTech.UI;
using UnityEngine;
using HBS;
using HBS.Extensions;

namespace WarTechIIC {
    [HarmonyPatch(typeof(AAR_SalvageScreen), "OnCompleted")]
    public static class AAR_SalvageScreen_OnCompleted {
        private static void Postfix(AAR_SalvageScreen __instance) {
            try {
                Contract contract = __instance.contract;
                ExtendedContract ec = Utilities.currentExtendedContract();

                WIIC.l.Log($"AAR_SalvageScreen_OnCompleted: ec={ec}, currentContractName={ec.currentContractName}, Override.ID={contract.Override.ID}");

                if (ec?.currentContractName == contract.Override.ID) {
                    ec.currentContractName = null;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
