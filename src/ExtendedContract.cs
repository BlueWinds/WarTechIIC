using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    public enum DeclinePenalty {
        None,
        BadFaith,
        BreakContract
    }

    public class Entry {
        public string[] triggerEvent;
        public float contractChance;
        public string[] contract;
        public int[] allowedContractTypes;
        public DeclinePenalty declinePenalty;
        public float contractPayoutMultiplier;
        public int contractBonusSalvage;
        public string contractMessage;
        public Dictionary<int, string> rewardByDifficulty = new Dictionary<int, string>();
    }

    public class ExtendedContractType {
        public string name;
        public RequirementDef[] requirementList;
        public int weight = 1;
        public string[] employer;
        public string[] spawnLocation;
        public string[] target;
        public string hireContract;
        public Tuple<int, int> availableFor;
        public string[] schedule;
        public Entry[] entries;

        public ExtendedContractType(string newName) {
            name = newName;
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

            spawnParticipationContract();
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
                return currentDay == extendedType.schedule.Count;
            }
            countdown--;
            if (countdown <= 0) {
                removeParticipationContract();
                return true;
            }

            return false;
        }

        public virtual void spawnParticipationContract(string contract = extendedType.hireContract, string contractEmployer = employerName, string contractTarget = targetName) {
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

            removeParticipationContract();
            WIIC.sim.CompanyTags.Add("WIIC_extended_contract");
            WIIC.sim.SetSimRoomState(DropshipLocation.SHIP);
            WIIC.sim.RoomManager.AddWorkQueueEntry(flareup.workOrder);
            WIIC.sim.RoomManager.RefreshTimeline(false);
        }

        public virtual void removeParticipationContract(string contract = extendedType.hireContract) {
            WIIC.modLog.Debug?.Write($"Cleaning up participation contract {contract} for {location.Name}.");
            WIIC.sim.GlobalContracts.RemoveAll(c => (c.Override.ID == contract && c.TargetSystem == location.Name));
        }

        public void runEntry(Entry entry) {
            throw new Exception("TODO");
            foreach (string eventID in entry.triggerEvent) {
                WIIC.modLog.Info?.Write($"Should maybe execute {eventID}.");
            }
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

            description.AppendLine(Strings.T("<b><color=#de0202>{0} has hired you for {1}.</color></b>", employer.ShortName, type));
            description.AppendLine(Strings.T("You've been on assignment {0} out of {1} days.", currentDay, extendedType.schedule.Count));

            return description.ToString();
        }

        private WorkOrderEntry_Notification _workOrder;
        public virtual WorkOrderEntry_Notification workOrder {
            get {
                if (_workOrder == null) {
                    string title = Strings.T($"{type} Complete");
                    _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "extendedContractComplete", title);
                }

                _workOrder.SetCost(extendedType.schedule.Count - currentDay);
                return _workOrder;
            }
            set {
                _workOrder = value;
            }
        }
    }
}
