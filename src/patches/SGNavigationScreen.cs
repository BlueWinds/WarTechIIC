using System;
using System.Collections.Generic;
using Harmony;
using BattleTech;
using BattleTech.UI;
using Localize;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGNavigationScreen), "OnTravelCourseAccepted")]
    public static class SGNavigationScreen_OnTravelCourseAcceptedPatch {
        private static bool Prefix(SGNavigationScreen __instance) {
            try {
                ExtendedContract extendedContract = Utilities.currentExtendedContract();
                WIIC.modLog.Warn?.Write($"OnTravelCourseAccepted. extendedContract: {extendedContract}, ActiveTravelContract: {WIIC.sim.ActiveTravelContract}");
                if (extendedContract == null) {
                    return true;
                }

                void cleanup() {
                    __instance.uiManager.ResetFader(UIManagerRootType.PopupRoot);
                    WIIC.sim.Starmap.Screen.AllowInput(true);
                }

                string title = Strings.T("Navigation Change");
                string primaryButtonText = Strings.T("Break Contract");
                string cancel = Strings.T("Cancel");
                string message = Strings.T("Leaving {0} will break our current contract. Our reputation with {1} and the MRB will suffer, Commander.", extendedContract.location.Name, extendedContract.employer.FactionDef.ShortName);
                WIIC.modLog.Info?.Write(message);
                PauseNotification.Show(title, message, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, true, delegate {
                    try {
                        WIIC.modLog.Info?.Write("Breaking {extendedContract.type.Name} contract");

                        ExtendedContract extendedContract2 = Utilities.currentExtendedContract();
                        WIIC.modLog.Info?.Write("ExtendedContract: {extendedContract2}");


                        if (extendedContract2 != null) {
                            extendedContract2.applyDeclinePenalty(DeclinePenalty.BreakContract);
                        }

                        WIIC.sim.Starmap.SetActivePath();
                        WIIC.sim.SetSimRoomState(DropshipLocation.SHIP);

                        cleanup();
                    } catch (Exception e) {
                        WIIC.modLog.Error?.Write(e);
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
                WIIC.modLog.Error?.Write(e);
                return true;
            }
        }
    }
}
