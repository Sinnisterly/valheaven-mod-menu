# Valheaven version.dll proxy

Native x64 shell:

1. Forwards Version APIs to `System32\version.dll`
2. Waits for `mono-2.0-bdwgc.dll`
3. Loads RCDATA 102 (`0Harmony`) and 101 (`ValheavenModMenu`) via Mono
4. Calls `Valheaven.Entrypoint.Start`

Users only drop `version.dll`. ILRepack can't merge this Unity assembly (Cecil NRE), so both managed images stay as RCDATA in one PE.

## Build

1. `dotnet build ValheimAdminMenu/ValheimAdminMenu.csproj -c Release`
2. `.\Native\VersionProxy\Build-VersionDll.ps1` (needs CMake + MSVC x64)

Doesn't touch `winhttp.dll` / Doorstop / BepInEx.

`menu.bin` / `harmony.bin` stubs get overwritten by `Embed-Payload.ps1` from the Release payload folder.
