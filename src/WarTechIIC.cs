using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Harmony;
using BattleTech;
using YamlDotNet.Serialization;
using HBS.Logging;

namespace WarTechIIC {
    public class WIIC {
        public static Dictionary<string, ExtendedContractType> extendedContractTypes = new Dictionary<string, ExtendedContractType>();
        internal static Dictionary<string, ExtendedContract> extendedContracts = new Dictionary<string, ExtendedContract>();

        internal static ILog l;
        internal static string modDir;
        internal static Settings settings;
        internal static Dictionary<string, string> systemControl = new Dictionary<string, string>();
        internal static Dictionary<string, string> fluffDescriptions = new Dictionary<string, string>();
        internal static List<(string, string)> eventResultsCache = new List<(string, string)>();

        internal static SimGameState sim;
        internal static JsonSerializerSettings serializerSettings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        public static Dictionary<string, Campaign> campaigns = new Dictionary<string, Campaign>();
        public static CampaignSettings campaignSettings;
        public static List<ActiveCampaign> activeCampaigns = new List<ActiveCampaign>();

        internal static List<string> validationErrors = new List<string>();

        public static void Init(string modDirectory, string settingsJSON) {
            modDir = modDirectory;
            l = Logger.GetLogger("WartechIIC");

            try {
                using (StreamReader reader = new StreamReader($"{modDir}/settings.json")) {
                    string jdata = reader.ReadToEnd();
                    settings = JsonConvert.DeserializeObject<Settings>(jdata);
                }
                using (StreamReader reader = new StreamReader($"{modDir}/campaignSettings.json")) {
                    string jdata = reader.ReadToEnd();
                    campaignSettings = JsonConvert.DeserializeObject<CampaignSettings>(jdata);
                }
                l.Log($"Loaded settings from {modDir}/settings.json and campaignSettings.json");
            }

            catch (Exception e) {
                l.LogException(e);
                settings = new Settings();
            }

            HarmonyInstance harmony = HarmonyInstance.Create("blue.winds.WarTechIIC");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public static void FinishedLoading(Dictionary<string, Dictionary<string, VersionManifestEntry>> customResources) {
            if (customResources != null && customResources.ContainsKey("ExtendedContractType")) {
                foreach (VersionManifestEntry entry in customResources["ExtendedContractType"].Values) {
                    l.Log($"Loading ExtendedContractType from {entry.FilePath}.");
                    try {
                        string jdata;
                        using (StreamReader reader = new StreamReader(entry.FilePath)) {
                            jdata = reader.ReadToEnd();
                        }
                        ExtendedContractType ect = JsonConvert.DeserializeObject<ExtendedContractType>(jdata);
                        ect.validate();
                        extendedContractTypes[ect.name] = ect;
                    } catch (Exception e) {
                        l.LogException(e);
                    }
                }
            }

            IDeserializer deserializer = new DeserializerBuilder().Build();

            if (customResources != null && customResources.ContainsKey("Campaign")) {
                foreach (VersionManifestEntry entry in customResources["Campaign"].Values) {
                    WIIC.l.Log($"Loading Campaign from {entry.FilePath}.");
                    try {
                        string yaml;
                        using (StreamReader reader = new StreamReader(entry.FilePath)) {
                            yaml = reader.ReadToEnd();
                        }

                        Campaign campaign = deserializer.Deserialize<Campaign>(yaml);
                        campaign.validate(entry.FilePath);
                        campaigns[campaign.name] = campaign;
                    } catch (Exception e) {
                        WIIC.l.LogException(e);
                        WIIC.validationErrors.Add($"{entry.FilePath} failed to load.");
                    }
                }
            }

            WhoAndWhere.init();
        }
    }
}
