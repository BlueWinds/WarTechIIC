using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using Localize;
using BattleTech;
using BattleTech.Data;
using BattleTech.UI;
using HBS.Collections;

namespace WarTechIIC {
    public class CampaignIf {
        public string companyHasTag;
        public string companyDoesNotHaveTag;

        public void validate(string nodeName, int entryIndex) {
            if (String.IsNullOrEmpty(companyHasTag) && String.IsNullOrEmpty(companyDoesNotHaveTag)) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].if must have \"companyHasTag\" and/or \"companyDoesNotHaveTag\". THE CAMPAIGN WILL NOT WORK.");
            }
        }
    }

    public class CampaignEvent {
        public string id;

        public void validate(string nodeName, int entryIndex) {
            if (String.IsNullOrEmpty(id)) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].event must have an \"id\". THE CAMPAIGN WILL NOT WORK.");
            }
            if (MetadataDatabase.Instance.GetEventDef(id) == null) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].event.id \"{id}\" does not seem to exist. THE CAMPAIGN WILL NOT WORK.");
            }
        }
    }

    public class CampaignFakeFlashpoint {
        public string name;
        public string employer;
        public string employerPortrait;
        public string target;
        public string at;
        public string description;

        public void validate(string nodeName, int entryIndex) {
            if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(employer) || String.IsNullOrEmpty(employerPortrait) ||  String.IsNullOrEmpty(target) ||  String.IsNullOrEmpty(at) ||  String.IsNullOrEmpty(description)) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].fakeFlashpoint must have all of [name, employer, employerPortrait, target, at, description]. THE CAMPAIGN WILL NOT WORK.");
            }
        }
    }

    public class CampaignContract {
        public string id;
        public string employer;
        public string target;
        public string at;
        public string onFailGoto;
        public bool blockOtherContracts = false;
        public string postContractEvent;

        public void validate(string nodeName, int entryIndex, Dictionary<string, List<CampaignEntry>> nodes) {
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(employer) || String.IsNullOrEmpty(target) || String.IsNullOrEmpty(onFailGoto)) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].contract must have all of [id, employer, target, onFailGoto]. THE CAMPAIGN WILL NOT WORK.");
            }

            if (MetadataDatabase.Instance.Query<Contract_MDD>("SELECT * from Contract WHERE ContractID = @ID", new { ID = id }).ToArray().Length == 0) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].contract.id \"{id}\" does not appear to exist. THE CAMPAIGN WILL NOT WORK.");
            }

            if (onFailGoto != "Exit" && !nodes.ContainsKey(onFailGoto)) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].contract.onFailGoto must point to another node or be \"Exit\"; \"{onFailGoto}\" is unknown. THE CAMPAIGN WILL NOT WORK.");
            }

            if (postContractEvent != null && MetadataDatabase.Instance.GetEventDef(id) == null) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].contract.postContractEvent \"{postContractEvent}\" does not seem to exist. THE CAMPAIGN WILL NOT WORK.");
            }
        }
    }

    public class CampaignConversation {
        public string id;
        public string header;
        public string subheader;

        public void validate(string nodeName, int entryIndex) {
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(header) || String.IsNullOrEmpty(subheader)) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].conversation must have all of [id, header, subheader]. THE CAMPAIGN WILL NOT WORK.");
            }
        }
    }

    public class CampaignEntry {
        public CampaignIf @if;
        public string @goto;
        public CampaignEvent @event;
        public string video;
        public string lootbox;
        public CampaignFakeFlashpoint fakeFlashpoint;
        public CampaignContract contract;
        public CampaignConversation conversation;

        public void validate(string nodeName, int entryIndex, Dictionary<string, List<CampaignEntry>> nodes, bool isLastEntry) {
            int count = 0;
            count += @goto == null ? 0 : 1;
            count += @event == null ? 0 : 1;
            count += video == null ? 0 : 1;
            count += lootbox == null ? 0 : 1;
            count += fakeFlashpoint == null ? 0 : 1;
            count += contract == null ? 0 : 1;
            count += conversation == null ? 0 : 1;

            if (count != 1) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}] must have exactly one of [goto, event, video, lootbox, fakeFlashpoint, contract]. It has {count} of them. THE CAMPAIGN WILL NOT WORK.");
            }
            if (@goto != null && @goto != "Exit" && !nodes.ContainsKey(@goto)) {
                throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}].goto must point to another node or be \"Exit\"; \"{@goto}\" is unknown. THE CAMPAIGN WILL NOT WORK.");
            }

            if (@if != null) { @if.validate(nodeName, entryIndex); }
            if (@event != null) { @event.validate(nodeName, entryIndex); }
            if (fakeFlashpoint != null) { fakeFlashpoint.validate(nodeName, entryIndex); }
            if (contract != null) { contract.validate(nodeName, entryIndex, nodes); }
            if (conversation != null) { conversation.validate(nodeName, entryIndex); }

            if (isLastEntry) {
                if (@goto == null) {
                    throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}] is the last entry in the node, and is missing \"goto\". You must say what happens next! Use \"goto: Exit\" if this the end of the campaign. THE CAMPAIGN WILL NOT WORK.");
                }

                if ( @if != null) {
                    throw new Exception($"VALIDATION: nodes[\"{nodeName}\"][{entryIndex}] is the last entry in the node, and cannot have an \"if\" clause. THE CAMPAIGN WILL NOT WORK.");
                }
            }
        }
    }

    public class Campaign {
        public string name;
        Dictionary<string, List<CampaignEntry>> nodes = new Dictionary<string, List<CampaignEntry>>();

        public void validate() {
            if (String.IsNullOrEmpty(name)) {
                throw new Exception($"VALIDATION: Campaign is missing a \"name\". THE CAMPAIGN WILL NOT WORK.");
            }

            if (!nodes.ContainsKey("Start")) {
                throw new Exception($"VALIDATION: nodes[\"Start\"] is required; campaign needs a starting point. THE CAMPAIGN WILL NOT WORK.");
            }

            if (!nodes.ContainsKey("Exit")) {
                throw new Exception($"VALIDATION: nodes[\"Exit\"] is not allowed; it is a reserved name. THE CAMPAIGN WILL NOT WORK.");
            }

            foreach (string nodeKey in nodes.Keys) {
                List<CampaignEntry> node = nodes[nodeKey];
                if (node.Count == 0) {
                    throw new Exception($"VALIDATION: nodes[\"{nodeKey}\"] must have at least one entry. THE CAMPAIGN WILL NOT WORK.");
                }

                for (int i = 0; i < node.Count; i++) {
                    node[i].validate(nodeKey, i, nodes, i == node.Count - 1);
                }
            }
        }
    }
}
