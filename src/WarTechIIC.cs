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
        internal static CampaignSettings campaignSettings;
        internal static Dictionary<string, ActiveCampaign> activeCampaigns = new Dictionary<string, ActiveCampaign>();

        public static void Init(string modDirectory, string settingsJSON) {
            modDir = modDirectory;

            try {
                using (StreamReader reader = new StreamReader($"{modDir}/settings.json")) {
                    string jdata = reader.ReadToEnd();
                    settings = JsonConvert.DeserializeObject<Settings>(jdata);
                }
                using (StreamReader reader = new StreamReader($"{modDir}/campaignSettings.json")) {
                    string jdata = reader.ReadToEnd();
                    campaignSettings = JsonConvert.DeserializeObject<CampaignSettings>(jdata);
                }
                l = Logger.GetLogger("WartechIIC");
                l.Log($"Loaded settings from {modDir}/settings.json and campaignSettings.json");
            }

            catch (Exception e) {
                settings = new Settings();
                l = Logger.GetLogger("WartechIIC");
                l.LogException(e);
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
                HashSet<string> involvedSystems = new HashSet<string>();
                foreach (VersionManifestEntry entry in customResources["Campaign"].Values) {
                    WIIC.l.Log($"Loading Campaign from {entry.FilePath}.");
                    try {
                        string yaml;
                        using (StreamReader reader = new StreamReader(entry.FilePath)) {
                            yaml = reader.ReadToEnd();
                        }

                        Campaign campaign = deserializer.Deserialize<Campaign>(yaml);

                        HashSet<string> campaignSystems = campaign.validate(involvedSystems);
                        involvedSystems.UnionWith(campaignSystems);

                        campaigns[campaign.name] = campaign;
                    } catch (Exception e) {
                        WIIC.l.LogException(e);
                    }
                }
            }

            WhoAndWhere.init();
        }
    }
}
