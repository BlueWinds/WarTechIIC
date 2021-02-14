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
            WIIC.modLog.Debug?.Write($"Checking for new flareup: {rand} / {WIIC.settings.dailyFlareupChance}");
            if (rand > WIIC.settings.dailyFlareupChance || (WIIC.sim.IsCampaign && !WIIC.sim.CompanyTags.Contains("story_complete"))) {
                return;
            }

            FactionValue attacker = getAggressiveFaction();
            StarSystem system = getEnemyBorderWorld(attacker);

            string text = Strings.T("{0} attacking {1} at {2}.", attacker.FactionDef.ShortName, system.OwnerValue.FactionDef.ShortName, system.Name);
            WIIC.modLog.Info?.Write(text);
            WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(text));

            Flareup flareup = new Flareup(system, attacker, WIIC.sim);
            WIIC.flareups[system.Name] = flareup;
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
                withMult[entry.Key] = entry.Value * modifier;
                WIIC.modLog.Debug?.Write($"Potential aggressor {entry.Key.Name} with weight {withMult[entry.Key]} (modifier {modifier})");
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
                if (attackerNeighboringSystems == 0 || WIIC.flareups.ContainsKey(system.ID)) {
                    continue;
                }

                if (!enemyBorderWorlds.ContainsKey(system.OwnerValue)) {
                    enemyBorderWorlds[system.OwnerValue] = new Dictionary<StarSystem, double>();
                    enemyAttackWeight[system.OwnerValue] = s.baseTargetChance;
                    WIIC.modLog.Debug?.Write($"Potential defender {system.OwnerValue.Name} added with inital weight {s.baseTargetChance}");
                }
                enemyBorderWorlds[system.OwnerValue][system] = attackerNeighboringSystems;
                enemyAttackWeight[system.OwnerValue] += s.targetChancePerBorderWorld;
            }

            FactionValue defender = Utilities.WeightedChoice(enemyAttackWeight);
            WIIC.modLog.Debug?.Write($"Chose defender {defender.Name} based on weight {enemyAttackWeight[defender]}");
            return Utilities.WeightedChoice(enemyBorderWorlds[defender]);
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
