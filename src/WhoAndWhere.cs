using BattleTech;
using HBS.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using IRBTModUtils.Logging;
using Localize;

namespace WarTechIIC {
    public class WhoAndWhere {
        private static TagSet pirateTags;

        public static void init() {
            pirateTags = new TagSet(WIIC.settings.pirateTags);
        }

        public static void checkForNewFlareup() {
            double rand = Utilities.rng.NextDouble();
            double chance = Utilities.statOrDefault("WIIC_dailyFlareupChance", WIIC.settings.dailyFlareupChance);
            WIIC.modLog.Debug?.Write($"Checking for new flareup: {rand} / {chance}");
            if (rand > chance || (WIIC.sim.IsCampaign && !WIIC.sim.CompanyTags.Contains("story_complete"))) {
                return;
            }

            FactionValue attacker = getAggressiveFaction();
            StarSystem system = getEnemyBorderWorld(attacker);

            string text = Strings.T("{0} attacking {1} at {2}.", attacker.FactionDef.ShortName, system.OwnerValue.FactionDef.ShortName, system.Name);
            WIIC.modLog.Info?.Write(text);
            WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(text));

            Flareup flareup = new Flareup(system, attacker, WIIC.sim);
            WIIC.flareups[system.ID] = flareup;
            flareup.addToMap();
        }

        public static FactionValue getAggressiveFaction() {
            Settings s = WIIC.settings;
            var weightedFactions = new Dictionary<FactionValue, double>();
            foreach (StarSystem system in WIIC.sim.StarSystems) {
                FactionValue faction = system.OwnerValue;

                if (s.ignoreFactions.Contains(faction.Name)) {
                    continue;
                }

                if (!weightedFactions.ContainsKey(faction)) {
                    SimGameReputation reputation = WIIC.sim.GetReputation(faction);
                    weightedFactions[faction] = s.aggressionByReputation[reputation.ToString()];
                }

                weightedFactions[faction] += s.aggressionPerSystemOwned;
            }

            var withMult = new Dictionary<FactionValue, double>();
            foreach (KeyValuePair<FactionValue, double> entry in weightedFactions) {
                double modifier = 1;

                if (s.aggressionMultiplier.ContainsKey(entry.Key.Name)) {
                    modifier = s.aggressionMultiplier[entry.Key.Name];
                }
                withMult[entry.Key] = entry.Value * Utilities.statOrDefault($"WIIC_{entry.Key.Name}", modifier);;
            }

            return Utilities.WeightedChoice(withMult);
        }

        public static StarSystem getEnemyBorderWorld(FactionValue attacker) {
            Settings s = WIIC.settings;
            var enemyBorderWorlds = new Dictionary<FactionValue, Dictionary<StarSystem, double>>();
            var enemyAttackWeight = new Dictionary<FactionValue, double>();
            foreach (StarSystem system in WIIC.sim.StarSystems) {
                if (s.ignoreFactions.Contains(system.OwnerValue.Name) || s.cantBeAttacked.Contains(system.OwnerValue.Name)) {
                    continue;
                }
                if (s.limitTargetsToFactionEnemies && !attacker.FactionDef.Enemies.Contains(system.OwnerValue.Name)) {
                    continue;
                }

                List<StarSystem> neighbors = WIIC.sim.Starmap.GetAvailableNeighborSystem(system);
                int attackerNeighboringSystems = neighbors.Where(n => n.OwnerValue == attacker).Count();
                if (attackerNeighboringSystems == 0 || WIIC.flareups.ContainsKey(system.ID) || Utilities.flashpointInSystem(system)) {
                    continue;
                }

                if (!enemyBorderWorlds.ContainsKey(system.OwnerValue)) {
                    enemyBorderWorlds[system.OwnerValue] = new Dictionary<StarSystem, double>();
                    enemyAttackWeight[system.OwnerValue] = s.baseTargetChance;
                }
                enemyBorderWorlds[system.OwnerValue][system] = attackerNeighboringSystems;
                enemyAttackWeight[system.OwnerValue] += s.targetChancePerBorderWorld;
            }

            foreach (FactionValue potentianDefender in enemyAttackWeight.Keys) {
                enemyAttackWeight[potentianDefender] *= targetMultiplier(attacker, potentianDefender);
            }

            FactionValue defender = Utilities.WeightedChoice(enemyAttackWeight);
            WIIC.modLog.Debug?.Write($"Chose defender {defender.Name} based on weight {enemyAttackWeight[defender]}");
            return Utilities.WeightedChoice(enemyBorderWorlds[defender]);
        }

        public static List<StarSystem> getDefenderBorderWorlds(FactionValue attacker, FactionValue defender, int count) {
            Settings s = WIIC.settings;
            var defenderBorderWorlds = new Dictionary<StarSystem, double>();
            foreach (StarSystem system in WIIC.sim.StarSystems) {
                if (system.OwnerValue != defender || Utilities.flashpointInSystem(system)) {
                    continue;
                }

                List<StarSystem> neighbors = WIIC.sim.Starmap.GetAvailableNeighborSystem(system);
                int attackerNeighboringSystems = neighbors.Where(n => n.OwnerValue == attacker).Count();
                if (attackerNeighboringSystems == 0 || WIIC.flareups.ContainsKey(system.ID)) {
                    continue;
                }

                defenderBorderWorlds[system] = attackerNeighboringSystems;
            }
            List<StarSystem> targets = new List<StarSystem>();
            while (count > 0 && defenderBorderWorlds.Count > 0) {
                StarSystem choice = Utilities.WeightedChoice(defenderBorderWorlds);
                targets.Add(choice);
                defenderBorderWorlds.Remove(choice);
            }

            return targets;
        }

        private static double targetMultiplier(FactionValue attacker, FactionValue defender) {
            Settings s = WIIC.settings;
            double defaultValue = 1;

            if (s.targetChoiceMultiplier.ContainsKey(attacker.Name)) {
                if (s.targetChoiceMultiplier[attacker.Name].ContainsKey(defender.Name)) {
                    defaultValue = s.targetChoiceMultiplier[attacker.Name][defender.Name];
                }
            }

            return Utilities.statOrDefault($"WIIC_{attacker.Name}_hates_{defender.Name}", defaultValue);
        }

        public static List<string> getEmployers(StarSystem system) {
            Settings s = WIIC.settings;
            List<string> employers = new List<string>();

            // Owning faction and locals are always valid employers
            employers.Add("Locals");
            employers.Add(system.OwnerValue.Name);

            if (system.Tags.ContainsAny(pirateTags)) {
                employers.Add(FactionEnumeration.GetAuriganPiratesFactionValue().Name);
            }

            // Look across neighboring systems, and add employers of factions that border this system
            foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                if (!s.ignoreFactions.Contains(neighbor.OwnerValue.Name)) {
                    employers.Add(neighbor.OwnerValue.Name);
                }
            }
            return employers.Distinct().ToList();
        }

        // We don't currently have separate logic for targets / employers. If they can hire you, someone can hire you to attack them
        public static List<string> getTargets(StarSystem system) {
            return getEmployers(system);
        }
    }
}
