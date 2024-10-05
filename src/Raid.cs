using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using BattleTech;
using BattleTech.UI;
using Localize;

namespace WarTechIIC {
    public class Raid : Attack {
        public Raid() {
            // Empty constructor used for deserialization.
        }

        public Raid(StarSystem flareupLocation, FactionValue attacker, ExtendedContractType flareupType) : base(flareupLocation, attacker, flareupType) {
            Settings s = WIIC.settings;
            attackerStrength = (int) Math.Ceiling(attackerStrength * s.raidStrengthMultiplier);
            defenderStrength = (int) Math.Ceiling(defenderStrength * s.raidStrengthMultiplier);
        }

        public override string completionText() {
            CompletionResult result = getCompletionResult();

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

            return "Something went wrong. Attacker: {0}. Defender: {1}. Location: {2}.";
        }

        public override string reward() {
            Settings s = WIIC.settings;
            CompletionResult result = getCompletionResult();

            if (result == CompletionResult.AttackerWonReward || result == CompletionResult.DefenderWonReward) {
                return s.factionRaidReward.ContainsKey(employer.Name) ? s.factionRaidReward[employer.Name] : s.defaultRaidReward;
            }
            return null;
        }

        public override void finalEffects() {
            SimGameEventResult result = new SimGameEventResult();
            result.Scope = EventScope.Company;
            result.TemporaryResult = true;
            result.ResultDuration = WIIC.settings.raidResultDuration;

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

        public override string basicMapDescription() {
            return Strings.T("<b><color=#de0202>{0} is being raided by {1}</color></b>", location.Name, employer.FactionDef.ShortName);
        }
    }
}
