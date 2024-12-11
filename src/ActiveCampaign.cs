using System;
using System.Linq;
using System.Text.RegularExpressions;
using BattleTech;
using Newtonsoft.Json;
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    public class ActiveCampaign {
        [JsonProperty]
        public string campaign;
        public Campaign c;

        [JsonProperty]
        public int? availableFor;

        [JsonProperty]
        public string node;

        [JsonProperty]
        public int? nodeIndex;

        [JsonProperty]
        public int? entryCountdown;

        [JsonProperty]
        public string location;

        public CampaignEntry currentEntry {
            get {
                if (node == null || nodeIndex == null) {
                    return null;
                }
                return c.nodes[node][nodeIndex ?? 0];
            }
        }

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
            MapMarker mapMarker = new MapMarker(location, WIIC.campaignSettings.mapMarker);
            ColourfulFlashPoints.Main.addMapMarker(mapMarker);
        }

        private Flashpoint _fp;
        public Flashpoint currentFakeFlashpoint {
            get {
                CampaignFakeFlashpoint fakeFp = availableFor != null ? c.entrypoint : currentEntry?.fakeFlashpoint;

                // If fakeFp is null, this will null out _fp as well.
                if (_fp?.Def.Description.Name != fakeFp?.name) {
                    _fp = fakeFp?.toFlashpoint();
                }

                return _fp;
            }
        }

        public void fakeFlashpointComplete() {
            if (currentFakeFlashpoint == null) {
                throw new Exception("fakeFlashpointComplete on {campaign} but not currently expecting one?");
            }

            // The player has accepted the entrypoint fakeFlashpoint, beginning the campaign.
            if (availableFor != null) {
                availableFor = null;
                node = "Start";
                nodeIndex = -1;
            }

            entryComplete();
        }

        public string currentContract {
            get {
                return currentEntry?.contract?.id;
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
                // entryComplete will be triggered from SimGameState_PlayVideoComplete_Patch
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

                // No action to take; the flashpoint is added to the starmap via SGNavigationScreen_ShowFlashpointSystems_patch
                // and when the system is clicked via SimGameState_GetFlashpointInSystem_Patch
                return;
            }

            if (e.contract != null) {
                entryCountdown = e.contract.expiresAfter;

                FactionValue employer = FactionEnumeration.GetFactionByName(e.contract.employer);
                FactionValue target = FactionEnumeration.GetFactionByName(e.contract.target);
                Contract contract = ContractManager.getContractByName(e.contract.id, WIIC.sim.CurSystem, employer, target);
                if (entryCountdown == 0) {
                    WIIC.sim.ForceTakeContract(contract, false);
                } else {
                    WIIC.sim.CurSystem.SystemContracts.Add(contract);
                }

                // entryComplete will be triggered from SimGameState_OnAttachUXComplete_Patch
                return;
            }

            // TODO:
            //  - Block travel while waiting on contract
            //  - Add workorder item when contract is pending
            //  - Block other contracts when contract is pending?
            //  - Countdown / trigger contract on day pass
            // public CampaignConversation conversation;
            // Add popup support?
        }
    }
}
