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
using HBS;
using HBS.Collections;


namespace WarTechIIC {
    using CampaignNodes = Dictionary<string, List<CampaignEntry>>;

    public class CampaignIf {
        public string companyHasTag;
        public string companyDoesNotHaveTag;
        public List<string> companyHasAnyTag;
        public List<string> companyHasAllTags;
        public List<string> companyDoesNotHaveTags;

        public void validate(string path) {
            if (String.IsNullOrEmpty(companyHasTag) && String.IsNullOrEmpty(companyDoesNotHaveTag) && companyHasAnyTag == null && companyHasAllTags == null && companyDoesNotHaveTags == null) {
                WIIC.validationErrors.Add($"{path} must have at least one condition, such as \"companyHasTag\" or its variants.");
            }

            if (companyHasAnyTag != null && companyHasAnyTag.Count < 2) {
                WIIC.validationErrors.Add($"{path}.companyHasAnyTag must have at least two tags if present. If you only want one, use \"companyHasTag\" instead.");
            }

            if (companyHasAllTags != null && companyHasAllTags.Count < 2) {
                WIIC.validationErrors.Add($"{path}.companyHasAllTags must have at least two tags if present. If you only want one, use \"companyHasTag\" instead.");
            }

            if (companyDoesNotHaveTags != null && companyDoesNotHaveTags.Count == 0) {
                WIIC.validationErrors.Add($"{path}.companyDoesNotHaveTags must have at least two tags if present. If you only want one, use \"companyDoesNotHaveTag\" instead.");
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

            if (companyHasAnyTag != null && !WIIC.sim.CompanyTags.Intersect(companyHasAnyTag).Any()) {
                WIIC.l.Log($"    Company has none of the tags: \"{String.Join("\", \"", companyHasAnyTag.ToArray())}\", any of which is required");
                return false;
            }

