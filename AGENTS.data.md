# AGENTS.data.md

## Scope
- Covers persisted data layout, configuration files, cache directories, JSON write patterns, protected secrets, and model or schema changes that affect storage or imports and exports.
- Covers `AppDataPaths`, `FileHelper`, `DpapiProtector`, app settings, manual translation data layout, and shared DTO synchronization.
- Does not own UI workflows or packaging logic except where they interact with persisted data.

## Continue Reading When
- Editing `UnityLocalizationToolkit-WebUI/Infrastructure/AppDataPaths.cs`, `FileHelper.cs`, `DpapiProtector.cs`, or data-bearing models.
- Changing `settings.json`, `library.json`, app-data folder layout, import or export behavior, or any cache or per-game JSON file.
- Adding a new stored field that must stay aligned across C#, TypeScript, persistence, and UI.

## Current Structure
- App-data root owner: `UnityLocalizationToolkit-WebUI/Infrastructure/AppDataPaths.cs`
- Atomic JSON helper: `UnityLocalizationToolkit-WebUI/Infrastructure/FileHelper.cs`
- Secret protection: `UnityLocalizationToolkit-WebUI/Infrastructure/DpapiProtector.cs`
- Shared config and DTO models: `UnityLocalizationToolkit-WebUI/Models`
- Import and export behavior: `UnityLocalizationToolkit-WebUI/Endpoints/SettingsEndpoints.cs`

## Rules And Conventions
- `[data:appdata-root]` Keep `%AppData%\\UnityLocalizationToolkit` as the default runtime data root.
  - Scope: data
  - Last confirmed: 2026-04-17
  - Notes: `AppData:Root` can override it, and `AppDataPaths` still carries a legacy migration path from `%AppData%\\XUnityToolkit`.
- `[data:owned-layout]` Treat `AppDataPaths.cs` as the source of truth for runtime directories.
  - Scope: data
  - Last confirmed: 2026-04-17
  - Notes: Common top-level areas include `library.json`, `settings.json`, `local-llm-settings.json`, `glossaries`, `script-tags`, `translation-memory`, `dynamic-patterns`, `term-candidates`, `cache`, `models`, `llama`, `generated-fonts`, `font-backups`, `custom-fonts`, `logs`, `backups`, and `manual-translation`.
- `[data:manual-translation-layout]` Keep manual translation data under the dedicated app-data subtree.
  - Scope: data
  - Last confirmed: 2026-04-17
  - Notes: Per-game data lives under `manual-translation/projects/<gameId>/` with `manifest.json`, `asset-index.json`, `overrides/`, `overrides/media/`, `exports/`, `builds/`, and `code-patches/`; backups live under `manual-translation/backups/<gameId>/`.
- `[data:atomic-json]` Use the shared atomic JSON helper for persisted JSON writes.
  - Scope: data
  - Last confirmed: 2026-04-17
  - Notes: `FileHelper.WriteJsonAtomicAsync` writes through a `.tmp` file and should remain the default path for durable JSON changes.
- `[data:protected-secrets]` Keep API keys and similar secrets on the DPAPI protector flow.
  - Scope: data
  - Last confirmed: 2026-04-17
  - Notes: `DpapiProtector` uses the `ENC:DPAPI:` prefix and `CurrentUser` scope; do not replace it with plaintext storage.
- `[data:shared-field-sync]` Treat shared field changes as cross-layer changes.
  - Scope: data
  - Last confirmed: 2026-04-17
  - Notes: When changing `AppSettings`, `AiTranslationSettings`, `LocalLlmSettings`, manual translation DTOs, or similar shared models, update C# models, TypeScript API types, persistence code, and the matching UI together.
- `[data:new-path-checklist]` Update the full ownership chain when adding a new persistent directory or file.
  - Scope: data
  - Last confirmed: 2026-04-17
  - Notes: New stored paths usually require changes in `AppDataPaths.cs`, cleanup or delete flows, import and export behavior, and any rebuild or regeneration flow that should recreate the data.

## Commands And Checks
- `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`
- Use `GET /api/settings/data-path` and the import or export endpoints when validating data-path-sensitive changes through the running app.

## Common Issues And Handling
- If a persisted field appears in only one layer, assume a synchronization gap and inspect both backend models and `UnityLocalizationToolkit-Vue/src/api`.
- If a JSON file is written directly without the shared helper, expect partial-write and directory-creation issues on failure paths.

## Recent Relevant Changes
- 2026-04-17: Data guidance was rewritten into the standard domain template while preserving the verified app-data root, manual translation layout, DPAPI, and atomic write rules.
