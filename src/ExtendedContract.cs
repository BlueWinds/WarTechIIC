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
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    [JsonObject(MemberSerialization.OptIn)]
    public class ExtendedContract {
        public StarSystem location;
        [JsonProperty]
        public string locationID;

        public ExtendedContractType extendedType;
        [JsonProperty]
        public string type;

        [JsonProperty]
        public int countdown;

        // Inits to -1 because acceptContract() immediately invokes passDayEmployed()
        // in order to run the day 0 entry.
        [JsonProperty]
        public int currentDay = -1;

        [JsonProperty]
        public int playerDrops = 0;

        public FactionValue employer;
        [JsonProperty]
        public string employerName;

        public FactionValue target;
        [JsonProperty]
        public string targetName;

        public string currentContractName = "";
        public string currentEntry;

        public bool droppingForContract = false;

        public ExtendedContract() {
            // Empty constructor used for deserialization.
        }

        // WIIC:Attack:{...}
        private static Regex SERIALIZED_TAG = new Regex("^WIIC:(?<type>.*?):(?<json>\\{.*\\})$", RegexOptions.Compiled);
        private static Regex OLD_SERIALIZED_TAG = new Regex("^WIIC:(?<json>\\{.*\\})$", RegexOptions.Compiled);
        private static Regex OLD_SERIALIZED_TAG_TYPE = new Regex("\"type\":\"(?<type>Attack|Raid)\"", RegexOptions.Compiled);
        public static bool isSerializedExtendedContract(string tag) {
            return tag.StartsWith("WIIC:");
        }

        public string Serialize() {
            string json = JsonConvert.SerializeObject(this);
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

            if (extendedType.travelContracts) {
                spawnParticipationContracts();
            }
        }

        public bool isEmployedHere {
            get {
                return WIIC.sim.CurSystem == location && (WIIC.sim.CompanyTags.Contains("WIIC_extended_contract"));
            }
        }

        public virtual void onEnterSystem() {
            WIIC.modLog.Debug?.Write($"Entering Extended Contract system ({type}). In-system contracts: {!extendedType.travelContracts}");
            if (!extendedType.travelContracts) {
                this.spawnParticipationContracts();
            }
        }

        public virtual void onLeaveSystem() {
            WIIC.modLog.Debug?.Write($"Leaving Extended Contract system ({type}). In-system contracts: {!extendedType.travelContracts}");
            if (!extendedType.travelContracts) {
                this.removeParticipationContracts();
            }
        }

        public virtual bool passDay() {

            if (isEmployedHere) {
                return passDayEmployed();
            }

            countdown--;
            WIIC.modLog.Trace?.Write($"Countdown {countdown} for {type} at {locationID}.");
            // Don't remove this extended contract if the player has accepted it and is flying there to participate.
            if (countdown <= 0) {
                if (!isParticipationContract(WIIC.sim.ActiveTravelContract)) {
                    removeParticipationContracts();
                    return true;
                }
                WIIC.modLog.Trace?.Write($"    Leaving active because participation contract has been accepted.");
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

            currentEntry = extendedType.schedule[currentDay];

            if (currentEntry == "") {
                WIIC.modLog.Info?.Write($"Day {currentDay} of {type}, nothing happening.");
            } else if (extendedType.entries.ContainsKey(currentEntry)) {
                WIIC.modLog.Info?.Write($"Day {currentDay} of {type}, running {currentEntry}.");
                runEntry(extendedType.entries[currentEntry]);
            } else {
                WIIC.modLog.Error?.Write($"ExtendedContractType references '{currentEntry}' at schedule[{currentDay}], but this is not present in its entries dictionary. Valid keys are {string.Join(", ", extendedType.entries.Keys)}");
            }

            return currentDay == extendedType.schedule.Length - 1;
        }

        public void runEntry(Entry entry) {
            if (entry.invokeMethod != null) {
                this.GetType().GetMethod(entry.invokeMethod).Invoke(this, new object[]{});
            }

            foreach (string eventID in entry.triggerEvent) {
                if (!WIIC.sim.DataManager.SimGameEventDefs.TryGet(eventID, out SimGameEventDef eventDef)) {
                    WIIC.modLog.Error?.Write($"Couldn't find event {eventID} on day {currentDay} of {type}.");
                    return;
                }

                if (WIIC.sim.MeetsRequirements(eventDef.Requirements) && WIIC.sim.MeetsRequirements(eventDef.AdditionalRequirements)) {
                    WIIC.modLog.Info?.Write($"Triggering event {eventID} on day {currentDay} of {type}.");

                    SimGameEventTracker eventTracker = new SimGameEventTracker();
                    eventTracker.Init(new[] { EventScope.Company }, 0, 0, SimGameEventDef.SimEventType.NORMAL, WIIC.sim);
                    WIIC.sim.GetInterruptQueue().QueueEventPopup(eventDef, EventScope.Company, eventTracker);

                    return;
                }

                WIIC.modLog.Debug?.Write($"Considered event {eventID}, but requirements did not match.");
            }

            double rand = Utilities.rng.NextDouble();
            if (rand <= entry.contractChance) {
                WIIC.modLog.Info?.Write($"Triggering contract on day {currentDay} of {type} ({entry.contractChance} chance)");
                Contract contract = null;

                if (entry.contract.Length > 0) {
                    foreach (string contractName in entry.contract) {
                        contract = ContractManager.getContractByName(contractName, location, employer, target);
                        if (entry.ignoreContractRequirements) {
                            WIIC.modLog.Debug?.Write($"Considering {contractName} - Ignoring requirements");
                            break;
                        }
                        else if (WIIC.sim.MeetsRequirements(contract.Override.requirementList.ToArray())) {
                            WIIC.modLog.Debug?.Write($"Considering {contractName} - Meets requirements");
                            break;
                        } else {
                            WIIC.modLog.Debug?.Write($"Considering {contractName} - Does not meet requirements");
                            contract = null;
                        }
                    }
                } else if (entry.randomContract.Length > 0) {
                    List<Contract> contracts = new List<Contract>();
                    foreach (string contractName in entry.randomContract) {
                        contract = ContractManager.getContractByName(contractName, location, employer, target);
                        if (entry.ignoreContractRequirements) {
                            WIIC.modLog.Debug?.Write($"Considering {contractName} - Ignoring requirements");
                            contracts.Add(contract);
                        }
                        else if (WIIC.sim.MeetsRequirements(contract.Override.requirementList.ToArray())) {
                            WIIC.modLog.Debug?.Write($"Considering {contractName} - Meets requirements");
                            contracts.Add(contract);
                        } else {
                            WIIC.modLog.Debug?.Write($"Considering {contractName} - Does not meet requirements");
                        }
                    }
                    contract = contracts.Count > 0 ? Utilities.Choice(contracts) : null;
                } else {
                    WIIC.modLog.Debug?.Write($"Generating procedural contract.");
                    contract = ContractManager.getNewProceduralContract(location, employer, target, entry.allowedContractTypes);
                }

                if (contract != null) {
                    if (contract.Override.contractRewardOverride < 0) {
                        WIIC.modLog.Debug?.Write($"contractRewardOverride < 0, generating.");
                        contract.Override.contractRewardOverride = WIIC.sim.CalculateContractValueByContractType(contract.ContractTypeValue, contract.Override.finalDifficulty, WIIC.sim.Constants.Finances.ContractPricePerDifficulty, WIIC.sim.Constants.Finances.ContractPriceVariance, 0);
                    }

                    WIIC.modLog.Debug?.Write($"contractRewardOverride: {contract.Override.contractRewardOverride}, contractPayoutMultiplier: {entry.contractPayoutMultiplier}, InitialContractValue: {contract.InitialContractValue}");
                    contract.SetInitialReward((int)Math.Floor(contract.Override.contractRewardOverride * entry.contractPayoutMultiplier));

                    WIIC.modLog.Debug?.Write($"salvagePotential: {contract.Override.salvagePotential}, contractBonusSalvage: {entry.contractBonusSalvage}");

                    int salvage = contract.Override.salvagePotential + entry.contractBonusSalvage;
                    salvage = Math.Min(WIIC.sim.Constants.Salvage.MaxSalvagePotential, Math.Max(0, salvage));
                    contract.SalvagePotential = contract.Override.salvagePotential = salvage;

                    contract.SetupContext();

                    string message = contract.RunMadLib(entry.contractMessage);
                    launchContract(message, contract, entry.declinePenalty);

                    return;
                }
            }

            if (entry.rewardByDifficulty.Keys.Count > 1) {
                int diff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);
                WIIC.modLog.Debug?.Write($"Considering rewardByDifficulty on day {currentDay} of {type}. System difficulty is {diff}");
                string itemCollection = null;
                for (int i = 0; i <= diff; i++) {
                    if (entry.rewardByDifficulty.ContainsKey(i)) { itemCollection = entry.rewardByDifficulty[i]; }
                }

                WIIC.modLog.Info?.Write($"rewardByDifficulty chose {itemCollection}.");
                Utilities.giveReward(itemCollection);
            }
        }

        public virtual void spawnParticipationContracts() {
            int diff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);

            if (WIIC.settings.wontHirePlayer.Contains(employer.Name)) {
                WIIC.modLog.Trace?.Write($"Skipping hireContract for {type} at {location.Name} because employer {employer.Name} wontHirePlayer");
            } else {
                WIIC.modLog.Trace?.Write($"Spawning travel hireContract {extendedType.hireContract} at {location.Name} for {type}");
                ContractManager.addTravelContract(extendedType.hireContract, location, employer, target, diff);
            }

            if (extendedType.targetHireContract != null) {
                if (WIIC.settings.wontHirePlayer.Contains(target.Name)) {
                    WIIC.modLog.Trace?.Write($"Skipping targetHireContract for {type} at {location.Name} because target {target.Name} wontHirePlayer");
                } else {
                    WIIC.modLog.Trace?.Write($"    Also adding {extendedType.targetHireContract} from targetHireContract");
                    ContractManager.addTravelContract(extendedType.targetHireContract, location, target, employer, diff);
                }
            }
        }

        public bool isParticipationContract(Contract c) {
            if (c == null) { return false; }
            return c != null && (c.Override.ID == extendedType.hireContract || c.Override.ID == extendedType.targetHireContract) && c.TargetSystem == locationID;
        }

        public virtual void removeParticipationContracts() {
            WIIC.modLog.Debug?.Write($"Cleaning up participation contracts for {type} at {location.Name}.");
            WIIC.sim.GlobalContracts.RemoveAll(isParticipationContract);
        }

        public virtual void acceptContract(string contract) {
            countdown = 0;
            WIIC.modLog.Info?.Write($"Player embarked on {type} at {location.Name}. Adding WIIC_extended_contract company tag and work order item.");

            if (contract == extendedType.targetHireContract) {
                (employer, target) = (target, employer);
                (employerName, targetName) = (targetName, employerName);
            }

            removeParticipationContracts();
            WIIC.sim.CompanyTags.Add("WIIC_extended_contract");
            WIIC.sim.SetSimRoomState(DropshipLocation.SHIP);

            passDayEmployed();
            WIIC.sim.RoomManager.AddWorkQueueEntry(workOrder);
            if (extraWorkOrder != null) {
                WIIC.sim.RoomManager.AddWorkQueueEntry(extraWorkOrder);
            }

            WIIC.sim.RoomManager.RefreshTimeline(false);
        }

        public virtual void launchContract(string message, Contract contract, DeclinePenalty declinePenalty) {
            string title = Strings.T($"{extendedType.name} Mission");
            string primaryButtonText = Strings.T("Launch mission");
            string cancel = Strings.T("Pass");

            if (declinePenalty == DeclinePenalty.BadFaith) {
                message += $"\n\nDeclining this contract is equivelent to a bad faith withdrawal. It will severely harm our reputation with {employer.FactionDef.ShortName}.";
            } else if (declinePenalty == DeclinePenalty.BreakContract) {
                message += $"\n\nDeclining this contract will break our {type} contract with {employer.FactionDef.ShortName}.";
            }

            WIIC.modLog.Debug?.Write(message);

            SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
            queue.QueuePauseNotification(title, message, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, delegate {
                try {
                    WIIC.modLog.Info?.Write($"Accepted {type} mission {contract.Name}.");

                    currentContractName = contract.Name;
                    WIIC.sim.RoomManager.ChangeRoom(DropshipLocation.CMD_CENTER);
                    WIIC.sim.RoomManager.ForceShipRoomChangeOfRoom(DropshipLocation.CMD_CENTER);
                    WIIC.sim.RoomManager.RefreshDisplay();
                    WIIC.sim.ForceTakeContract(contract, false);
                    WIIC.sim.RoomManager.LeftDrawerWidget.SetCollapsed(true);
                } catch (Exception e) {
                    WIIC.modLog.Error?.Write(e);
                }
            }, primaryButtonText, delegate {
                WIIC.modLog.Info?.Write($"Passed on {type} mission, declinePenalty is {declinePenalty.ToString()}.");
                applyDeclinePenalty(declinePenalty);
            }, cancel);

            if (!queue.IsOpen) {
                queue.DisplayIfAvailable();
            }
        }

        public virtual void applyDeclinePenalty(DeclinePenalty declinePenalty) {
            WIIC.modLog.Info?.Write($"Applying DeclinePenalty {declinePenalty.ToString()} for {type} with employer {employer.Name}");

            if (declinePenalty == DeclinePenalty.BadFaith || declinePenalty == DeclinePenalty.BreakContract) {
                if (employer.DoesGainReputation) {
                    float employerRepBadFaithMod = WIIC.sim.Constants.Story.EmployerRepBadFaithMod;
                    WIIC.modLog.Info?.Write($"employerRepBadFaithMod: {employerRepBadFaithMod}");
                    WIIC.modLog.Info?.Write($"difficulty: {location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
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

            description.AppendLine(Strings.T("<b><color=#de0202>Hired by {0} for {1}</color></b>", employer.FactionDef.ShortName, type));
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

                _workOrder.SetCost(extendedType.schedule.Length - currentDay - 1);
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
                    WIIC.modLog.Debug?.Write("Generated _extraWorkOrder");
                }

                for (int day = currentDay + 1; day < extendedType.schedule.Length; day++) {
                    string entryName = extendedType.schedule[day];
                    if (extendedType.entries.ContainsKey(entryName)) {
                        Entry entry = extendedType.entries[entryName];
                        if (entry.workOrder != null) {
                            _extraWorkOrder.SetDescription(entry.workOrder);
                            _extraWorkOrder.SetCost(day - currentDay);

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
                WIIC.modLog.Trace?.Write($"Filled fluff description entry for {location.ID}: {location.Def.Description.Details}");
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
            bool old = false;

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
                old = true;
                json =  matches[0].Groups["json"].Value;
                type = OLD_SERIALIZED_TAG_TYPE.Matches(json)[0].Groups["type"].Value;
            }

            if (type == "Attack") {
                Attack attack = JsonConvert.DeserializeObject<Attack>(json);
                if (old) { attack.fixOldEmployer(); }
                attack.initAfterDeserialization();
                return attack;
            }
            if (type == "Raid") {
                Raid raid = JsonConvert.DeserializeObject<Raid>(json);
                if (old) { raid.fixOldEmployer(); }
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