            if (companyHasAllTags != null && !companyHasAllTags.Except(WIIC.sim.CompanyTags).Any()) {
                WIIC.l.Log($"    Company missing one of: \"{String.Join("\", \"", companyHasAllTags.ToArray())}\", all of which are required");
                return false;
            }

            if (companyDoesNotHaveTags != null && companyDoesNotHaveTags.Intersect(WIIC.sim.CompanyTags).Any()) {
                WIIC.l.Log($"    Company has at least one of the tags: \"{String.Join("\", \"", companyDoesNotHaveTags.ToArray())}\", none of which are allowed");
                return false;
            }

            return true;
        }
    }

    public class CampaignEvent {
        public string id;

        public void validate(string path) {
            if (String.IsNullOrEmpty(id)) {
                WIIC.validationErrors.Add($"{path} must have an \"id\"");
                return;
            }

            EventDef_MDD eventDefMDD = MetadataDatabase.Instance.GetEventDef(id);
            if (eventDefMDD == null) {
                WIIC.validationErrors.Add($"{path}.id \"{id}\" does not seem to exist");
            }

            EventScope scope = eventDefMDD.EventScopeEntry.EventScope;
            if (scope != EventScope.Company && scope != EventScope.Commander) {
                WIIC.validationErrors.Add($"{path}.id has scope \"{scope}\". Only Company and Commander events are supported");
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
                WIIC.validationErrors.Add($"{path} must have all of [name, employer, employerPortrait, target, at, description]");
                return;
            }

            if (!Campaign.defExists(BattleTechResourceType.CastDef, employerPortrait)) {
                WIIC.validationErrors.Add($"{path}.employerPortrait \"{employerPortrait}\" does not seem to be a valid castDef");
            }
            if (employer != "OWNER" && !Campaign.defExists(BattleTechResourceType.FactionDef, "faction_" + employer)) {
                WIIC.validationErrors.Add($"{path}.employer \"{employer}\" does not seem to be a valid factionDef");
            }
            if (target != "OWNER" && !Campaign.defExists(BattleTechResourceType.FactionDef, "faction_" + target)) {
                WIIC.validationErrors.Add($"{path}.target \"{target}\" does not seem to be a valid factionDef");
            }
            if (!Campaign.defExists(BattleTechResourceType.StarSystemDef, at)) {
                WIIC.validationErrors.Add($"{path}.at \"{at}\" does not seem to be a valid star system");
            }
        }

        public Flashpoint toFlashpoint() {
            Flashpoint fp = new Flashpoint();
            fp.GUID = "CampaignFakeFlashpoint";
            fp.CurStatus = Flashpoint.Status.WAITING_FOR_DATA;
            fp.CurSystem = WIIC.sim.GetSystemById(at);
            fp.EmployerValue = Utilities.getFactionValueByName(employer, fp.CurSystem);

            fp.Def = new FlashpointDef();

            // Use the HM prefix to style it in green, differentiating from normal campaign flashpoints
            fp.Def.Description = new BaseDescriptionDef("fp_HM_" + name, name, description, "uixTxrSpot_campaignOutcomeVictory");
            fp.Def.Difficulty = difficulty;
            fp.Def.TargetFaction = target  == "OWNER" ? Utilities.getFactionValueByName(target, fp.CurSystem).FactionDef.factionID : target;
            fp.Def.FlashpointDescriberCastDefId = employerPortrait;
            fp.Def.FlashpointLength = FlashpointDef.EngagementLength.CAMPAIGN;
            fp.Def.AllowRefitTime = true;
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
        public string mapName;
        public string onFailGoto;
        public string postContractEvent;

        public bool immediate;
        public int? withinDays;

        public void validate(string path, CampaignNodes nodes) {
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(employer) || String.IsNullOrEmpty(target) || String.IsNullOrEmpty(onFailGoto)) {
                WIIC.validationErrors.Add($"{path} must have all of [id, employer, target, onFailGoto]");
                return;
            }

            if (MetadataDatabase.Instance.Query<Contract_MDD>("SELECT * from Contract WHERE ContractID = @ID", new { ID = id }).ToArray().Length == 0) {
                WIIC.validationErrors.Add($"{path}.id \"{id}\" does not appear to exist");
            }

            if (employer != "OWNER" && !Campaign.defExists(BattleTechResourceType.FactionDef, "faction_" + employer)) {
                WIIC.validationErrors.Add($"{path}.employer \"{employer}\" does not seem to be 'EMPLOYER' or a valid factionDef");
            }
            if (target != "OWNER" &&!Campaign.defExists(BattleTechResourceType.FactionDef, "faction_" + target)) {
                WIIC.validationErrors.Add($"{path}.target \"{target}\" does not seem to be 'EMPLOYER' or a valid factionDef");
            }

            if (onFailGoto != "Exit" && !nodes.ContainsKey(onFailGoto)) {
                WIIC.validationErrors.Add($"{path}.onFailGoto must point to another node or be \"Exit\"; \"{onFailGoto}\" is unknown");
            }

            if (postContractEvent != null && MetadataDatabase.Instance.GetEventDef(postContractEvent) == null) {
                WIIC.validationErrors.Add($"{path}.postContractEvent \"{postContractEvent}\" does not seem to exist");
            }

            if (immediate && withinDays != null) {
                WIIC.validationErrors.Add($"{path} can only have onee of [immediate, withinDays]");
            }

            if (withinDays < 0) {
                WIIC.validationErrors.Add($"{path}.withinDays must be >= 0");
            }
        }
    }

    public class CampaignConversation {
        public string id;
        public string header;
        public string subheader;
        public CampaignConversationCharacters characters = new CampaignConversationCharacters();

        public void validate(string path) {
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(header) || String.IsNullOrEmpty(subheader)) {
                WIIC.validationErrors.Add($"{path} must have all of [id, header, subheader]");
                return;
            }
        }
    }

    public class CampaignConversationCharacters {
        public bool Darius = true;
        public bool Farah = true;
        public bool Sumire = true;
        public bool Yang = true;
        public bool Kamea = false;
        public bool Alexander = false;

        public void apply() {
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.DARIUS, this.Darius);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.FARAH, this.Farah);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.SUMIRE, this.Sumire);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.YANG, this.Yang);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.KAMEA, this.Kamea);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.ALEXANDER, this.Alexander);
        }

        public void reset() {
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.DARIUS, true);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.FARAH, true);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.SUMIRE, true);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.YANG, true);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.KAMEA, false);
            WIIC.sim.SetCharacterVisibility(SimGameState.SimGameCharacterType.ALEXANDER, false);
        }
    }

    public class CampaignWait {
        public int days;
        public string workOrder;
        public string sprite;

        public void validate(string path) {
            if (days < 1) {
                WIIC.validationErrors.Add($"{path}.days must be > 0  (currently {days})");
            }

            if (workOrder == null && sprite != null) {
                WIIC.validationErrors.Add($"{path}.sprite does not make sense without \"workOrder\" also set");
            }

            if (sprite != null && !Campaign.defExists(BattleTechResourceType.Sprite, sprite)) {
                WIIC.validationErrors.Add($"{path}.sprite \"{sprite}\" does not seem to be a valid sprite");
            }
        }
    }

    public class CampaignPopup {
        public string title;
        public string sprite;
        public string message;

        public void validate(string path) {
            if (title == null || sprite == null || message == null) {
                WIIC.validationErrors.Add($"{path} must have all of [title, sprite, message]");
            }

            if (!Campaign.defExists(BattleTechResourceType.Sprite, sprite)) {
                WIIC.validationErrors.Add($"{path}.sprite \"{sprite}\" does not seem to be a valid sprite");
            }
        }
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
        public CampaignWait wait;
        public CampaignPopup popup;

        public void validate(string path, CampaignNodes nodes, bool isLastEntry) {
            int count = 0;
            count += @goto == null ? 0 : 1;
            count += @event == null ? 0 : 1;
            count += video == null ? 0 : 1;
            count += reward == null ? 0 : 1;
            count += fakeFlashpoint == null ? 0 : 1;
            count += contract == null ? 0 : 1;
            count += conversation == null ? 0 : 1;
            count += wait == null ? 0 : 1;
            count += popup == null ? 0 : 1;

            if (count != 1) {
                WIIC.validationErrors.Add($"{path} must have exactly one of [goto, event, video, reward, fakeFlashpoint, contract, conversation, wait, popup]. It has {count} of them");
            }
            if (@goto != null && @goto != "Exit" && !nodes.ContainsKey(@goto)) {
                WIIC.validationErrors.Add($"{path}.goto must point to another node or be \"Exit\"; \"{@goto}\" is unknown");
            }

            @if?.validate($"{path}.if");
            @event?.validate($"{path}.event");
            fakeFlashpoint?.validate($"{path}.fakeFlashpoint");
            contract?.validate($"{path}.contract", nodes);
            conversation?.validate($"{path}.conversation");
            wait?.validate($"{path}.wait");
            popup?.validate($"{path}.popup");

            if (isLastEntry) {
                if (@goto == null) {
                    WIIC.validationErrors.Add($"{path} is the last entry in the node, and is missing \"goto\". You must say what happens next! Use \"goto: Exit\" if this the end of the campaign");
                }

                if ( @if != null) {
                    WIIC.validationErrors.Add($"{path} is the last entry in the node, and cannot have an \"if\" clause");
                }
            }
        }
    }

    public class Campaign {
        public string name;
        public CampaignNodes nodes = new CampaignNodes();
        public string beginsAt;

        public static List<string> loadSprites = new List<string>();

        public void validate(string filepath) {
            if (String.IsNullOrEmpty(name) || String.IsNullOrEmpty(beginsAt)) {
                WIIC.validationErrors.Add($"{filepath} is missing a \"name\" or \"beginsAt\"");
            }

            if (!nodes.ContainsKey("Start")) {
                WIIC.validationErrors.Add($"{name}.nodes[\"Start\"] is required; campaign needs a starting point");
            }

            if (nodes.ContainsKey("Exit")) {
                WIIC.validationErrors.Add($"{name}.nodes[\"Exit\"] is not allowed; it is a reserved name");
            }

            foreach (string nodeKey in nodes.Keys) {
                List<CampaignEntry> node = nodes[nodeKey];
                if (node.Count == 0) {
                    WIIC.validationErrors.Add($"{name}.nodes[\"{nodeKey}\"] must have at least one entry");
                }

                for (int i = 0; i < node.Count; i++) {
                    node[i].validate($"{name}.nodes.{nodeKey}.{i}", nodes, i == node.Count - 1);
                }
            }
        }

        public static bool defExists(BattleTechResourceType type, string id) {
            BattleTechResourceLocator rl = SceneSingletonBehavior<DataManagerUnityInstance>.Instance.DataManager.ResourceLocator;

            if (type == BattleTechResourceType.Sprite) {
                loadSprites.Add(id);
            }
            return rl.EntryByID(id, type) != null;
        }
    }
}
