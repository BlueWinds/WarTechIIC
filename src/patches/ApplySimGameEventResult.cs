using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameState), "ApplySimGameEventResult", new Type[] {typeof(SimGameEventResult), typeof(List<object>), typeof(SimGameEventTracker)})]
    public static class SimGameState_ApplySimGameEventResult_Patch {
        // WIIC_give_systemdef_Sol_to_Clan Wolf
        private static Regex GIVE_SYSTEM = new Regex("^WIIC_give_(?<system>.*?)_to_(?<faction>.*)$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_attacks_systemdef_Sol
        private static Regex ATTACK_SYSTEM = new Regex("^WIIC_(?<faction>.*?)_attacks_(?<system>.*)$", RegexOptions.Compiled);

        // WIIC_set_systemdef_Sol_attacker_strength_10
        private static Regex ATTACKER_FORCES = new Regex("^WIIC_set_(?<system>.*?)_attacker_strength_(?<strength>.*)$", RegexOptions.Compiled);

        // WIIC_set_systemdef_Sol_defender_strength_10
        private static Regex DEFENDER_FORCES = new Regex("^WIIC_set_(?<system>.*?)_defender_strength_(?<strength>.*)$", RegexOptions.Compiled);

        public static void Prefix(ref SimGameEventResult result) {
            try {
                Settings s = WIIC.settings;
                WIIC.modLog.Debug?.Write($"ApplySimGameEventResult");

                if (result.Scope == EventScope.Company && result.AddedTags != null) {
                    foreach (string addedTag in result.AddedTags.ToList()) {
                        MatchCollection matches = GIVE_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            string factionID = matches[0].Groups["faction"].Value;
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult GIVE_SYSTEM: systemId {systemId}, factionID {factionID}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            if (system == null) {
                                WIIC.modLog.Debug?.Write($"ERROR: Could not find system with systemId {systemId}");
                            }
                            FactionValue faction = Utilities.getFactionValueByFactionID(factionID);
                            if (faction == null) {
                                WIIC.modLog.Debug?.Write($"ERROR: Could not find faction with factionID {factionID}");
                            }

                            if (faction == null || system == null) continue;

                            Utilities.cleanupSystem(system);
                            Utilities.applyOwner(system, faction);

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACK_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string factionID = matches[0].Groups["faction"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult ATTACK_SYSTEM: factionID {factionID}, systemId {systemId}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            if (system == null) {
                                WIIC.modLog.Debug?.Write($"ERROR: Could not find system with systemId {systemId}");
                            }
                            FactionValue faction = Utilities.getFactionValueByFactionID(factionID);
                            if (faction == null) {
                                WIIC.modLog.Debug?.Write($"ERROR: Could not find faction with factionID {factionID}");
                            }
                            if (faction == null || system == null) continue;

                            if (system.OwnerValue.Name == faction.Name) {
                                WIIC.modLog.Info?.Write($"Tagged system {system.Name} already owned by attacker {faction.Name}, ignoring");
                                continue;
                            }

                            Utilities.cleanupSystem(system);
                            Flareup flareup = new Flareup(system, faction, WIIC.sim);
                            WIIC.flareups[system.ID] = flareup;
                            flareup.addToMap();

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACKER_FORCES.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            int strength = int.Parse(matches[0].Groups["strength"].Value);
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult ATTACKER_FORCES: systemId {systemId}, strength {strength}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId); 
                            if (system == null) {
                                WIIC.modLog.Debug?.Write($"ERROR: Could not find system with systemId {systemId}");
                                continue;
                            }

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
                            if (system == null) {
                                WIIC.modLog.Debug?.Write($"ERROR: Could not find system with systemId {systemId}");
                                continue;
                            }

                            if (WIIC.flareups.ContainsKey(system.ID)) {
                                WIIC.flareups[system.ID].attackerStrength = strength;
                            } else {
                                WIIC.modLog.Error?.Write($"ApplySimGameEventResult: No flareup found at {systemId}");
                            }

                            result.AddedTags.Remove(addedTag);
                        }
                    }
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
