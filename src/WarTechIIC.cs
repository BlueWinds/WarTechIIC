using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Harmony;
using IRBTModUtils.Logging;
using BattleTech;
using BattleTech.UI;
using HBS.Collections;
using UnityEngine;

namespace WarTechIIC
{
    public class WIIC {
        internal static DeferringLogger modLog;
        internal static string modDir;
        internal static Settings settings;
        internal static Dictionary<string, Flareup> flareups = new Dictionary<string, Flareup>();
        internal static Dictionary<string, string> systemControl = new Dictionary<string, string>();
        internal static Dictionary<string, string> fluffDescriptions = new Dictionary<string, string>();
        internal static SimGameState sim;

        public static void Init(string modDirectory, string settingsJSON) {
            modDir = modDirectory;

            try {
                using (StreamReader reader = new StreamReader($"{modDir}/settings.json")) {
                    string jdata = reader.ReadToEnd();
                    settings = JsonConvert.DeserializeObject<Settings>(jdata);
                }
                modLog = new DeferringLogger(modDirectory, "WarTechIIC", "WIIC", settings.debug, settings.trace);
                modLog.Debug?.Write($"Loaded settings from {modDir}/settings.json");
            }

            catch (Exception e) {
                settings = new Settings();
                modLog = new DeferringLogger(modDir, "WarTechIIC", "WIIC", true, true);
                modLog.Error?.Write(e);
            }

            var harmony = HarmonyInstance.Create("blue.winds.WarTechIIC");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public static void FinishedLoading() {
            WhoAndWhere.init();
        }

        public static void cleanupSystem(StarSystem system) {
            if (flareups.ContainsKey(system.ID)) {
                modLog.Debug?.Write($"Removing flareup at {system.ID}");
                flareups.Remove(system.ID);
            }

            if (system == sim.CurSystem) {
                modLog.Debug?.Write($"Player was participating in flareup at {system.ID}; Removing company tags");
                sim.CompanyTags.Remove("WIIC_helping_attacker");
                sim.CompanyTags.Remove("WIIC_helping_defender");
            }

            // Revert system description to the default
            if (fluffDescriptions.ContainsKey(system.ID)) {
                modLog.Debug?.Write($"Reverting map description for {system.ID}");
                AccessTools.Method(typeof(DescriptionDef), "set_Details").Invoke(system.Def.Description, new object[] { fluffDescriptions[system.ID] });
            }

            Utilities.redrawMap();
        }

        internal static void serializeToJson() {
            try {
                string path = Path.Combine(modDir, settings.saveFolder, "WIIC_systemControl.json");

                using (StreamWriter writer = new StreamWriter(path, false)) {
                    writer.Write(JsonConvert.SerializeObject(systemControl, Formatting.Indented));
                    writer.Flush();
                }
                modLog.Info?.Write($"Wrote {path} with {systemControl.Count} control tags");
            } catch (Exception e) {
                modLog.Error?.Write(e);
            }
        }

        internal static void readFromJson() {
            try {
                string path = Path.Combine(modDir, "WIIC_systemControl.json");
                if (!File.Exists(path)) {
                    modLog.Info?.Write($"No {path} found, doing nothing.");
                    return;
                }

                using (StreamReader reader = new StreamReader(path)) {
                    string jdata = reader.ReadToEnd();
                    Dictionary<string, string> systemControl = JsonConvert.DeserializeObject<Dictionary<string, string>>(jdata);
                    foreach (string id in systemControl.Keys) {
                        StarSystem system = sim.GetSystemById(id);
                        FactionValue ownerFromTag = Utilities.controlFromTag(systemControl[id]);
                        modLog.Info?.Write($"id: {id}, system: {system}, tag: {systemControl[id]}, owner: {ownerFromTag}");
                        Utilities.applyOwner(system, ownerFromTag, true);
                    }
                    modLog.Info?.Write($"Set control of {systemControl.Count} star systems based on {path}");
                }
            } catch (Exception e) {
                modLog.Error?.Write(e);
            }
        }
    }
}
