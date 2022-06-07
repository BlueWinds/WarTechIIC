using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Harmony;
using BattleTech;
using Localize;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameState), "ApplySimGameEventResult", new Type[] {typeof(SimGameEventResult), typeof(List<object>), typeof(SimGameEventTracker)})]
    public static class SimGameState_ApplySimGameEventResult_Patch {
        // WIIC_give_systemdef_Sol_to_Clan Wolf
        private static Regex GIVE_SYSTEM = new Regex("^WIIC_give_(?<system>.*?)_to_(?<faction>.*)$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_attacks_systemdef_Sol
        private static Regex ATTACK_SYSTEM = new Regex("^WIIC_(?<faction>.*?)_attacks_(?<system>.*)$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_raids_systemdef_Sol
        private static Regex RAID_SYSTEM = new Regex("^WIIC_(?<faction>.*?)_raids_(?<system>.*)$", RegexOptions.Compiled);

        // WIIC_set_systemdef_Sol_attacker_strength_10
        private static Regex ATTACKER_FORCES = new Regex("^WIIC_set_(?<system>.*?)_attacker_strength_(?<strength>.*)$", RegexOptions.Compiled);

        // WIIC_set_systemdef_Sol_defender_strength_10
        private static Regex DEFENDER_FORCES = new Regex("^WIIC_set_(?<system>.*?)_defender_strength_(?<strength>.*)$", RegexOptions.Compiled);

        // WIIC_add_ives_rebellion_to_systemdef_Sol
        private static Regex ADD_SYSTEM_TAG = new Regex("^WIIC_add_(?<faction>.*?)_to_(?<system>.*?)$", RegexOptions.Compiled);

        // WIIC_remove_ives_rebellion_from_systemdef_Sol
        private static Regex REMOVE_SYSTEM_TAG = new Regex("^WIIC_remove_(?<faction>.*?)_from_(?<system>.*?)$", RegexOptions.Compiled);

        public static void Prefix(ref SimGameEventResult result) {
            Settings s = WIIC.settings;

            if (result.Scope == EventScope.Company && result.AddedTags != null) {
                foreach (string addedTag in result.AddedTags.ToList()) {
                    try {
                        MatchCollection matches = GIVE_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            string factionID = matches[0].Groups["faction"].Value;
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult GIVE_SYSTEM: systemId {systemId}, factionID {factionID}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            FactionValue faction = Utilities.getFactionValueByFactionID(factionID);

                            WIIC.cleanupSystem(system);
                            Utilities.applyOwner(system, faction, true);

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACK_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string factionID = matches[0].Groups["faction"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult ATTACK_SYSTEM: factionID {factionID}, systemId {systemId}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            FactionValue faction = Utilities.getFactionValueByFactionID(factionID);

                            if (system.OwnerValue.Name == faction.Name) {
                                WIIC.modLog.Info?.Write($"Tagged system {system.Name} already owned by attacker {faction.Name}, ignoring");
                                continue;
                            }

                            WIIC.cleanupSystem(system);
                            Flareup flareup = new Flareup(system, faction, Flareup.Attack);
                            WIIC.flareups[system.ID] = flareup;
                            Utilities.redrawMap();

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = RAID_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string factionID = matches[0].Groups["faction"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult RAID_SYSTEM: factionID {factionID}, systemId {systemId}");

                            FactionValue faction = Utilities.getFactionValueByFactionID(factionID);
                            StarSystem system = WIIC.sim.GetSystemById(systemId);

                            WIIC.cleanupSystem(system);
                            Flareup flareup = new Flareup(system, faction, Flareup.Raid);
                            WIIC.flareups[system.ID] = flareup;
                            Utilities.redrawMap();

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACKER_FORCES.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            int strength = int.Parse(matches[0].Groups["strength"].Value);
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult ATTACKER_FORCES: systemId {systemId}, strength {strength}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);

                            if (WIIC.flareups.ContainsKey(system.ID)) {
                                WIIC.flareups[system.ID].attackerStrength = strength;
                            } else {
                                WIIC.modLog.Error?.Write($"ApplySimGameEventResult: No flareup found at {systemId}");
                            }

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = DEFENDER_FORCES.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            int strength = int.Parse(matches[0].Groups["strength"].Value);
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult DEFENDER_FORCES: systemId {systemId}, strength {strength}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);

                            if (WIIC.flareups.ContainsKey(system.ID)) {
                                WIIC.flareups[system.ID].attackerStrength = strength;
                            } else {
                                WIIC.modLog.Error?.Write($"ApplySimGameEventResult: No flareup found at {systemId}");
                            }

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ADD_SYSTEM_TAG.Matches(addedTag);
                        if (matches.Count > 0) {
                            string tag = matches[0].Groups["tag"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult ADD_SYSTEM_TAG: tag {tag}, systemId {systemId}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            system.Tags.Add(tag);

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = REMOVE_SYSTEM_TAG.Matches(addedTag);
                        if (matches.Count > 0) {
                            string tag = matches[0].Groups["tag"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult REMOVE_SYSTEM_TAG: tag {tag}, systemId {systemId}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            system.Tags.Remove(tag);

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }
                    } catch (Exception e) {
                        WIIC.modLog.Error?.Write(e);
                    }
                }
            }

            try {
                if (result.Scope == EventScope.Company && result.Stats != null) {
                    WIIC.modLog.Info?.Write($"ApplySimGameEventResult: Searching for WIIC stats from {result.Stats.Length}");
                    foreach (SimGameStat stat in result.Stats) {
                        if (stat.Validate() && stat.name.StartsWith("WIIC")) {
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult: Applying {stat.name} and removing from stats");
                            SimGameState.SetSimGameStat(stat, WIIC.sim.CompanyStats);
                        }
                    }

                    result.Stats = result.Stats.Where(stat => stat.name == null || !stat.name.StartsWith("WIIC")).ToArray();
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write($"result.ToEditorSummaryString(): {result.ToString()}");
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
