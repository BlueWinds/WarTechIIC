using System;
using Harmony;
using BattleTech;
using BattleTech.UI;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using Localize;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SGTravelManager), "DisplayEnteredOrbitPopup")]
    public static class SGTravelManager_DisplayEnteredOrbitPopup_Patch {
        [HarmonyPrefix]
        static bool Prefix(SGTravelManager __instance) {
            try {
                if (!WIIC.flareups.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    return true;
                }

                Flareup flareup = WIIC.flareups[WIIC.sim.CurSystem.ID];

                string text = Strings.T("We've arrived at {0}, Commander. The system is currently controlled by {1}, but {2} will attack it soon. If we have good enough reputation with one or both factions, they may have a contract for us to sign on with their side.", flareup.location.Name, flareup.location.OwnerValue.FactionDef.ShortName, flareup.attacker.FactionDef.ShortName);

                void saveNow() {
                  WIIC.sim.TriggerSaveNow(SaveReason.SIM_GAME_ARRIVED_AT_PLANET, SimGameState.TriggerSaveNowOption.QUEUE_IF_NEEDED);
                  WIIC.modLog.Info?.Write($"Passed on flareup mission.");
                }

                void gotoContracts() {
                  WIIC.sim.TriggerSaveNow(SaveReason.SIM_GAME_ARRIVED_AT_PLANET, SimGameState.TriggerSaveNowOption.QUEUE_IF_NEEDED);
                  WIIC.sim.RoomManager.SetQueuedUIActivationID(DropshipMenuType.Contract, DropshipLocation.CMD_CENTER, true);
                  WIIC.sim.RoomManager.ForceShipRoomChangeOfRoom(DropshipLocation.CMD_CENTER);
                }

                WIIC.sim.GetInterruptQueue().QueueTravelPauseNotification("Arrived", text, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), "notification_travelcomplete", gotoContracts, "View contracts", saveNow, "Continue");

                if (!WIIC.sim.TimeMoving) {
                  WIIC.sim.GetInterruptQueue().DisplayIfAvailable();
                }

                return false;
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
                return true;
            }
        }
    }
}
