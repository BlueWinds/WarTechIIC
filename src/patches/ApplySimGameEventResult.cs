using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    [HarmonyPatch(typeof(SimGameState), "ApplySimGameEventResult")]
    public static class SimGameState_ApplySimGameEventResult_Patch {
        // WIIC_give_Sol_to_ClanWolf
        private static Regex GIVE_SYSTEM = new Regex("^WIIC_give_(.*?)_to_(.*)$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_attacks_Sol
        private static Regex ATTACK_SYSTEM = new Regex("^WIIC_(.*?)_attacks_(.*)$", RegexOptions.Compiled);

        // WIIC_ClanDiamondShark_attacks_ClanJadeFalcon_x3
        private static Regex ATTACK_FACTION = new Regex("^WIIC_(.*?)_attacks_(.*?)_(.*)x$", RegexOptions.Compiled);

        // WIIC_set_Sol_attacker_strength_10
        private static Regex ATTACKER_FORCES = new Regex("^WIIC_set_(.*?)_attacker_strength_(.*)$", RegexOptions.Compiled);

        // WIIC_set_Sol_defender_strength_10
        private static Regex DEFENDER_FORCES = new Regex("^WIIC_set_(.*?)_defender_strength_(.*)$", RegexOptions.Compiled);

        public static void Prefix(ref SimGameEventResult result) {
            try {
                Settings s = WIIC.settings;
                WIIC.modLog.Debug?.Write($"ApplySimGameEventResult");

                if (result.Scope == EventScope.Company && result.AddedTags != null) {
                    foreach (string addedTag in result.AddedTags.ToList()) {
                        MatchCollection matches = GIVE_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            StarSystem system = WIIC.sim.GetSystemById(matches[0].Groups[0].Value);
                            FactionValue newOwner = FactionEnumeration.GetFactionByName(matches[0].Groups[1].Value);
                            WIIC.modLog.Debug?.Write($"ApplySimGameEventResult: Setting control of {system.Name} to {newOwner.Name}");

                            Utilities.applyOwner(system, newOwner);

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACK_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            FactionValue attacker = FactionEnumeration.GetFactionByName(matches[0].Groups[0].Value);
                            StarSystem system = WIIC.sim.GetSystemById(matches[0].Groups[1].Value);
                            WIIC.modLog.Debug?.Write($"ApplySimGameEventResult: {attacker.Name} attacking {system.Name}");

                            Flareup flareup = new Flareup(system, attacker, WIIC.sim);
                            WIIC.flareups[system.ID] = flareup;
                            flareup.addToMap();

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACK_FACTION.Matches(addedTag);
                        if (matches.Count > 0) {
                            FactionValue attacker = FactionEnumeration.GetFactionByName(matches[0].Groups[0].Value);
                            FactionValue defender = FactionEnumeration.GetFactionByName(matches[0].Groups[1].Value);
                            int count = int.Parse(matches[0].Groups[2].Value);

                            List<StarSystem> targets = WhoAndWhere.getDefenderBorderWorlds(attacker, defender, count);
                            WIIC.modLog.Debug?.Write($"ApplySimGameEventResult: {attacker.Name} attacking {defender.Name} in {count} systems (found {targets.Count} viable border worlds)");

                            foreach (StarSystem system in targets) {
                                Flareup flareup = new Flareup(system, attacker, WIIC.sim);
                                WIIC.flareups[system.ID] = flareup;
                                flareup.addToMap();
                            }

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ATTACKER_FORCES.Matches(addedTag);
                        if (matches.Count > 0) {
                            StarSystem system = WIIC.sim.GetSystemById(matches[0].Groups[0].Value);
                            int strength = int.Parse(matches[0].Groups[1].Value);

                            if (WIIC.flareups.ContainsKey(system.ID)) {
                                WIIC.modLog.Debug?.Write($"ApplySimGameEventResult: Setting attacker strength to {strength} at {system.Name} in flareup");
                                WIIC.flareups[system.ID].attackerStrength = strength;
                            } else {
                                WIIC.modLog.Debug?.Write($"ApplySimGameEventResult: Would set attacker strength to {strength} at {system.Name}, but no flareup found");
                            }

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = DEFENDER_FORCES.Matches(addedTag);
                        if (matches.Count > 0) {
                            StarSystem system = WIIC.sim.GetSystemById(matches[0].Groups[0].Value);
                            int strength = int.Parse(matches[0].Groups[1].Value);

                            if (WIIC.flareups.ContainsKey(system.ID)) {
                                WIIC.modLog.Debug?.Write($"ApplySimGameEventResult: Setting defender strength to {strength} at {system.Name} in flareup");
                                WIIC.flareups[system.ID].defenderStrength = strength;
                            } else {
                                WIIC.modLog.Debug?.Write($"ApplySimGameEventResult: Would set defender strength to {strength} at {system.Name}, but no flareup found");
                            }

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }
                    }
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
