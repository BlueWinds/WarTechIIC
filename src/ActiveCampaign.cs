using System;
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

        public Flashpoint currentFakeFlashpoint {
            get {
                if (availableFor != null) {
                    return c.entrypoint.toFlashpoint();
                }

                if (currentEntry != null && currentEntry.fakeFlashpoint != null) {
                    return currentEntry.fakeFlashpoint.toFlashpoint();
                }

                return null;
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

        public void entryComplete() {
            nodeIndex++;
            runEntry();
        }

        public void runEntry() {
            CampaignEntry e = currentEntry;
            WIIC.modLog.Info?.Write($"{campaign}: Running nodes[{node}][{nodeIndex}]: {e}");

            if (e == null) {
                throw new Exception("Ran into null currentEntry. This should be impossible.");
            }


            if (e.@if != null && !e.@if.check()) {
                WIIC.modLog.Info?.Write($"    \"if\" failed. Continuing to next entry in {node}.");
                entryComplete();
                return;
            }

            if (e.@goto != null) {
                if (e.@goto == "Exit") {
                    WIIC.modLog.Info?.Write($"goto Exit. Campaign complete!");
                    WIIC.activeCampaigns.Remove(location);
                    return;
                }

                WIIC.modLog.Info?.Write($"goto {e.@goto}. Starting new node.");
                node = e.@goto;
                nodeIndex = -1;
                entryComplete();
                return;
            }

            // public CampaignEvent @event;
            // public string video;
            // public string lootbox;
            // public CampaignFakeFlashpoint fakeFlashpoint;
            // public CampaignContract contract;
            // public CampaignConversation conversation;
        }
    }
}
