using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Harmony;
using BattleTech;

namespace WarTechIIC {
    public class Utilities {
        public static Random rng = new Random();
        private static MethodInfo methodSetOwner = AccessTools.Method(typeof(StarSystemDef), "set_OwnerValue");
        private static FieldInfo fieldSetContractEmployers = AccessTools.Field(typeof(StarSystemDef), "contractEmployerIDs");
        private static FieldInfo fieldSetContractTargets = AccessTools.Field(typeof(StarSystemDef), "contractTargetIDs");

        public static TKey WeightedChoice<TKey>(Dictionary<TKey, double> weights) {
            double totalWeight = 0;
            foreach (KeyValuePair<TKey, double> entry in weights) {
                totalWeight += entry.Value;
            }

            double rand = totalWeight * rng.NextDouble();
            WIIC.modLog.Trace?.Write($"WeightedChoice totalWeight: {totalWeight}, rand: {rand}");
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

        public static void applyOwner(StarSystem system, FactionValue newOwner) {
            WIIC.modLog.Trace?.Write($"Flipping control of ${system.Name} to ${newOwner.Name}");
            List<string> tagList = system.Tags.ToList();
            system.Tags.Remove(tagList.Find(t => t.StartsWith("planet_faction_")));
            system.Tags.Remove(tagList.Find(isControlTag));
            system.Tags.Add($"planet_faction_${newOwner.Name.ToLower()}");
            system.Tags.Add($"WIIC_control_${newOwner.Name}");

            methodSetOwner.Invoke(system.Def, new object[] { newOwner });
            setActiveFactions(system);
        }

        public static void setActiveFactions(StarSystem system) {
            if (WIIC.settings.ignoreFactions.Contains(system.OwnerValue.Name)) {
                return;
            }

            fieldSetContractEmployers.SetValue(system.Def, WhoAndWhere.getEmployers(system) );
            fieldSetContractTargets.SetValue(system.Def, WhoAndWhere.getTargets(system));
        }

        public static FactionValue controlFromTag(string tag) {
            if (tag != null) {
                WIIC.modLog.Trace?.Write($"controlFromTag tag: {tag}, substr: {tag.Substring(13)}");
                if (tag.StartsWith("WIIC_control_")) {
                    WIIC.modLog.Trace?.Write($"Faction: {FactionEnumeration.GetFactionByName(tag.Substring(13))}");
                    return FactionEnumeration.GetFactionByName(tag.Substring(13));
                }
            }
            return null;
        }

        public static Flareup currentFlareup() {
            // Usually happens from skirmish bay.
            if (WIIC.sim == null) {
                return null;
            }

            if (!WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker") && !WIIC.sim.CompanyTags.Contains("WIIC_helping_defender")) {
                return null;
            }

            if (!WIIC.flareups.ContainsKey(WIIC.sim.CurSystem.ID)) {
                WIIC.modLog.Warn?.Write($"Found company tag indicating flareup participation, but no matching flareup for {WIIC.sim.CurSystem.ID}");
                WIIC.sim.CompanyTags.Remove("WIIC_helping_attacker");
                WIIC.sim.CompanyTags.Remove("WIIC_helping_defender");
                return null;
            }

            return WIIC.flareups[WIIC.sim.CurSystem.ID];
        }

        public static string forcesToDots(int forces) {
            return $"<color=#debc02>{new String('O', forces / 10 + 1)}</color>";
        }
    }
}
