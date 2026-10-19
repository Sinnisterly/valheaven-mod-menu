# Valheaven

Drop-in Valheim mod menu. You put one file (`version.dll`) next to `valheim.exe` and you're done. It doesn't need BepInEx; if you already use BepInEx for other mods, leave that alone.

- **Version:** 3.0.6
- **Game:** Valheim 1.0.7+ (checked through 1.0.20)
- **Open menu:** F8 (rebindable in Settings)

## Support me on Ko-Fi if you want!
- https://ko-fi.com/loathed
- Any donation is appreciated, but none are needed; I was asked to add this by users, that is all.

### Changelog (3.0.0+)
- **3.0.6:** Hammer all prefabs can now place dungeons and world locations. Dungeons (crypts, caves, infested mines, Hildir's dungeons, ...) build a new random layout with loot and enemies up in the sky, with a named entrance portal where you place it and an exit inside; camps and villages build in place. Locations (troll caves, burial chambers, frost caves, altars, ...) place whole, with their interiors and doors. Removing either with the hammer also removes what it spawned. Build bar: categories that don't fit get << / >> page tabs (or mouse wheel, Q/E). Fixed: drag building, free rotation, snap, placement HUD, paste and bag/stack settings resetting on restart; placed spawners vanishing; some items jumping back to Repair; placed objects that couldn't be removed after a reload; perfect-block window doing nothing (and an error every frame with auto-parry). Logging: Valheaven.log now records a session header, menu actions, toggles and hotkeys, and errors that used to be silent; Settings → Diagnostics adds a Debug logging toggle, Write to log and Open log folder.
- **3.0.5:** Fixed the real cause of the "falling/sliding mode" and "can't place anything (hammer/hoe/cultivator)" bugs: Hammer all prefabs left a broken creature inside build ghosts, which stayed in the game's character list until restart and stopped AI and player movement from updating. Creature ghosts are now fully stripped, and a guard cleans up any leftover. Drag build: the click always places through vanilla, holding extends the line, and line pieces respect wards and material costs. Walls and pillars can now be dragged up or down as well as sideways. Hammer all prefabs: night and raid creatures (e.g. Ghost) no longer despawn right after placing, props with no visible mesh show a real preview or a marker cube, and placements that vanish or show nothing are named in Valheaven.log. Fall damage works again. DX11: extra bootstrap path when the menu never started, menu-visibility details in Valheaven.log, and a hint when Exclusive Fullscreen is active.
- **3.0.4:** God mode / flight keep working when other mods overwrite vanilla debug flags (re-assert + local fly fallback only if needed). Settings → Interface → Section layout: Dropdowns (default) or Full list.
- **3.0.3:** Craft-from-chests no longer hitch when opening the crafting menu (cache nearby chest stacks; stop ClaimOwnership spam on every recipe check).
- **3.0.2:** Hardened related 3.0 features - free-rotation / universal snap / drag only on build hammer (not hoe/cultivator); paste/drag/freecam-build/mount float state clears on portal/death; mount AI gravity restored on detach.
- **3.0.1:** Drag-build no longer blocks hammer/hoe/cultivator placement (off by default; click places, hold to drag walls/floors/beams/roofs). Freecam-build / portal leftover no-gravity “falling slide” cleared.
- **DX11 menu not showing (Vulkan OK):** stop auto-closing on focus loss (Exclusive Fullscreen blips when unlocking the cursor). Bootstrap failures now log + retry; menu canvas/show path is more resilient. Ready line logs GFX + fullscreen mode.

---

## What you get

A full overlay menu: character cheats and QoL, building / crafting helpers, item spawner + editor, farming, world tools (weather, terrain, structure copy/paste + blueprints), teleport bookmarks, camera stuff, ESP, events, and hotkey binds.

Settings, bookmarks, blueprints, and a log land next to the game folder. If that folder isn't writable, they go under `%AppData%\Valheaven\` instead.

---

## Install

1. Build `version.dll` (below) or grab a built one.
2. Copy it next to `valheim.exe`, or:

```powershell
.\Install-Valheaven.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Valheim"
```

3. Launch Valheim and hit **F8**.

Don't touch `winhttp.dll` / Doorstop for this mod.

To uninstall, delete `version.dll`. Optional leftovers: `Valheaven.settings.json`, `Valheaven.bookmarks.json`, `Valheaven.blueprints.json`, `Valheaven.log`, and a `blueprints\` folder.

---

## Branches

| Branch | What's on it |
|--------|----------------|
| `release/valheaven-full-credit` | Default. Normal credits. |
| `release/forum-credit` | Same code; in-menu credit text is just `Loathecsgo`. |
| `original-source` | Old 2.1.0 snapshot. Don't base new work here. |

New features go on `release/valheaven-full-credit`.

---

## Localization

Strings are JSON packs in `ValheimAdminMenu/Locales/`. **`en.json` is the source of truth.**

Shipped: English, Russian, German, French, Spanish, Portuguese (Brazil), Chinese Simplified/Traditional, Japanese.

See [`ValheimAdminMenu/Locales/README.md`](ValheimAdminMenu/Locales/README.md) for the short how-to.

Quick rules:

- Change values, not keys
- Keep `{0}` / `{1}` in the same order as English
- Keys starting with `_` are metadata

**Try a translation without rebuilding:** Settings > Locales > Export, edit the files under `locales/` beside the game (or `%AppData%\Valheaven\locales\`), then Reload locales.

**New language:** copy `en.json` to `xx.json`, fill it in, add it to the `EmbeddedResource` list in the `.csproj`, and register the code in `LocaleCodes.cs`. Translation-only PRs are great; keep them small (one language is ideal).

---

## Build

You'll need Windows x64, a .NET SDK (net472 build + net8 for validation), a Valheim install for references, and CMake + VS 2022 C++ (MSVC x64) for the native DLL.

Projects default to:

`D:\SteamLibrary\steamapps\common\Valheim`

Pass `-p:GamePath="..."` / `-GamePath` if yours is elsewhere.

**1. Managed menu**

```powershell
dotnet build ValheimAdminMenu/ValheimAdminMenu.csproj -c Release -p:GamePath="D:\SteamLibrary\steamapps\common\Valheim"
```

That drops `ValheavenModMenu.dll` + `0Harmony.dll` under `ValheimAdminMenu/bin/Release/net472/payload/`. Those get baked into `version.dll` next (ILRepack can't merge this Unity assembly cleanly).

**2. Native host**

```powershell
.\Native\VersionProxy\Build-VersionDll.ps1 -RepoRoot .
```

Produces `version.dll` at the repo root. More notes in [`Native/VersionProxy/README.md`](Native/VersionProxy/README.md).

**3. Optional checks**

```powershell
.\Build-And-Validate.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Valheim"
```

**4. Install**

```powershell
.\Install-Valheaven.ps1 -GamePath "D:\SteamLibrary\steamapps\common\Valheim"
```

---

## Contributing

Fork (or branch from `release/valheaven-full-credit`), keep the PR focused, and don't commit build junk (`bin/`, `obj/`, `*.dll`, `*.pdb`, `version.dll`, zips).

Before you open a PR:

- Release build works against your Valheim path
- `Build-And-Validate.ps1` is nice to have green
- UI copy: update `en.json` first; say what's still English elsewhere
- Mention what you actually clicked in-game (Valheim version + tabs)

Feature code lands on full-credit first; `Mirror-ForumCredit.ps1` resets forum-credit to that tip and rewrites locale credit strings to `Loathecsgo`.

```
ValheimAdminMenu/     C# menu + patches
  Locales/            JSON string packs
Native/VersionProxy/  version.dll host
Validation/           offline checks
```

---

## Credits

Loathe (@Sinnisterly). Forum builds credit `Loathecsgo` only.

Valheim is © Iron Gate AB / Coffee Stain. Unofficial fan project; not affiliated.
