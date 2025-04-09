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
using BattleTech.StringInterpolation;
using BattleTech.UI;
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    [JsonObject(MemberSerialization.OptIn)]
    public class ExtendedContract {
        [JsonProperty]
        public string locationID;
        public StarSystem location;

        [JsonProperty]
        public string type;
        public ExtendedContractType extendedType;

        [JsonProperty]
        public int countdown;

        [JsonProperty]
        public int? currentDay = null;

        [JsonProperty]
        public string employerName;
        public FactionValue employer;

        [JsonProperty]
        public string targetName;
        public FactionValue target;

        [JsonProperty]
        public string currentContractName;

        public ExtendedContract() {
            // Empty constructor used for deserialization.
        }

        // WIIC:Attack:{...}
        private static Regex SERIALIZED_TAG = new Regex("^WIIC:(?<type>.*?):(?<json>\\{.*\\})$", RegexOptions.Compiled);
        private static Regex OLD_SERIALIZED_TAG = new Regex("^WIIC:(?<json>\\{.*\\})$", RegexOptions.Compiled);
        private static Regex OLD_SERIALIZED_TAG_TYPE = new Regex("\"type\":\"(?<type>Attack|Raid)\"", RegexOptions.Compiled);
        private static Regex OLD_SERIALIZED_TAG_ATTACKER = new Regex("\"attackerName\":\"(?<attacker>.+?)\"", RegexOptions.Compiled);
        public static bool isSerializedExtendedContract(string tag) {
            return tag.StartsWith("WIIC:");
        }

        public string Serialize() {
            string json = JsonConvert.SerializeObject(this, WIIC.serializerSettings);
            return $"WIIC:{this.type}:{json}";
        }

        public ExtendedContract(StarSystem contractLocation, FactionValue employerFaction, FactionValue targetFaction, ExtendedContractType contractType) {
            location = contractLocation;
            locationID = contractLocation.ID;

            employer = employerFaction;
            employerName = employerFaction.Name;
            target = targetFaction;
            targetName = targetFaction.Name;

            extendedType = contractType;
            type = contractType.name;
            countdown = Utilities.rng.Next(extendedType.availableFor[0], extendedType.availableFor[1]);

            if (extendedType.travelContracts || WIIC.sim.CurSystem == location) {
                spawnParticipationContracts();
            }
        }

        public bool isEmployedHere {
            get {
                return WIIC.sim.CurSystem == location && (WIIC.sim.CompanyTags.Contains("WIIC_extended_contract"));
            }
        }

        public virtual void onEnterSystem() {
            WIIC.l.Log($"Entering Extended Contract system ({type}). In-system contracts: {!extendedType.travelContracts}");
            if (!extendedType.travelContracts) {
                this.spawnParticipationContracts();
            }
        }

        public virtual void onLeaveSystem() {
            WIIC.l.Log($"Leaving Extended Contract system ({type}). In-system contracts: {!extendedType.travelContracts}");
            if (!extendedType.travelContracts) {
                this.removeParticipationContracts();
            }
        }

        public virtual Entry currentEntry {
            get {
                if (currentDay == null) {
                    return null;
                }

                string entryName = extendedType.schedule[currentDay ?? 0];

                if (entryName == "") {
                    return null;
                }

                return extendedType.entries[entryName];
            }
        }

        public virtual bool passDay() {
            if (isEmployedHere) {
                return passDayEmployed();
            }

            countdown--;
            WIIC.l.Log($"Countdown {countdown} for {type} at {locationID}.");
            // Don't remove this extended contract if the player has accepted it and is flying there to participate.
            if (countdown <= 0) {
                if (!isParticipationContract(WIIC.sim.ActiveTravelContract)) {
                    return true;
                }
                WIIC.l.Log($"    Leaving active because participation contract has been accepted.");
            }

            return false;
        }

        public virtual bool passDayEmployed() {
            currentDay++;

            if (currentDay >= extendedType.schedule.Length) {
                // This should never happen; on the final day, we should already have removed the contract.
                // But in case there was an error somehow, we at least want to clean up the following day.
                return true;
            }

            if (currentEntry == null) {
                WIIC.l.Log($"Day {currentDay} of {type}, nothing happening.");
            } else {
                WIIC.l.Log($"Day {currentDay} of {type}, running {extendedType.schedule[currentDay ?? 0]}.");
                runEntry(currentEntry);
            }

            return currentDay == extendedType.schedule.Length - 1;
        }

        public void runEntry(Entry entry) {
            if (entry.invokeMethod != null) {
                this.GetType().GetMethod(entry.invokeMethod).Invoke(this, new object[]{});
            }

            foreach (string eventID in entry.triggerEvent) {
                if (!WIIC.sim.DataManager.SimGameEventDefs.TryGet(eventID, out SimGameEventDef eventDef)) {
                    WIIC.l.LogError($"Couldn't find event {eventID} on day {currentDay} of {type}.");
                    return;
                }

                if (WIIC.sim.MeetsRequirements(eventDef.Requirements) && WIIC.sim.MeetsRequirements(eventDef.AdditionalRequirements)) {
                    WIIC.l.Log($"Triggering event {eventID} on day {currentDay} of {type}.");

                    SimGameEventTracker eventTracker = new SimGameEventTracker();
                    eventTracker.Init(new[] { EventScope.Company }, 0, 0, SimGameEventDef.SimEventType.NORMAL, WIIC.sim);
                    WIIC.sim.GetInterruptQueue().QueueEventPopup(eventDef, EventScope.Company, eventTracker);

                    return;
                }

                WIIC.l.Log($"Considered event {eventID}, but requirements did not match.");
            }

            double rand = Utilities.rng.NextDouble();
            if (rand <= entry.contractChance) {
                WIIC.l.Log($"Triggering contract on day {currentDay} of {type} ({entry.contractChance} chance)");
                Contract contract = null;

                if (entry.contract.Length > 0) {
                    foreach (string contractName in entry.contract) {
                        contract = ContractManager.getContractByName(contractName, employer, target);
                        if (entry.ignoreContractRequirements) {
                            WIIC.l.Log($"Considering {contractName} - Ignoring requirements");
                            break;
                        }
                        else if (WIIC.sim.MeetsRequirements(contract.Override.requirementList.ToArray())) {
                            WIIC.l.Log($"Considering {contractName} - Meets requirements");
                            break;
                        } else {
                            WIIC.l.Log($"Considering {contractName} - Does not meet requirements");
                            contract = null;
                        }
                    }
                } else if (entry.randomContract.Length > 0) {
                    List<Contract> contracts = new List<Contract>();
                    foreach (string contractName in entry.randomContract) {
                        contract = ContractManager.getContractByName(contractName, employer, target);
                        if (entry.ignoreContractRequirements) {
                            WIIC.l.Log($"Considering {contractName} - Ignoring requirements");
                            contracts.Add(contract);
                        }
                        else if (WIIC.sim.MeetsRequirements(contract.Override.requirementList.ToArray())) {
                            WIIC.l.Log($"Considering {contractName} - Meets requirements");
                            contracts.Add(contract);
                        } else {
                            WIIC.l.Log($"Considering {contractName} - Does not meet requirements");
                        }
                    }
                    contract = contracts.Count > 0 ? Utilities.Choice(contracts) : null;
                } else {
                    WIIC.l.Log($"Generating procedural contract.");
                    contract = ContractManager.getNewProceduralContract(employer, target, entry.allowedContractTypes);
                }

                if (contract != null) {
                    launchContract(entry, contract);

                    return;
                }
            }

            if (!String.IsNullOrEmpty(entry.popupMessage)) {
                WIIC.l.Log($"Running popupMessage on day {currentDay} of {type}.");
                GameContext context = new GameContext(WIIC.sim.Context);
                context.SetObject(GameContextObjectTagEnum.TeamEmployer, employer);
                context.SetObject(GameContextObjectTagEnum.TeamTarget, target);
                string title = Interpolator.Interpolate(String.IsNullOrEmpty(entry.popupTitle) ? type : entry.popupTitle, context);
                WIIC.l.Log($"    {title}");
                string message = Interpolator.Interpolate(entry.popupMessage, context);
                WIIC.l.Log($"    {message}");

                SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
                queue.QueuePauseNotification(title, message, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, delegate {
                    try {
                        WIIC.l.Log($"popupMessage button clicked.");
                        giveRewardByDifficulty(entry);
                    } catch (Exception e) {
                        WIIC.l.LogException(e);
                    }
                });
                return;
            }

            giveRewardByDifficulty(entry);
        }

        public void giveRewardByDifficulty(Entry entry) {
            int diff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);
            WIIC.l.Log($"Considering rewardByDifficulty on day {currentDay} of {type}. System difficulty is {diff}");
            string itemCollection = null;
            for (int i = 0; i <= diff; i++) {
                if (entry.rewardByDifficulty.ContainsKey(i)) { itemCollection = entry.rewardByDifficulty[i]; }
            }

            WIIC.l.Log($"rewardByDifficulty chose {itemCollection}.");
            Utilities.giveReward(itemCollection);
        }

        public virtual void spawnParticipationContracts() {
            SimGameReputation employerRep = WIIC.sim.GetReputation(employer);
            SimGameReputation targetRep = WIIC.sim.GetReputation(target);

            if (WIIC.settings.wontHirePlayer.Contains(employer.Name)) {
                WIIC.l.Log($"Skipping hireContract for {type} at {location.Name} because employer {employer.Name} wontHirePlayer");
            } else if (employerRep < minRepToHelp()) {
                WIIC.l.Log($"Skipping hireContract for {type} at {location.Name} because player has too low rep with {employer.Name} (has {employerRep}, needs {minRepToHelp()}).");
            } else {
                WIIC.l.Log($"Spawning travel hireContract {extendedType.hireContract} at {location.Name} for {type}");
                ContractManager.addTravelContract(extendedType.hireContract, location, employer, target);
            }

            if (extendedType.targetHireContract != null) {
                if (WIIC.settings.wontHirePlayer.Contains(target.Name)) {
                    WIIC.l.Log($"Skipping targetHireContract for {type} at {location.Name} because target {target.Name} wontHirePlayer");
                } else if (targetRep < minRepToHelp()) {
                    WIIC.l.Log($"Skipping targetHireContract for {type} at {location.Name} because player has too low rep with {target.Name} (has {targetRep}, needs {minRepToHelp()}).");
                } else {
                    WIIC.l.Log($"    Also adding {extendedType.targetHireContract} from targetHireContract");
                    ContractManager.addTravelContract(extendedType.targetHireContract, location, target, employer);
                }
            }
        }

        public virtual SimGameReputation minRepToHelp() {
            return SimGameReputation.DISLIKED;
        }

        public bool isParticipationContract(Contract c) {
            if (c == null) { return false; }
            return c != null && (c.Override.ID == extendedType.hireContract || c.Override.ID == extendedType.targetHireContract) && c.TargetSystem == locationID;
        }

        public virtual void removeParticipationContracts() {
            WIIC.l.Log($"Cleaning up participation contracts for {type} at {location.Name}.");
            WIIC.sim.GlobalContracts.RemoveAll(isParticipationContract);
        }

        public virtual void acceptContract(string contract) {
            countdown = 0;
            WIIC.l.Log($"Player embarked on {type} at {location.Name}. Adding WIIC_extended_contract company tag and work order item.");

            if (contract == extendedType.targetHireContract) {
                (employer, target) = (target, employer);
                (employerName, targetName) = (targetName, employerName);
            }

            removeParticipationContracts();
            WIIC.sim.CompanyTags.Add("WIIC_extended_contract");
            WIIC.sim.SetSimRoomState(DropshipLocation.SHIP);

            currentDay = -1;
            passDayEmployed();
            WIIC.sim.RoomManager.AddWorkQueueEntry(workOrder);
            if (extraWorkOrder != null) {
                WIIC.sim.RoomManager.AddWorkQueueEntry(extraWorkOrder);
            }

            WIIC.sim.RoomManager.RefreshTimeline(false);
        }

        public virtual void launchContract(Entry entry, Contract contract) {
            WIIC.l.Log($"Offering {type} mission ID={contract.Override.ID}.");
            if (contract.Override.contractRewardOverride < 0) {
                WIIC.l.Log($"contractRewardOverride < 0, generating.");
                contract.Override.contractRewardOverride = WIIC.sim.CalculateContractValueByContractType(contract.ContractTypeValue, contract.Override.finalDifficulty, WIIC.sim.Constants.Finances.ContractPricePerDifficulty, WIIC.sim.Constants.Finances.ContractPriceVariance, 0);
            }

            WIIC.l.Log($"contractRewardOverride: {contract.Override.contractRewardOverride}, contractPayoutMultiplier: {entry.contractPayoutMultiplier}, InitialContractValue: {contract.InitialContractValue}");
            contract.SetInitialReward((int)Math.Floor(contract.Override.contractRewardOverride * entry.contractPayoutMultiplier));

            contract.SetupContext();

            WIIC.l.Log($"salvagePotential: {contract.Override.salvagePotential}, contractBonusSalvage: {entry.contractBonusSalvage}");
            contract.SalvagePotential += entry.contractBonusSalvage;
            contract.SalvagePotential = Math.Min(WIIC.sim.Constants.Salvage.MaxSalvagePotential, Math.Max(0, contract.SalvagePotential));
            contract.Override.salvagePotential = contract.SalvagePotential;
            contract.SetExpiration(0);

            // Make contract values available under RES_OBJ
            // eg: "Contract name: {RES_OBJ.Name} is a {RES_OBJ.ContractTypeValue.FriendlyName} contract"
            // This is done automatically by our constructor patch, but that can be bypassed if this contract
            // was rehdyrated from a save game.
            contract.GameContext.SetObject(GameContextObjectTagEnum.ResultObject, contract);
            string message = contract.RunMadLib(entry.contractMessage);

            string title = Strings.T($"{extendedType.name} Mission");
            string primaryButtonText = Strings.T("Launch mission");
            string cancel = Strings.T("Pass");

            if (entry.declinePenalty == DeclinePenalty.BadFaith) {
                message += $"\n\nDeclining this contract is equivelent to a bad faith withdrawal. It will severely harm our reputation with {employer.FactionDef.ShortName}.";
            } else if (entry.declinePenalty == DeclinePenalty.BreakContract) {
                message += $"\n\nDeclining this contract will break our {type} contract with {employer.FactionDef.ShortName}.";
            }

            WIIC.l.Log(message);

            SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
            queue.QueuePauseNotification(title, message, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, delegate {
                try {
                    WIIC.l.Log($"Accepted {type} mission ID={contract.Override.ID}.");
                    if (!WIIC.sim.GlobalContracts.Contains(contract)) {
                        WIIC.sim.GlobalContracts.Add(contract);
                    }

                    currentContractName = contract.Override.ID;
                    Utilities.sendToCommandCenter();
                } catch (Exception e) {
                    WIIC.l.LogException(e);
                }
            }, primaryButtonText, delegate {
                WIIC.l.Log($"Passed on {type} mission, declinePenalty is {entry.declinePenalty.ToString()}.");
                if (!WIIC.sim.CurSystem.SystemContracts.Contains(contract)) {
                    // Happens when restoring a pre-drop save and declining a contract they had previously accepted
                    WIIC.sim.CurSystem.SystemContracts.Remove(contract);
                }
                applyDeclinePenalty(entry.declinePenalty);
            }, cancel);

            if (!queue.IsOpen) {
                queue.DisplayIfAvailable();
            }
        }

        public virtual void applyDeclinePenalty(DeclinePenalty declinePenalty) {
            WIIC.l.Log($"Applying DeclinePenalty {declinePenalty.ToString()} for {type} with employer {employer.Name}");

            if (declinePenalty == DeclinePenalty.BadFaith || declinePenalty == DeclinePenalty.BreakContract) {
                if (employer.DoesGainReputation) {
                    float employerRepBadFaithMod = WIIC.sim.Constants.Story.EmployerRepBadFaithMod;
                    WIIC.l.Log($"employerRepBadFaithMod: {employerRepBadFaithMod}");
                    WIIC.l.Log($"difficulty: {location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
                    int num = (int) Math.Round(location.Def.GetDifficulty(SimGameState.SimGameType.CAREER) * employerRepBadFaithMod);

                    WIIC.sim.SetReputation(employer, num);
                    WIIC.sim.SetReputation(FactionEnumeration.GetMercenaryReviewBoardFactionValue(), num);
                }
            }

            if (declinePenalty == DeclinePenalty.BreakContract) {
                WIIC.extendedContracts.Remove(location.ID);
                WIIC.sim.CompanyTags.Remove("WIIC_extended_contract");
                WIIC.sim.RoomManager.RefreshTimeline(false);
            }
        }

        public virtual string getDescription() {
            StringBuilder description = new StringBuilder();

            description.AppendLine(Strings.T("<b><color=#ee4242>Hired by {0} for {1}</color></b>", employer.FactionDef.ShortName, type));
            description.AppendLine(Strings.T("We've been on assignment {0} days. The contract will complete after {1}.", currentDay, extendedType.schedule.Length - 1));

            return description.ToString();
        }

        protected WorkOrderEntry_Notification _workOrder;
        public virtual WorkOrderEntry_Notification workOrder {
            get {
                if (_workOrder == null) {
                    string title = Strings.T($"{type} Complete");
                    _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "extendedContractComplete", title);
                }

                _workOrder.SetCost(extendedType.schedule.Length - (currentDay ?? 0) - 1);
                return _workOrder;
            }
            set {
                _workOrder = value;
            }
        }

        protected WorkOrderEntry_Notification _extraWorkOrder;
        public virtual WorkOrderEntry_Notification extraWorkOrder {
            get {
                if (_extraWorkOrder == null) {
                    _extraWorkOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "extendedContractExtra", "");
                    WIIC.l.Log("Generated _extraWorkOrder");
                }

                for (int day = (currentDay ?? 0) + 1; day < extendedType.schedule.Length; day++) {
                    string entryName = extendedType.schedule[day];
                    if (extendedType.entries.ContainsKey(entryName)) {
                        Entry entry = extendedType.entries[entryName];
                        if (entry.workOrder != null) {
                            _extraWorkOrder.SetDescription(entry.workOrder);
                            _extraWorkOrder.SetCost(day - (currentDay ?? 0));

                            return _extraWorkOrder;
                        }
                    }
                }

                return null;
            }
            set {
                _extraWorkOrder = value;
            }
        }

        public virtual void addToMap() {
            if (extendedType.mapMarker == null) {
                return;
            }

            MapMarker mapMarker = new MapMarker(location.ID, extendedType.mapMarker);
            ColourfulFlashPoints.Main.addMapMarker(mapMarker);

            if (!WIIC.fluffDescriptions.ContainsKey(location.ID)) {
                WIIC.l.Log($"Filled fluff description entry for {location.ID}: {location.Def.Description.Details}");
                WIIC.fluffDescriptions[location.ID] = location.Def.Description.Details;
            }

            string description = getMapDescription() + WIIC.fluffDescriptions[location.ID];
            location.Def.Description.Details = description;
        }

        public virtual string getMapDescription() {
            return "";
        }

        public static ExtendedContract Deserialize(string tag) {
            MatchCollection matches = SERIALIZED_TAG.Matches(tag);
            string type;
            string json;
            string oldAttacker = "";

            if (matches.Count > 0) {
                json = matches[0].Groups["json"].Value;
                type = matches[0].Groups["type"].Value;
            } else {
                matches = OLD_SERIALIZED_TAG.Matches(tag);

                if (matches.Count == 0) {
                    throw new Exception($"Tried to deserialize invalid Extended Contract tag: {tag}");
                }

                // This is a flareup from before the Extended Contract rewrite; we'll have to do some
                // massaging to read it into the new format
                json =  matches[0].Groups["json"].Value;
                type = OLD_SERIALIZED_TAG_TYPE.Matches(json)[0].Groups["type"].Value;
                oldAttacker = OLD_SERIALIZED_TAG_ATTACKER.Matches(json)[0].Groups["attacker"].Value;
            }

            if (type == "Attack") {
                Attack attack = JsonConvert.DeserializeObject<Attack>(json);
                if (oldAttacker != "") { attack.fixOldEmployer(oldAttacker); }
                attack.initAfterDeserialization();
                return attack;
            }
            if (type == "Raid") {
                Raid raid = JsonConvert.DeserializeObject<Raid>(json);
                if (oldAttacker != "") { raid.fixOldEmployer(oldAttacker); }
                raid.initAfterDeserialization();
                return raid;
            }

            ExtendedContract newExtendedContract = JsonConvert.DeserializeObject<ExtendedContract>(json);
            newExtendedContract.initAfterDeserialization();

            return newExtendedContract;
        }

        public virtual void initAfterDeserialization() {
            location = WIIC.sim.GetSystemById(locationID);
            employer = FactionEnumeration.GetFactionByName(employerName);
            target = FactionEnumeration.GetFactionByName(targetName);
            extendedType = WIIC.extendedContractTypes[type];
        }
    }
}
