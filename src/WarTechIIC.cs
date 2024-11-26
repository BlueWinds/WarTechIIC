using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Harmony;
using IRBTModUtils.Logging;
using BattleTech;

namespace WarTechIIC {
    public class WIIC {
        public static Dictionary<string, ExtendedContractType> extendedContractTypes = new Dictionary<string, ExtendedContractType>();

        internal static DeferringLogger modLog;
        internal static string modDir;
        internal static Settings settings;
        internal static Dictionary<string, ExtendedContract> extendedContracts = new Dictionary<string, ExtendedContract>();
        internal static Dictionary<string, string> systemControl = new Dictionary<string, string>();
        internal static Dictionary<string, string> fluffDescriptions = new Dictionary<string, string>();
        internal static List<(string, string)> eventResultsCache = new List<(string, string)>();
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

        public static void FinishedLoading(Dictionary<string, Dictionary<string, VersionManifestEntry>> customResources) {
            if (customResources != null && customResources.ContainsKey("ExtendedContractType")) {
                foreach (VersionManifestEntry entry in customResources["ExtendedContractType"].Values) {
                    WIIC.modLog.Info?.Write($"Loading ExtendedContractType from {entry.FilePath}.");
                    try {
                        string jdata;
                        using (StreamReader reader = new StreamReader(entry.FilePath)) {
                            jdata = reader.ReadToEnd();
                        }
                        ExtendedContractType ect = JsonConvert.DeserializeObject<ExtendedContractType>(jdata);
                        ect.validate();
                        extendedContractTypes[ect.name] = ect;
                    } catch (Exception e) {
                        WIIC.modLog.Error?.Write(e);
                    }
                }
            }

            WhoAndWhere.init();
        }
    }
}
