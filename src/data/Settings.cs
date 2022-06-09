using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    public class Settings {
        public bool debug = false;
        public bool trace = false;
        public double dailyAttackChance = 0;
        public double dailyRaidChance = 0;
        public bool setActiveFactionsForAllSystems = false;
        public List<string> clearEmployersAndTargetsForSystemTags = new List<string>();
        public List<string> ignoreFactions = new List<string>();

        public Dictionary<string, double> reputationMultiplier = new Dictionary<string, double>();
        public int distanceFactor = 100;
        public Dictionary<string, double> aggression = new Dictionary<string, double>();

        public List<string> cantBeAttacked = new List<string>();
        public Dictionary<string, double> systemAggressionByTag = new Dictionary<string, double>();

        public Dictionary<string, Dictionary<string, double>> hatred = new Dictionary<string, Dictionary<string, double>>();
        public bool limitTargetsToFactionEnemies = true;

        public FpMarker attackMarker = new FpMarker();
        public FpMarker raidMarker = new FpMarker();

        public int minCountdown = 30;
        public int maxCountdown = 45;
        public int defaultAttackStrength = 10;
        public int defaultDefenseStrength = 10;
        public int strengthVariation = 0;
        public Dictionary<string, int> attackStrength = new Dictionary<string, int>();
        public Dictionary<string, int> defenseStrength = new Dictionary<string, int>();
        public Dictionary<string, int> addStrengthTags = new Dictionary<string, int>();

        public double raidStrengthMultiplier = 1.0;

        public string minReputationToHelpAttack = "INDIFFERENT";
        public string minReputationToHelpRaid = "INDIFFERENT";
        public List<string> wontHirePlayer = new List<string>();
        public int daysBetweenMissions = 2;
        public int combatForceLossMin = 2;
        public int combatForceLossMax = 5;
        public int attackBonusPerHalfSkull = 0;
        public int attackBonusSalvage = 0;
        public int raidBonusPerHalfSkull = 0;
        public int raidBonusSalvage = 0;
        public int raidResultDuration = 360;

        public string defaultAttackReward = "itemCollection_loot_ItemTriple_uncommon";
        public string defaultRaidReward = "itemCollection_loot_ItemTriple_uncommon";
        public Dictionary<string, string> factionAttackReward = new Dictionary<string, string>();
        public Dictionary<string, string> factionRaidReward = new Dictionary<string, string>();

        public Dictionary<string, List<string>> factionActivityTags = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> factionInvasionTags = new Dictionary<string, List<string>>();

        public List<int> customContractEnums = new List<int>();

        public string saveFolder;
    }
}
