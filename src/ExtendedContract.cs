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
        GoodFaith,
        BreakContract
    }

    public struct Entry {
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
        public string[] attacker;
        public string attackerHireContract;
        public string defenderHireContract;
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

        public FactionValue attacker;
        [JsonProperty]
        public string attackerName;

        [JsonProperty]
        public int playerDrops = 0;

        [JsonProperty]
        public int countdown;
        public string currentContractName = "";

        public bool droppingForContract = false;

        public ExtendedContract() {
            // Empty constructor used for deserialization.
        }

        public ExtendedContract(StarSystem contractLocation, FactionValue attackerFaction, ExtendedContractType contractType) {
            location = contractLocation;
            locationID = contractLocation.ID;
            attacker = attackerFaction;
            attackerName = attackerFaction.Name;
            extendedType = contractType;
            type = contractType.name;

            spawnParticipationContracts();
        }

        public bool passDay() {

        }

        public void spawnParticipationContracts() {
            SimGameReputation minRep = SimGameReputation.INDIFFERENT;
            int diff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);
            string contractPrefix = type == "Attack" ? "wiic_help" : "wiic_raid";

            if (!WIIC.settings.wontHirePlayer.Contains(attacker.Name) && WIIC.sim.GetReputation(attacker) >= minRep) {
                WIIC.modLog.Info?.Write($"Adding contract {contractPrefix}_attacker. Target={location.OwnerValue.Name}, Employer={attacker.Name}, TargetSystem={location.ID}, Difficulty={location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
                Contract attackContract = WIIC.sim.AddContract(new SimGameState.AddContractData {
                    ContractName = $"{contractPrefix}_attacker",
                    Target = location.OwnerValue.Name,
                    Employer = attacker.Name,
                    TargetSystem = location.ID,
                    Difficulty = diff
                });
                attackContract.SetFinalDifficulty(diff);
            }

            if (!WIIC.settings.wontHirePlayer.Contains(location.OwnerValue.Name) && WIIC.sim.GetReputation(location.OwnerValue) >= minRep) {
                WIIC.modLog.Info?.Write($"Adding contract {contractPrefix}_defender. Target={attacker.Name}, Employer={location.OwnerValue.Name}, TargetSystem={location.ID}, Difficulty={location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
                Contract defendContract = WIIC.sim.AddContract(new SimGameState.AddContractData {
                    ContractName = $"{contractPrefix}_defender",
                    Target = attacker.Name,
                    Employer = location.OwnerValue.Name,
                    TargetSystem = location.ID,
                    Difficulty = diff
                });
                defendContract.SetFinalDifficulty(diff);
            }
        }

        public void removeParticipationContracts() {
            if (location == WIIC.sim.CurSystem) {
                WIIC.modLog.Debug?.Write($"Cleaning up participation contracts for {location.Name}.");
                WIIC.sim.GlobalContracts.RemoveAll(c => (c.Override.ID == "wiic_help_attacker" || c.Override.ID == "wiic_help_defender" || c.Override.ID == "wiic_raid_attacker" || c.Override.ID == "wiic_raid_defender"));
            }
        }
    }
}
