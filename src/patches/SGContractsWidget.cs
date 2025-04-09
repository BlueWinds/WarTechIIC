using System;
using Harmony;
using BattleTech;
using BattleTech.Framework;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGContractsWidget), "GetContractComparePriority")]
    public static class SGContractsWidget_GetContractComparePriority_Patch {
        static bool Prefix(SGContractsWidget __instance, ref int __result, Contract contract) {
            try {
                int difficulty = contract.Override.GetUIDifficulty();
                if (WIIC.sim.ContractUserMeetsReputation(contract)) {
                    if (contract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignRestoration) {
                        __result = 0;
                    } else if (contract.Override.contractDisplayStyle == ContractDisplayStyle.HeavyMetalFlashpointCampaign) {
                        __result = 1;
                    } else if (contract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignStory) {
                        __result = 2;
                    } else if (contract.TargetSystem.Replace("starsystemdef_", "").Equals(WIIC.sim.CurSystem.Name)) {
                        __result = difficulty + 2;
                    } else {
                        __result = difficulty + 12;
                    }
                } else {
                    __result = difficulty + 22;
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }

            return false;
        }
    }
}
