using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Data;
using BattleTech.Framework;
using Newtonsoft.Json;

namespace WarTechIIC {
    public class ContractManager {
        private static readonly int[] contractTypes = new int[]
        {
            (int)ContractType.AmbushConvoy, (int)ContractType.Assassinate, (int)ContractType.CaptureBase,
            (int)ContractType.CaptureEscort, (int)ContractType.DefendBase, (int)ContractType.DestroyBase, (int)ContractType.Rescue,
            (int)ContractType.SimpleBattle, (int)ContractType.FireMission, (int)ContractType.AttackDefend,
            (int)ContractType.ThreeWayBattle
        };

        public static Contract getNewProceduralContract(StarSystem system, FactionValue employer, FactionValue target, int[] validTypes) {
            // In order to force a given employer and target, we have to temoporarily munge the system we're in, such that
            // our employer/target are the only valid ones. We undo this at the end of getNewProceduralContract.
            var oldEmployers = system.Def.contractEmployerIDs;
            var oldTargets = system.Def.contractTargetIDs;
            system.Def.contractEmployerIDs = new List<string>(){ employer.Name };
            system.Def.contractTargetIDs = new List<string>(){ target.Name };

            // In addition, we have to make sure that our target is a valid enemy for the employer - otherwise the base game's
            // `GenerateContractParticipants` will return an empty list and the contract will fail to generate.
            var oldEnemies = employer.FactionDef.Enemies;
            List<string> enemies = oldEnemies.ToList();
            enemies.Add(target.Name);
            employer.FactionDef.Enemies = enemies.ToArray();

            WIIC.l.Log($"getNewProceduralContract: SimGameMode {WIIC.sim.SimGameMode}, GlobalDifficulty {WIIC.sim.GlobalDifficulty}");
            var difficultyRange = WIIC.sim.GetContractRangeDifficultyRange(system, WIIC.sim.SimGameMode, WIIC.sim.GlobalDifficulty);

            WIIC.l.Log($"difficultyRange: MinDifficulty {difficultyRange.MinDifficulty}, MaxDifficulty {difficultyRange.MaxDifficulty}, MinClamped {difficultyRange.MinDifficultyClamped}, MaxClamped {difficultyRange.MaxDifficultyClamped}");

            if (validTypes.Length == 0) {
                validTypes = WIIC.settings.customContractEnums.Concat(contractTypes).ToArray();
            }

            system.SetCurrentContractFactions(employer, target);
            var potentialContracts = (Dictionary<int, List<ContractOverride>>) WIIC.sim.GetContractOverrides(difficultyRange, validTypes);

            WeightedList<MapAndEncounters> playableMaps =
                MetadataDatabase.Instance.GetReleasedMapsAndEncountersBySinglePlayerProceduralContractTypeAndTags(
                    system.Def.MapRequiredTags, system.Def.MapExcludedTags, system.Def.SupportedBiomes, true)
                    .ToWeightedList(WeightedListType.SimpleRandom);

            var validParticipants = WIIC.sim.GetValidParticipants(system);

            if (!WIIC.sim.HasValidMaps(system, playableMaps)
                || !WIIC.sim.HasValidContracts(difficultyRange, potentialContracts)
                || !WIIC.sim.HasValidParticipants(system, validParticipants)
            ) {
                return null;
            }

            WIIC.sim.ClearUsedBiomeFromDiscardPile(playableMaps);
            IEnumerable<int> mapWeights = from map in playableMaps
                                          select map.Map.Weight;

            var activeMaps = new WeightedList<MapAndEncounters>(WeightedListType.WeightedRandom, playableMaps.ToList(), mapWeights.ToList<int>(), 0);

            WIIC.sim.FilterActiveMaps(activeMaps, WIIC.sim.GlobalContracts);
            activeMaps.Reset(false);
            MapAndEncounters level = activeMaps.GetNext(false);

            var MapEncounterContractData = WIIC.sim.FillMapEncounterContractData(system, difficultyRange, potentialContracts, validParticipants, level);
            while (!MapEncounterContractData.HasContracts && activeMaps.ActiveListCount > 0) {
                level = activeMaps.GetNext(false);
                MapEncounterContractData = WIIC.sim.FillMapEncounterContractData(system, difficultyRange, potentialContracts, validParticipants, level);
            }

            if (MapEncounterContractData == null || MapEncounterContractData.Contracts.Count == 0) {
                if (WIIC.sim.mapDiscardPile.Count > 0) {
                    WIIC.sim.mapDiscardPile.Clear();
                } else {
                    WIIC.l.LogError($"Unable to find any valid contracts for available map pool.");
                }
            }

            GameContext gameContext = new GameContext(WIIC.sim.Context);
            gameContext.SetObject(GameContextObjectTagEnum.TargetStarSystem, system);

            Contract contract = WIIC.sim.CreateProceduralContract(system, true, level, MapEncounterContractData, gameContext);

            // Restore system and faction to previous values, now that we've forced the game to generate our desired contract.
            system.Def.contractEmployerIDs = oldEmployers;
            system.Def.contractTargetIDs = oldTargets;
            employer.FactionDef.Enemies = oldEnemies;
            return contract;
        }

