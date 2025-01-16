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
                if (Utilities.shouldBlockContract(__instance.Contract)) {
                    string reason = "Extended";
                    foreach (ActiveCampaign ac in WIIC.activeCampaigns) {
                        if (ac.currentEntry.contract?.withinDays != null || ac.currentEntry.contract?.immediate == true) { reason = "Campaign"; }
                    }
                    __instance.enableObjects.ForEach((GameObject obj) => obj.SetActive(false));
                    __instance.disableObjects.ForEach((GameObject obj) => tweakTooltip(obj, reason));
                    __instance.button.SetState(ButtonState.Unavailable, true);

                    return false;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }

        public static void tweakTooltip(GameObject obj, string reason) {
            obj.SetActive(true);
            HBSTooltip tooltip = obj.GetComponent<HBSTooltip>();
            if (tooltip != null) {
                tooltip.defaultStateData.stringValue = $"DM.BaseDescriptionDefs[ContractBlockedBecause{reason}]";
            }
        }
    }

    [HarmonyPatch(typeof(SGContractsListItem), "OnClicked")]
    public static class SGContractsListItem_OnClicked_Patch {
        public static bool Prefix(SGContractsListItem __instance) {
            try {
                return !Utilities.shouldBlockContract(__instance.Contract);
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }
    }
}
