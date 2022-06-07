using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Harmony;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    public class Utilities {
        public static Random rng = new Random();
        private static MethodInfo methodSetOwner = AccessTools.Method(typeof(StarSystemDef), "set_OwnerValue");
        private static FieldInfo _fieldSetContractEmployers = AccessTools.Field(typeof(StarSystemDef), "contractEmployerIDs");
        private static FieldInfo _fieldSetContractTargets = AccessTools.Field(typeof(StarSystemDef), "contractTargetIDs");

        public static List<string> deferredToasts = new List<string>();

        public static FactionValue getFactionValueByFactionID(string id) {
            return WIIC.sim.DataManager.Factions.FirstOrDefault(x => x.Value.FactionValue.Name == id).Value.FactionValue;
        }

        public static TKey WeightedChoice<TKey>(Dictionary<TKey, double> weights) {
            double totalWeight = 0;
            foreach (KeyValuePair<TKey, double> entry in weights) {
                totalWeight += entry.Value;
            }

            double rand = totalWeight * rng.NextDouble();
            WIIC.modLog.Debug?.Write($"WeightedChoice totalWeight: {totalWeight}, rand: {rand}");
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
            WIIC.modLog.Trace?.Write($"Flipping control of {system.Name} to {newOwner.Name}");
            List<string> tagList = system.Tags.ToList();
            WIIC.systemControl[system.ID] = $"WIIC_control_{newOwner.Name}";

            methodSetOwner.Invoke(system.Def, new object[] { newOwner });
            setActiveFactions(system);

            if (refresh) {
                system.RefreshSystem();
                system.ResetContracts();
            }

            // Refreshes the system description with appropriate defender name
            if (WIIC.flareups.ContainsKey(system.ID)) {
                WIIC.flareups[system.ID].addToMap();
            }
        }

        public static void setActiveFactions(StarSystem system) {
            if (WIIC.settings.ignoreFactions.Contains(system.OwnerValue.Name)) {
                return;
            }

            _fieldSetContractEmployers.SetValue(system.Def, WhoAndWhere.getEmployers(system));
            _fieldSetContractTargets.SetValue(system.Def, WhoAndWhere.getTargets(system));
        }

        public static FactionValue controlFromTag(string tag) {
            if (tag != null) {
                if (tag.StartsWith("WIIC_control_")) {
                    return FactionEnumeration.GetFactionByName(tag.Substring(13));
                }
            }
            return null;
        }

        public static Flareup currentExtendedContract() {
            // Usually happens from skirmish bay.
            if (WIIC.sim == null) {
                return null;
            }

            if (WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker") || WIIC.sim.CompanyTags.Contains("WIIC_helping_defender")) {
                if (WIIC.flareups.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    return WIIC.flareups[WIIC.sim.CurSystem.ID];
                }
                WIIC.modLog.Warn?.Write($"Found company tag indicating flareup participation, but no matching flareup for {WIIC.sim.CurSystem.ID}");
                WIIC.sim.CompanyTags.Remove("WIIC_helping_attacker");
                WIIC.sim.CompanyTags.Remove("WIIC_helping_defender");
            }

            if (WIIC.sim.CompanyTags.Contains("WIIC_extended_contract")) {
                if (WIIC.cxtendedContracts.ContainsKey(WIIC.sim.CurSystem.ID)) {
                    return WIIC.extendedContracts[WIIC.sim.CurSystem.ID];
                }
                WIIC.modLog.Warn?.Write($"Found company tag indicating extended contract participation, but no matching contract for {WIIC.sim.CurSystem.ID}");
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
            foreach (Flareup flareup in WIIC.flareups.Values) {
                flareup.addToMap();
            }
        }

        public static void cleanupFlareupSystem(StarSystem system) {
            if (WIIC.flareups.ContainsKey(system.ID)) {
                modLog.Debug?.Write($"Removing flareup at {system.ID}");
                flareups.Remove(system.ID);
            }

            if (system == WIIC.sim.CurSystem) {
                modLog.Debug?.Write($"Player was participating in flareup at {system.ID}; Removing company tags");
                WIIC.sim.CompanyTags.Remove("WIIC_helping_attacker");
                WIIC.sim.CompanyTags.Remove("WIIC_helping_defender");
            }

            // Revert system description to the default
            if (WIIC.fluffDescriptions.ContainsKey(system.ID)) {
                modLog.Debug?.Write($"Reverting map description for {system.ID}");
                AccessTools.Method(typeof(DescriptionDef), "set_Details").Invoke(system.Def.Description, new object[] { WIIC.fluffDescriptions[system.ID] });
            }

            redrawMap();
        }

        public static void slowDownFloaties() {
            var playPause = (SGTimePlayPause)AccessTools.Field(typeof(SGRoomController_Ship), "TimePlayPause").GetValue(WIIC.sim.RoomManager.ShipRoom);
            var floatyStack = (SGTimeFloatyStack)AccessTools.Field(typeof(SGTimePlayPause), "eventFloatyToasts").GetValue(playPause);
            floatyStack.timeBetweenFloaties = 0.5f;
        }

        public static double getReputationMultiplier(FactionValue faction) {
            // The enum for "ALLIED" is the same as "HONORED". HBS_why.
            // Apparently the player is also allied to the locals? HBS_why_9000
            if (WIIC.sim.IsFactionAlly(faction) && faction.Name != "Locals") {
                WIIC.modLog.Trace?.Write($"Allied with {faction.Name}, reputationMultiplier is {WIIC.settings.reputationMultiplier["ALLIED"]}");
                return WIIC.settings.reputationMultiplier["ALLIED"];
            }

            SimGameReputation reputation = WIIC.sim.GetReputation(faction);
            WIIC.modLog.Trace?.Write($"{faction.Name} is {reputation.ToString()}, reputationMultiplier is {WIIC.settings.reputationMultiplier[reputation.ToString()]}");
            return WIIC.settings.reputationMultiplier[reputation.ToString()];
        }
    }
}
