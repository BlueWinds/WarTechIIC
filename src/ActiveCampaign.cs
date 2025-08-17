using System;
using System.Linq;
using System.Text.RegularExpressions;
using BattleTech;
using BattleTech.StringInterpolation;
using BattleTech.UI;
using Newtonsoft.Json;
using ColourfulFlashPoints.Data;
using isogame;
using Localize;
using UnityEngine;

namespace WarTechIIC {
    [JsonObject(MemberSerialization.OptIn)]
    public class ActiveCampaign {
        [JsonProperty]
        public string campaign;

        public Campaign c;

        [JsonProperty]
        public string node = "Start";

        [JsonProperty]
        public int nodeIndex = 0;

        [JsonProperty]
        public int? entryCountdown;

        // Used during deserialization, don't actually call this.
        public ActiveCampaign() {}

        // Use this one instead.
        public ActiveCampaign(Campaign c) {
            this.c = c;
            this.campaign = c.name;
        }

        public static bool isSerializedCampaign(string tag) {
            return tag.StartsWith("WIIC-Campaign:");
        }

        public string Serialize() {
            string json = JsonConvert.SerializeObject(this, WIIC.serializerSettings);
            return $"WIIC-Campaign:{json}";
        }

        private static Regex SERIALIZED_TAG = new Regex("^WIIC-Campaign:(?<json>\\{.*\\})$", RegexOptions.Compiled);
        public static ActiveCampaign Deserialize(string tag) {
            MatchCollection matches = SERIALIZED_TAG.Matches(tag);
            string json = matches[0].Groups["json"].Value;

            ActiveCampaign newCampaign = JsonConvert.DeserializeObject<ActiveCampaign>(json);
            newCampaign.c = WIIC.campaigns[newCampaign.campaign];

            return newCampaign;
        }

        public void addToMap() {
            if (currentEntry.fakeFlashpoint != null) {
                MapMarker mapMarker = new MapMarker(currentEntry.fakeFlashpoint.at, WIIC.campaignSettings.mapMarker);
                ColourfulFlashPoints.Main.addMapMarker(mapMarker);
            }
        }

        public CampaignEntry currentEntry {
            get {
                return c.nodes[node][nodeIndex];
            }
        }

        private Flashpoint _fp;
        public Flashpoint currentFakeFlashpoint {
            get {
                CampaignFakeFlashpoint fakeFp = currentEntry.fakeFlashpoint;

                // Ensure that any OWNER values are kept up to date; if the system owner has changed, invalidate the cache.
                if (_fp != null) {
                    if (fakeFp.employer == "OWNER" && _fp.EmployerValue != Utilities.getFactionValueByName(fakeFp.employer, _fp.CurSystem)
                    ) {
                        _fp = null;
                    }

                    if (fakeFp.target == "OWNER" && _fp.Def.TargetFaction != Utilities.getFactionValueByName(fakeFp.target, _fp.CurSystem).FactionDef.factionID) {
                        _fp = null;
                    }
                }

                // If fakeFp is null, this will null out _fp as well.
                if (_fp?.Def.Description.Name != fakeFp?.name) {
                    _fp = fakeFp?.toFlashpoint();
                }


                return _fp;
            }
        }

        public void entryComplete() {
            WorkOrderEntry_Notification oldWorkOrder = workOrder;
            Flashpoint oldFp = currentFakeFlashpoint;

            entryCountdown = null;
            nodeIndex++;
            runEntry();

            if (oldFp != currentFakeFlashpoint) {
                // If we've just moved away from a fakeFP, it might still be lingering on the map.
                Utilities.redrawMap();
            }

            // We might need to remove a previous work order
            if (workOrder == null) {
                WIIC.sim.RoomManager.RefreshTimeline(false);
            } else {
                WIIC.sim.RoomManager.AddWorkQueueEntry(workOrder);
            }
        }

