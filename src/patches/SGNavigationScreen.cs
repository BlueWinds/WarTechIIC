using System;
using System.Collections.Generic;
using System.Reflection;
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

                UIManager uiManager = (UIManager) AccessTools.Field(typeof(SGNavigationScreen), "uiManager").GetValue(__instance);

                void cleanup() {
                    uiManager.ResetFader(UIManagerRootType.PopupRoot);
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

                        if (extendedContract2 != null && extendedContract2.employer.DoesGainReputation) {
                            WIIC.modLog.Info?.Write("Employer: {extendedContract2.employer}");

                            float employerRepBadFaithMod = WIIC.sim.Constants.Story.EmployerRepBadFaithMod;
                            WIIC.modLog.Info?.Write("employerRepBadFaithMod: {employerRepBadFaithMod}");
                            WIIC.modLog.Info?.Write("difficulty: {extendedContract2.location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
                            int num = (int) Math.Round(extendedContract2.location.Def.GetDifficulty(SimGameState.SimGameType.CAREER) * employerRepBadFaithMod);

                            WIIC.sim.SetReputation(extendedContract2.employer, num);
                            WIIC.sim.SetReputation(FactionEnumeration.GetMercenaryReviewBoardFactionValue(), num);
                        }

                        WIIC.sim.CompanyTags.Remove("WIIC_helping_attacker");
                        WIIC.sim.CompanyTags.Remove("WIIC_helping_defender");

                        WIIC.extendedContracts.Remove(extendedContract2.location.ID);
                        WIIC.sim.CompanyTags.Remove("WIIC_extended_contract");

                        WIIC.sim.RoomManager.RefreshTimeline(false);
                        WIIC.sim.Starmap.SetActivePath();
                        WIIC.sim.SetSimRoomState(DropshipLocation.SHIP);

                        cleanup();
                    } catch (Exception e) {
                        WIIC.modLog.Error?.Write(e);
                    }
                }, primaryButtonText, cleanup, cancel);

                WIIC.sim.Starmap.Screen.AllowInput(false);
                uiManager.SetFaderColor(uiManager.UILookAndColorConstants.PopupBackfill, UIManagerFader.FadePosition.FadeInBack, UIManagerRootType.PopupRoot);
                return false;
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
                return true;
            }
        }
    }
}
