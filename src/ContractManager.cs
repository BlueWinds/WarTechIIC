using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Harmony;
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

        private static MethodInfo _getContractRangeDifficultyRange = AccessTools.Method(typeof(SimGameState), "GetContractRangeDifficultyRange");
        private static MethodInfo _getContractOverrides = AccessTools.Method(typeof(SimGameState), "GetContractOverrides");
        private static MethodInfo _getValidParticipants = AccessTools.Method(typeof(SimGameState), "GetValidParticipants");
        private static MethodInfo _hasValidMaps = AccessTools.Method(typeof(SimGameState), "HasValidMaps");
        private static MethodInfo _hasValidContracts = AccessTools.Method(typeof(SimGameState), "HasValidContracts");
        private static MethodInfo _hasValidParticipants = AccessTools.Method(typeof(SimGameState), "HasValidParticipants");
        private static MethodInfo _clearUsedBiomeFromDiscardPile = AccessTools.Method(typeof(SimGameState), "ClearUsedBiomeFromDiscardPile");
        private static MethodInfo _filterActiveMaps = AccessTools.Method(typeof(SimGameState), "FilterActiveMaps");
        private static MethodInfo _fillMapEncounterContractData = AccessTools.Method(typeof(SimGameState), "FillMapEncounterContractData");
        private static MethodInfo _createProceduralContract = AccessTools.Method(typeof(SimGameState), "CreateProceduralContract");

        private static FieldInfo _fieldSetContractEmployers = AccessTools.Field(typeof(StarSystemDef), "contractEmployerIDs");
        private static FieldInfo _fieldSetContractTargets = AccessTools.Field(typeof(StarSystemDef), "contractTargetIDs");

        public static Contract getNewProceduralContract(StarSystem system, FactionValue employer, FactionValue target, int[] validTypes) {
            // In order to force a given employer and target, we have to temoporarily munge the system we're in, such that
            // our employer/target are the only valid ones. We undo this at the end of getNewProceduralContract.
            var oldEmployers = (List<string>)_fieldSetContractEmployers.GetValue(system.Def);
            var oldTargets = (List<string>)_fieldSetContractTargets.GetValue(system.Def);
            _fieldSetContractEmployers.SetValue(system.Def, new List<string>(){ employer.Name });
            _fieldSetContractTargets.SetValue(system.Def, new List<string>(){ target.Name });

            // In addition, we have to make sure that our target is a valid enemy for the employer - otherwise the base game's
            // `GenerateContractParticipants` will return an empty list and the contract will fail to generate.
            string[] oldEnemies = employer.FactionDef.Enemies;
            List<string> enemies = oldEnemies.ToList();
            enemies.Add(target.Name);
            Traverse.Create(employer.FactionDef).Property("Enemies").SetValue(enemies.ToArray());

            WIIC.modLog.Debug?.Write($"getNewProceduralContract: SimGameMode {WIIC.sim.SimGameMode}, GlobalDifficulty {WIIC.sim.GlobalDifficulty}");
            var difficultyRange = _getContractRangeDifficultyRange.Invoke(WIIC.sim, new object[] { system, WIIC.sim.SimGameMode, WIIC.sim.GlobalDifficulty });

            Type Diff = difficultyRange.GetType();
            int min = (int)AccessTools.Field(Diff, "MinDifficulty").GetValue(difficultyRange);
            int max = (int)AccessTools.Field(Diff, "MaxDifficulty").GetValue(difficultyRange);
            int minClamped = (int)AccessTools.Field(Diff, "MinDifficultyClamped").GetValue(difficultyRange);
            int maxClamped = (int)AccessTools.Field(Diff, "MaxDifficultyClamped").GetValue(difficultyRange);
            WIIC.modLog.Debug?.Write($"difficultyRange: MinDifficulty {min}, MaxDifficulty {max}, MinClamped {minClamped}, MaxClamped {maxClamped}");

            if (validTypes.Length == 0) {
                validTypes = contractTypes.AddRangeToArray(WIIC.settings.customContractEnums.ToArray());
            }

            var potentialContracts = (Dictionary<int, List<ContractOverride>>)_getContractOverrides.Invoke(WIIC.sim, new object[] { difficultyRange, validTypes });

            WeightedList<MapAndEncounters> playableMaps =
                MetadataDatabase.Instance.GetReleasedMapsAndEncountersBySinglePlayerProceduralContractTypeAndTags(
                    system.Def.MapRequiredTags, system.Def.MapExcludedTags, system.Def.SupportedBiomes, true)
                    .ToWeightedList(WeightedListType.SimpleRandom);

            var validParticipants = _getValidParticipants.Invoke(WIIC.sim, new object[] { system });

            if (!(bool)_hasValidMaps.Invoke(WIIC.sim, new object[] { system, playableMaps })
                || !(bool)_hasValidContracts.Invoke(WIIC.sim, new object[] { difficultyRange, potentialContracts })
                || !(bool)_hasValidParticipants.Invoke(WIIC.sim, new object[] { system, validParticipants })) {
                return null;
            }

            _clearUsedBiomeFromDiscardPile.Invoke(WIIC.sim, new object[] { playableMaps });
            IEnumerable<int> mapWeights = from map in playableMaps
                                          select map.Map.Weight;

            var activeMaps = new WeightedList<MapAndEncounters>(WeightedListType.WeightedRandom, playableMaps.ToList(), mapWeights.ToList<int>(), 0);

            _filterActiveMaps.Invoke(WIIC.sim, new object[] { activeMaps, WIIC.sim.GlobalContracts });
            activeMaps.Reset(false);
            MapAndEncounters level = activeMaps.GetNext(false);

            var MapEncounterContractData = _fillMapEncounterContractData.Invoke(WIIC.sim, new object[] { system, difficultyRange, potentialContracts, validParticipants, level });
            bool HasContracts = Traverse.Create(MapEncounterContractData).Property("HasContracts").GetValue<bool>();
            while (!HasContracts && activeMaps.ActiveListCount > 0) {
                level = activeMaps.GetNext(false);
                MapEncounterContractData = _fillMapEncounterContractData.Invoke(WIIC.sim, new object[] { system, difficultyRange, potentialContracts, validParticipants, level });
            }
            system.SetCurrentContractFactions(FactionEnumeration.GetInvalidUnsetFactionValue(), FactionEnumeration.GetInvalidUnsetFactionValue());
            HashSet<int> Contracts = Traverse.Create(MapEncounterContractData).Field("Contracts").GetValue<HashSet<int>>();

            if (MapEncounterContractData == null || Contracts.Count == 0) {
                List<string> mapDiscardPile = Traverse.Create(WIIC.sim).Field("mapDiscardPile").GetValue<List<string>>();
                if (mapDiscardPile.Count > 0) {
                    mapDiscardPile.Clear();
                } else {
                    WIIC.modLog.Error?.Write($"Unable to find any valid contracts for available map pool.");
                }
            }
            GameContext gameContext = new GameContext(WIIC.sim.Context);
            gameContext.SetObject(GameContextObjectTagEnum.TargetStarSystem, system);

            Contract contract = (Contract)_createProceduralContract.Invoke(WIIC.sim, new object[] { system, true, level, MapEncounterContractData, gameContext });

            // Restore system and faction to previous values, now that we've forced the game to generate our desired contract.
            _fieldSetContractEmployers.SetValue(system.Def, oldEmployers);
            _fieldSetContractTargets.SetValue(system.Def, oldTargets);
            Traverse.Create(employer.FactionDef).Property("Enemies").SetValue(oldEnemies);
            return contract;
        }

        public static Contract getContractByName(string contractName, StarSystem location, FactionValue employer, FactionValue target) {
            SimGameState.AddContractData addContractData = new SimGameState.AddContractData {
                ContractName = contractName,
                Employer = employer.Name,
                Target = target.Name,
                TargetSystem = location.ID
            };

            Contract contract = WIIC.sim.AddContract(addContractData);
            location.SystemContracts.Remove(contract);
            return contract;
        }

        public static Contract addTravelContract(string contractName, StarSystem location, FactionValue employer, FactionValue target, int difficulty) {
            WIIC.modLog.Info?.Write($"Adding travel contract {contractName} to {location.ID}. employer: {employer.Name}, target: {target.Name}, difficulty: {difficulty}");

            FactionValue inv = FactionEnumeration.GetInvalidUnsetFactionValue();

            ContractOverride contractOverride = WIIC.sim.DataManager.ContractOverrides.Get(contractName).Copy();
            contractOverride.travelSeed = WIIC.sim.NetworkRandom.Int(1, int.MaxValue);

            GameContext gameContext = new GameContext(WIIC.sim.Context);
            gameContext.SetObject(GameContextObjectTagEnum.TargetStarSystem, location);

            Contract contract = new Contract(null, null, null, contractOverride.ContractTypeValue, WIIC.sim.BattleTechGame, contractOverride, gameContext, fromSim: true, 0);
            WIIC.sim.PrepContract(contract, employer, employer, target, target, inv, inv, Biome.BIOMESKIN.generic, contractOverride.travelSeed, location);
            WIIC.sim.GlobalContracts.Add(contract);
            contract.SetFinalDifficulty(difficulty);

            return contract;
        }
    }
}
