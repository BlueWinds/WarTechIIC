using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using Harmony;
using Localize;
using BattleTech;
using BattleTech.Data;
using BattleTech.UI;

namespace WarTechIIC {
    public enum DeclinePenalty {
        None,
        BadFaith,
        BreakContract
    }

    public enum SpawnLocation {
        Any,
        OwnSystem,
        NearbyEnemy
    }

    public class Entry {
        public string[] triggerEvent = new string[0];
        public double contractChance;
        public string[] contract = new string[0];
        public int[] allowedContractTypes = new int[0];
        public DeclinePenalty declinePenalty;
        public double contractPayoutMultiplier = 1;
        public int contractBonusSalvage = 0;
        public string contractMessage;
        public Dictionary<int, string> rewardByDifficulty = new Dictionary<int, string>();

        public void validate(string type, string key) {
            foreach (string evt in triggerEvent) {
                if (MetadataDatabase.Instance.GetEventDef(evt) == null) {
                    throw new Exception($"triggerEvent {evt} is not a valid event in {type} schedule[{key}].");
                }
            }
        }
    }

    public class ExtendedContractType {
        public string name;
        public RequirementDef[] requirementList;
        public int weight = 1;
        public SpawnLocation spawnLocation;
        public string[] employer;
        public string[] target;
        public string hireContract;
        public int[] availableFor;
        public string[] schedule;
        public Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        public ExtendedContractType(string newName) {
            name = newName;
        }

        public void validate() {
            FactionValue invalid = FactionEnumeration.GetInvalidUnsetFactionValue();

            foreach (string emp in employer) {
                if (emp == "Any") {
                    if (spawnLocation == SpawnLocation.Any) { throw new Exception($"employer Any is not valid with spawnLocation Any for ExtendedContractType {name}."); }
                } else if (emp == "OwnSystem") {
                    if (spawnLocation == SpawnLocation.OwnSystem) { throw new Exception($"employer OwnSystem is not valid with spawnLocation OwnSystem for ExtendedContractType {name}."); }
                    if (spawnLocation == SpawnLocation.NearbyEnemy) { throw new Exception($"employer OwnSystem is not valid with spawnLocation NearbyEnemy for ExtendedContractType {name}."); }
                } else if (emp == "Allied") {
                } else if (FactionEnumeration.GetFactionByName(emp) == invalid) {
                  throw new Exception($"employer {emp} is not a valid faction in ExtendedContractType {name}.");
                }
            }

            foreach (string tar in target) {
                if (tar == "Employer" || tar == "SystemOwner" || tar == "NearbyEnemy") {
                } else if (FactionEnumeration.GetFactionByName(tar) == invalid) {
                    throw new Exception($"target {tar} is not a valid faction in ExtendedContractType {name}.");
                }
            }

            foreach (string key in schedule) {
                if (key != "" && !entries.ContainsKey(key)) {
                    throw new Exception($"'{key}' in the schedule is not in the entries dictionary in ExtendedContractType {name}.");
                }
            }

            foreach (string key in entries.Keys) {
                if (!schedule.Contains(key)) {
                    throw new Exception($"Entry '{key}' is not used in the schedule of ExtendedContractType {name}. This probably indicates an error in your configuration. The goggles, they do nothing.");
                }

                entries[key].validate(name, key);
            }

            bool hireContractExists = MetadataDatabase.Instance.Query<Contract_MDD>("SELECT * from Contract WHERE ContractID = @ID", new { ID = hireContract }).ToArray().Length > 0;
            if (!hireContractExists) {
                throw new Exception($"Couldn't find hireContract '{hireContract}' for ExtendedContractType {name}.");
            }

            if (availableFor.Length != 2 || availableFor[0] < 0 || availableFor[0] > availableFor[1]) {
                throw new Exception($"Invalid availableFor availableFor for ExtendedContractType {name}.");
            }
        }
    }

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

        [JsonProperty]
        public int currentDay = 0;

        [JsonProperty]
        public int playerDrops = 0;

        public FactionValue employer;
        [JsonProperty]
        public string employerName;

        public FactionValue target;
        [JsonProperty]
        public string targetName;

        public string currentContractName = "";

