using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Harmony;
using BattleTech;

namespace WarTechIIC {

    [HarmonyPatch(typeof(SimGameState), "ApplySimGameEventResult", new Type[] {typeof(SimGameEventResult), typeof(List<object>), typeof(SimGameEventTracker)})]
    public static class SimGameState_ApplySimGameEventResult_Patch {
        // WIIC_give_Sol_to_ClanWolf
        private static Regex GIVE_SYSTEM = new Regex("^WIIC_give_(?<system>.*?)_to_(?<faction>.*)$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_attacks_Sol
        private static Regex ATTACK_SYSTEM = new Regex("^WIIC_(?<faction>.*?)_attacks_(?<system>.*)$", RegexOptions.Compiled);

        // WIIC_set_Sol_attacker_strength_10
        private static Regex ATTACKER_FORCES = new Regex("^WIIC_set_(?<system>.*?)_attacker_strength_(?<strength>.*)$", RegexOptions.Compiled);

        // WIIC_set_Sol_defender_strength_10
        private static Regex DEFENDER_FORCES = new Regex("^WIIC_set_(?<system>.*?)_defender_strength_(?<strength>.*)$", RegexOptions.Compiled);

        public static void Prefix(ref SimGameEventResult result) {
            try {
                Settings s = WIIC.settings;
                WIIC.modLog.Debug?.Write($"ApplySimGameEventResult");

                if (result.Scope == EventScope.Company && result.AddedTags != null) {
                    foreach (string addedTag in result.AddedTags.ToList()) {
                        MatchCollection matches = GIVE_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = $"starsystemdef_{matches[0].Groups["system"].Value}";
                            string factionName = matches[0].Groups["faction"].Value;
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult GIVE_SYSTEM: systemId {systemId}, factionName {factionName}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            FactionValue faction = Utilities.GetFactionValueByFactionID(factionName);

                            Utilities.applyOwner(system, faction);

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACK_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string factionName = matches[0].Groups["faction"].Value;
                            string systemId = $"starsystemdef_{matches[0].Groups["system"].Value}";
                            WIIC.modLog.Info?.Write($"ApplySimGameEventResult ATTACK_SYSTEM: factionName {factionName}, systemId {systemId}");

                            FactionValue faction = Utilities.GetFactionValueByFactionID(factionName);
                            StarSystem system = WIIC.sim.GetSystemById(systemId);

                            Flareup flareup = new Flareup(system, faction, WIIC.sim);
                            WIIC.flareups[system.ID] = flareup;
                            flareup.addToMap();

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACKER_FORCES.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = $"starsystemdef_{matches[0].Groups["system"].Value}";
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
                            string systemId = $"starsystemdef_{matches[0].Groups["system"].Value}";
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
                    }
                }
            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
