# Valheaven Mod Menu 2.1.0

BepInEx menu for Valheim 1.0.7. Canvas uGUI + TextMeshPro, using Valheim's Norse and Averia fonts.

## Install

Run `Install-Valheaven.ps1`, restart Valheim, press F8 (or whatever you saved). Change the key in Settings. Escape closes a dropdown/confirm first, then the menu.

## Pages

Character, Items, Entities, Equipment, Terrain, World, ESP, Settings.

Drag the header to move. Item and entity browsers have search, pagination, icons, and quantity/quality controls. Sliders apply size on release. Tooltips clear on exit, click, scroll, page change, close, and focus loss. Clicks and camera input stay blocked while open.

## Settings

Stored in `BepInEx/config/Valheaven.settings.json`. Keybinds, ESP, and feature prefs carry over; window position resets to center once. Save on close is on by default. "Remember character toggles" is optional.

Hotkey is ignored while typing. To recover a bad keybind, set `hotkey` to `289` (F8) in the JSON with the game closed.

## Notable features

| Page | Feature | Notes |
| --- | --- | --- |
| Character | Super jump | Jump force 1-8x |
| Character | Instant mining/chopping | One hit on ore section, tree, or log |
| Character | Unlock recipes | Discovers recipes/pieces; stations and mats still required |
| Character | Fast training | Each XP gain does one level-up, cap 100 |
| Character | Guardian Power | Equip/activate without placing the trophy |
| Equipment | Unlimited durability | Weapons, tools, armor |
| Equipment | Item editor | Quality, durability, weight, armor, block, damage types |
| World | Always daytime | Holds noon visually; world time still runs |
| World | Location teleport | Nearest boss altar, trader, or other location |

No Players tab. Teleports live under World (coords, markers, visible players, locations). Player targets use shared map position, including unloaded players. Location list is enabled templates; a template may not exist in your world. Location requests time out at 12s.

Item stat overrides sit in custom save data on that item only. Different overrides won't stack-merge while the plugin is loaded. `-1` means original; Restore drops overrides but keeps quality. Without the plugin, custom stats do nothing; quality and normal durability still apply.

World/character edits can stick. Destructive actions confirm in-menu. Uninstall does not undo them.

## Build

`Build-And-Validate.ps1` builds net472 against your game assemblies and runs the offline validator. Game assemblies are not shipped.

`MenuRuntimeCheck.cs` is compile-guarded; only runs with `VALHEAVEN_UI_CHECK` and `-valheaven-ui-check`. Not in the release DLL.
