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
                Flareup flareup = Utilities.currentFlareup();
                if (flareup == null) {
                    return true;
                }

                if (!WIIC.flareups.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    WIIC.modLog.Warn?.Write($"Found company tag indicating flareup participation, but no matching flareup for {WIIC.sim.CurSystem.ID}");
                    WIIC.sim.CompanyTags.Remove("WIIC_helping_attacker");
                    WIIC.sim.CompanyTags.Remove("WIIC_helping_defender");
                    return true;
                }

                UIManager uiManager = (UIManager) AccessTools.Field(typeof(SGNavigationScreen), "uiManager").GetValue(__instance);

                void cleanup() {
                    uiManager.ResetFader(UIManagerRootType.PopupRoot);
                    WIIC.sim.Starmap.Screen.AllowInput(true);
                }

                string title = Strings.T("Navigation Change");
                string primaryButtonText = Strings.T("Break Deployment");
                string cancel = Strings.T("Cancel");
                string message = Strings.T("Leaving {0} will break our current deployment. Our reputation with {1} and the MRB will suffer, Commander.", flareup.location.Name, flareup.employer.FactionDef.ShortName);
                PauseNotification.Show(title, message, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, true, delegate {
                    cleanup();
                    WIIC.sim.CompanyTags.Remove("WIIC_helping_attacker");
                    WIIC.sim.CompanyTags.Remove("WIIC_helping_defender");

                    if (flareup.employer.DoesGainReputation) {
                        float employerRepBadFaithMod = WIIC.sim.Constants.Story.EmployerRepBadFaithMod;
                        int num = (int) Math.Round(flareup.location.Def.GetDifficulty(SimGameState.SimGameType.CAREER) * employerRepBadFaithMod);

                        WIIC.sim.SetReputation(flareup.employer, num);
                        WIIC.sim.SetReputation(FactionEnumeration.GetFactionByName("faction_MercenaryReviewBoard"), num);
                    }

                    WIIC.sim.Starmap.SetActivePath();
                    WIIC.sim.SetSimRoomState(DropshipLocation.SHIP);
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
