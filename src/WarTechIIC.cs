using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Harmony;
using IRBTModUtils.Logging;
using BattleTech;
using HBS.Collections;

namespace WarTechIIC
{
    public class WIIC
    {
        internal static DeferringLogger modLog;
        internal static Settings settings;
        internal static string modDir;
        public static Dictionary<string, Flareup> flareups = new Dictionary<string, Flareup>();
        public static Dictionary<string, string> fluffDescriptions = new Dictionary<string, string>();
        public static SimGameState sim;

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

            catch (Exception ex) {
                settings = new Settings();
                modLog = new DeferringLogger(modDir, "WarTechIIC", "WIIC", true, true);
                modLog.Error?.Write(ex);
            }

            WhoAndWhere.init();

            var harmony = HarmonyInstance.Create("blue.winds.WarTechIIC");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }
}
