using System;
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
                foreach (StarSystem system in WIIC.sim.StarSystems) {
                    if (WIIC.settings.setActiveFactionsForAllSystems) {
                        Utilities.setActiveFactions(system);
                    }
                    foreach (string tag in system.Tags) {
                        // Check if a previous flareup flipped control of the system
                        FactionValue ownerFromTag = Utilities.controlFromTag(tag);
                        if (ownerFromTag != null) {
                            WIIC.modLog.Debug?.Write($"Found new owner {ownerFromTag.Name} at {system.Name}");
                            Utilities.applyOwner(system, ownerFromTag);
                        }
                    }
                    count++;
                }
                WIIC.modLog.Info?.Write($"Finished patching starmap (checked {count} systems)");

            } catch (Exception e) {
                WIIC.modLog.Error?.Write(e);
            }
        }
    }
}
