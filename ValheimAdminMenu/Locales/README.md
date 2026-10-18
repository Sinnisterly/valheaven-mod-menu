# Valheaven menu locales

JSON packs for the in-game menu. `en.json` is the source of truth.

| File | Language |
|------|----------|
| `en.json` | English |
| `ru.json` | Russian |
| `de.json` | German |
| `fr.json` | French |
| `es.json` | Spanish |
| `pt-BR.json` | Portuguese (Brazil) |
| `zh-CN.json` | Chinese (Simplified) |
| `zh-TW.json` | Chinese (Traditional) |
| `ja.json` | Japanese |

## Edit a pack

1. Open the language file in a text editor.
2. Leave keys alone; only change the quoted values.
3. Keep `{0}` / `{1}` placeholders in the same order as English.
4. Keys starting with `_` are metadata.

## Test without rebuilding

1. Menu > Settings > Locales > Export locale files
2. Edit the JSON under `locales/` next to `valheim.exe` (or `%AppData%\Valheaven\locales\`)
3. Reload locales in the menu (or restart)

Missing keys fall back to English.

## New language

Copy `en.json` to `xx.json`, set `_language` / `_name`, translate values, drop into `locales/`. Maintainers still need to wire the code into the Language list.
