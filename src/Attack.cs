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
    public class Attack : ExtendedContract {
        [JsonProperty]
        public string attackerName;
        [JsonProperty]
        public int attackerStrength;
        [JsonProperty]
        public int defenderStrength;
        [JsonProperty]
        public int currentContractForceLoss = 0;
        [JsonProperty]
        public string giveOnWin;

        public Attack() {
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

        public Attack(StarSystem location, FactionValue attacker, ExtendedContractType type) : base(location, attacker, location.OwnerValue, type) {
            Settings s = WIIC.settings;

            int v;
            attackerStrength = s.attackStrength.TryGetValue(employer.Name, out v) ? v : s.defaultAttackStrength;
            defenderStrength = s.defenseStrength.TryGetValue(target.Name, out v) ? v : s.defaultDefenseStrength;

            foreach (string tag in s.addStrengthTags.Keys) {
                if (location.Tags.Contains(tag)) {
                    attackerStrength += s.addStrengthTags[tag];
                    defenderStrength += s.addStrengthTags[tag];
                }
            }

            attackerName = attacker.Name;
            attackerStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);
            defenderStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);

            string stat = $"WIIC_{employer.Name}_attack_strength";
            attackerStrength += WIIC.sim.CompanyStats.ContainsStatistic(stat) ? WIIC.sim.CompanyStats.GetValue<int>(stat) : 0;
            stat = $"WIIC_{target.Name}_defense_strength";
            defenderStrength += WIIC.sim.CompanyStats.ContainsStatistic(stat) ? WIIC.sim.CompanyStats.GetValue<int>(stat) : 0;
        }

        public FactionValue attacker {
            get {
                if (attackerName == employer.Name) {
                    return employer;
                }
                return target;
            }
        }

        public FactionValue defender {
            get {
                if (attackerName == employer.Name) {
                    return target;
                }
                return employer;
            }
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

            currentDay++;

            string today = extendedType.schedule[currentDay % extendedType.schedule.Length];
            runEntry(extendedType.entries[today]);

            return false;
        }

        public void flareupForceLoss() {
            Settings s = WIIC.settings;
            double rand = Utilities.rng.NextDouble();
            if (rand > 0.5) {
                attackerStrength -= Utilities.rng.Next(s.combatForceLossMin, s.combatForceLossMax);
            } else {
                defenderStrength -= Utilities.rng.Next(s.combatForceLossMin, s.combatForceLossMax);
            }

            WIIC.modLog.Debug?.Write($"{type} progressed at {location.Name}. attackerStrength: {attackerStrength}, defenderStrength: {defenderStrength}");
        }

        public CompletionResult getCompletionResult() {
            if (attackerStrength <= 0) {
                if (isEmployedHere) {
                    if (employer == attacker) { return CompletionResult.DefenderWonEmployerLost; }
                    if (employer == location.OwnerValue && playerDrops > 0) { return CompletionResult.DefenderWonReward; }
                    if (employer == location.OwnerValue) { return CompletionResult.DefenderWonNoReward; }
                }
                return CompletionResult.DefenderWonUnemployed;
            } else {
                if (isEmployedHere) {
                    if (employer == location.OwnerValue) { return CompletionResult.AttackerWonEmployerLost; }
                    if (employer == attacker && playerDrops > 0) { return CompletionResult.AttackerWonReward; }
                    if (employer == attacker) { return CompletionResult.AttackerWonNoReward; }
                }
                return CompletionResult.AttackerWonUnemployed;
            }
        }

        public virtual string completionText() {
            CompletionResult result = getCompletionResult();

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

            return "Something went wrong. Attacker: {0}. Defender: {1}. Location: {2}.";
        }

        public virtual string reward() {
            Settings s = WIIC.settings;
            CompletionResult result = getCompletionResult();

            if (result == CompletionResult.AttackerWonReward || result == CompletionResult.DefenderWonReward) {
                return s.factionAttackReward.ContainsKey(employer.Name) ? s.factionAttackReward[employer.Name] : s.defaultAttackReward;
            }
            return null;
        }

        public void conclude() {
            Settings s = WIIC.settings;

            removeParticipationContracts();
            string text = Strings.T(completionText(), employer.FactionDef.Name, target.FactionDef.Name, location.Name);
            // Because shortnames can start with a lowercase 'the' ("the Aurigan Coalition", for example), we have to fix the capitalization or the result can look weird.
            text = text.Replace(". the ", ". The ");
            text = char.ToUpper(text[0]) + text.Substring(1);
            WIIC.modLog.Info?.Write(text);

            // At the current location, an attack gets a popup - whether or not the player was involved, it's important.
            if (WIIC.sim.CurSystem == location) {
                SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
                string primaryButtonText = Strings.T("Acknowledged");
                string itemCollection = reward();

                Sprite sprite = attackerStrength > 0 ? employer.FactionDef.GetSprite() : target.FactionDef.GetSprite();
                queue.QueuePauseNotification($"{extendedType.name} Complete", text, sprite, string.Empty, delegate {
                    Utilities.giveReward(itemCollection);
                }, primaryButtonText);
                if (!queue.IsOpen) {
                    queue.DisplayIfAvailable();
                }
            // Things happening elsewhere in the galaxy just get an event toast.
            } else {
                WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(text));
            }

            finalEffects();
        }

        public virtual void finalEffects() {
            // Now apply the owner or stat changes
            if (defenderStrength <= 0 && attackerStrength > 0) {
                Utilities.applyOwner(location, employer, true);
            }
        }

        public override string getMapDescription() {
            StringBuilder description = new StringBuilder();
            description.AppendLine(basicMapDescription());

            if (countdown > 0) {
               description.AppendLine(Strings.T("{0} days until the fighting starts", countdown));
            }
            description.AppendLine("\n" + Strings.T("{0} forces: {1}", employer.Name.Replace("the ", ""), Utilities.forcesToString(attackerStrength)));
            description.AppendLine(Strings.T("{0} forces: {1}", target.FactionDef.Name.Replace("the ", ""), Utilities.forcesToString(defenderStrength)));

            description.AppendLine("");
            return description.ToString();
        }

        public virtual string basicMapDescription() {
            return Strings.T("<b><color=#de0202>{0} is under attack by {1}</color></b>", location.Name, employer.FactionDef.ShortName);
        }

        public override void applyDeclinePenalty(DeclinePenalty declinePenalty) {
            if (employer == attacker) {
                attackerStrength -= currentContractForceLoss;
                WIIC.modLog.Debug?.Write($"defenderStrength -= {currentContractForceLoss}");
            } else {
                defenderStrength -= currentContractForceLoss;
                WIIC.modLog.Debug?.Write($"attackerStrength -= {currentContractForceLoss}");
            }

            base.applyDeclinePenalty(declinePenalty);
        }
    }
}