        public bool droppingForContract = false;

        public ExtendedContract() {
            // Empty constructor used for deserialization.
        }

        public string Serialize() {
            string json = JsonConvert.SerializeObject(this);
            return $"WIIC:Extended:{json}";
        }

        public static bool isSerializedExtendedContract(string tag) {
            return tag.StartsWith("WIIC:Extended:");
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

            spawnParticipationContract(extendedType.hireContract, employer, target);
        }

        public virtual bool passDay() {
            if (WIIC.sim.CompanyTags.Contains("WIIC_extended_contract") && WIIC.sim.CurSystem == location) {
                // Player is employed at this extended contract.
                string entryName = extendedType.schedule[currentDay];
                if (!extendedType.entries.ContainsKey(entryName)) {
                    throw new Exception($"ExtendedContractType references '{entryName}' at schedule[{currentDay}], but this is not present in its entries dictionary. Valid keys are {string.Join(", ", extendedType.entries.Keys)}");
                }

                WIIC.modLog.Info?.Write($"Day {currentDay} of {type}, running {entryName}.");
                runEntry(extendedType.entries[entryName]);

                currentDay++;
                return currentDay == extendedType.schedule.Length;
            }
            countdown--;
            if (countdown <= 0) {
                removeParticipationContract(extendedType.hireContract);
                return true;
            }

            return false;
        }

        public void runEntry(Entry entry) {
            foreach (string eventID in entry.triggerEvent) {
                WIIC.modLog.Debug?.Write($"Considering event {eventID}");
                if (!WIIC.sim.DataManager.SimGameEventDefs.TryGet(eventID, out SimGameEventDef eventDef)) {
                    throw new Exception($"Couldn't find event {eventID} on day {currentDay} of {type}");
                }

                if (WIIC.sim.MeetsRequirements(eventDef.Requirements) && WIIC.sim.MeetsRequirements(eventDef.AdditionalRequirements)) {
                    WIIC.modLog.Info?.Write($"Triggering event {eventID} on day {currentDay} of {type}.");

                    SimGameEventTracker eventTracker = new SimGameEventTracker();
                    eventTracker.Init(new[] { EventScope.Company }, 0, 0, SimGameEventDef.SimEventType.NORMAL, WIIC.sim);
                    WIIC.sim.GetInterruptQueue().QueueEventPopup(eventDef, EventScope.Company, eventTracker);

                    return;
                }
            }

            double rand = Utilities.rng.NextDouble();
            if (rand <= entry.contractChance) {
                WIIC.modLog.Info?.Write($"Triggering contract on day {currentDay} of {type} ({entry.contractChance} chance)");
                Contract contract = null;
                if (entry.contract.Length > 0) {
                    foreach (string contractName in entry.contract) {
                        WIIC.modLog.Debug?.Write($"Considering {contractName}");
                        contract = ContractManager.getContractByName(contractName, location, employer, target);
                        if (WIIC.sim.MeetsRequirements(contract.Override.requirementList.ToArray())) {
                            break;
                        } else {
                            contract = null;
                        }
                    }
                } else {
                    WIIC.modLog.Debug?.Write($"Generating procedural contract.");
                    contract = ContractManager.getNewProceduralContract(location, employer, target, entry.allowedContractTypes);
                }

                if (contract != null) {
                    if (contract.Override.contractRewardOverride < 0) {
                        contract.Override.contractRewardOverride = WIIC.sim.CalculateContractValueByContractType(contract.ContractTypeValue, contract.Override.finalDifficulty, WIIC.sim.Constants.Finances.ContractPricePerDifficulty, WIIC.sim.Constants.Finances.ContractPriceVariance, 0);
                    }
                    contract.Override.contractRewardOverride = (int)Math.Floor(contract.Override.contractRewardOverride * entry.contractPayoutMultiplier);
                    contract.Override.salvagePotential += entry.contractBonusSalvage;
                    contract.Override.salvagePotential = Math.Min(WIIC.sim.Constants.Salvage.MaxSalvagePotential, Math.Max(0, contract.Override.salvagePotential));
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
                if (itemCollection != null) {
                    SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
                    queue.QueueRewardsPopup(itemCollection);

                    return;
                }
            }
        }

        public virtual void spawnParticipationContract(string contractName, FactionValue contractEmployer, FactionValue contractTarget) {
            int diff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);
            ContractManager.addTravelContract(contractName, location, contractEmployer, contractTarget, diff);
        }

