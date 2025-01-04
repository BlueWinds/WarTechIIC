using System;
using System.Linq;
using Harmony;
using BattleTech;
using BattleTech.UI;

namespace WarTechIIC {
    [HarmonyPatch(typeof(MainMenu), "OnAddedToHierarchy")]
    public static class MainMenu_OnAddedToHierarchy_Patch {
        public static void Postfix(MainMenu __instance) {
            if (WIIC.validationErrors.Count > 0) {
                WIIC.l.LogError("Validation Errors:");
                foreach (string e in WIIC.validationErrors) {
                    WIIC.l.LogError("    " + e);
                }

                string firstFile = WIIC.validationErrors.Select(e => e.Split('.')[0]).First();
                string message = $"Failed to load {firstFile}. See log for details.";
                GenericPopupBuilder.Create("WIIC Campaign Error", message)
                    .AddButton("Exit", UnityGameInstance.Instance.ShutdownGame, false)
                    .Render();
            }
        }
    }
}
