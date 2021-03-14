using BattleTech;
using HBS.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using IRBTModUtils.Logging;
using Localize;

namespace WarTechIIC {
    public class WhoAndWhere {
        private static Dictionary<string, TagSet> factionActivityTags = new Dictionary<string, TagSet>();
        private static Dictionary<string, TagSet> factionInvasionTags = new Dictionary<string, TagSet>();
        private static TagSet cantBeAttackedTags;

        public static void init() {
            Settings s = WIIC.settings;
            FactionValue invalid = FactionEnumeration.GetInvalidUnsetFactionValue();

            cantBeAttackedTags = new TagSet(s.cantBeAttackedTags);

            // Initializing tagsets for use when creating flareups
            foreach (string faction in s.factionActivityTags.Keys) {
                if (FactionEnumeration.GetFactionByName(faction) == invalid) {
                    WIIC.modLog.Warn?.Write($"Can't find faction {faction} from factionActivityTags");
                   continue;
                }
                factionActivityTags[faction] = new TagSet(s.factionActivityTags[faction]);
            }

            foreach (string faction in s.factionInvasionTags.Keys) {
                if (FactionEnumeration.GetFactionByName(faction) == invalid) {
                    WIIC.modLog.Warn?.Write($"Can't find faction {faction} from factionInvasionTags");
                   continue;
                }
                factionInvasionTags[faction] = new TagSet(s.factionInvasionTags[faction]);
            }

            // Validation for factions in various settings
            foreach (string faction in s.aggression.Keys) {
                if (FactionEnumeration.GetFactionByName(faction) == invalid) {
                    WIIC.modLog.Warn?.Write($"Can't find faction {faction} from aggression");
                }
            }
            foreach (string faction in s.hatred.Keys) {
                if (FactionEnumeration.GetFactionByName(faction) == invalid) {
                    WIIC.modLog.Warn?.Write($"Can't find faction {faction} from hatred");
                }
                foreach (string target in s.hatred[faction].Keys) {
                    if (FactionEnumeration.GetFactionByName(target) == invalid) {
                        WIIC.modLog.Warn?.Write($"Can't find faction {target} from hatred[{faction}]");
                    }
                }
            }
            foreach (string faction in s.cantBeAttacked) {
                if (FactionEnumeration.GetFactionByName(faction) == invalid) {
                    WIIC.modLog.Warn?.Write($"Can't find faction {faction} from cantBeAttacked");
                }
            }
        }

        public static void checkForNewFlareup() {
            double rand = Utilities.rng.NextDouble();
            double flareupChance = Utilities.statOrDefault("WIIC_dailyAttackChance", WIIC.settings.dailyAttackChance);
            double raidChance = Utilities.statOrDefault("WIIC_dailyRaidChance", WIIC.settings.dailyRaidChance);
            WIIC.modLog.Debug?.Write($"Checking for new flareup: {rand} flareupChance: {flareupChance}, raidChance: {raidChance}");

            string type = "";
            if (rand < flareupChance) {
                type = "Attack";
            } else if (rand < flareupChance + raidChance) {
                type = "Raid";
            }

            if (type == "") {
                return;
            }

            (StarSystem system, FactionValue attacker) = getAttackerAndLocation(type);

            Flareup flareup = new Flareup(system, attacker, type, WIIC.sim);
            WIIC.flareups[system.ID] = flareup;
            flareup.addToMap();
        }

        public static List<string> getTargets(StarSystem system) {
            Settings s = WIIC.settings;
            List<string> targets = new List<string>();

            // Owning faction and locals are always valid targets
            targets.Add("Locals");
            targets.Add(system.OwnerValue.Name);

            foreach (string faction in factionActivityTags.Keys) {
                if (system.Tags.ContainsAny(factionActivityTags[faction])) {
                    targets.Add(faction);
                }
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

        public static (StarSystem, FactionValue) getAttackerAndLocation(string type) {
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

                if (system.Tags.ContainsAny(cantBeAttackedTags)) {
                    continue;
                }

                if (!reputations.ContainsKey(defender)) {
                    SimGameReputation reputation = WIIC.sim.GetReputation(defender);
                    reputations[defender] = s.reputationMultiplier[reputation.ToString()];
                }

                FakeVector3 p1 = system.Def.Position;
                FakeVector3 p2 = WIIC.sim.CurSystem.Def.Position;
                double distanceMult = 1 / (100 + Math.Sqrt((p1.x - p2.x) * (p1.x - p2.x) + (p1.y - p2.y) * (p1.y - p2.y)));

                Action<FactionValue> considerAttacker = (FactionValue attacker) => {
                    if (s.ignoreFactions.Contains(attacker.Name)) {
                        return;
                    }

                    // Factions only attack themselves if they are their own enemy (eg, extremely fractured factions).
                    if ((s.limitTargetsToFactionEnemies || attacker == system.OwnerValue) && !attacker.FactionDef.Enemies.Contains(defender.Name)) {
                        return;
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
                };

                foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                    considerAttacker(neighbor.OwnerValue);
                }

                if (type == "Attack") {
                    foreach (string faction in factionInvasionTags.Keys) {
                        if (system.Tags.ContainsAny(factionInvasionTags[faction])) {
                            considerAttacker(FactionEnumeration.GetFactionByName(faction));
                        }
                    }
                }
                if (type == "Raid") {
                    foreach (string faction in factionActivityTags.Keys) {
                        if (system.Tags.ContainsAny(factionActivityTags[faction])) {
                            considerAttacker(FactionEnumeration.GetFactionByName(faction));
                        }
                    }
                }
            }
            return Utilities.WeightedChoice(weightedLocations);
        }
    }
}
