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
    public class Flareup : ExtendedContract {
        [JsonProperty]
        public int daysUntilMission;
        [JsonProperty]
        public int attackerStrength;
        [JsonProperty]
        public int defenderStrength;
        [JsonProperty]
        public int currentContractForceLoss = 0;

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

        public static ExtendedContractType Attack = new ExtendedContractType("Attack");
        public static ExtendedContractType Raid = new ExtendedContractType("Raid");

        public static bool isSerializedFlareup(string tag) {
            return tag.StartsWith("WIIC:");
        }

        public Flareup(StarSystem flareupLocation, FactionValue attackerFaction, ExtendedContractType flareupType) : base(flareupLocation, attackerFaction, flareupLocation.OwnerValue, flareupType) {
            Settings s = WIIC.settings;
            countdown = Utilities.rng.Next(s.minCountdown, s.maxCountdown);

            int v;
            attackerStrength = s.attackStrength.TryGetValue(employer.Name, out v) ? v : s.defaultAttackStrength;
            defenderStrength = s.defenseStrength.TryGetValue(target.Name, out v) ? v : s.defaultDefenseStrength;

            foreach (string tag in s.addStrengthTags.Keys) {
                if (location.Tags.Contains(tag)) {
                    attackerStrength += s.addStrengthTags[tag];
                    defenderStrength += s.addStrengthTags[tag];
                }
            }

            attackerStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);
            defenderStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);

            string stat = $"WIIC_{employer.Name}_attack_strength";
            attackerStrength += WIIC.sim.CompanyStats.ContainsStatistic(stat) ? WIIC.sim.CompanyStats.GetValue<int>(stat) : 0;
            stat = $"WIIC_{target.Name}_defense_strength";
            defenderStrength += WIIC.sim.CompanyStats.ContainsStatistic(stat) ? WIIC.sim.CompanyStats.GetValue<int>(stat) : 0;

            if (type == "Raid") {
                attackerStrength = (int) Math.Ceiling(attackerStrength * s.raidStrengthMultiplier);
                defenderStrength = (int) Math.Ceiling(defenderStrength * s.raidStrengthMultiplier);
            }

            string text = type == "Raid" ? "{0} launches raid on {1} at {2}" : "{0} attacks {1} for control of {2}";
            text = Strings.T(text, employer.FactionDef.ShortName, target.FactionDef.ShortName, location.Name);

            Utilities.deferredToasts.Add(text);
            WIIC.modLog.Info?.Write(text);
        }

        public bool employedByEmployer {
            get {
                return WIIC.sim.CurSystem == location && WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker");
            }
        }

        public bool employedByTarget {
            get {
                return WIIC.sim.CurSystem == location && WIIC.sim.CompanyTags.Contains("WIIC_helping_defender");
            }
        }

        // base.employer is the attacker in this flareup. The player might actually be employed by the defender, or by no one at all.
        public FactionValue actualEmployer {
            get {
                return employedByEmployer ? employer : (employedByTarget ? target : null);
            }
        }

        // base.target is the defender in this flareup. The player might actually be attacking the employer, or by no one at all.
        public FactionValue actualTarget {
            get {
                return employedByEmployer ? target : (employedByTarget ? employer : null);
            }
        }

        public override void acceptContract(string contract) {
            base.acceptContract(contract);
            daysUntilMission = 1;

            string tag = (contract == "wiic_help_defender" || contract == "wiic_raid_defender") ? "WIIC_helping_defender" : "WIIC_helping_attacker";
            WIIC.sim.CompanyTags.Add(tag);
            WIIC.sim.CompanyTags.Remove("WIIC_extended_contract");
            WIIC.modLog.Info?.Write($"Replaced tag with {tag} for flareup.");
        }

        public override bool passDay() {
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

            if (employedByEmployer || employedByTarget) {
                launchMission();
            }

            return false;
        }

        public CompletionResult getCompletionResult() {
            if (attackerStrength <= 0) {
                if (employedByEmployer) { return CompletionResult.DefenderWonEmployerLost; }
                if (employedByTarget && playerDrops > 0) { return CompletionResult.DefenderWonReward; }
                if (employedByTarget) { return CompletionResult.DefenderWonNoReward; }
                return CompletionResult.DefenderWonUnemployed;
            } else {
                if (employedByTarget) { return CompletionResult.AttackerWonEmployerLost; }
                if (employedByEmployer && playerDrops > 0) { return CompletionResult.AttackerWonReward; }
                if (employedByEmployer) { return CompletionResult.AttackerWonNoReward; }
                return CompletionResult.AttackerWonUnemployed;
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
                    return s.factionAttackReward.ContainsKey(actualEmployer.Name) ? s.factionAttackReward[actualEmployer.Name] : s.defaultAttackReward;
                } else {
                    return s.factionRaidReward.ContainsKey(actualEmployer.Name) ? s.factionRaidReward[actualEmployer.Name] : s.defaultRaidReward;
                }
            }
            return null;
        }

        public void conclude() {
            Settings s = WIIC.settings;

            removeParticipationContract();
            string text = Strings.T(completionText(), employer.FactionDef.Name, target.FactionDef.Name, location.Name);
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

                Sprite sprite = attackerStrength > 0 ? employer.FactionDef.GetSprite() : target.FactionDef.GetSprite();
                queue.QueuePauseNotification(title, text, sprite, string.Empty, delegate {
                    try {
                        if (itemCollection != null) {
                            WIIC.modLog.Info?.Write($"Reward: {itemCollection} from {actualEmployer.Name}");
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
            if (type == "Attack") {
                if (defenderStrength <= 0 && attackerStrength > 0) {
                    Utilities.applyOwner(location, employer, true);
                }
            } else if (type == "Raid") {
                SimGameEventResult result = new SimGameEventResult();
                result.Scope = EventScope.Company;
                result.TemporaryResult = true;
                result.ResultDuration = s.raidResultDuration;

                if (attackerStrength <= 0) {
                    SimGameStat attackStat =  new SimGameStat($"WIIC_{employer.Name}_attack_strength", 1, false);
                    SimGameStat defenseStat =  new SimGameStat($"WIIC_{target.Name}_defense_strength", -1, false);
                    result.Stats = new SimGameStat[] { attackStat, defenseStat };
                } else if (defenderStrength <= 0) {
                    SimGameStat attackStat = new SimGameStat($"WIIC_{employer.Name}_attack_strength", -1, false);
                    SimGameStat defenseStat =  new SimGameStat($"WIIC_{target.Name}_defense_strength", 1, false);
                    result.Stats = new SimGameStat[] { attackStat, defenseStat };
                } else {
                    // Draw is possible for raids, if they both hit strength 0 at the same time
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

        public override string getDescription() {
            StringBuilder description = new StringBuilder();
            if (type == "Raid") {
                description.AppendLine(Strings.T("<b><color=#de0202>{0} is being raided by {1}</color></b>", location.Name, employer.FactionDef.ShortName));
            } else {
                description.AppendLine(Strings.T("<b><color=#de0202>{0} is under attack by {1}</color></b>", location.Name, employer.FactionDef.ShortName));
            }

            if (countdown > 0) {
               description.AppendLine(Strings.T("{0} days until the fighting starts", countdown));
            }
            if (daysUntilMission > 0) {
               description.AppendLine(Strings.T("{0} days until the next mission", daysUntilMission));
            }
            description.AppendLine("\n" + Strings.T("{0} forces: {1}", employer.Name.Replace("the ", ""), Utilities.forcesToString(attackerStrength)));
            description.AppendLine(Strings.T("{0} forces: {1}", target.FactionDef.Name.Replace("the ", ""), Utilities.forcesToString(defenderStrength)));

            return description.ToString();
        }

        // ExtendedContract calls this, but we ignore what it passes in and use our own logic.
        public void spawnParticipationContract() {

            if (location != WIIC.sim.CurSystem) {
                return;
            }

            SimGameReputation minRep = SimGameReputation.INDIFFERENT;
            if (type == "Attack") {
                Enum.TryParse(WIIC.settings.minReputationToHelpAttack, out minRep);
            } else if (type == "Raid") {
                Enum.TryParse(WIIC.settings.minReputationToHelpRaid, out minRep);
            }
            string contractPrefix = type == "Attack" ? "wiic_help" : "wiic_raid";

            if (!WIIC.settings.wontHirePlayer.Contains(employer.Name) && WIIC.sim.GetReputation(employer) >= minRep) {
                base.spawnParticipationContract($"{contractPrefix}_attacker", employerName, targetName);
            }

            if (!WIIC.settings.wontHirePlayer.Contains(location.OwnerValue.Name) && WIIC.sim.GetReputation(location.OwnerValue) >= minRep) {
                base.spawnParticipationContract($"{contractPrefix}_defender", targetName, employerName);
            }
        }

        public void removeParticipationContract() {
            if (type == "Attack") {
                base.removeParticipationContract("wiic_help_attacker");
                base.removeParticipationContract("wiic_help_defender");
            } else {
                base.removeParticipationContract("wiic_raid_attacker");
                base.removeParticipationContract("wiic_raid_defender");
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
            message += "\n\nIf we pass, we'll lose this chance to influence the outcome of the conflict but face no penalties.";

            launchProceduralMission(message, actualEmployer, actualTarget, DeclinePenalty.None);
        }

        public override void applyDeclinePenalty(DeclinePenalty declinePenalty) {
            if (employedByEmployer) {
                attackerStrength -= currentContractForceLoss;
                WIIC.modLog.Debug?.Write($"defenderStrength -= {currentContractForceLoss}");
            } else {
                defenderStrength -= currentContractForceLoss;
                WIIC.modLog.Debug?.Write($"attackerStrength -= {currentContractForceLoss}");
            }
        }

        public override WorkOrderEntry_Notification workOrder {
            get {
                if (_workOrder == null) {
                    string title = Strings.T($"{type} Contract");
                    _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "nextlareupContract", title);
                }

                _workOrder.SetCost(daysUntilMission);
                return _workOrder;
            }
        }

        public static new Flareup Deserialize(string tag) {
            Flareup newFlareup = JsonConvert.DeserializeObject<Flareup>(tag.Substring(5));
            newFlareup.initAfterDeserialization();

            return newFlareup;
        }
    }
}
