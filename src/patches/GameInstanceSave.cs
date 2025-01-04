using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using BattleTech;
using BattleTech.Data;
using BattleTech.Save;
using BattleTech.Save.Test;
using UnityEngine;

namespace WarTechIIC {
    [HarmonyPatch(typeof(GameInstanceSave), "PreSerialization")]
    public static class GameInstanceSave_PreSerialization_Patch {
        public static void Prefix(GameInstanceSave __instance) {
            if (__instance.serializePassOne) {
                return;
            }

            WIIC.l.Log($"Saving {WIIC.activeCampaigns.Count} campaigns, {WIIC.extendedContracts.Keys.Count} extended contracts, and {WIIC.systemControl.Keys.Count} system control tags");

            string saves = "Campaigns:";
            try {
                foreach (ActiveCampaign ac in WIIC.activeCampaigns) {
                    saves += "\n    " + ac.Serialize();
                    WIIC.sim.CompanyTags.Add(ac.Serialize());
                }
                saves += "\n\nExtendedContracts:";
                foreach (ExtendedContract extendedContract in WIIC.extendedContracts.Values) {
                    saves += "\n    " + extendedContract.Serialize();
                    extendedContract.location.Tags.Add(extendedContract.Serialize());
                }
                foreach (KeyValuePair<string, string> control in WIIC.systemControl) {
                    WIIC.sim.GetSystemById(control.Key).Tags.Add(control.Value);
                }

                WIIC.l.Log(saves);
            } catch (Exception e) {
                WIIC.l.Log(saves);
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(GameInstanceSave), "PostSerialization")]
    public static class GameInstanceSave_PostSerialization_Patch {
        public static void Prefix(GameInstanceSave __instance) {
            if (!__instance.serializePassOne) {
                return;
            }

            WIIC.l.Log("Clearing extendedContract system tags post-save");

            try {
                foreach (ExtendedContract extendedContract in WIIC.extendedContracts.Values) {
                    List<string> tagList = extendedContract.location.Tags.ToList().FindAll(t => t.StartsWith("WIIC:"));
                    foreach (string tag in tagList) {
                        extendedContract.location.Tags.Remove(tag);
                    }
                }
                List<string> companyTagList = WIIC.sim.CompanyTags.ToList().FindAll(ActiveCampaign.isSerializedCampaign);
                foreach (string tag in companyTagList) {
                    WIIC.sim.CompanyTags.Remove(tag);
                }
                foreach (KeyValuePair<string, string> control in WIIC.systemControl) {
                    WIIC.sim.GetSystemById(control.Key).Tags.Remove(control.Value);
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

    [HarmonyPatch(typeof(GameInstanceSave), "HandleFlashpointData")]
    public static class GameInstanceSave_HandleFlashpointData_Patch {
        public static void Prefix(GameInstanceSave __instance, DataManager dataManager) {
            WIIC.l.Log("GameInstanceSave_HandleFlashpointData_Patch: Loading campaign sprites");

            try {
                LoadRequest loadRequest = dataManager.CreateLoadRequest();

                foreach (string id in Campaign.loadSprites.Distinct()) {
                    loadRequest.AddLoadRequest<Sprite>(BattleTechResourceType.Sprite, id, delegate (string id, Sprite sprite) {
                        WIIC.l.Log($"Loaded sprite {id}");
                    });
                }

                loadRequest.ProcessRequests();
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }

}