        public void runEntry() {

            CampaignEntry e = currentEntry;
            WIIC.l.Log($"{campaign}: Running nodes[{node}][{nodeIndex}].");

            if (e == null) {
                throw new Exception("Ran into null currentEntry. This should be impossible.");
            }

            if (e.@if != null && !e.@if.check()) {
                WIIC.l.Log($"    \"if\" failed. Continuing to next entry in {node}.");
                entryComplete();
                return;
            }

            if (e.@goto != null) {
                if (e.@goto == "Exit") {
                    WIIC.l.Log($"    goto Exit. Campaign complete!");
                    WIIC.activeCampaigns.Remove(this);
                    return;
                }

                WIIC.l.Log($"    goto {e.@goto}. Starting new node.");
                node = e.@goto;
                nodeIndex = 0;
                runEntry();
                return;
            }

            if (e.@event != null) {
                // entryComplete will be triggered from SimGameState_OnEventDismissed_Patch
                WIIC.l.Log($"    event {e.@event.id}.");
                e.@event.run();
                return;
            }

            if (e.video != null) {
                // entryComplete will be triggered from SimGameState_OnVideoComplete_Patch
                WIIC.l.Log($"    video {e.video}.");
                WIIC.sim.PlayVideo(e.video);
                return;
            }

            if (e.reward != null) {
                // entryComplete will be triggered from SimGameInterruptManager_PopupClose_Patch
                WIIC.l.Log($"    reward {e.reward}.");
                Utilities.giveReward(e.reward);
                return;
            }

            if (e.fakeFlashpoint != null) {
                // entryComplete will be triggered from SimGameState_SetActiveFlashpoint_Patch
                WIIC.l.Log($"    fakeFlashpoint {e.fakeFlashpoint.name}.");
                WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text($"{currentEntry.fakeFlashpoint.name} available at {currentFakeFlashpoint.CurSystem.Name}"));

                // No action to take; the flashpoint is added to the starmap via SGNavigationScreen_ShowFlashpointSystems_patch
                // and when the system is clicked via SimGameState_SetActiveFlashpoint_Patch
                return;
            }

            if (e.contract != null) {
                WIIC.l.Log($"    contract {e.contract.id}.");
                FactionValue employer = Utilities.getFactionValueByName(e.contract.employer);
                FactionValue target = Utilities.getFactionValueByName(e.contract.target);
                Contract contract = ContractManager.getContractByName(e.contract.id, employer, target);
                WIIC.sim.GlobalContracts.Add(contract);

                if (e.contract.withinDays != null || e.contract.immediate) {
                    entryCountdown = e.contract.withinDays ?? 0;
                    contract.SetExpiration(e.contract.withinDays ?? 0);
                }

                Utilities.sendToCommandCenter(e.contract.immediate);

                // entryComplete will be triggered from SimGameState_OnAttachUXComplete_Patch
                return;
            }

            if (e.conversation != null) {
                CampaignConversation conv = e.conversation;
                WIIC.l.Log($"    conversation {conv.id}.");

                conv.characters.apply();
                WIIC.sim.DataManager.SimGameConversations.TryGet(conv.id, out Conversation conversation);

                if (conversation == null) {
                    WIIC.l.Log($"    Cannot find conversation; prompting to continue or exit.");
                    GenericPopupBuilder.Create("WIIC Campaign Error", $"Unable to find conversation {conv.id}. Skip to next entry or exit game?")
                        .AddButton("Skip", delegate {
                            WIIC.l.Log($"    Player chose to skip conversation; running entryComplete.");
                            entryComplete();
                        }, true)
                        .AddButton("Exit", UnityGameInstance.Instance.ShutdownGame, false)
                        .Render();

                    return;
                }
                WIIC.sim.interruptQueue.QueueConversation(conversation, conv.header, conv.subheader);

                // entryComplete will be triggered from SimGameConversationManager_EndConversation_Patch
                return;
            }

            if (e.wait != null) {
                entryCountdown = e.wait.days;

                // entryComplete will be triggered from SimGameState_OnDayPassed_Patch
                return;
            }

            if (e.popup != null) {
                string title = Interpolator.Interpolate(e.popup.title, WIIC.sim.Context);
                string message = Interpolator.Interpolate(e.popup.message, WIIC.sim.Context);
                Sprite sprite = WIIC.sim.DataManager.SpriteCache.GetSprite(e.popup.sprite);

                WIIC.sim.GetInterruptQueue().QueueTravelPauseNotification(title, message, sprite, "notification_travelcomplete", delegate {
                    WIIC.l.Log($"Popup dismissed");
                    entryComplete();
                }, "Continue");

                WIIC.sim.GetInterruptQueue().DisplayIfAvailable();

                // entryComplete will be triggered from the deligate above
                return;
            }
        }

        protected WorkOrderEntry_Notification _workOrder;
        protected string _workOrderIndex;
        public virtual WorkOrderEntry_Notification workOrder {
            get {
                string curIdx = $"{node}.{nodeIndex}";
                if (_workOrderIndex != curIdx) {
                    WIIC.l.Log($"Generating work order for {campaign} nodes.{curIdx}");
                    if (currentEntry.contract?.withinDays != null) {
                        Contract contract = WIIC.sim.GlobalContracts.Find(c => c.Override.ID == currentEntry.contract.id);
                        _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationCmdCenter, "campaignContract", contract?.Name ?? campaign);
                    } else if (currentEntry.wait?.workOrder != null) {
                        _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationCmdCenter, "campaignWait", currentEntry.wait.workOrder);
                    } else {
                        _workOrder = null;
                    }
                    _workOrderIndex = curIdx;
                }

                _workOrder?.SetCost(entryCountdown ?? 0);
                return _workOrder;
            }
        }
    }
}
