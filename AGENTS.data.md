# AGENTS.data.md

<!-- agents-maintainer:auto:start -->
## Read This When
- Touching persisted data layout, config files, caches, schemas, models, or migration-sensitive paths.

## Relevant Paths
- Confirm the owning paths from the repository layout before adding new notes here.

## Commands / Constraints
- Relevant command: `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Relevant command: `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Relevant command: `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Relevant command: `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`
- Keep notes specific to this topic and move path-local rules into closer directory AGENTS.md files when possible.

## Verification
- Run `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
- Run `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
- Run `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
<!-- agents-maintainer:auto:end -->

## Manual Notes
<!-- agents-maintainer:manual:start -->
- Runtime data defaults to `%AppData%\\UnityLocalizationToolkit`; `AppData:Root` can override it. Path ownership is centralized in `UnityLocalizationToolkit-WebUI/Infrastructure/AppDataPaths.cs`.
- High-churn root files and directories include:
  - `library.json`
  - `settings.json`
  - `local-llm-settings.json`
  - `glossaries/`
  - `script-tags/`
  - `translation-memory/`
  - `dynamic-patterns/`
  - `term-candidates/`
  - `cache/`
  - `models/`
  - `llama/`
  - `generated-fonts/`
  - `font-backups/`
  - `custom-fonts/`
  - `logs/`
  - `update-staging/`
- Manual translation data lives under `manual-translation/`, especially:
  - `projects/<gameId>/manifest.json`
  - `projects/<gameId>/asset-index.json`
  - `projects/<gameId>/overrides/`
  - `projects/<gameId>/exports/`
  - `projects/<gameId>/builds/`
  - `projects/<gameId>/code-patches/`
  - `backups/<gameId>/`
- When adding a new per-game cache, directory, or data file, update `AppDataPaths.cs`, game-removal cleanup, settings export or import behavior, and any rebuild flow that should recreate it.
- `cache/pre-translation-regex/<gameId>.txt` is only a compatibility mirror for legacy custom-regex state. The authoritative runtime file remains `BepInEx/Translation/<lang>/Text/_PreTranslated_Regex.txt`.
- Secrets such as API keys and the SteamGridDB key are DPAPI-protected. Keep using the existing protector flow, atomic JSON helpers such as `FileHelper.WriteJsonAtomicAsync`, and `PathSecurity` helpers for joins and external URL validation.
- Shared field changes usually require coordinated updates across C# models, TypeScript API types, persistence code, and the matching UI. Watch `AppSettings`, `AiTranslationSettings`, `LocalLlmSettings`, and translation-editor related DTOs.
<!-- agents-maintainer:manual:end -->
