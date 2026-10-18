# Valheaven

Drop-in Valheim mod menu. You put one file (`version.dll`) next to `valheim.exe` and you're done. It doesn't need BepInEx; if you already use BepInEx for other mods, leave that alone.

- **Version:** 2.5.11
- **Game:** Valheim 1.0.7+ (checked through 1.0.20)
- **Open menu:** F8 (rebindable in Settings)

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

New features go on `release/valheaven-full-credit`. Forum branch gets mirrored when a forum package is cut.

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

Feature code lands on full-credit first; forum-credit is for the credit string / package mirror.

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