        public static Contract getContractByName(string contractName, StarSystem location, FactionValue employer, FactionValue target) {
            FactionValue hostile = chooseHostileToAll(employer, target);

            SimGameState.AddContractData addContractData = new SimGameState.AddContractData {
                ContractName = contractName,
                Employer = employer.Name,
                Target = target.Name,
                HostileToAll = hostile.Name,
                TargetSystem = location.ID,
                IsGlobal =  location.ID != WIIC.sim.CurSystem.ID,
            };

            location.SetCurrentContractFactions(employer, target);
            Contract contract = WIIC.sim.AddContract(addContractData);
            location.SystemContracts.Remove(contract);
            return contract;
        }

        public static Contract addTravelContract(string contractName, StarSystem location, FactionValue employer, FactionValue target) {
            WIIC.l.Log($"Adding travel contract {contractName} to {location.ID}. employer: {employer.Name}, target: {target.Name}");

            FactionValue inv = FactionEnumeration.GetInvalidUnsetFactionValue();

            ContractOverride contractOverride = WIIC.sim.DataManager.ContractOverrides.Get(contractName).Copy();
            contractOverride.travelSeed = WIIC.sim.NetworkRandom.Int(1, int.MaxValue);

            GameContext gameContext = new GameContext(WIIC.sim.Context);
            gameContext.SetObject(GameContextObjectTagEnum.TargetStarSystem, location);

            Contract contract = new Contract(null, null, null, contractOverride.ContractTypeValue, WIIC.sim.BattleTechGame, contractOverride, gameContext, fromSim: true, 0);
            WIIC.sim.PrepContract(contract, employer, employer, target, target, inv, inv, Biome.BIOMESKIN.generic, contractOverride.travelSeed, location);
            WIIC.sim.GlobalContracts.Add(contract);

            int finalDiff = contract.Override?.finalDifficulty ?? contract.Override?.difficulty ?? contract.Difficulty;
            if (finalDiff == 1000) {
                finalDiff = location.Def.GetDifficulty(SimGameState.SimGameType.CAREER);
                WIIC.l.Log($"    Contract difficulty was magic value 1000, overriding it with system difficulty {finalDiff}");
            } else {
                WIIC.l.Log($"    Contract difficulty is {finalDiff}");
            }
            contract.SetFinalDifficulty(finalDiff);

            return contract;
        }

        public static FactionValue chooseHostileToAll(FactionValue employer, FactionValue target) {
            List<FactionValue> hostile = FactionEnumeration.PossibleHostileToAllList.Where(f => !employer.Equals(f) && !target.Equals(f)).ToList();
            return Utilities.Choice(hostile);
        }
    }
}
