using System;
using System.Linq;
using System.Text.RegularExpressions;
using BattleTech;
using Newtonsoft.Json;
using ColourfulFlashPoints.Data;
using isogame;

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

        [JsonProperty]
        public string location;

        // Used during deserialization, don't actually call this.
        public ActiveCampaign() {}

        // Use this one instead.
        public ActiveCampaign(Campaign c) {
            this.c = c;
            this.campaign = c.name;
            this.location = c.beginsAt;
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
            MapMarker mapMarker = new MapMarker(location, WIIC.campaignSettings.mapMarker);
            ColourfulFlashPoints.Main.addMapMarker(mapMarker);
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

                // If fakeFp is null, this will null out _fp as well.
                if (_fp?.Def.Description.Name != fakeFp?.name) {
                    _fp = fakeFp?.toFlashpoint();
                }

                return _fp;
            }
        }

        public void entryComplete() {
            entryCountdown = null;
            nodeIndex++;
            runEntry();
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
                    WIIC.activeCampaigns.Remove(location);
                    return;
                }

                WIIC.l.Log($"    goto {e.@goto}. Starting new node.");
                node = e.@goto;
                nodeIndex = -1;
                entryComplete();
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

                WIIC.activeCampaigns.Remove(location);
                location = e.fakeFlashpoint.at;

                WIIC.activeCampaigns[location] = this;

                // No action to take; the flashpoint is added to the starmap via SGNavigationScreen_ShowFlashpointSystems_patch
                // and when the system is clicked via SimGameState_GetFlashpointInSystem_Patch
                return;
            }

            if (e.contract != null) {
                FactionValue employer = FactionEnumeration.GetFactionByName(e.contract.employer);
                FactionValue target = FactionEnumeration.GetFactionByName(e.contract.target);

                if (e.contract.forced != null) {
                    Contract contract = ContractManager.getContractByName(e.contract.id, WIIC.sim.CurSystem, employer, target);

                    entryCountdown = e.contract.forced?.maxDays;
                    contract.SetExpiration(entryCountdown ?? 0);
                    WIIC.sim.RoomManager.AddWorkQueueEntry(workOrder);
                    WIIC.sim.CurSystem.SystemContracts.Add(contract);
                    WIIC.sim.activeBreadcrumb = contract;

                    if (entryCountdown == 0) {
                        WIIC.sim.ForceTakeContract(contract, false);
                    }
                }

                if (e.contract.travel != null) {
                    StarSystem at = WIIC.sim.GetSystemById(e.contract.travel.at);
                    ContractManager.addTravelContract(e.contract.id, at, employer, target);

                    WIIC.activeCampaigns.Remove(location);
                    location = e.contract.travel.at;
                    WIIC.activeCampaigns[location] = this;
                }

                // entryComplete will be triggered from SimGameState_OnAttachUXComplete_Patch
                return;
            }

            if (e.conversation != null) {
                CampaignConversation conv = e.conversation;

                Conversation conversation = WIIC.sim.DataManager.SimGameConversations.Get(conv.id);
                WIIC.sim.interruptQueue.QueueConversation(conversation, conv.header, conv.subheader);

                // entryComplete will be triggered from SimGameConversationManager_EndConversation_Patch
                return;
            }

            if (e.wait != null) {
                entryCountdown = e.wait.days;

                // entryComplete will be triggered from SimGameState_OnDayPassed_Patch
                return;
            }

            // TODO:
            // - Add popup support?
        }

        protected WorkOrderEntry_Notification _workOrder;
        public virtual WorkOrderEntry_Notification workOrder {
            get {
                if (entryCountdown == null) {
                    return null;
                }

                if (_workOrder == null) {
                    if (currentEntry.contract != null) {
                        string title = WIIC.sim.activeBreadcrumb.Name;
                        _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationCmdCenter, "campaignContract", title);
                    } else if (currentEntry.wait?.workOrder != null) {
                        _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationCmdCenter, "campaignWait", currentEntry.wait.workOrder);
                    }
                }

                _workOrder.SetCost(entryCountdown ?? 0);
                return _workOrder;
            }
        }
    }
}
