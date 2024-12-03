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
        // WIIC_give_starsystemdef_Terra_to_Clan Wolf
        private static Regex GIVE_SYSTEM = new Regex("^WIIC_give_(?<system>.*?)_to_(?<faction>.*)$", RegexOptions.Compiled);

        // WIIC_give_starsystemdef_Terra_to_Clan Wolf_on_attacker_win
        private static Regex GIVE_SYSTEM_ON_WIN = new Regex("^WIIC_give_(?<system>.*?)_to_(?<faction>.*)_on_attacker_win$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_attacks_starsystemdef_Terra
        private static Regex ATTACK_SYSTEM = new Regex("^WIIC_(?<faction>.*?)_attacks_(?<system>.*)$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_raids_starsystemdef_Terra
        private static Regex RAID_SYSTEM = new Regex("^WIIC_(?<faction>.*?)_raids_(?<system>.*)$", RegexOptions.Compiled);

        // WIIC_set_starsystemdef_Terra_attacker_strength_10
        private static Regex ATTACKER_FORCES = new Regex("^WIIC_set_(?<system>.*?)_attacker_strength_(?<strength>.*)$", RegexOptions.Compiled);

        // WIIC_set_starsystemdef_Terra_defender_strength_10
        private static Regex DEFENDER_FORCES = new Regex("^WIIC_set_(?<system>.*?)_defender_strength_(?<strength>.*)$", RegexOptions.Compiled);

        // WIIC_add_ives_rebellion_to_starsystemdef_Terra
        private static Regex ADD_SYSTEM_TAG = new Regex("^WIIC_add_(?<tag>.*?)_to_(?<system>.*?)$", RegexOptions.Compiled);

        // WIIC_remove_ives_rebellion_from_starsystemdef_Terra
        private static Regex REMOVE_SYSTEM_TAG = new Regex("^WIIC_remove_(?<tag>.*?)_from_(?<system>.*?)$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_offers_StoryTime_3_Axylus_Default_at_starsystemdef_Terra_against_ClanWolf
        private static Regex OFFER_CONTRACT_TAG = new Regex("^WIIC_(?<employer>.*?)_offers_(?<contractName>.*?)_at_(?<system>.*?)_against_(?<target>.*?)$", RegexOptions.Compiled);

        // WIIC_ClanJadeFalcon_offers_Garrison Duto_at_starsystemdef_Terra_against_ClanWolf
        private static Regex OFFER_EXTENDED_CONTRACT_TAG = new Regex("^WIIC_(?<employer>.*?)_offers_extended_(?<extendedContractType>.*?)_at_(?<system>.*?)_against_(?<target>.*?)$", RegexOptions.Compiled);

        public static string anS(FactionValue faction) {
            if (faction.FactionDef.Name.EndsWith("s")) { return ""; }
            return "s";
        }

        public static void Prefix(ref SimGameEventResult result) {
            Settings s = WIIC.settings;
            WIIC.eventResultsCache.Clear();

            if (result.Scope == EventScope.Company && result.AddedTags != null) {
                foreach (string addedTag in result.AddedTags.ToList()) {
                    try {
                        MatchCollection matches = GIVE_SYSTEM_ON_WIN.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            string factionID = matches[0].Groups["faction"].Value;
                            WIIC.l.Log($"ApplySimGameEventResult GIVE_SYSTEM_ON_WIN: systemId {systemId}, factionID {factionID}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);

                            if (WIIC.extendedContracts.ContainsKey(system.ID)) {
                                if (WIIC.extendedContracts[system.ID] is Attack) {
                                    Attack attack = (Attack)WIIC.extendedContracts[system.ID];
                                    attack.giveOnWin = factionID;
                                } else {
                                    WIIC.l.LogError($"ApplySimGameEventResult: Flareup at {systemId} is '{WIIC.extendedContracts[system.ID].type}' rather than an Attack");
                                }
                            } else {
                                WIIC.l.LogError($"ApplySimGameEventResult: No flareup found at {systemId}");
                            }

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = GIVE_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            string factionID = matches[0].Groups["faction"].Value;
                            WIIC.l.Log($"ApplySimGameEventResult GIVE_SYSTEM: systemId {systemId}, factionID {factionID}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            FactionValue faction = Utilities.getFactionValueByFactionID(factionID);

                            Utilities.cleanupSystem(system);
                            Utilities.applyOwner(system, faction, true);

                            result.AddedTags.Remove(addedTag);
                            WIIC.eventResultsCache.Add(($"[[DM.Factions[faction_{factionID}],{faction.FactionDef.CapitalizedName}]] take{anS(faction)} control of", $"[[DM.SystemDefs[{systemId}],{system.Name}]]"));
                            continue;
                        }


                        matches = ATTACK_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string factionID = matches[0].Groups["faction"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.l.Log($"ApplySimGameEventResult ATTACK_SYSTEM: factionID {factionID}, systemId {systemId}");

                            FactionValue faction = Utilities.getFactionValueByFactionID(factionID);
                            StarSystem system;
                            if (systemId == "SOMEWHERE") {
                                (system, FactionValue _ignored) = WhoAndWhere.getFlareupEmployerAndLocation(WIIC.extendedContractTypes["Attack"], faction);
                            } else {
                                system = WIIC.sim.GetSystemById(systemId);
                            }

                            if (system.OwnerValue.Name == faction.Name) {
                                WIIC.l.Log($"Tagged system {system.Name} already owned by attacker {faction.Name}, ignoring");
                                continue;
                            }

                            Utilities.cleanupSystem(system);
                            WIIC.extendedContracts[system.ID] = new Attack(system, faction, WIIC.extendedContractTypes["Attack"]);
                            Utilities.redrawMap();

                            result.AddedTags.Remove(addedTag);
                            WIIC.eventResultsCache.Add(($"[[DM.Factions[faction_{factionID}],{faction.FactionDef.CapitalizedName}]] invade{anS(faction)}", $"[[DM.SystemDefs[{systemId}],{system.Name}]]"));
                            continue;
                        }

                        matches = RAID_SYSTEM.Matches(addedTag);
                        if (matches.Count > 0) {
                            string factionID = matches[0].Groups["faction"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.l.Log($"ApplySimGameEventResult RAID_SYSTEM: factionID {factionID}, systemId {systemId}");

                            FactionValue faction = Utilities.getFactionValueByFactionID(factionID);
                            StarSystem system;
                            if (systemId == "SOMEWHERE") {
                                (system, FactionValue _ignored) = WhoAndWhere.getFlareupEmployerAndLocation(WIIC.extendedContractTypes["Raid"], faction);
                            } else {
                                system = WIIC.sim.GetSystemById(systemId);
                            }

                            Utilities.cleanupSystem(system);
                            WIIC.extendedContracts[system.ID] = new Raid(system, faction, WIIC.extendedContractTypes["Raid"]);
                            Utilities.redrawMap();

                            result.AddedTags.Remove(addedTag);
                            WIIC.eventResultsCache.Add(($"[[DM.Factions[faction_{factionID}],{faction.FactionDef.CapitalizedName}]] raid{anS(faction)}", $"[[DM.SystemDefs[{systemId}],{system.Name}]]"));
                            continue;
                        }

                        matches = ATTACKER_FORCES.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            int strength = int.Parse(matches[0].Groups["strength"].Value);
                            WIIC.l.Log($"ApplySimGameEventResult ATTACKER_FORCES: systemId {systemId}, strength {strength}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);

                            if (WIIC.extendedContracts.ContainsKey(system.ID)) {
                                (WIIC.extendedContracts[system.ID] as Attack).attackerStrength = strength;
                            } else {
                                WIIC.l.LogError($"ApplySimGameEventResult: No flareup found at {systemId}");
                            }

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = DEFENDER_FORCES.Matches(addedTag);
                        if (matches.Count > 0) {
                            string systemId = matches[0].Groups["system"].Value;
                            int strength = int.Parse(matches[0].Groups["strength"].Value);
                            WIIC.l.Log($"ApplySimGameEventResult DEFENDER_FORCES: systemId {systemId}, strength {strength}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);

                            if (WIIC.extendedContracts.ContainsKey(system.ID)) {
                                (WIIC.extendedContracts[system.ID] as Attack).defenderStrength = strength;
                            } else {
                                WIIC.l.LogError($"ApplySimGameEventResult: No flareup found at {systemId}");
                            }

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = ADD_SYSTEM_TAG.Matches(addedTag);
                        if (matches.Count > 0) {
                            string tag = matches[0].Groups["tag"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.l.Log($"ApplySimGameEventResult ADD_SYSTEM_TAG: tag {tag}, systemId {systemId}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            system.Tags.Add(tag);

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = REMOVE_SYSTEM_TAG.Matches(addedTag);
                        if (matches.Count > 0) {
                            string tag = matches[0].Groups["tag"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            WIIC.l.Log($"ApplySimGameEventResult REMOVE_SYSTEM_TAG: tag {tag}, systemId {systemId}");

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            system.Tags.Remove(tag);

                            result.AddedTags.Remove(addedTag);
                            continue;
                        }

                        matches = OFFER_CONTRACT_TAG.Matches(addedTag);
                        if (matches.Count > 0) {
                            string employerID = matches[0].Groups["employer"].Value;
                            string contractName = matches[0].Groups["contractName"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            string targetID = matches[0].Groups["target"].Value;

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            FactionValue employer = Utilities.getFactionValueByFactionID(employerID);
                            FactionValue target = Utilities.getFactionValueByFactionID(targetID);

                            var diffRange = WIIC.sim.GetContractRangeDifficultyRange(system, WIIC.sim.SimGameMode, WIIC.sim.GlobalDifficulty);
                            int difficulty = WIIC.sim.NetworkRandom.Int(diffRange.MinDifficulty, diffRange.MaxDifficulty + 1);

                            ContractManager.addTravelContract(contractName, system, employer, target, difficulty);

                            result.AddedTags.Remove(addedTag);
                            WIIC.eventResultsCache.Add(($"[[DM.Factions[faction_{employerID}],{employer.FactionDef.Name}]] offer{anS(employer)} a contract at", $"[[DM.SystemDefs[{systemId}],{system.Name}]]"));
                            continue;
                        }

                        matches = OFFER_EXTENDED_CONTRACT_TAG.Matches(addedTag);
                        if (matches.Count > 0) {
                            string employerID = matches[0].Groups["employer"].Value;
                            string extendedContractType = matches[0].Groups["extendedContractType"].Value;
                            string systemId = matches[0].Groups["system"].Value;
                            string targetID = matches[0].Groups["target"].Value;

                            if (!WIIC.extendedContractTypes.ContainsKey(extendedContractType)) {
                                throw new Exception($"Unable to find extended contract type '{extendedContractType}'.");
                            }
                            if (extendedContractType == "Attack" || extendedContractType == "Raid") {
                                throw new Exception($"Use WIIC_(faction)_attacks_(system) or WIIC_(faction)_raids_(system) instead of {addedTag}. Doing nothing.");
                            }

                            StarSystem system = WIIC.sim.GetSystemById(systemId);
                            FactionValue employer = Utilities.getFactionValueByFactionID(employerID);
                            FactionValue target = Utilities.getFactionValueByFactionID(targetID);
                            ExtendedContractType type = WIIC.extendedContractTypes[extendedContractType];

                            Utilities.cleanupSystem(system);
                            WIIC.extendedContracts[system.ID] = new ExtendedContract(system, employer, target, type);
                            Utilities.redrawMap();

                            result.AddedTags.Remove(addedTag);
                            WIIC.eventResultsCache.Add(($"[[DM.Factions[faction_{employerID}],{employer.FactionDef.Name}]] offer{anS(employer)} an extended contract at", $"[[DM.SystemDefs[{systemId}],{system.Name}]]"));
                            continue;
                        }
                    } catch (Exception e) {
                        WIIC.l.LogError($"Error while processing tag '{addedTag}'");
                        WIIC.l.LogException(e);
                    }
                }
            }

            try {
                if (result.Scope == EventScope.Company && result.Stats != null) {
                    WIIC.l.Log($"ApplySimGameEventResult: Searching for WIIC entries among {result.Stats.Length} stats");
                    foreach (SimGameStat stat in result.Stats) {
                        if (stat.Validate() && stat.name.StartsWith("WIIC")) {
                            WIIC.l.Log($"ApplySimGameEventResult: Applying {stat.ToEditorString()} and removing from stats");
                            SimGameState.SetSimGameStat(stat, WIIC.sim.CompanyStats);
                        }
                    }

                    result.Stats = result.Stats.Where(stat => stat.name == null || !stat.name.StartsWith("WIIC")).ToArray();
                }
            } catch (Exception e) {
                WIIC.l.LogError($"result.ToString(): {result.ToString()}");
                WIIC.l.LogException(e);
            }
        }
    }
}
