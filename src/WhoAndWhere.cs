using BattleTech;
using HBS.Collections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WarTechIIC {
    public class WhoAndWhere {
        private static Dictionary<string, TagSet> factionActivityTags = new Dictionary<string, TagSet>();
        private static Dictionary<string, TagSet> factionInvasionTags = new Dictionary<string, TagSet>();
        private static Dictionary<(StarSystem, FactionValue), double> weightedLocationCache = null;
        public static TagSet clearEmployersTags;

        public static void clearLocationCache() {
            weightedLocationCache = null;
        }

        public static void init() {
            Settings s = WIIC.settings;
            FactionValue invalid = FactionEnumeration.GetInvalidUnsetFactionValue();

            clearEmployersTags = new TagSet(s.clearEmployersAndTargetsForSystemTags);

            // Initializing tagsets for use when creating attacks and raids
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

        public static Attack checkForNewFlareup() {
            double rand = Utilities.rng.NextDouble();
            double flareupChance = Utilities.statOrDefault("WIIC_dailyAttackChance", WIIC.settings.dailyAttackChance);
            double raidChance = Utilities.statOrDefault("WIIC_dailyRaidChance", WIIC.settings.dailyRaidChance);
            WIIC.modLog.Debug?.Write($"Checking for new flareup: {rand} flareupChance: {flareupChance}, raidChance: {raidChance}");

            ExtendedContractType type;
            if (rand < flareupChance) {
                type = WIIC.extendedContractTypes["Attack"];
            } else if (rand < flareupChance + raidChance) {
                type = WIIC.extendedContractTypes["Raid"];
            } else {
                return null;
            }

            (StarSystem system, FactionValue employer) = getFlareupEmployerAndLocation(type);

            if (type == WIIC.extendedContractTypes["Attack"]) {
                WIIC.extendedContracts[system.ID] = new Attack(system, employer, type);
            } else {
                WIIC.extendedContracts[system.ID] = new Raid(system, employer, type);
            }
            return WIIC.extendedContracts[system.ID] as Attack;
        }

        public static bool checkForNewExtendedContract() {
            Settings s = WIIC.settings;

            double rand = Utilities.rng.NextDouble();
            int count = WIIC.extendedContracts.Values.Count(ec => ec.type != "Attack" && ec.type != "Raid");
            WIIC.modLog.Debug?.Write($"Checking for new extended contract: {rand} existing contracts: {count}, chance if 0: {s.dailyExtConChanceIfNoneAvailable}, chance if < {s.maxAvailableExtendedContracts}: {s.dailyExtConChanceIfSomeAvailable}");

            if (count >= s.maxAvailableExtendedContracts) { return false; }
            if (count> 0 && rand > s.dailyExtConChanceIfSomeAvailable) { return false; }
            if (count == 0 && rand > s.dailyExtConChanceIfNoneAvailable) { return false; }

            Dictionary<string, double> weightedTypes = new Dictionary<string, double>();
            foreach (ExtendedContractType possibleType in WIIC.extendedContractTypes.Values) {
                if (possibleType.weight > 0 && possibleType.requirementList.Where(r => r.Scope != EventScope.StarSystem).All(r => WIIC.sim.MeetsRequirements(r))) {
                    weightedTypes[possibleType.name] = (double)possibleType.weight;
                }
            }

            if (weightedTypes.Count == 0) { return false; }


            for (int i = 0; i < 3; i++) {
                ExtendedContractType type = WIIC.extendedContractTypes[Utilities.WeightedChoice(weightedTypes)];
                RequirementDef[] systemReqs = type.requirementList.Where(r => r.Scope == EventScope.StarSystem).ToArray();

                StarSystem system;
                FactionValue employer;
                try {
                    (system, employer) = getExtendedEmployerAndLocation(type.employer, type.spawnLocation, systemReqs);
                } catch (InvalidOperationException) {
                    WIIC.modLog.Debug?.Write($"Chose {type.name}, but couldn't find a system and employer. i={i}");
                    continue;
                }

                FactionValue target = getExtendedTarget(system, employer, type.target);
                ExtendedContract contract = new ExtendedContract(system, employer, target, type);
                WIIC.extendedContracts[system.ID] = contract;

                return true;
            }

            return false;
        }

        public static List<string> getTargets(StarSystem system) {
            Settings s = WIIC.settings;
            List<string> targets = new List<string>();

            if (system.Tags.ContainsAny(clearEmployersTags, false)) {
                return targets;
            }

            // Owning faction and locals are always valid targets
            targets.Add("Locals");
            targets.Add(system.OwnerValue.Name);

            foreach (string faction in factionActivityTags.Keys) {
                if (system.Tags.ContainsAny(factionActivityTags[faction], false)) {
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

        public static double getDistance(StarSystem system) {
            FakeVector3 p1 = system.Def.Position;
            FakeVector3 p2 = WIIC.sim.CurSystem.Def.Position;
            return Math.Sqrt((p1.x - p2.x) * (p1.x - p2.x) + (p1.y - p2.y) * (p1.y - p2.y));
        }

        public static double getDistanceMultiplier(StarSystem system) {
            return 1 / (WIIC.settings.distanceFactor + getDistance(system));
        }

        public static (StarSystem, FactionValue) getFlareupEmployerAndLocation(ExtendedContractType type, FactionValue forceEmployer = null) {
            if (weightedLocationCache != null && forceEmployer == null) {
                return Utilities.WeightedChoice(weightedLocationCache);
            }

            Settings s = WIIC.settings;
            var weightedLocations = new Dictionary<(StarSystem, FactionValue), double>();
            var reputations = new Dictionary<FactionValue, double>();
            var aggressions = new Dictionary<FactionValue, double>();
            var hatred = new Dictionary<(FactionValue, FactionValue), double>();

            foreach (StarSystem system in WIIC.sim.StarSystems) {
                if (getDistance(system) > WIIC.settings.maxAttackRaidDistance) {
                    continue;
                }

                FactionValue owner = system.OwnerValue;

                if (Utilities.flashpointInSystem(system) || WIIC.extendedContracts.ContainsKey(system.ID)) {
                    continue;
                }

                if (s.ignoreFactions.Contains(owner.Name) || s.cantBeAttacked.Contains(owner.Name)) {
                    continue;
                }

                if (!reputations.ContainsKey(owner)) {
                    reputations[owner] = Utilities.getReputationMultiplier(owner);
                }

                double systemMultiplier = 1;
                foreach (string tag in s.systemAggressionByTag.Keys) {
                    if (system.Tags.Contains(tag)) {
                        systemMultiplier *= s.systemAggressionByTag[tag];
                    }
                }

                double distanceMult = getDistanceMultiplier(system);
                // WIIC.modLog.Trace?.Write($"Potential Flareup at {system.Name}, distanceMult: {distanceMult}, distanceFactor: {s.distanceFactor}, owner {owner.Name}");

                Action<FactionValue> considerEmployer = (FactionValue employer) => {
                    if (forceEmployer != null && employer != forceEmployer) {
                        return;
                    }

                    if (s.ignoreFactions.Contains(employer.Name)) {
                        return;
                    }

                    // Factions only attack themselves if they are their own enemy (eg, extremely fractured factions).
                    if ((s.limitTargetsToFactionEnemies || employer == system.OwnerValue) && !employer.FactionDef.Enemies.Contains(owner.Name)) {
                        return;
                    }

                    if (!reputations.ContainsKey(employer)) {
                        reputations[employer] = Utilities.getReputationMultiplier(employer);
                    }

                    if (!aggressions.ContainsKey(employer)) {
                        aggressions[employer] = Utilities.getAggression(employer);
                    }

                    if (!hatred.ContainsKey((employer, owner))) {
                        hatred[(employer, owner)] = Utilities.getHatred(employer, owner);
                    }

                    if (!weightedLocations.ContainsKey((system, employer))) {
                        weightedLocations[(system, employer)] = 0;
                    }

                    double weight = systemMultiplier * aggressions[employer] * (reputations[employer] + reputations[owner]) * distanceMult * hatred[(employer, owner)];
                    // WIIC.modLog.Trace?.Write($"    {employer.Name}: {weightedLocations[(system, employer)]} + {weight} from systemMultiplier {systemMultiplier}, rep[att] {reputations[employer]}, rep[own] {reputations[owner]}, mult {distanceMult}, hatred[(att, own)] {hatred[(employer, owner)]}");
                    weightedLocations[(system, employer)] += weight;
                };

                foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                    considerEmployer(neighbor.OwnerValue);
                }

                if (type == WIIC.extendedContractTypes["Attack"]) {
                    foreach (string faction in factionInvasionTags.Keys) {
                        if (system.Tags.ContainsAny(factionInvasionTags[faction], false)) {
                            considerEmployer(FactionEnumeration.GetFactionByName(faction));
                        }
                    }
                }
                if (type == WIIC.extendedContractTypes["Raid"]) {
                    foreach (string faction in factionActivityTags.Keys) {
                        if (system.Tags.ContainsAny(factionActivityTags[faction], false)) {
                            considerEmployer(FactionEnumeration.GetFactionByName(faction));
                        }
                    }
                }
            }

            if (forceEmployer == null) {
                weightedLocationCache = weightedLocations;
            }

            return Utilities.WeightedChoice(weightedLocations);
        }

        public static (StarSystem, FactionValue) getExtendedEmployerAndLocation(string[] potentialEmployers, SpawnLocation spawnLocation, RequirementDef[] requirementList) {
            Settings s = WIIC.settings;
            var weightedLocations = new Dictionary<(StarSystem, FactionValue), double>();

            WIIC.modLog.Trace?.Write($"Checking locations for extended contract, using distanceFactor: {s.distanceFactor}");
            foreach (StarSystem system in WIIC.sim.StarSystems) {
                if (getDistance(system) > WIIC.settings.maxExtendedContractDistance) {
                    continue;
                }

                FactionValue owner = system.OwnerValue;

                if (Utilities.flashpointInSystem(system) || WIIC.extendedContracts.ContainsKey(system.ID)) {
                    continue;
                }

                if (!requirementList.All(r => SimGameState.MeetsRequirements(r, system.Tags, system.Stats))) {
                    continue;
                }

                double distanceMult = getDistanceMultiplier(system);
                // WIIC.modLog.Trace?.Write($"    {system.Name}, distanceMult: {distanceMult}, owner {owner.Name}");

                foreach (FactionValue employer in potentialExtendedEmployers(system, spawnLocation, potentialEmployers)) {
                    weightedLocations[(system, employer)] = distanceMult;
                }
            }

            return Utilities.WeightedChoice(weightedLocations);
        }

        public static List<FactionValue> potentialExtendedEmployers(StarSystem system, SpawnLocation spawnLocation, string[] potentialEmployers) {
            Settings s = WIIC.settings;
            List<FactionValue> employers = new List<FactionValue>();
            FactionValue owner = system.OwnerValue;

            if (spawnLocation == SpawnLocation.Any) {
                foreach (string employerOption in potentialEmployers) {
                    if (employerOption == "Any") { throw new Exception("Any is not a valid employer for spawnLocation Any."); }
                    else if (employerOption == "OwnSystem") { employers.Add(owner); }
                    else if (employerOption == "Allied") { employers.AddRange(Utilities.getAllies()); }
                    else { employers.Add(FactionEnumeration.GetFactionByName(employerOption)); }
                }
            } else if (spawnLocation == SpawnLocation.OwnSystem) {
                foreach (string employerOption in potentialEmployers) {
                    if (employerOption == "Any" && !s.ignoreFactions.Contains(owner.Name) && !s.wontHirePlayer.Contains(owner.Name)) { employers.Add(owner); }
                    else if (employerOption == "OwnSystem") { throw new Exception("OwnSystem is not a valid employer for spawnLocation OwnSystem."); }
                    else if (employerOption == "Allied" && WIIC.sim.IsFactionAlly(owner)) { employers.Add(owner); }
                    else if (employerOption == owner.Name) { employers.Add(owner); }
                }
            } else if (spawnLocation == SpawnLocation.NearbyEnemy) {
                foreach (string employerOption in potentialEmployers) {
                    if (employerOption == "Any") {
                        foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                            if (s.ignoreFactions.Contains(neighbor.OwnerValue.Name) || s.wontHirePlayer.Contains(neighbor.OwnerValue.Name)) { continue; }
                            if (neighbor.OwnerValue.FactionDef.Enemies.Contains(owner.Name)) {
                                employers.Add(neighbor.OwnerValue);
                            }
                        }
                    }
                    else if (employerOption == "OwnSystem") { throw new Exception("OwnSystem is not a valid employer for spawnLocation NearbyEnemy."); }
                    else if (employerOption == "Allied") {
                        foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                            if (neighbor.OwnerValue.FactionDef.Enemies.Contains(owner.Name) && WIIC.sim.IsFactionAlly(neighbor.OwnerValue)) {
                                employers.Add(neighbor.OwnerValue);
                            }
                        }
                    }
                    else {
                        foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                            if (neighbor.OwnerValue.Name == employerOption && neighbor.OwnerValue.FactionDef.Enemies.Contains(owner.Name)) {
                                employers.Add(neighbor.OwnerValue);
                            }
                        }
                    }
                }
            }

            return employers.Distinct().ToList();
        }

        public static FactionValue getExtendedTarget(StarSystem system, FactionValue employer, string[] potentialTargets) {
            List<FactionValue> factions = new List<FactionValue>();
            foreach (string target in potentialTargets) {
                if (target == "Employer") { factions.Add(employer); }
                else if (target == "SystemOwner") { factions.Add(system.OwnerValue); }
                else if (target == "NearbyEnemy") {
                    foreach (StarSystem neighbor in  WIIC.sim.Starmap.GetAvailableNeighborSystem(system)) {
                        if (employer.FactionDef.Enemies.Contains(neighbor.OwnerValue.Name)) {
                            factions.Add(neighbor.OwnerValue);
                        }
                    }
                }
                else {
                    factions.Add(FactionEnumeration.GetFactionByName(target));
                }
            }

            return Utilities.Choice(factions);
        }
    }
}
