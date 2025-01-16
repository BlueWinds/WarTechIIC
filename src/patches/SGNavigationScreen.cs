using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using BattleTech;
using BattleTech.UI;
using Localize;
using UnityEngine;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGNavigationScreen), "OnTravelCourseAccepted")]
    public static class SGNavigationScreen_OnTravelCourseAccepted_patch {
        private static bool Prefix(SGNavigationScreen __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                WIIC.l.Log($"OnTravelCourseAccepted. extendedContract: {extendedContract}, ActiveTravelContract: {WIIC.sim.ActiveTravelContract}");

                void cleanup() {
                    __instance.uiManager.ResetFader(UIManagerRootType.PopupRoot);
                    WIIC.sim.Starmap.Screen.AllowInput(true);
                }

                Sprite sumire = WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire);

                foreach (ActiveCampaign ac in WIIC.activeCampaigns.Where(ac => ac.currentEntry.contract != null)) {
                    string cTitle = Strings.T("Navigation not allowed");
                    string cMessage = Strings.T("We can't leave {0} while we have an important contract pending, Commander.", WIIC.sim.CurSystem.Name);
                    PauseNotification.Show(cTitle, cMessage, sumire, "", true, cleanup);
                    return false;
                }

                if (extendedContract == null) {
                    return true;
                }

                string title = Strings.T("Navigation Change");
                string primaryButtonText = Strings.T("Break Contract");
                string cancel = Strings.T("Cancel");
                string message = Strings.T("Leaving {0} will break our current contract. Our reputation with {1} and the MRB will suffer, Commander.", extendedContract.location.Name, extendedContract.employer.FactionDef.ShortName);
                WIIC.l.Log(message);
                PauseNotification.Show(title, message, sumire, "", true, delegate {
                    try {
                        WIIC.l.Log("Breaking {extendedContract.type.Name} contract");

                        ExtendedContract extendedContract2 = Utilities.currentExtendedContract();
                        WIIC.l.Log("ExtendedContract: {extendedContract2}");


                        if (extendedContract2 != null) {
                            extendedContract2.applyDeclinePenalty(DeclinePenalty.BreakContract);
                        }

                        WIIC.sim.Starmap.SetActivePath();
                        WIIC.sim.SetSimRoomState(DropshipLocation.SHIP);

                        cleanup();
                    } catch (Exception e) {
                        WIIC.l.LogException(e);
                    }
                }, primaryButtonText, cleanup, cancel);

                WIIC.sim.Starmap.Screen.AllowInput(false);
                __instance.uiManager.SetFaderColor(
                    __instance.uiManager.UILookAndColorConstants.PopupBackfill,
                    UIManagerFader.FadePosition.FadeInBack,
                    UIManagerRootType.PopupRoot
                );
                return false;
            } catch (Exception e) {
                WIIC.l.LogException(e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(SGNavigationScreen), "ShowFlashpointSystems")]
    public static class SGNavigationScreen_ShowFlashpointSystems_patch {
        private static void Poostfix(SGNavigationScreen __instance) {
            try {
                WIIC.l.Log("SGNavigationScreen_ShowFlashpointSystems_patch acs={WIIC.activeCampaigns.Count}");
                foreach (ActiveCampaign ac in WIIC.activeCampaigns) {
                    Flashpoint fp = ac.currentFakeFlashpoint;
                    if (fp != null) {
                        WIIC.l.Log("Adding fakeFlashpoint {fp.Def.Description.Name} for {ac.campaign} to {fp.CurrSystem.ID}");
                        __instance.GetSystemFlashpoint(fp);
                    }
                }
            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
