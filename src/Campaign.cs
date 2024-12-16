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
    using CampaignNodes = Dictionary<string, List<CampaignEntry>>;

    public class CampaignIf {
        public string companyHasTag;
        public string companyDoesNotHaveTag;

        public void validate(string path) {
            if (String.IsNullOrEmpty(companyHasTag) && String.IsNullOrEmpty(companyDoesNotHaveTag)) {
                throw new Exception($"VALIDATION: {path} must have \"companyHasTag\" and/or \"companyDoesNotHaveTag\". THE CAMPAIGN WILL NOT WORK.");
            }
        }

        public bool check() {
            if (!String.IsNullOrEmpty(companyHasTag) && !WIIC.sim.CompanyTags.Contains(companyHasTag)) {
                WIIC.l.Log($"    Company does not have {companyHasTag}, which is required");
                return false;
            }

            if (!String.IsNullOrEmpty(companyDoesNotHaveTag) && WIIC.sim.CompanyTags.Contains(companyDoesNotHaveTag)) {
                WIIC.l.Log($"    Company has {companyHasTag}, which is not allowed");
                return false;
            }

            return true;
        }
    }

    public class CampaignEvent {
        public string id;

        public void validate(string path) {
            if (String.IsNullOrEmpty(id)) {
                throw new Exception($"VALIDATION: {path} must have an \"id\". THE CAMPAIGN WILL NOT WORK.");
            }

            EventDef_MDD eventDefMDD = MetadataDatabase.Instance.GetEventDef(id);
            if (eventDefMDD == null) {
                throw new Exception($"VALIDATION: {path}.id \"{id}\" does not seem to exist. THE CAMPAIGN WILL NOT WORK.");
            }

            EventScope scope = eventDefMDD.EventScopeEntry.EventScope;
            if (scope != EventScope.Company && scope != EventScope.Commander) {
                throw new Exception($"VALIDATION: {path}.id has scope \"{scope}\". Only Company and Commander events are supported. THE CAMPAIGN WILL NOT WORK.");
            }
        }

        public void run() {
            WIIC.sim.DataManager.SimGameEventDefs.TryGet(id, out SimGameEventDef eventDef);

            SimGameEventTracker eventTracker = new SimGameEventTracker();
            eventTracker.Init(new[] { eventDef.Scope }, 0, 0, SimGameEventDef.SimEventType.NORMAL, WIIC.sim);

            WIIC.sim.GetInterruptQueue().QueueEventPopup(eventDef, eventDef.Scope, eventTracker);
        }
    }

    public class CampaignFakeFlashpoint {
        public string name;
        public string employer;
        public string employerPortrait;
        public string target;
        public int difficulty;
        public string at;
        public string description;

        public void validate(string path) {
            if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(employer) || String.IsNullOrEmpty(employerPortrait) ||  String.IsNullOrEmpty(target) ||  String.IsNullOrEmpty(at) ||  String.IsNullOrEmpty(description)) {
                throw new Exception($"VALIDATION: {path} must have all of [name, employer, employerPortrait, target, at, description]. THE CAMPAIGN WILL NOT WORK.");
            }

            if (!employerPortrait.StartsWith("castDef_")) {
                throw new Exception($"VALIDATION: {path}.employerPortrait \"{employerPortrait}\" must start with \"castDef_\". THE CAMPAIGN WILL NOT WORK.");
            }
        }

        public Flashpoint toFlashpoint() {
            Flashpoint fp = new Flashpoint();
            fp.GUID = "CampaignFakeFlashpoint";
            fp.EmployerValue = FactionEnumeration.GetFactionByName(employer);

            fp.CurStatus = Flashpoint.Status.WAITING_FOR_DATA;
            fp.CurSystem = WIIC.sim.GetSystemById(at);
            fp.Def = new FlashpointDef();

            // Use the HM prefix to style it in green, differentiating from normal campaign flashpoints
            fp.Def.Description = new BaseDescriptionDef("fp_HM_" + name, name, description, "uixTxrSpot_campaignOutcomeVictory");
            fp.Def.Difficulty = difficulty;
            fp.Def.TargetFaction = target;
            fp.Def.FlashpointDescriberCastDefId = employerPortrait;
            fp.Def.FlashpointLength = FlashpointDef.EngagementLength.CAMPAIGN;
            fp.Def.FlashpointShortDescription = description;
            fp.Def.InternalName = name;

            LoadRequest request = WIIC.sim.DataManager.CreateLoadRequest();
            request.AddLoadRequest<CastDef>(BattleTechResourceType.CastDef, employerPortrait, (string id, CastDef castDef) => {
                fp.FlashpointDescriberCastDef = castDef;
                fp.CurStatus = Flashpoint.Status.AVAILABLE;
            });
            request.ProcessRequests();

            return fp;
        }
    }

    public class CampaignContract {
        public string id;
        public string employer;
        public string target;
        public string onFailGoto;
        public string postContractEvent;

        public CampaignContractForced forced;
        public CampaignContractTravel travel;

        public void validate(string path, CampaignNodes nodes) {
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(employer) || String.IsNullOrEmpty(target) || String.IsNullOrEmpty(onFailGoto)) {
                throw new Exception($"VALIDATION: {path} must have all of [id, employer, target, onFailGoto]. THE CAMPAIGN WILL NOT WORK.");
            }

            if (MetadataDatabase.Instance.Query<Contract_MDD>("SELECT * from Contract WHERE ContractID = @ID", new { ID = id }).ToArray().Length == 0) {
                throw new Exception($"VALIDATION: {path}.id \"{id}\" does not appear to exist. THE CAMPAIGN WILL NOT WORK.");
            }

            if (onFailGoto != "Exit" && !nodes.ContainsKey(onFailGoto)) {
                throw new Exception($"VALIDATION: {path}.onFailGoto must point to another node or be \"Exit\"; \"{onFailGoto}\" is unknown. THE CAMPAIGN WILL NOT WORK.");
            }

            if (postContractEvent != null && MetadataDatabase.Instance.GetEventDef(postContractEvent) == null) {
                throw new Exception($"VALIDATION: {path}.postContractEvent \"{postContractEvent}\" does not seem to exist. THE CAMPAIGN WILL NOT WORK.");
            }

            if ((forced != null && travel != null) || forced == null && travel == null) {
                throw new Exception($"VALIDATION: {path} must have exactly one of [forced, travel]. THE CAMPAIGN WILL NOT WORK.");
            }

            forced?.validate($"{path}.forced");
            travel?.validate($"{travel}.forced");
        }
    }

    public class CampaignContractForced {
        public int? maxDays = 0;

        public void validate(string path) {
            if (maxDays == null || maxDays < 0) {
                throw new Exception($"VALIDATION: {path}.maxDays is required and must be >= 0. THE CAMPAIGN WILL NOT WORK.");
            }
        }
    }

    public class CampaignContractTravel {
        public string at;

        public void validate(string path) {
            if (String.IsNullOrEmpty(at)) {
                throw new Exception($"VALIDATION: {path} must have \"at\". THE CAMPAIGN WILL NOT WORK.");
            }
        }
    }

    public class CampaignConversation {
        public string id;
        public string header;
        public string subheader;

        public void validate(string path) {
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(header) || String.IsNullOrEmpty(subheader)) {
                throw new Exception($"VALIDATION: {path} must have all of [id, header, subheader]. THE CAMPAIGN WILL NOT WORK.");
            }
        }
    }

    public class CampaignTags {

    }

    public class CampaignEntry {
        public CampaignIf @if;
        public string @goto;
        public CampaignEvent @event;
        public string video;
        public string reward;
        public CampaignFakeFlashpoint fakeFlashpoint;
        public CampaignContract contract;
        public CampaignConversation conversation;
        public List<string> wiicEvents;

        public void validate(string path, CampaignNodes nodes, bool isLastEntry) {
            int count = 0;
            count += @goto == null ? 0 : 1;
            count += @event == null ? 0 : 1;
            count += video == null ? 0 : 1;
            count += reward == null ? 0 : 1;
            count += fakeFlashpoint == null ? 0 : 1;
            count += contract == null ? 0 : 1;
            count += conversation == null ? 0 : 1;
            count += wiicEvents == null ? 0 : 1;

            if (count != 1) {
                throw new Exception($"VALIDATION: {path} must have exactly one of [goto, event, video, reward, fakeFlashpoint, contract, conversation, wiicEvents]. It has {count} of them. THE CAMPAIGN WILL NOT WORK.");
            }
            if (@goto != null && @goto != "Exit" && !nodes.ContainsKey(@goto)) {
                throw new Exception($"VALIDATION: {path}.goto must point to another node or be \"Exit\"; \"{@goto}\" is unknown. THE CAMPAIGN WILL NOT WORK.");
            }

            @if?.validate($"{path}.if");
            @event?.validate($"{path}.event");
            fakeFlashpoint?.validate($"{path}.fakeFlashpoint");
            contract?.validate($"{path}.contract", nodes);
            conversation?.validate($"{path}.conversation");

            if (isLastEntry) {
                if (@goto == null) {
                    throw new Exception($"VALIDATION: {path} is the last entry in the node, and is missing \"goto\". You must say what happens next! Use \"goto: Exit\" if this the end of the campaign. THE CAMPAIGN WILL NOT WORK.");
                }

                if ( @if != null) {
                    throw new Exception($"VALIDATION: {path} is the last entry in the node, and cannot have an \"if\" clause. THE CAMPAIGN WILL NOT WORK.");
                }
            }
        }
    }

    public class Campaign {
        public string name;
        public string beginsAt;
        public CampaignNodes nodes = new CampaignNodes();

        public HashSet<string> validate(HashSet<string> otherCampaignSystems) {
            if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(beginsAt)) {
                throw new Exception($"VALIDATION: Campaign is missing a \"name\" or \"beginsAt\". THE CAMPAIGN WILL NOT WORK.");
            }

            if (!nodes.ContainsKey("Start")) {
                throw new Exception($"VALIDATION: nodes[\"Start\"] is required; campaign needs a starting point. THE CAMPAIGN WILL NOT WORK.");
            }

            if (nodes.ContainsKey("Exit")) {
                throw new Exception($"VALIDATION: nodes[\"Exit\"] is not allowed; it is a reserved name. THE CAMPAIGN WILL NOT WORK.");
            }

            HashSet<string> involvedSystems = new HashSet<string>();
            involvedSystems.Add(beginsAt);

            foreach (string nodeKey in nodes.Keys) {
                List<CampaignEntry> node = nodes[nodeKey];
                if (node.Count == 0) {
                    throw new Exception($"VALIDATION: nodes[\"{nodeKey}\"] must have at least one entry. THE CAMPAIGN WILL NOT WORK.");
                }

                for (int i = 0; i < node.Count; i++) {
                    node[i].validate($"nodes[\"{nodeKey}\"][{i}]", nodes, i == node.Count - 1);

                    if (node[i].fakeFlashpoint != null) {
                        involvedSystems.Add(node[i].fakeFlashpoint.at);
                    }

                    if (node[i].contract?.travel?.at != null) {
                        involvedSystems.Add(node[i].contract.travel.at);
                    }
                }
            }

            if (involvedSystems.Overlaps(otherCampaignSystems)) {
                throw new Exception($"VALIDATION: Campaign \"{name}\" shares star systems with another campaign. The set of systems (beginsAt / fakeFlashpoint.at / contract.travel.at) involved in each campaign MUST BE UNIQUE. THE CAMPAIGN WILL NOT WORK.");
            }

            return involvedSystems;
        }
    }
}
