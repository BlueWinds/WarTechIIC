using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    public class Utilities {
        public static Random rng = new Random();
        public static List<string> deferredToasts = new List<string>();

        public static FactionValue getFactionValueByFactionID(string id) {
            try {
                return WIIC.sim.DataManager.Factions.FirstOrDefault(x => x.Value.FactionValue.Name == id).Value.FactionValue;
            } catch (Exception e) {
                WIIC.l.LogError($"Error getting faction: {id}");
                throw e;
            }
        }

        public static TKey WeightedChoice<TKey>(Dictionary<TKey, double> weights) {
            double totalWeight = 0;
            foreach (KeyValuePair<TKey, double> entry in weights) {
                totalWeight += entry.Value;
            }

            if (totalWeight == 0) {
                throw new InvalidOperationException("No entries with weight > 0 in weights.");
            }

            double rand = totalWeight * rng.NextDouble();
            WIIC.l.Log($"WeightedChoice totalWeight: {totalWeight}, rand: {rand}");
            foreach (KeyValuePair<TKey, double> entry in weights) {
                rand -= entry.Value;
                if (rand <= 0) {
                    return entry.Key;
                }
            }

            throw new NullReferenceException("Wha...? No entry selected in WeightedChoice.");
        }

        public static TItem Choice<TItem>(List<TItem> items) {
            int rand = rng.Next(items.Count);
            return items[rand];
        }

        public static bool isControlTag(string tag) {
          return tag.StartsWith("WIIC_control_");
        }

        public static void applyOwner(StarSystem system, FactionValue newOwner, bool refresh) {
            WIIC.l.Log($"Flipping control of {system.Name} to {newOwner.Name}");

            WhoAndWhere.clearLocationCache();

            List<string> tagList = system.Tags.ToList();
            WIIC.systemControl[system.ID] = $"WIIC_control_{newOwner.Name}";

            system.Def.OwnerValue = newOwner;
            setActiveFactions(system);

            if (refresh) {
                system.RefreshSystem();
                system.ResetContracts();
            }

            // Refreshes the system description with appropriate defender name
            if (WIIC.extendedContracts.ContainsKey(system.ID)) {
                WIIC.extendedContracts[system.ID].addToMap();
            }
        }

        public static void setActiveFactions(StarSystem system) {
            if (WIIC.settings.ignoreFactions.Contains(system.OwnerValue.Name)) {
                return;
            }

            system.Def.contractEmployerIDs = WhoAndWhere.getEmployers(system);
            system.Def.contractTargetIDs = WhoAndWhere.getTargets(system);
        }

        public static FactionValue controlFromTag(string tag) {
            if (tag != null) {
                if (tag.StartsWith("WIIC_control_")) {
                    return FactionEnumeration.GetFactionByName(tag.Substring(13));
                }
            }
            return null;
        }

        public static ExtendedContract currentExtendedContract() {
            // Usually happens from skirmish bay.
            if (WIIC.sim == null) {
                return null;
            }

            if (WIIC.sim.CompanyTags.Contains("WIIC_extended_contract")) {
                if (WIIC.extendedContracts.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    return WIIC.extendedContracts[WIIC.sim.CurSystem.ID];
                }
                WIIC.l.LogError($"Found company tag indicating extended contract participation, but no matching contract for {WIIC.sim.CurSystem.ID}");
                WIIC.sim.CompanyTags.Remove("WIIC_extended_contract");
            }

            return null;
        }

        public static string forcesToString(int forces) {
            return $"<color=#debc02>{forces}</color>";
        }

        public static double statOrDefault(string stat, double defaultValue) {
            if (WIIC.sim.CompanyStats.ContainsStatistic(stat) && WIIC.sim.CompanyStats.GetValue<float>(stat) >= 0) {
              return (double) WIIC.sim.CompanyStats.GetValue<float>(stat);
            }

            return defaultValue;
        }

        public static bool flashpointInSystem(StarSystem system) {
            return WIIC.sim.AvailableFlashpoints.Find(f => f.CurSystem == system) != null;
        }

        public static void redrawMap() {
            ColourfulFlashPoints.Main.clearMapMarkers();
            foreach (ExtendedContract extendedContract in WIIC.extendedContracts.Values) {
                extendedContract.addToMap();
            }

            foreach (ActiveCampaign ac in WIIC.activeCampaigns.Values) {
                ac.addToMap();
            }
        }

        public static void cleanupSystem(StarSystem system) {
            if (WIIC.extendedContracts.ContainsKey(system.ID)) {
                WIIC.l.Log($"Removing ExtendedContract at {system.ID}");
                WIIC.extendedContracts.Remove(system.ID);
            }

            if (system == WIIC.sim.CurSystem) {
                WIIC.l.Log($"Player was participating in flareup at {system.ID}; Removing company tags");
                WIIC.sim.CompanyTags.Remove("WIIC_extended_contract");
            }

            // Revert system description to the default
            if (WIIC.fluffDescriptions.ContainsKey(system.ID)) {
                WIIC.l.Log($"Reverting map description for {system.ID}");
                system.Def.Description.Details = WIIC.fluffDescriptions[system.ID];
            }

            redrawMap();
        }

        public static void slowDownFloaties() {
            SGTimeFloatyStack floatyStack = WIIC.sim.RoomManager.ShipRoom.TimePlayPause.eventFloatyToasts;
            floatyStack.timeBetweenFloaties = 0.6f;
        }

        public static double getReputationMultiplier(FactionValue faction) {
            // The enum for "ALLIED" is the same as "HONORED". HBS_why.
            // Apparently the player is also allied to the locals? HBS_why_9000
            if (WIIC.sim.IsFactionAlly(faction) && faction.Name != "Locals") {
                return WIIC.settings.reputationMultiplier["ALLIED"];
            }

            SimGameReputation reputation = WIIC.sim.GetReputation(faction);
            return WIIC.settings.reputationMultiplier[reputation.ToString()];
        }

        public static double getAggression(FactionValue faction) {
             double aggression = WIIC.settings.aggression.ContainsKey(faction.Name) ? WIIC.settings.aggression[faction.Name] : 1;
             return statOrDefault($"WIIC_{faction.Name}_aggression", aggression);
        }

        public static double getHatred(FactionValue faction, FactionValue target) {
            double hate = WIIC.settings.hatred.ContainsKey(faction.Name) && WIIC.settings.hatred[faction.Name].ContainsKey(target.Name) ? WIIC.settings.hatred[faction.Name][target.Name] : 1;
            return statOrDefault($"WIIC_{faction.Name}_hates_{target.Name}", hate);
        }

        public static List<FactionValue> getAllies() {
            return WIIC.sim.AlliedFactions.Select(f => FactionEnumeration.GetFactionByName(f)).ToList();
        }

        public static void giveReward(string itemCollection) {
            if (itemCollection != null) {
                try {
                    SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
                    queue.QueueRewardsPopup(itemCollection);
                } catch (Exception e) {
                    WIIC.l.LogException(e);
                }
            }
        }

        public static FactionDef getEmployer(Contract c) {
            return c.GetTeamFaction("ecc8d4f2-74b4-465d-adf6-84445e5dfc230").FactionDef;
        }

        public static bool shouldBlockContract(Contract contract) {
            string employer = getEmployer(contract).factionID;
            if (WIIC.settings.neverBlockContractsOfferedBy.Contains(employer) && contract.TargetSystem == WIIC.sim.CurSystem.ID) {
                return true;
            }

            ExtendedContract extendedContract = currentExtendedContract();
            if (extendedContract?.extendedType.blockOtherContracts == true) {
                return true;
            }

            WIIC.activeCampaigns.TryGetValue(WIIC.sim.CurSystem.ID, out ActiveCampaign ac);
            string blockExcept = ac?.currentEntry.contract?.forced == null ? null : ac.currentEntry.contract.id;

            if (blockExcept != null && blockExcept != contract.Name) {
                return true;
            }

            return false;
        }
    }
}
