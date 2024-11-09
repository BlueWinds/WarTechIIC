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
        public string[] randomContract = new string[0];
        public int[] allowedContractTypes = new int[0];
        public bool ignoreContractRequirements = false;
        public DeclinePenalty declinePenalty;
        public double contractPayoutMultiplier = 1;
        public int contractBonusSalvage = 0;
        public string contractMessage;
        public Dictionary<int, string> rewardByDifficulty = new Dictionary<int, string>();
        public string invokeMethod;
        public string workOrder;

        public void validate(string type, string key) {
            if (contract.Length > 0 && randomContract.Length > 0 || contract.Length > 0 && allowedContractTypes.Length > 0 || randomContract.Length > 0 && allowedContractTypes.Length > 0) {
                throw new Exception($"VALIDATION: schedule[{key}] has multiple of 'contract', 'randomContract' and 'allowedContractTypes'. Only use one.");
            }

            foreach (string evt in triggerEvent) {
                if (MetadataDatabase.Instance.GetEventDef(evt) == null) {
                    throw new Exception($"VALIDATION: triggerEvent {evt} is not a valid event in {type} schedule[{key}].");
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
        public string targetHireContract;
        public int[] availableFor;
        public string[] schedule;
        public FpMarker mapMarker = null;
        public bool travelContracts = true;
        public bool blockOtherContracts = false;
        public Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        public ExtendedContractType(string newName) {
            name = newName;
        }

        public void validate() {
            FactionValue invalid = FactionEnumeration.GetInvalidUnsetFactionValue();

            foreach (string emp in employer) {
                if (emp == "Any") {
                    if (spawnLocation == SpawnLocation.Any) { throw new Exception($"VALIDATION: employer Any is not valid with spawnLocation Any for ExtendedContractType {name}."); }
                } else if (emp == "OwnSystem") {
                    if (spawnLocation == SpawnLocation.OwnSystem) { throw new Exception($"VALIDATION: employer OwnSystem is not valid with spawnLocation OwnSystem for ExtendedContractType {name}."); }
                    if (spawnLocation == SpawnLocation.NearbyEnemy) { throw new Exception($"VALIDATION: employer OwnSystem is not valid with spawnLocation NearbyEnemy for ExtendedContractType {name}."); }
                } else if (emp == "Allied") {
                } else if (FactionEnumeration.GetFactionByName(emp) == invalid) {
                  throw new Exception($"VALIDATION: employer {emp} is not a valid faction in ExtendedContractType {name}.");
                }
            }

            foreach (string tar in target) {
                if (tar == "Employer" || tar == "SystemOwner" || tar == "NearbyEnemy") {
                } else if (FactionEnumeration.GetFactionByName(tar) == invalid) {
                    throw new Exception($"VALIDATION: target {tar} is not a valid faction in ExtendedContractType {name}.");
                }
            }

            foreach (string key in schedule) {
                if (key != "" && !entries.ContainsKey(key)) {
                    throw new Exception($"VALIDATION: '{key}' in the schedule is not in the entries dictionary in ExtendedContractType {name}.");
                }
            }

            foreach (string key in entries.Keys) {
                if (!schedule.Contains(key)) {
                    throw new Exception($"VALIDATION: Entry '{key}' is not used in the schedule of ExtendedContractType {name}. This probably indicates an error in your configuration. The goggles, they do nothing.");
                }

                entries[key].validate(name, key);
            }

            bool hireContractExists = MetadataDatabase.Instance.Query<Contract_MDD>("SELECT * from Contract WHERE ContractID = @ID", new { ID = hireContract }).ToArray().Length > 0;
            if (!hireContractExists) {
                throw new Exception($"VALIDATION: Couldn't find hireContract '{hireContract}' for ExtendedContractType {name}.");
            }

            bool targetHireContractExists = MetadataDatabase.Instance.Query<Contract_MDD>("SELECT * from Contract WHERE ContractID = @ID", new { ID = targetHireContract }).ToArray().Length > 0;
            if (targetHireContract != null && !targetHireContractExists) {
                throw new Exception($"VALIDATION: Couldn't find targetHireContract '{targetHireContract}' for ExtendedContractType {name}.");
            }

            if (availableFor.Length != 2 || availableFor[0] < 0 || availableFor[0] > availableFor[1]) {
                throw new Exception($"VALIDATION: Invalid availableFor availableFor for ExtendedContractType {name}.");
            }

            if (mapMarker == null && !travelContracts) {
                throw new Exception($"VALIDATION: No map marker found for ExtendedContractType {name}, nor does it generate travelContracts. Users won't be able to find it - set `\"travelContracts\": true`.");
            }
        }
    }
}
