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
            ToggleRow(page, L.T("build.auto_paste_preview"), () => Release250.AutoPastePreview, v => Release250.AutoPastePreview = v, L.T("build.auto_paste_preview.help"));
            ToggleRow(page, L.T("build.paste_snap_ground"), () => Release250.PasteSnapToGround, v => Release250.PasteSnapToGround = v, L.T("build.paste_snap_ground.help"));
            ToggleRow(page, L.T("build.paste_anchor_snap"), () => Release250.PasteAnchorSnap, v => Release250.PasteAnchorSnap = v, L.T("build.paste_anchor_snap.help"));
            ToggleRow(page, L.T("build.apply_terrain_profile"), () => Release250.ApplyTerrainProfile, v => Release250.ApplyTerrainProfile = v, L.T("build.apply_terrain_profile.help"));
            ActionRow(page, "Preview at feet", "Preview", () => Release250.BeginPastePreview(false), L.T("build.preview_feet.help"));
            ActionRow(page, "Preview at crosshair", "Preview", () => Release250.BeginPastePreview(true), L.T("build.preview_crosshair.help"));
            ActionRow(page, "Lock / unlock preview", "Toggle", Release250.TogglePasteLock, L.T("build.paste_lock.help"));
            ActionRow(page, "Confirm paste", "Commit", Release250.ConfirmPaste, L.T("build.confirm_paste.help"));
            ActionRow(page, "Cancel preview", "Cancel", () => Release250.CancelPastePreview(), L.T("build.cancel_preview.help"));
            string a = Release250.CornerA.HasValue ? Release250.CornerA.Value.ToString("0.#") : "unset";
            string b = Release250.CornerB.HasValue ? Release250.CornerB.Value.ToString("0.#") : "unset";
            string terrainNote = Release250.ClipboardTerrain != null ? "  ·  Terrain profile" : "";
            Note(page, "A: " + a + "  ·  B: " + b + "  ·  Clipboard pieces: " + Release250.Clipboard.Count
                + (Release250.PastePreviewActive ? (Release250.PasteLocked ? "  ·  Preview LOCKED" : "  ·  Preview ON") : "")
                + (Release250.IncludeWorldStructures ? "  ·  World ON" : "  ·  World OFF")
                + terrainNote);
            Section(page, "Paste transform");
            SliderRow(page, "Paste height (m)", () => Release250.PasteHeightOffset, v => Release250.PasteHeightOffset = v, -20f, 40f, L.T("build.paste_height.help"));
            SliderRow(page, "Paste pitch (°)", () => Release250.PastePitch, v => Release250.PastePitch = v, -180f, 180f, L.T("build.paste_pitch.help"));
            SliderRow(page, "Paste yaw (°)", () => Release250.PasteYaw, v => Release250.PasteYaw = v, -180f, 180f, L.T("build.paste_yaw.help"));
            SliderRow(page, "Paste roll (°)", () => Release250.PasteRoll, v => Release250.PasteRoll = v, -180f, 180f, L.T("build.paste_roll.help"));
            SliderRow(page, "Paste nudge X (m)", () => Release250.PasteNudgeX, v => Release250.PasteNudgeX = v, -20f, 20f, L.T("build.paste_nudge_x.help"));
            SliderRow(page, "Paste nudge Z (m)", () => Release250.PasteNudgeZ, v => Release250.PasteNudgeZ = v, -20f, 20f, L.T("build.paste_nudge_z.help"));
            Section(page, "Paste leveling");
            ToggleRow(page, L.T("build.paste_level"), () => Release250.PasteLevelGround, v => Release250.PasteLevelGround = v, L.T("build.paste_level.help"));
            ToggleRow(page, L.T("build.paste_per_piece"), () => Release250.PasteLevelPerPiece, v => Release250.PasteLevelPerPiece = v, L.T("build.paste_per_piece.help"));
            SliderRow(page, L.T("build.paste_feather"), () => Release250.PasteFeather, v => Release250.PasteFeather = v, 0f, 12f, L.T("build.paste_feather.help"));

            Section(page, "Free rotation");
            ToggleRow(page, L.T("build.free_rotation"), () => FreeBuildRotation.Enabled, v => FreeBuildRotation.Enabled = v, L.T("build.free_rotation.help"));
            ToggleRow(page, L.T("build.universal_snap"), () => PlacementSnap.UniversalSnap, v => PlacementSnap.UniversalSnap = v, L.T("build.universal_snap.help"));
            ToggleRow(page, L.T("build.free_rotation_local"), () => FreeBuildRotation.LocalFrame, v => FreeBuildRotation.LocalFrame = v, L.T("build.free_rotation_local.help"));
            SliderRow(page, L.T("build.rotation_divisions"), () => (float)FreeBuildRotation.SnapDivisions, v => FreeBuildRotation.SnapDivisions = Mathf.RoundToInt(v), 2f, 256f, L.T("build.rotation_divisions.help"), whole: true);
            Note(page, L.T("build.free_rotation.note"));

            Section(page, "Placement HUD");
            ToggleRow(page, L.T("build.hud_enabled"), () => PlacementHud.Enabled, v => PlacementHud.Enabled = v, L.T("build.hud_enabled.help"));
            ToggleRow(page, L.T("build.hud_rotation"), () => PlacementHud.ShowRotation, v => PlacementHud.ShowRotation = v, null);
            ToggleRow(page, L.T("build.hud_axis"), () => PlacementHud.ShowAxisStep, v => PlacementHud.ShowAxisStep = v, null);
            ToggleRow(page, L.T("build.hud_pieces"), () => PlacementHud.ShowPieceCount, v => PlacementHud.ShowPieceCount = v, null);
            ToggleRow(page, L.T("build.hud_lock"), () => PlacementHud.ShowLockState, v => PlacementHud.ShowLockState = v, null);
            ToggleRow(page, L.T("build.hud_ground"), () => PlacementHud.ShowGroundSnap, v => PlacementHud.ShowGroundSnap = v, null);
            ToggleRow(page, L.T("build.hud_terrain"), () => PlacementHud.ShowInTerrain, v => PlacementHud.ShowInTerrain = v, null);
            ToggleRow(page, L.T("build.hud_prefab"), () => PlacementHud.ShowPrefabName, v => PlacementHud.ShowPrefabName = v, null);

            Section(page, "Drag build");
            ToggleRow(page, L.T("build.drag_enabled"), () => DragBuilding.Enabled, v => DragBuilding.Enabled = v, L.T("build.drag_enabled.help"));
            SliderRow(page, L.T("build.drag_max"), () => (float)DragBuilding.MaxPieces, v => DragBuilding.MaxPieces = Mathf.RoundToInt(v), 10f, 500f, L.T("build.drag_max.help"), whole: true);

            Section(page, "Saved blueprints");
            Note(page, L.T("build.blueprints.note"));
            Field(page, "Blueprint name", blueprintName, v => blueprintName = v, "Name used when saving or renaming.", 48);
            ActionRow(page, "Current clipboard", "Save blueprint", () => BlueprintStore.SaveFromClipboard(blueprintName),
                "Saves the current clipboard into your blueprint library.");
            SelectRow(page, "Blueprint", () => string.IsNullOrEmpty(selectedBlueprint) ? "Choose blueprint" : selectedBlueprint, BlueprintChoices);
            ActionRow(page, "Selected", "Load to clipboard", () => BlueprintStore.LoadToClipboard(selectedBlueprint),
                L.T("build.load_clipboard.help"));
            ActionRow(page, "Selected", "Paste at feet", () =>
            {
                bool prev = Release250.AutoPastePreview;
                Release250.AutoPastePreview = false;
                try
                {
                    BlueprintStore.LoadToClipboard(selectedBlueprint);
                    Release250.PasteStructure(false);
                }
                finally { Release250.AutoPastePreview = prev; }
            }, L.T("build.paste_feet.help"));
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
                L.T("build.import_path.help"), 120);
            SelectRow(page, "Export folder file", () => string.IsNullOrEmpty(blueprintImport) ? "Pick file" : blueprintImport, BlueprintImportChoices);
            ActionRow(page, "Import into library", "Import", () => BlueprintStore.ImportFile(blueprintImport),
                L.T("build.import.help"));

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
