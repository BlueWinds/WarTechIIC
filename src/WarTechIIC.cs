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
                modLog.Debug?.Write($"Loaded settings from {modDir}/settings.json. Version {typeof(Settings).Assembly.GetName().Version}");
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
                    GalaxyData data = new GalaxyData(systemControl, flareups);
                    writer.Write(JsonConvert.SerializeObject(data, Formatting.Indented));
                    writer.Flush();
                }
                modLog.Info?.Write($"Wrote {path} with {systemControl.Count} control tags");
            } catch (Exception e) {
                modLog.Error?.Write(e);
            }
        }

        internal static void readFromJson(string filename, bool deleteAfterImport) {
            try {
                string path = Path.Combine(modDir, filename);
                if (!File.Exists(path)) {
                    modLog.Info?.Write($"No {path} found, doing nothing");
                    return;
                }

                using (StreamReader reader = new StreamReader(path)) {
                    modLog.Info?.Write($"Reading GalaxyData from {path}");

                    GalaxyData data;
                    string jdata = reader.ReadToEnd();
                        // Current serialization format
                        data = JsonConvert.DeserializeObject<GalaxyData>(jdata);
                    if (data.systemControl == null) {
                        modLog.Info?.Write($"Failed to read GalaxyData, attempting to interpret as old format");

                        // Older serialization format, for backwards compatibility
                        Dictionary<string, string> control = JsonConvert.DeserializeObject<Dictionary<string, string>>(jdata);
                        data = new GalaxyData(control, new Dictionary<string, Flareup>());
                    }

                    data.apply();

                    if (deleteAfterImport) {
                        modLog.Info?.Write($"Deleting {path}");
                        File.Delete(path);
                    }
                }
            } catch (Exception e) {
                modLog.Error?.Write(e);
            }
        }
    }

    public class GalaxyData {
        public Dictionary<string, string> systemControl = new Dictionary<string, string>();
        public Dictionary<string, Flareup> flareups = new Dictionary<string, Flareup>();

        public GalaxyData(Dictionary<string, string> control, Dictionary<string, Flareup> flares) {
            systemControl = control;
            flareups = flares;
        }

        public void apply() {
            foreach (string id in systemControl.Keys) {
                StarSystem system = WIIC.sim.GetSystemById(id);
                FactionValue ownerFromTag = Utilities.controlFromTag(systemControl[id]);
                Utilities.applyOwner(system, ownerFromTag, true);
            }

            WIIC.modLog.Info?.Write($"Set control of {systemControl.Count} star systems based on GalaxyData");

            foreach (string id in flareups.Keys) {
                WIIC.flareups[id] = flareups[id];
                WIIC.flareups[id].initAfterDeserialization();
            }

            Utilities.redrawMap();

            WIIC.modLog.Info?.Write($"Created {flareups.Count} flareups based on GalaxyData");
        }
    }
}
