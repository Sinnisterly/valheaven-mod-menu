using System;
using System.Globalization;
using UnityEngine;

namespace ValheimAdminMenu
{
    internal sealed partial class MenuView
    {
        private void FarmingPage(RectTransform page)
        {
            if (!RequireCharacter(page)) return;
            var p = app.Phase;
            Section(page, L.T("ui.cultivator"));
            ToggleRow(page,L.T("ui.plant_everything"),()=>app.Settings.Data.plantEverything,v=>{app.Settings.Data.plantEverything=v;PlantEverything.Tick();app.Settings.Save(false);},L.T("ui.plant_crops_trees_and_decorations"));
            Note(page,L.T("ui.your_plants_stay_when_turned"));
            if(app.Settings.Data.plantEverything)ActionRow(page,L.T("ui.plant_list"),"Refresh",PlantEverything.Refresh,L.T("ui.reload_the_plants_available_in"));
            Section(page, "Growth");
            ToggleRow(page, "Instant plant growth", () => p.InstantPlantGrow, v => p.InstantPlantGrow = v,
                "Crops mature almost immediately, including PlantEverything / Ashlands plants (biome and heat checks are relaxed).");
            SliderRow(page, "Production speed", () => p.ProductionSpeedMult, v => p.ProductionSpeedMult = v, 1, 25, "Also speeds cooking, fermenting, smelting, and growth on stations you own. 1 is vanilla.");
            ActionRow(page, "Nearby plants", "Force grow", Release250.GrowNearbyPlants,
                "Finishes nearby plants in the world radius, including Advize PlantEverything saplings.");
            Section(page, "Taming");
            SliderRow(page, "World / farm radius (m)", () => app.World.Radius, v => app.World.Radius = v, 1, 200, "Shared radius for grow, tame, and heal actions.");
            ActionRow(page, "Nearby creatures", "Tame", Release250.TameNearby, "Tames eligible creatures within the radius.");
            ToggleRow(page, "Auto-feed tames", () => p.AutoFeedTames, v => p.AutoFeedTames = v, "Keeps hungry tames fed in range when you own them.");
            SliderRow(page, "Auto-feed radius (m)", () => Release250.AutoFeedRadius, v => Release250.AutoFeedRadius = v, 1, 100, "Range for auto-feed and breeding helpers.");
            ActionRow(page, "Nearby tames", "Heal", Release250.HealTames, "Fully heals tamed creatures in the heal radius.");
            SliderRow(page, "Heal radius (m)", () => Release250.FarmHealRadius, v => Release250.FarmHealRadius = v, 1, 100, "Range for heal tames.");
            Section(page, "Breeding");
            ActionRow(page, "Nearby Procreation", "Make pregnant", Release250.SpeedBreeding, "Starts pregnancy on nearby breedable animals you own. Birth still follows normal timing.");
            Note(page, "Breeding only works on creatures you own. On co-op, the host usually needs to run this.");
        }

