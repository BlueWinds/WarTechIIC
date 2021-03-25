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
        static void Postfix(Starmap __instance) {
            try {
                WIIC.modLog.Info?.Write($"Patching starmap with new owners (setActiveFactionsForAllSystems: {WIIC.settings.setActiveFactionsForAllSystems})");
                int count = 0;
                int controlCount = 0;
                foreach (StarSystem system in WIIC.sim.StarSystems) {
                    if (WIIC.settings.setActiveFactionsForAllSystems) {
                        Utilities.setActiveFactions(system);
                    }

                    // Check if a previous flareup flipped control of the system
                    if (WIIC.systemControl.ContainsKey(system.ID)) {
                        FactionValue ownerFromTag = Utilities.controlFromTag(WIIC.systemControl[system.ID]);
                        WIIC.modLog.Info?.Write($"Found new owner {ownerFromTag.Name} at {system.Name}");

                        Utilities.applyOwner(system, ownerFromTag, false);
                        controlCount++;
                    }
                    count++;
                }
                Utilities.redrawMap();
                WIIC.modLog.Info?.Write($"Finished patching starmap (checked {count} systems, flipped control of {controlCount})");

            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
