using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Harmony;
using BattleTech;
using BattleTech.UI;
using Localize;
using ColourfulFlashPoints;
using ColourfulFlashPoints.Data;

namespace WarTechIIC {
    [JsonObject(MemberSerialization.OptIn)]
    public class Flareup {
        public SimGameState sim;

        public StarSystem location;
        [JsonProperty]
        public string locationID;

        public FactionValue attacker;
        [JsonProperty]
        public string attackerName;

        [JsonProperty]
        public int countdown;
        [JsonProperty]
        public int daysUntilMission;
        [JsonProperty]
        public int attackerStrength;
        [JsonProperty]
        public int defenderStrength;
        [JsonProperty]
        public string currentContractName = "";
        [JsonProperty]
        public int currentContractForceLoss = 0;

        public bool droppingForContract = false;

        public Flareup() {
            // Empty constructor used for deserialization.
        }

        public Flareup(StarSystem flareupLocation, FactionValue attackerFaction, SimGameState __instance) {
            Settings s = WIIC.settings;

            sim = __instance;
            location = flareupLocation;
            locationID = flareupLocation.ID;
            attacker = attackerFaction;
            attackerName = attackerFaction.Name;
            countdown = Utilities.rng.Next(s.minCountdown, s.maxCountdown);

            int v;
            attackerStrength = s.attackStrength.TryGetValue(attacker.Name, out v) ? v : s.defaultAttackStrength;
            defenderStrength = s.defenseStrength.TryGetValue(location.OwnerValue.Name, out v) ? v : s.defaultDefenseStrength;

            attackerStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);
            defenderStrength += Utilities.rng.Next(-s.strengthVariation, s.strengthVariation);
        }

        public FactionValue employer {
            get {
                if (WIIC.sim.CurSystem != location) {
                    return null;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker")) {
                    return attacker;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_defender")) {
                    return location.OwnerValue;
                }
                return null;
            }
        }

        public FactionValue target {
            get {
                if (WIIC.sim.CurSystem != location) {
                    return null;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_attacker")) {
                    return location.OwnerValue;
                }
                if (WIIC.sim.CompanyTags.Contains("WIIC_helping_defender")) {
                    return attacker;
                }
                return null;
            }
        }

        public bool passDay() {
            Settings s = WIIC.settings;
            if (countdown > 0) {
                countdown--;
                return false;
            }

            if (daysUntilMission > 1) {
                daysUntilMission--;
                return false;
            }

            double rand = Utilities.rng.NextDouble();
            if (rand > 0.5) {
                attackerStrength -= Utilities.rng.Next(s.combatForceLossMin, s.combatForceLossMax);
            } else {
                defenderStrength -= Utilities.rng.Next(s.combatForceLossMin, s.combatForceLossMax);
            }
            WIIC.modLog.Debug?.Write($"Flareup progressed at {location.Name}. attackerStrength: {attackerStrength}, defenderStrength: {defenderStrength}");

            daysUntilMission = s.daysBetweenMissions;

            if (attackerStrength <= 0 || defenderStrength <= 0) {
              conclude();
              return true;
            }

            if (employer != null) {
                launchMission();
            }
            return false;
        }

        public void conclude() {
            WIIC.modLog.Info?.Write($"Flareup finished at {location.Name}.");

            removeParticipationContracts();
            if (attackerStrength <= 0) {
                string text = Strings.T("Battle for {0} concludes - {1} holds off the {2} attack", location.Name, location.OwnerValue.FactionDef.ShortName, attacker.FactionDef.ShortName);
                sim.RoomManager.ShipRoom.AddEventToast(new Text(text));
            } else if (defenderStrength <= 0) {
                string text = Strings.T("Battle for {0} concludes - {1} takes the system from {2}", location.Name, attacker.FactionDef.ShortName, location.OwnerValue.FactionDef.ShortName);
                sim.RoomManager.ShipRoom.AddEventToast(new Text(text));

                Utilities.applyOwner(location, attacker);
            }
        }

        public void addToMap() {
            MapMarker mapMarker = new MapMarker(location.ID, WIIC.settings.flareupMarker);
            ColourfulFlashPoints.Main.addMapMarker(mapMarker);

            if (!WIIC.fluffDescriptions.ContainsKey(location.ID)) {
                WIIC.modLog.Debug?.Write($"Filled fluff description entry for {location.ID}: {location.Def.Description.Details}");
                WIIC.fluffDescriptions[location.ID] = location.Def.Description.Details;
            }

            string description = getDescription() + "\n" + WIIC.fluffDescriptions[location.ID];
            AccessTools.Method(typeof(DescriptionDef), "set_Details").Invoke(location.Def.Description, new object[] { description });
        }

