using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using BattleTech;
using BattleTech.Save;
using BattleTech.Save.Test;

namespace WarTechIIC {
    [HarmonyPatch(typeof(Starmap), "PopulateMap", new Type[] { typeof(SimGameState) })]
    public static class Starmap_PopulateMap_Patch {
        public static List<(StarSystem, FactionValue)> deferredOwnershipChanges = new List<(StarSystem, FactionValue)>();

        static void Postfix(Starmap __instance) {
            try {
                WIIC.l.Log($"Patching starmap with new owners (setActiveFactionsForAllSystems: {WIIC.settings.setActiveFactionsForAllSystems})");
                int count = 0;
                int controlCount = 0;
                int clearCount = 0;

                foreach (StarSystem system in WIIC.sim.StarSystems) {
                    if (system.Tags.ContainsAny(WhoAndWhere.clearEmployersTags, false)) {
                        clearCount++;
                        Utilities.setActiveFactions(system);
                    } else if (WIIC.settings.setActiveFactionsForAllSystems) {
                        Utilities.setActiveFactions(system);
                    }

                    // Check if a previous flareup flipped control of the system
                    if (WIIC.systemControl.ContainsKey(system.ID)) {
                        FactionValue ownerFromTag = Utilities.controlFromTag(WIIC.systemControl[system.ID]);
                        Utilities.applyOwner(system, ownerFromTag, false);
                        controlCount++;
                    }
                    count++;
                }

                // In post-contract events, we might add company tags (and thus attempt to flip system control) while the starmap doesn't exist;
                // in this case, SimGameState_ApplySimGameEventResult_Patch will push changes into this array
                // so that we can process them once the player arrives back in the simgame.
                foreach ((StarSystem system, FactionValue newOwner) in deferredOwnershipChanges) {
                    Utilities.applyOwner(system, newOwner, true);
                }
                deferredOwnershipChanges.Clear();

                Utilities.redrawMap();
                WIIC.l.Log($"Finished patching starmap (checked {count} systems, flipped control of {controlCount}, cleared targets and employers for {clearCount})");

            } catch (Exception e) {
                WIIC.l.LogException(e);
            }
        }
    }
}