        public virtual void acceptContract(string contract) {
            countdown = 0;
            WIIC.modLog.Info?.Write($"Player embarked on {type} at {location.Name}. Adding WIIC_extended_contract company tag and work order item.");

            removeParticipationContract(extendedType.hireContract);
            WIIC.sim.CompanyTags.Add("WIIC_extended_contract");
            WIIC.sim.SetSimRoomState(DropshipLocation.SHIP);
            WIIC.sim.RoomManager.AddWorkQueueEntry(workOrder);
            WIIC.sim.RoomManager.RefreshTimeline(false);
        }

        public virtual void removeParticipationContract(string contract) {
            WIIC.modLog.Debug?.Write($"Cleaning up participation contract {contract} for {location.Name}.");
            WIIC.sim.GlobalContracts.RemoveAll(c => (c.Override.ID == contract && c.TargetSystem == location.Name));
        }

        public void launchContract(string message, Contract contract, DeclinePenalty declinePenalty) {
            string title = Strings.T($"{extendedType.name} Mission");
            string primaryButtonText = Strings.T("Launch mission");
            string cancel = Strings.T("Pass");

            if (declinePenalty == DeclinePenalty.BadFaith) {
                message += "\n\nDeclining this contract is equivelent to a bad faith withdrawal. It will severely harm our reputation.";
            } else if (declinePenalty == DeclinePenalty.BreakContract) {
                message += "\n\nDeclining this contract is will break our {type} contract with {employer.ShortName}.";
            }

            WIIC.modLog.Debug?.Write(message);

            SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
            queue.QueuePauseNotification(title, message, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, delegate {
                try {
                    WIIC.modLog.Info?.Write($"Accepted {type} mission {contract.Name}.");
                    currentContractName = contract.Name;

                    WIIC.sim.RoomManager.ForceShipRoomChangeOfRoom(DropshipLocation.CMD_CENTER);
                    WIIC.sim.ForceTakeContract(contract, false);
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
            WIIC.modLog.Info?.Write("Applying DeclinePenalty {declinePenalty.ToString()} for {type} with employer {employer.Name}");

            if (declinePenalty == DeclinePenalty.BadFaith || declinePenalty == DeclinePenalty.BreakContract) {
                if (employer.DoesGainReputation) {
                    float employerRepBadFaithMod = WIIC.sim.Constants.Story.EmployerRepBadFaithMod;
                    WIIC.modLog.Info?.Write("employerRepBadFaithMod: {employerRepBadFaithMod}");
                    WIIC.modLog.Info?.Write("difficulty: {location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
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

            description.AppendLine(Strings.T("<b><color=#de0202>{0} has hired you for {1}.</color></b>", employer.FactionDef.ShortName, type));
            description.AppendLine(Strings.T("You've been on assignment {0} out of {1} days.", currentDay, extendedType.schedule.Length));

            return description.ToString();
        }

        protected WorkOrderEntry_Notification _workOrder;
        public virtual WorkOrderEntry_Notification workOrder {
            get {
                if (_workOrder == null) {
                    string title = Strings.T($"{type} Complete");
                    _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "extendedContractComplete", title);
                }

                _workOrder.SetCost(extendedType.schedule.Length - currentDay);
                return _workOrder;
            }
            set {
                _workOrder = value;
            }
        }

        public static ExtendedContract Deserialize(string tag) {
            ExtendedContract newExtendedContract = JsonConvert.DeserializeObject<ExtendedContract>(tag.Substring(14));
            newExtendedContract.initAfterDeserialization();

            return newExtendedContract;
        }

        public void initAfterDeserialization() {
            location = WIIC.sim.GetSystemById(locationID);
            employer = FactionEnumeration.GetFactionByName(employerName);
            target = FactionEnumeration.GetFactionByName(targetName);
            extendedType = WIIC.extendedContractTypes[type];
        }
    }
}
