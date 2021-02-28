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
        public static FactionValue employer;
        public static FactionValue target;

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

        public static Contract getNewProceduralContract(StarSystem system, FactionValue emp, FactionValue tar) {
            employer = emp;
            target = tar;
            var difficultyRange = _getContractRangeDifficultyRange.Invoke(WIIC.sim, new object[] { system, WIIC.sim.SimGameMode, WIIC.sim.GlobalDifficulty });

            var validTypes = contractTypes.AddRangeToArray(WIIC.settings.customContractEnums.ToArray());
//            var validTypes = WIIC.settings.customContractEnums.ToArray();

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

            return contract;
        }
    }

    [HarmonyPatch(typeof(SimGameState), "PrepContract")]
    public static class SimGameState_PrepContract_Patch {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Contract contract, ref FactionValue employer, ref FactionValue employersAlly, ref FactionValue target, ref FactionValue targetsAlly, ref FactionValue NeutralToAll, ref FactionValue HostileToAll) {
            WIIC.modLog.Debug?.Write($"Prepping contract. employer: {ContractManager.employer}, arg: {employer}. name: {contract.Name}");
            WIIC.modLog.Debug?.Write($"CurSystem: {WIIC.sim.CurSystem.ID}, CurMaxContracts: {WIIC.sim.CurSystem.CurMaxContracts}, SystemContracts: {WIIC.sim.CurSystem.SystemContracts.Count}");
            try {
                if (ContractManager.employer != null) {
                    employer = ContractManager.employer;
                    employersAlly = ContractManager.employer;
                    target = ContractManager.target;
                    targetsAlly = ContractManager.target;
                }
            }
            catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
