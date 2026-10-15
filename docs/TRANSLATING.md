# Translating Bloodborne Checkpoint Manager

The application starts in English. Use **Language** in the top right to switch immediately; the choice is saved independently in `ui-settings.json` beside your existing `settings.json`. Switching does not change paths, checkpoints, search, selection or an unfinished name edit. The language selector is disabled during an operation.

## Files

Every release includes `Languages/en.json` and `Languages/es.json` next to the application. English is the reference; Spanish is a complete translation. The source copies live in `src/Checkpoint.Core/Languages/`. Version 0.8.1 has 232 keys covering navigation, buttons, progress, confirmations, recovery, external connection status and manager errors. Product branding stays **Bloodborne Checkpoint Manager** in both files.

English is also embedded in the Core assembly as a fallback. A missing key uses English. A malformed file, unsupported schema, wrong code or invalid placeholders causes that file to be rejected and English to be used. Invalid files are not offered in the selector. No translation file contains executable code.

## Add a language without recompiling

1. Copy `en.json` to a new file such as `pt-BR.json` in `Languages/`.
2. Keep `schemaVersion: 1`. Set `language` to the exact filename code without `.json`, and `name` to the language's own display name, such as `Português (Brasil)`. Codes may contain letters, digits and hyphens, between 2 and 35 characters.
3. Translate the values inside `strings`. Keep the keys unchanged. Partial translations are supported, but untranslated keys will appear in English.
4. Preserve every positional placeholder (`{0}`, `{1}`, etc.). You may reorder them, but must not remove one or introduce another. Preserve format suffixes such as `{1:yyyy-MM-dd HH:mm:ss}` and `{0:N1}` unless you intentionally choose another valid .NET format. Escape literal braces as `{{` and `}}`.
5. Keep JSON valid and save as UTF-8. Represent line breaks as `\n`. In the file filters, retain `|`, `*.exe`, `*.bat` and `*.cmd`.
6. Restart the manager to discover the new file, then select it in **Language**. Editing an existing file also requires reselecting its language or restarting.

Example of a partial pack:

```json
{
  "schemaVersion": 1,
  "language": "pt-BR",
  "name": "Português (Brasil)",
  "strings": {
    "nav.settings": "Configurações",
    "library.restore": "Restaurar selecionado"
  }
}
```

Use either shipped file as the starting point for a complete translation. A regional code controls number formatting; date formats are explicit in their keys. Unknown cultures use invariant formatting. This release has a left-to-right layout; right-to-left layout support needs a separate UI adaptation.

## Scope

Checkpoint names and paths are user data and are never translated or renamed. Existing copies remain compatible. Windows file/folder pickers and system-generated diagnostics follow Windows' own language. Known bridge errors are translated; unrecognized external diagnostics remain verbatim. The documentation is separate from the runtime catalogs.

## Validation for 0.4.0

45 automated tests passed, including complete English/Spanish keys, matching placeholders, third-party language discovery, English fallback, malformed catalogs and path traversal rejection. UI tests confirmed that a language switch retains search, selection and a name edit. All three pages rendered in both languages at 1120×820 and 980×740. The synthetic Windows launch/close check also passed. Live gameplay was not repeated for this visual/language update; the storage and bridge regression suite passed, and the existing prepared port does not require a rebuild.
