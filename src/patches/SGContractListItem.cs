using System;
using Harmony;
using BattleTech.UI;
using BattleTech.UI.Tooltips;
using UnityEngine;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGContractsListItem), "setMode")]
    public static class SGContractsListItem_setMode_Patch {
        public static bool Prefix(SGContractsListItem __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                WIIC.l.Log($"SGContractsListItem_setMode_Patch extendedContract={extendedContract}");

                if (extendedContract != null && extendedContract.extendedType.blockOtherContracts) {
                    WIIC.l.Log($"Blocking contract because blockOtherContracts=true");

                    __instance.enableObjects.ForEach((GameObject obj) => obj.SetActive(false));
                    __instance.disableObjects.ForEach((GameObject obj) => tweakTooltip(obj));

                    __instance.button.SetState(ButtonState.Unavailable, true);

                    return false;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }

        public static void tweakTooltip(GameObject obj) {
            obj.SetActive(true);
            HBSTooltip tooltip = obj.GetComponent<HBSTooltip>();
            if (tooltip != null) {
                tooltip.defaultStateData.stringValue = "DM.BaseDescriptionDefs[ContractBlockedBecauseExtended]";
            }
        }
    }

    [HarmonyPatch(typeof(SGContractsListItem), "OnClicked")]
    public static class SGContractsListItem_OnClicked_Patch {
        public static bool Prefix(SGContractsListItem __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                WIIC.l.Log($"SGContractsListItem_OnClicked_Patch extendedContract={extendedContract}");

                if (extendedContract != null && extendedContract.extendedType.blockOtherContracts && __instance.Contract.TargetSystem == WIIC.sim.CurSystem.ID) {
                    return false;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }
    }
}
