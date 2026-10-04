# Desktop localization

The current UI supports English (`en`) and Russian (`ru`). English is the repository's primary language. The original Russian user guide is maintained as README.ru.md.

## Selection and persistence

LocalizationService starts in `system` mode unless settings contain a valid override. On Linux, nonempty LC_ALL, LC_MESSAGES or LANGUAGE (first colon-separated entry) is considered before CurrentUICulture; otherwise CurrentUICulture supplies the language. A Russian language code, including ru-RU and ru_RU.UTF-8, selects Russian. Other languages and C/POSIX fall back to English. An explicit `en` or `ru` preference overrides detection.

Settings normally live under the user's local application data directory, gmmt/settings.json. GMMT_SETTINGS can override the path for portable use or isolated tests. Settings contain `Language` with `system`, `en` or `ru`; JSON is written to a temporary sibling and replaced atomically. Corrupt/unreadable settings fall back to system mode. A failed save retains the old preference and reports an error.

The header selector applies translations in place. TextBox contents, selected tab, runner/game selection, checkbox state and expanded sections are preserved. Labels, placeholders, button captions, status, file-picker titles and stored result renderers update without rebuilding the window. Language changes are disabled during a user-triggered operation to keep its status consistent. Native OS dialog controls and third-party diagnostics follow platform behavior; CLI and manifest schemas remain in English.

## Catalogs

`src/Gmmt.Desktop/Localization/en.json` and `ru.json` are embedded resources with identical semantic keys. They contain only presentation strings; paths, runner IDs and game titles are never translated. Formatting uses numeric placeholders (`{0}`, `{1}`, etc.) with matching parameter sets. Technical symbols such as ELF/GMS/BC and executable names stay unchanged.

Add translations to both catalogs, then bind controls through MainWindow's Label/Note/Disclosure/LocalizedTab helpers or Bind callbacks. Use SetLog/SetStatus for result text that must update on a later language change. Do not translate paths or replace exception text by guessing substring matches. The runtime's original low-level diagnostics are retained.

## Validation

The runtime console test project links the actual LocalizationService source and embeds the same catalogs. Tests cover locale detection/fallback, explicit overrides, saved settings, system re-detection, malformed settings, failed writes without state loss, catalog completeness and placeholder parity. Run the normal solution build and tests, then inspect real English/Russian desktop windows and a live switch with an isolated GMMT_SETTINGS file. Check both wide and minimum-size layouts.
