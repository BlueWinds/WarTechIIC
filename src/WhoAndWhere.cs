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

            (StarSystem system, FactionValue attacker) = getFlareupAttackerAndLocation();

            string text = Strings.T("{0} attacking {1} at {2}.", attacker.FactionDef.ShortName, system.OwnerValue.FactionDef.ShortName, system.Name);
            WIIC.modLog.Info?.Write(text);
            WIIC.sim.RoomManager.ShipRoom.AddEventToast(new Text(text));

            Flareup flareup = new Flareup(system, attacker, WIIC.sim);
            WIIC.flareups[system.ID] = flareup;
            flareup.addToMap();
        }

        public static List<string> getTargets(StarSystem system) {
            Settings s = WIIC.settings;
            List<string> targets = new List<string>();

            // Owning faction and locals are always valid targets
            targets.Add("Locals");
            targets.Add(system.OwnerValue.Name);

            if (system.Tags.ContainsAny(pirateTags)) {
                targets.Add(FactionEnumeration.GetAuriganPiratesFactionValue().Name);
            }

            // Look across neighboring systems, and add targets of factions that border this system
            foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                if (!s.ignoreFactions.Contains(neighbor.OwnerValue.Name)) {
                    targets.Add(neighbor.OwnerValue.Name);
                }
            }
            return targets.Distinct().ToList();
        }

        public static List<string> getEmployers(StarSystem system) {
            List<string> employers = getTargets(system);
            employers.RemoveAll(f => WIIC.settings.wontHirePlayer.Contains(f));
            return employers;
        }

        public static (StarSystem, FactionValue) getFlareupAttackerAndLocation() {
            Settings s = WIIC.settings;
            var weightedLocations = new Dictionary<(StarSystem, FactionValue), double>();
            var reputations = new Dictionary<FactionValue, double>();
            var aggressions = new Dictionary<FactionValue, double>();
            var hatred = new Dictionary<(FactionValue, FactionValue), double>();
            foreach (StarSystem system in WIIC.sim.StarSystems) {
                FactionValue defender = system.OwnerValue;

                if (WIIC.flareups.ContainsKey(system.ID) || Utilities.flashpointInSystem(system)) {
                    continue;
                }

                if (s.ignoreFactions.Contains(defender.Name) || s.cantBeAttacked.Contains(defender.Name)) {
                    continue;
                }

                if (!reputations.ContainsKey(defender)) {
                    SimGameReputation reputation = WIIC.sim.GetReputation(defender);
                    reputations[defender] = s.reputationMultiplier[reputation.ToString()];
                }

                FakeVector3 p1 = system.Def.Position;
                FakeVector3 p2 = WIIC.sim.CurSystem.Def.Position;
                double distanceMult = 1 / (100 + Math.Sqrt((p1.x - p2.x) * (p1.x - p2.x) + (p1.y - p2.y) * (p1.y - p2.y)));

                foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                    FactionValue attacker = neighbor.OwnerValue;
                    if (s.ignoreFactions.Contains(attacker.Name)) {
                        continue;
                    }
                    if (s.limitTargetsToFactionEnemies && !attacker.FactionDef.Enemies.Contains(defender.Name)) {
                        continue;
                    }

                    if (!reputations.ContainsKey(attacker)) {
                        SimGameReputation reputation = WIIC.sim.GetReputation(attacker);
                        reputations[attacker] = s.reputationMultiplier[reputation.ToString()];
                    }
                    if (!aggressions.ContainsKey(attacker)) {
                        double aggression = s.aggression.ContainsKey(attacker.Name) ? s.aggression[attacker.Name] : 1;
                        aggressions[attacker] = Utilities.statOrDefault($"WIIC_{attacker.Name}_aggression", aggression);
                    }

                    if (!hatred.ContainsKey((attacker, defender))) {
                        double hate = s.hatred.ContainsKey(attacker.Name) && s.hatred[attacker.Name].ContainsKey(defender.Name) ? s.hatred[attacker.Name][defender.Name] : 1;
                        hatred[(attacker, defender)] = Utilities.statOrDefault($"WIIC_{attacker.Name}_hates_{defender.Name}", hate);
                    }

                    if (!weightedLocations.ContainsKey((system, attacker))) {
                        weightedLocations[(system, attacker)] = 0;
                    }

                    weightedLocations[(system, attacker)] += aggressions[attacker] * (reputations[attacker] + reputations[defender]) * distanceMult * hatred[(attacker, defender)];
                }
            }
            return Utilities.WeightedChoice(weightedLocations);
        }
    }
}
