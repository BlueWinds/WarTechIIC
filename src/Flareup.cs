using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Harmony;
using BattleTech;
using BattleTech.UI;
using UnityEngine;
using Localize;
using ColourfulFlashPoints;
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    [JsonObject(MemberSerialization.OptIn)]
    public class Flareup {
        public StarSystem location;
        [JsonProperty]
        public string locationID;

        [JsonProperty]
        public string type = "Attack";

        public FactionValue attacker;
        [JsonProperty]
        public string attackerName;

        [JsonProperty]
        public string giveOnWin;

        [JsonProperty]
        public int playerDrops = 0;

        [JsonProperty]
        public int countdown;
        [JsonProperty]
        public int daysUntilMission;
        [JsonProperty]
        public int attackerStrength;
        [JsonProperty]
        public int defenderStrength;
        [JsonProperty]
        public string currentContractName = "";
        [JsonProperty]
        public int currentContractForceLoss = 0;

        public bool droppingForContract = false;

        public Flareup() {
            // Empty constructor used for deserialization.
        }

        public enum CompletionResult {
            AttackerWonUnemployed,
            AttackerWonEmployerLost,
            AttackerWonReward,
            AttackerWonNoReward,
            DefenderWonUnemployed,
            DefenderWonEmployerLost,
            DefenderWonReward,
            DefenderWonNoReward,
        }

        public Flareup(StarSystem flareupLocation, FactionValue attackerFaction, string flareupType) {
            Settings s = WIIC.settings;

            location = flareupLocation;
            locationID = flareupLocation.ID;
            attacker = attackerFaction;
            attackerName = attackerFaction.Name;
            type = flareupType;
            countdown = Utilities.rng.Next(s.minCountdown, s.maxCountdown);

            int v;
            attackerStrength = s.attackStrength.TryGetValue(attacker.Name, out v) ? v : s.defaultAttackStrength;
            defenderStrength = s.defenseStrength.TryGetValue(location.OwnerValue.Name, out v) ? v : s.defaultDefenseStrength;

            foreach (string tag in s.addStrengthTags.Keys) {
                if (location.Tags.Contains(tag)) {
                    attackerStrength += s.addStrengthTags[tag];
                    defenderStrength += s.addStrengthTags[tag];
                }
            }

            attackerStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);
            defenderStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);

            string stat = $"WIIC_{attacker.Name}_attack_strength";
            attackerStrength += WIIC.sim.CompanyStats.ContainsStatistic(stat) ? WIIC.sim.CompanyStats.GetValue<int>(stat) : 0;
            stat = $"WIIC_{location.OwnerValue.Name}_defense_strength";
            defenderStrength += WIIC.sim.CompanyStats.ContainsStatistic(stat) ? WIIC.sim.CompanyStats.GetValue<int>(stat) : 0;

            if (type == "Raid") {
                attackerStrength = (int) Math.Ceiling(attackerStrength * s.raidStrengthMultiplier);
                defenderStrength = (int) Math.Ceiling(defenderStrength * s.raidStrengthMultiplier);
            }

            string text = type == "Raid" ? "{0} launches raid on {1} at {2}" : "{0} attacks {1} for control of {2}";
            text = Strings.T(text, attacker.FactionDef.ShortName, location.OwnerValue.FactionDef.ShortName, location.Name);
            Utilities.deferredToasts.Add(text);

            WIIC.modLog.Info?.Write(text);
            if (location == WIIC.sim.CurSystem) {
                spawnParticipationContracts();
            }
        }

        public FactionValue employer {
            get {
                if (WIIC.sim.CurSystem != location) {
                    return null;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker")) {
                    return attacker;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_defender")) {
                    return location.OwnerValue;
                }
                return null;
            }
        }

        public FactionValue target {
            get {
                if (WIIC.sim.CurSystem != location) {
                    return null;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker")) {
                    return location.OwnerValue;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_defender")) {
                    return attacker;
                }
                return null;
            }
        }

        public bool passDay() {
            Settings s = WIIC.settings;

            if (attackerStrength <= 0 || defenderStrength <= 0) {
              conclude();
              return true;
            }

            if (countdown > 0) {
                countdown--;
                return false;
            }

            if (daysUntilMission > 1) {
                daysUntilMission--;
                return false;
            }

            double rand = Utilities.rng.NextDouble();
            if (rand > 0.5) {
                attackerStrength -= Utilities.rng.Next(s.combatForceLossMin, s.combatForceLossMax);
            } else {
                defenderStrength -= Utilities.rng.Next(s.combatForceLossMin, s.combatForceLossMax);
            }
            WIIC.modLog.Debug?.Write($"{type} progressed at {location.Name}. attackerStrength: {attackerStrength}, defenderStrength: {defenderStrength}");

            daysUntilMission = s.daysBetweenMissions;

            if (employer != null) {
                launchMission();
            }
            return false;
        }

        public CompletionResult getCompletionResult() {
            if (attackerStrength <= 0) {
              if (employer == null) { return CompletionResult.DefenderWonUnemployed; }
              if (employer == attacker) { return CompletionResult.DefenderWonEmployerLost; }
              if (playerDrops > 0) { return CompletionResult.DefenderWonReward; }
              return CompletionResult.DefenderWonNoReward;
            } else {
              if (employer == null) { return CompletionResult.AttackerWonUnemployed; }
              if (employer == location.OwnerValue) { return CompletionResult.AttackerWonEmployerLost; }
              if (playerDrops > 0) { return CompletionResult.AttackerWonReward; }
              return CompletionResult.AttackerWonNoReward;
            }
        }

        public string completionText() {
            CompletionResult result = getCompletionResult();

            if (type == "Attack") {
                switch (result) {
                    case CompletionResult.AttackerWonUnemployed: return "{0} takes control of {2} from {1}.";
                    case CompletionResult.AttackerWonEmployerLost: return "{0} takes control of {2}. {1} withdraws their forces in haste, your contract ending with their defeat.";
                    case CompletionResult.AttackerWonReward: return "{0} takes control of {2}. {1} withdraws their forces in haste, leaving you to celebrate victory with your crew - and with a bonus from your employer.";
                    case CompletionResult.AttackerWonNoReward: return "{0} takes control of {2}. {1} withdraws their forces in haste, but your contact informs you that there will be no bonus forthcoming, since you never participated in a mission.";
                    case CompletionResult.DefenderWonUnemployed: return "{1} drives the invasion by {0} from {2}.";
                    case CompletionResult.DefenderWonEmployerLost: return "{1} drives the forces {0} sent to invade {2}. Your contract ends on a sour note with the invasion's defeat.";
                    case CompletionResult.DefenderWonReward: return "{1} drives the forces {0} sent to invade {2}, leaving you to celebrate victory with your crew - and with a bonus from your employer.";
                    case CompletionResult.DefenderWonNoReward: return "{1} drives the forces {0} sent to invade {2}, but your contact informs you that there will be no bonus forthcoming, since you never participated in a mission.";
                }
            } else {
                switch (result) {
                    case CompletionResult.AttackerWonUnemployed: return "{0} weakens {1} control of {2}.";
                    case CompletionResult.AttackerWonEmployerLost: return "{0} smashes through the forces {1} has defending {2}, withdrawing before a counter attack can be mounted. Your contract ends on a sour note.";
                    case CompletionResult.AttackerWonReward: return "{0} smashes through the forces {1} has defending {2}, withdrawing before they can mount a proper counter attack. They depart the system swiftly, leaving you to celebrate victory with your crew - and with a bonus from your employer.";
                    case CompletionResult.AttackerWonNoReward: return "{0} smashes through the force s{1} has defending {2}, withdrawing before they can mount a proper counter attack. They depart the system swiftly, but your contact informs you that there will be no bonus forthcoming since you never participated in a mission.";
                    case CompletionResult.DefenderWonUnemployed: return "{1} drives off the {0} raid on {2}.";
                    case CompletionResult.DefenderWonEmployerLost: return "{1} drives the remaining forces {0} had on the surface of {2}. Your contract ends on a sour note with the invasion's defeat.";
                    case CompletionResult.DefenderWonReward: return "{1} drives the remaining forces {0} had on the surface of {2}, leaving you to celebrate victory with your crew - and with a bonus from your employer.";
                    case CompletionResult.DefenderWonNoReward: return "{1} drives the remaining forces {0} had on the surface of {2}, but your contact informs you that there will be no bonus forthcoming, since you never participated in a mission.";
                }
            }

            return "Something went wrong. Type: {type}. Attacker: {0}. Defender: {1}. Location: {2}.";
        }

        public string reward() {
            Settings s = WIIC.settings;
            CompletionResult result = getCompletionResult();

            if (result == CompletionResult.AttackerWonReward || result == CompletionResult.DefenderWonReward) {
                if (type == "Attack") {
                    return s.factionAttackReward.ContainsKey(employer.Name) ? s.factionAttackReward[employer.Name] : s.defaultAttackReward;
                } else {
                    return s.factionRaidReward.ContainsKey(employer.Name) ? s.factionRaidReward[employer.Name] : s.defaultRaidReward;
                }
            }
            return null;
        }

        public void conclude() {
            Settings s = WIIC.settings;

            removeParticipationContracts();
            string text = Strings.T(completionText(), attacker.FactionDef.Name, location.OwnerValue.FactionDef.Name, location.Name);
            // Because shortnames can start with a lowercase 'the' ("the Aurigan Coalition", for example), we have to fix the capitalization or the result can look weird.
            text = text.Replace(". the ", ". The ");
            text = char.ToUpper(text[0]) + text.Substring(1);

            // At the current location, a flareup gets a popup - whether or not the player was involved, it's important.
            if (WIIC.sim.CurSystem == location) {
                SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
                string title = Strings.T($"{type} Complete");
                string primaryButtonText = Strings.T("Acknowledged");
                string itemCollection = reward();

                WIIC.modLog.Info?.Write(text);
                WIIC.modLog.Info?.Write($"Reward: {itemCollection} for {employer.Name}");

                Sprite sprite = attackerStrength > 0 ? attacker.FactionDef.GetSprite() : location.OwnerValue.FactionDef.GetSprite();
                queue.QueuePauseNotification(title, text, sprite, string.Empty, delegate {
                    try {
                        if (itemCollection != null) {
                            queue.QueueRewardsPopup(itemCollection);
                        }
                    } catch (Exception e) {
                        WIIC.modLog.Error?.Write(e);
                    }
                }, primaryButtonText);
                if (!queue.IsOpen) {
                    queue.DisplayIfAvailable();
                }
            // Things happening elsewhere in the galaxy just get an event toast.
            } else {
                WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(text));
            }

            // Now apply the owner or stat changes
            if (type == "Attack" && defenderStrength <= 0 && attackerStrength > 0) {
                // Try block in case the giveOnWin faction doesn't exist. We should validate that the
                // faction exists when it's applied, but didn't do that initially, so we simply catch the error here
                // so that flareups in existing saves with invalid giveOnWin factions can at least resolve.
                try {
                    FactionValue giveTo = string.IsNullOrEmpty(giveOnWin) ? attacker : Utilities.getFactionValueByFactionID(giveOnWin);
                    Utilities.applyOwner(location, giveTo, true);
                } catch (Exception e) {
                    WIIC.modLog.Error?.Write(e);
                }
            } else if (type == "Raid") {
                SimGameEventResult result = new SimGameEventResult();
                result.Scope = EventScope.Company;
                result.TemporaryResult = true;
                result.ResultDuration = s.raidResultDuration;

                if (attackerStrength <= 0) {
                    SimGameStat attackStat =  new SimGameStat($"WIIC_{attacker.Name}_attack_strength", 1, false);
                    SimGameStat defenseStat =  new SimGameStat($"WIIC_{location.OwnerValue.Name}_defense_strength", -1, false);
                    result.Stats = new SimGameStat[] { attackStat, defenseStat };
                } else if (defenderStrength <= 0) {
                    SimGameStat attackStat = new SimGameStat($"WIIC_{attacker.Name}_attack_strength", -1, false);
                    SimGameStat defenseStat =  new SimGameStat($"WIIC_{location.OwnerValue.Name}_defense_strength", 1, false);
                    result.Stats = new SimGameStat[] { attackStat, defenseStat };
                }

                SimGameEventResult[] results = {result};
                SimGameState.ApplySimGameEventResult(new List<SimGameEventResult>(results));
            }
        }

        public void addToMap() {
            Settings s = WIIC.settings;
            MapMarker mapMarker = new MapMarker(location.ID, type == "Raid" ? s.raidMarker : s.attackMarker);
            ColourfulFlashPoints.Main.addMapMarker(mapMarker);

            if (!WIIC.fluffDescriptions.ContainsKey(location.ID)) {
                WIIC.modLog.Debug?.Write($"Filled fluff description entry for {location.ID}: {location.Def.Description.Details}");
                WIIC.fluffDescriptions[location.ID] = location.Def.Description.Details;
            }

            string description = getDescription() + "\n" + WIIC.fluffDescriptions[location.ID];
            AccessTools.Method(typeof(DescriptionDef), "set_Details").Invoke(location.Def.Description, new object[] { description });
        }

        public string getDescription() {
            var description = new StringBuilder();
            if (type == "Raid") {
                description.AppendLine(Strings.T("<b><color=#de0202>{0} is being raided by {1}</color></b>", location.Name, attacker.FactionDef.ShortName));
            } else {
                description.AppendLine(Strings.T("<b><color=#de0202>{0} is under attack by {1}</color></b>", location.Name, attacker.FactionDef.ShortName));
            }

            if (countdown > 0) {
               description.AppendLine(Strings.T("{0} days until the fighting starts", countdown));
            }
            if (daysUntilMission > 0) {
               description.AppendLine(Strings.T("{0} days until the next mission", daysUntilMission));
            }
            description.AppendLine("\n" + Strings.T("{0} forces: {1}", attacker.FactionDef.Name.Replace("the ", ""), Utilities.forcesToString(attackerStrength)));
            description.AppendLine(Strings.T("{0} forces: {1}", location.OwnerValue.FactionDef.Name.Replace("the ", ""), Utilities.forcesToString(defenderStrength)));

            return description.ToString();
        }

        public void spawnParticipationContracts() {
            SimGameReputation minRep = SimGameReputation.INDIFFERENT;
            if (type == "Attack") {
                Enum.TryParse(WIIC.settings.minReputationToHelpAttack, out minRep);
            } else if (type == "Raid") {
                Enum.TryParse(WIIC.settings.minReputationToHelpRaid, out minRep);
            }
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

        public void launchMission() {
            Contract contract = ContractManager.getNewProceduralContract(location, employer, target);
            currentContractForceLoss = Utilities.rng.Next(WIIC.settings.combatForceLossMin, WIIC.settings.combatForceLossMax);

            string title = Strings.T("Flareup Mission");
            string primaryButtonText = Strings.T("Launch mission");
            string cancel = Strings.T("Pass");

            string message;
            try {
                message = $"{employer.FactionDef.Name.Replace("the ", "The ")} has a mission for us, Commander: {contract.Name}. Details will be provided en-route, but it seems to be a {contract.ContractTypeValue.FriendlyName.ToLower()} mission. Sounds urgent.";
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
                message = $"Our employer has a mission for us, Commander: {contract.Name}. Details will be provided en-route, but it seems to be a {contract.ContractTypeValue.FriendlyName.ToLower()} mission. Sounds urgent.";
            }
            message += "\nIf we pass, they'll have to dedicate one of their own units to it, weakening their war effort.";
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
                WIIC.modLog.Info?.Write($"Passed on {type} mission.");
                if (employer == attacker) {
                    attackerStrength -= currentContractForceLoss;
                    WIIC.modLog.Debug?.Write($"defenderStrength -= {currentContractForceLoss}");
                } else {
                    defenderStrength -= currentContractForceLoss;
                    WIIC.modLog.Debug?.Write($"attackerStrength -= {currentContractForceLoss}");
                }
            }, cancel);

            if (!queue.IsOpen) {
                queue.DisplayIfAvailable();
            }
        }

        private WorkOrderEntry_Notification _workOrder;
        public WorkOrderEntry_Notification workOrder {
          get {
            if (_workOrder == null) {
              string title = Strings.T($"{type} contract");
              _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "nextflareupContract", title);
            }

            _workOrder.SetCost(daysUntilMission);
            return _workOrder;
          }
        }

        public string Serialize() {
            string json = JsonConvert.SerializeObject(this);
            return $"WIIC:{json}";
        }

        public static bool isSerializedFlareup(string tag) {
            return tag.StartsWith("WIIC:");
        }

        public static Flareup Deserialize(string tag) {
            Flareup newFlareup = JsonConvert.DeserializeObject<Flareup>(tag.Substring(5));
            newFlareup.initAfterDeserialization();

            return newFlareup;
        }

        public void initAfterDeserialization() {
            location = WIIC.sim.GetSystemById(locationID);
            attacker = FactionEnumeration.GetFactionByName(attackerName);
        }
    }
}