        public string getDescription() {
            var description = new StringBuilder();
            description.AppendLine(Strings.T("<b><color=#de0202>{0} is under attack by {1}</color></b>", location.Name, attacker.FactionDef.ShortName));
            if (countdown > 0) {
               description.AppendLine(Strings.T("{0} days until the fighting starts", countdown));
            }
            if (daysUntilMission > 0) {
               description.AppendLine(Strings.T("{0} days until the next mission", daysUntilMission));
            }
            description.AppendLine("\n" + Strings.T("{0} forces: {1}", attacker.FactionDef.Name, Utilities.forcesToString(attackerStrength)));
            description.AppendLine(Strings.T("{0} forces: {1}", location.OwnerValue.FactionDef.Name, Utilities.forcesToString(defenderStrength)));

            return description.ToString();
        }

        public void spawnParticipationContracts() {
            Enum.TryParse(WIIC.settings.minReputationToHelp, out SimGameReputation minRep);
            int diff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);
            if (!WIIC.settings.wontHirePlayer.Contains(attacker.Name) && sim.GetReputation(attacker) >= minRep) {
                WIIC.modLog.Info?.Write($"Adding contract wiic_help_attacker. Target={location.OwnerValue.Name}, Employer={attacker.Name}, TargetSystem={location.ID}, Difficulty={location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
                Contract attackContract = sim.AddContract(new SimGameState.AddContractData {
                    ContractName = "wiic_help_attacker",
                    Target = location.OwnerValue.Name,
                    Employer = attacker.Name,
                    TargetSystem = location.ID,
                    Difficulty = diff
                });
                attackContract.SetFinalDifficulty(diff);
            }

            if (!WIIC.settings.wontHirePlayer.Contains(location.OwnerValue.Name) && sim.GetReputation(location.OwnerValue) >= minRep) {
                WIIC.modLog.Info?.Write($"Adding contract wiic_help_defender. Target={attacker.Name}, Employer={location.OwnerValue.Name}, TargetSystem={location.ID}, Difficulty={location.Def.GetDifficulty(SimGameState.SimGameType.CAREER)}");
                Contract defendContract = sim.AddContract(new SimGameState.AddContractData {
                    ContractName = "wiic_help_defender",
                    Target = attacker.Name,
                    Employer = location.OwnerValue.Name,
                    TargetSystem = location.ID,
                    Difficulty = diff
                });
                defendContract.SetFinalDifficulty(diff);
            }
        }

        public void removeParticipationContracts() {
            if (location == WIIC.sim.CurSystem) {
                WIIC.modLog.Debug?.Write($"Cleaning up participation contracts for {location.Name}.");
                WIIC.sim.GlobalContracts.RemoveAll(c => (c.Override.ID == "wiic_help_attacker" || c.Override.ID == "wiic_help_defender"));
            }
        }

        public void launchMission() {
            Contract contract = ContractManager.getNewProceduralContract(location, employer, target);

            string title = Strings.T("Flareup Mission");
            string primaryButtonText = Strings.T("Launch mission");
            string cancel = Strings.T("Pass");
            string message = $"{employer.FactionDef.Name} has a mission for us, Commander: {contract.Name}. Details will be provided en-route, but it seems to be a {contract.ContractTypeValue.FriendlyName.ToLower()} mission. Sounds urgent.";
            WIIC.modLog.Debug?.Write(message);

//             WIIC.sim.SetTimeMoving(false);
            SimGameInterruptManager queue = WIIC.sim.GetInterruptQueue();
            queue.QueuePauseNotification(title, message, WIIC.sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, delegate {
                try {
                    WIIC.modLog.Info?.Write($"Accepted flareup mission {contract.Name}.");
                    currentContractName = contract.Name;
                    currentContractForceLoss = Utilities.rng.Next(WIIC.settings.combatForceLossMin, WIIC.settings.combatForceLossMax);

                    WIIC.sim.RoomManager.ForceShipRoomChangeOfRoom(DropshipLocation.CMD_CENTER);
                    WIIC.sim.ForceTakeContract(contract, false);
                } catch (Exception e) {
                    WIIC.modLog.Error?.Write(e);
                }
            }, primaryButtonText, delegate {
                  WIIC.modLog.Info?.Write($"Passed on flareup mission.");
            }, cancel);

            if (!queue.IsOpen) {
                queue.DisplayIfAvailable();
            }
        }

        private WorkOrderEntry_Notification _workOrder;
        public WorkOrderEntry_Notification workOrder {
          get {
            if (_workOrder == null) {
              string title = Strings.T("Flareup contract");
              _workOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "nextflareupContract", title);
            }

            _workOrder.SetCost(daysUntilMission);
            return _workOrder;
          }
        }

        public string Serialize() {
            string json = JsonConvert.SerializeObject(this);
            return $"WIIC:{json}";
        }

        public static bool isSerializedFlareup(string tag) {
            return tag.StartsWith("WIIC:");
        }

        public static Flareup Deserialize(string tag, SimGameState __instance) {
            Flareup newFlareup = JsonConvert.DeserializeObject<Flareup>(tag.Substring(5));

            newFlareup.sim = __instance;
            newFlareup.location = __instance.GetSystemById(newFlareup.locationID);
            newFlareup.attacker = FactionEnumeration.GetFactionByName(newFlareup.attackerName);

            return newFlareup;
        }
    }
}