        private void BuildToolsPage(RectTransform page)
        {
            if (!RequireCharacter(page)) return;
            var p = app.Phase;
            Section(page, "Copy / paste structure");
            Note(page, "1) Set A/B corners (or Fit to location). 2) Optional Grow. 3) Copy. 4) Preview, then Confirm. Cyan box = volume. With World structures on, ruins and houses in the volume are included. Large copies can hitch.");
            ToggleRow(page, "Use feet for corners", () => Release250.CopyUseFeet, v => Release250.CopyUseFeet = v, "When on, corners use your feet instead of the look-hit. Useful for flat pads.");
            ToggleRow(page, "Include world structures", () => Release250.IncludeWorldStructures, v => Release250.IncludeWorldStructures = v,
                "When on, copy includes world ruins, houses, and arenas in the volume, including builds you do not own. Large copies can lag; paste may need host ownership.");
            ActionRow(page, "Corner A (look or feet)", "Set A", Release250.SetCornerA, "Sets the first corner. Looking at a piece snaps to that structure.");
            ActionRow(page, "Corner B (look or feet)", "Set B", Release250.SetCornerB, "Sets the second corner. Together with A this defines the copy box.");
            ActionRow(page, "Corners", "Clear", Release250.ClearCopyCorners, "Clears Corner A and Corner B.");
            ActionRow(page, "Nearby Location", "Fit A-B", Release250.FitSelectionToNearbyLocation,
                "Snaps A-B to the location under the crosshair (or nearest within 40 m) and turns on world structures. Huge areas can be thousands of pieces.");
            ActionRow(page, "Selection", "Grow to pieces", Release250.GrowCopyToOverlapping, "Expands A-B until overlapping structures in the volume are enclosed.");
            ActionRow(page, "Copy volume", "Copy", Release250.CopyStructureVolume,
                "Copies buildable pieces in the A-B box. Cap "
                + Release250.MaxClipboardPieces + ". Large copies are heavy; paste may need host ownership.");
            Section(page, "Paste preview");
            ActionRow(page, "Preview at feet", "Preview", () => Release250.BeginPastePreview(false), "Shows transparent blue ghosts at your feet. Adjust height/yaw/nudge, then Confirm.");
            ActionRow(page, "Preview at crosshair", "Preview", () => Release250.BeginPastePreview(true), "Shows transparent blue ghosts at the look-hit. Adjust, then Confirm.");
            SliderRow(page, "Paste height (m)", () => Release250.PasteHeightOffset, v => Release250.PasteHeightOffset = v, -20f, 40f, "Vertical offset for the active paste preview.");
            SliderRow(page, "Paste yaw (°)", () => Release250.PasteYaw, v => Release250.PasteYaw = v, -180f, 180f, "Yaw rotation of the paste preview.");
            SliderRow(page, "Paste nudge X (m)", () => Release250.PasteNudgeX, v => Release250.PasteNudgeX = v, -20f, 20f, "Horizontal nudge left/right in preview space.");
            SliderRow(page, "Paste nudge Z (m)", () => Release250.PasteNudgeZ, v => Release250.PasteNudgeZ = v, -20f, 20f, "Horizontal nudge forward/back in preview space.");
            ToggleRow(page, L.T("build.paste_level"), () => Release250.PasteLevelGround, v => Release250.PasteLevelGround = v, L.T("build.paste_level.help"));
            ToggleRow(page, L.T("build.paste_per_piece"), () => Release250.PasteLevelPerPiece, v => Release250.PasteLevelPerPiece = v, L.T("build.paste_per_piece.help"));
            SliderRow(page, L.T("build.paste_feather"), () => Release250.PasteFeather, v => Release250.PasteFeather = v, 0f, 12f, L.T("build.paste_feather.help"));
            ActionRow(page, "Confirm paste", "Commit", Release250.ConfirmPaste, "Places the clipboard at the preview position.");
            ActionRow(page, "Cancel preview", "Cancel", () => Release250.CancelPastePreview(), "Clears the paste ghost without placing.");
            string a = Release250.CornerA.HasValue ? Release250.CornerA.Value.ToString("0.#") : "unset";
            string b = Release250.CornerB.HasValue ? Release250.CornerB.Value.ToString("0.#") : "unset";
            Note(page, "A: " + a + "  ·  B: " + b + "  ·  Clipboard pieces: " + Release250.Clipboard.Count
                + (Release250.PastePreviewActive ? "  ·  Preview ON" : "")
                + (Release250.IncludeWorldStructures ? "  ·  World ON" : "  ·  World OFF"));

            Section(page, "Saved blueprints");
            Note(page, "Saved across worlds and characters. Export a .vbp file to share a full build (pieces included).");
            Field(page, "Blueprint name", blueprintName, v => blueprintName = v, "Name used when saving or renaming.", 48);
            ActionRow(page, "Current clipboard", "Save blueprint", () => BlueprintStore.SaveFromClipboard(blueprintName),
                "Saves the current clipboard into your blueprint library.");
            SelectRow(page, "Blueprint", () => string.IsNullOrEmpty(selectedBlueprint) ? "Choose blueprint" : selectedBlueprint, BlueprintChoices);
            ActionRow(page, "Selected", "Load to clipboard", () => BlueprintStore.LoadToClipboard(selectedBlueprint),
                "Loads the blueprint into the clipboard. Then use Paste preview or Confirm.");
            ActionRow(page, "Selected", "Paste at feet", () =>
            {
                BlueprintStore.LoadToClipboard(selectedBlueprint);
                Release250.PasteStructure(false);
            }, "Loads the blueprint and pastes at your feet (no preview).");
            ActionRow(page, "Selected", "Rename", () =>
            {
                BlueprintStore.Rename(selectedBlueprint, blueprintName);
                selectedBlueprint = Rules.Text(blueprintName, 64).Trim();
            }, "Renames the selected blueprint to the name field above.");
            DangerRow(page, "Selected blueprint", "Delete", "Removes this blueprint from your library.", () =>
            {
                BlueprintStore.Delete(selectedBlueprint);
                selectedBlueprint = "";
            });
            ActionRow(page, "Selected", "Export .vbp", () => BlueprintStore.Export(selectedBlueprint),
                "Writes a shareable blueprints/<name>.vbp file with all pieces.");
            Field(page, "Import file / path", blueprintImport, v => blueprintImport = v,
                "File name under blueprints/, or a full path to a .vbp.", 120);
            SelectRow(page, "Export folder file", () => string.IsNullOrEmpty(blueprintImport) ? "Pick .vbp" : blueprintImport, BlueprintImportChoices);
            ActionRow(page, "Import into library", "Import", () => BlueprintStore.ImportFile(blueprintImport),
                "Adds the file to your library. Same names overwrite.");

            Section(page, "Piece scale");
            SliderRow(page, "Scale", () => Release250.PieceScale, v => Release250.PieceScale = v, .25f, 4f, "Target scale for the look-at piece (0.25-4x).");
            ActionRow(page, "Look-at piece", "Apply scale", () => Release250.ScaleLookPiece(Release250.PieceScale), "Scales the piece under the crosshair (you must own it).");
            Section(page, "Hammer");
            ToggleRow(page, L.T("build.hammer_all_prefabs"), () => app.Settings.Data.hammerAllPrefabs, v =>
            {
                app.Settings.Data.hammerAllPrefabs = v;
                CreativeHammer.Tick();
                app.Settings.Save(false);
            }, L.T("build.hammer_all_prefabs.help"));
            if (app.Settings.Data.hammerAllPrefabs)
            {
                ActionRow(page, L.T("build.hammer_catalog"), "Refresh", CreativeHammer.Refresh, L.T("build.hammer_catalog.help"));
                ToggleRow(page, L.T("build.hammer_creature_ai"), () => app.Settings.Data.hammerCreatureFullAi, v =>
                {
                    app.Settings.Data.hammerCreatureFullAi = v;
                    app.Settings.Save(false);
                }, L.T("build.hammer_creature_ai.help"));
            }
            Note(page, app.Settings.Data.hammerAllPrefabs
                ? L.Tf("build.hammer_all_prefabs.note", CreativeHammer.RecipeCount)
                : L.T("build.hammer_all_prefabs.off"));
            ToggleRow(page, L.T("build.level_on_place"), () => Release250.LevelOnPlace, v => Release250.LevelOnPlace = v, L.T("build.level_on_place.help"));
            SliderRow(page, L.T("build.paste_feather"), () => Release250.PasteFeather, v => Release250.PasteFeather = v, 0f, 12f, L.T("build.level_on_place.feather"));
            Section(page, "Repair hammer AoE");
            ToggleRow(page, "Repair while hammering", () => p.RepairHammerAoe, v => p.RepairHammerAoe = v, "While holding a hammer, periodically repairs nearby structures.");
            SliderRow(page, "Hammer repair radius (m)", () => Release250.RepairHammerRadius, v => Release250.RepairHammerRadius = v, 1, 40, "AoE repair range.");
            ActionRow(page, "Repair once now", "Repair", () => Release250.RepairAoe(app.RequirePlayer(), Release250.RepairHammerRadius, false), "One-shot repair in hammer radius.");
        }
    }
}
