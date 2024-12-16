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
                if (shouldBlock(__instance.Contract.Name)) {
                    WIIC.activeCampaigns.TryGetValue(WIIC.sim.CurSystem.ID, out ActiveCampaign ac);
                    string reason = ac?.currentEntry.contract?.forced == null ? "Extended" : "Campaign";
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

        public static bool shouldBlock(string name) {
            ExtendedContract extendedContract = Utilities.currentExtendedContract();
            if (extendedContract?.extendedType.blockOtherContracts == true) {
                return true;
            }

            WIIC.activeCampaigns.TryGetValue(WIIC.sim.CurSystem.ID, out ActiveCampaign ac);
            string blockExcept = ac?.currentEntry.contract?.forced == null ? null : ac.currentEntry.contract.id;

            if (blockExcept != null && blockExcept != name) {
                return true;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(SGContractsListItem), "OnClicked")]
    public static class SGContractsListItem_OnClicked_Patch {
        public static bool Prefix(SGContractsListItem __instance) {
            try {
                return !SGContractsListItem_setMode_Patch.shouldBlock(__instance.Contract.Name);
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return true;
        }
    }
}
