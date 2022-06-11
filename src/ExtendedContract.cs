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
        public string[] triggerEvent;
        public double contractChance;
        public string[] contract;
        public int[] allowedContractTypes;
        public DeclinePenalty declinePenalty;
        public double contractPayoutMultiplier;
        public int contractBonusSalvage;
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
        public Tuple<int, int> availableFor;
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

            bool hireContractExists = MetadataDatabase.Instance.Query<Contract_MDD>("SELECT ct.* from Contract WHERE ct.ContractID = @ID", new { ID = hireContract }).ToArray().Length > 0;
            if (!hireContractExists) {
                throw new Exception($"Couldn't find hireContract '{hireContract}' for ExtendedContractType {name}.");
            }

            if (availableFor.Item1 < 0 || availableFor.Item1 > availableFor.Item2) {
                throw new Exception($"Invalid availableFor '[{availableFor.Item1}, {availableFor.Item2}]' for ExtendedContractType {name}.");
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
            countdown = Utilities.rng.Next(extendedType.availableFor.Item1, extendedType.availableFor.Item2);

            spawnParticipationContract(extendedType.hireContract, employerName, targetName);
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

        public void spawnParticipationContract(string contract, string contractEmployer, string contractTarget) {

            int diff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);

            WIIC.modLog.Info?.Write($"Adding contract {contract}. Target={contractTarget}, Employer={contractEmployer}, TargetSystem={location.ID}, Difficulty={location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
            Contract attackContract = WIIC.sim.AddContract(new SimGameState.AddContractData {
                ContractName = contract,
                Target = contractTarget,
                Employer = contractEmployer,
                TargetSystem = location.ID,
                Difficulty = diff
            });
            attackContract.SetFinalDifficulty(diff);
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

        public void runEntry(Entry entry) {
            foreach (string eventID in entry.triggerEvent) {
                WIIC.modLog.Info?.Write($"Should maybe execute {eventID}.");
            }
            throw new Exception("TODO");
        }

        public void launchProceduralMission(string message, FactionValue missionEmployer, FactionValue missionTarget, DeclinePenalty declinePenalty) {
            Contract contract = ContractManager.getNewProceduralContract(location, missionEmployer, missionTarget);

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
            throw new Exception("TODO");
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
