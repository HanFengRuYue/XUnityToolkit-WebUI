# AGENTS.md

## Project Snapshot
- `XUnityToolkit-WebUI` is a Windows-first Unity localization toolkit desktop app.
- The repo is a dual-stack app: `UnityLocalizationToolkit-WebUI` is a `.NET 10` `WinExe` host, and `UnityLocalizationToolkit-Vue` is a `Vue 3 + TypeScript + Vite 8` frontend that builds into `UnityLocalizationToolkit-WebUI/wwwroot`.
- Related executables and support projects include `TranslatorEndpoint` (`net35`, `LLMTranslate.dll` bridge), `Updater` (`net10.0`, `PublishAot=true`), `Installer`, `.github/workflows`, `bundled`, and `build.ps1`.
- High-signal paths: `UnityLocalizationToolkit-WebUI`, `UnityLocalizationToolkit-Vue`, `UnityLocalizationToolkit-WebUI.Tests`, `TranslatorEndpoint`, `Updater`, `Installer`, `.github`, `build.ps1`, `docs`, `bundled`.

## Reading Map
- Start here for repo-wide facts and rule priority.
- Read `AGENTS.frontend.md` when touching `UnityLocalizationToolkit-Vue`, `wwwroot`, views, components, stores, routes, styling, or Vite workflows.
- Read `AGENTS.backend.md` when touching `UnityLocalizationToolkit-WebUI`, `TranslatorEndpoint`, `Updater`, APIs, services, desktop host startup, or runtime orchestration.
- Read `AGENTS.data.md` when touching `%AppData%\\UnityLocalizationToolkit`, config files, persisted JSON, caches, DTOs, import or export behavior, or migration-sensitive paths.
- Read `AGENTS.infra.md` when touching `build.ps1`, `Installer`, `bundled`, `.github/workflows`, packaging, runtime ports, logging, release flow, or operational safeguards.
- Read `AGENTS.testing.md` when choosing verification scope, changing tests, or deciding which build, publish, type-check, or runtime checks are required.
- Read `AGENTS.history.md` when you need archived AGENTS decisions, replacement trails, or context for retired guidance files.

## Rule Priority
1. Current direct user instructions.
2. Latest durable rules recorded in live root-level `AGENTS*.md` files.
3. Current repository facts and conventions.
4. Archived notes in `AGENTS.history.md`.

## Durable Rules
- `[global:root-is-index]` Keep this file as an index, not a handbook.
  - Scope: global
  - Last confirmed: 2026-04-17
  - Notes: Move detailed workflows and domain-specific rules into the matching sidecar.
- `[global:read-matching-sidecar]` Read the matching sidecar before editing a related area.
  - Scope: global
  - Last confirmed: 2026-04-17
  - Notes: Frontend, backend, data, infra, and testing guidance are intentionally split so new sessions can load only the relevant context.
- `[global:nested-agents-threshold]` Add nested `AGENTS.md` files only when a subtree gains its own repeat-use workflow or safety rules.
  - Scope: global
  - Last confirmed: 2026-04-17
  - Notes: The current repo is intentionally covered by root-level sidecars only.
- `[global:agents-language]` Keep AGENTS files in English.
  - Scope: global
  - Last confirmed: 2026-04-17
  - Notes: README and user-facing docs may stay Chinese-facing without changing AGENTS language.
- `[global:live-agents-only]` Treat root-level `AGENTS*.md` files as the live instruction set.
  - Scope: global
  - Last confirmed: 2026-04-17
  - Notes: `.agents-maintainer/backups/` is archival context only and should not be treated as active guidance.

## Commands And Entrypoints
- App start: `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Backend build: `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Publish: `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Tests: `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`
- Frontend dev: `cd UnityLocalizationToolkit-Vue && npm run dev`
- Frontend checks: `cd UnityLocalizationToolkit-Vue && npx vue-tsc --build`
- Frontend production build: `cd UnityLocalizationToolkit-Vue && npm run build`
- Packaging: `.\build.ps1` or `.\build.ps1 -SkipDownload`
- Key entrypoints: `UnityLocalizationToolkit-WebUI/Program.cs`, `UnityLocalizationToolkit-Vue/src/main.ts`, `UnityLocalizationToolkit-WebUI/Infrastructure/AppDataPaths.cs`, `build.ps1`

## Current Constraints
- `UnityLocalizationToolkit-WebUI.csproj` runs frontend `npm install` and `npm run build` before backend build unless `-p:SkipFrontendBuild=true` is set.
- Runtime and development traffic should stay on `http://127.0.0.1:{port}` rather than `localhost`; the default port comes from `settings.json`.
- Do not record secrets, API keys, DPAPI payloads, certificates, or machine-local Git ownership fixes in AGENTS files.
