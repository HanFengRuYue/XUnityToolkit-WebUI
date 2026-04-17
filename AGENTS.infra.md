# AGENTS.infra.md

## Scope
- Covers build, packaging, installer, CI, bundled assets, runtime operations, and release-flow safeguards.
- Covers `build.ps1`, `Installer`, `bundled`, `.github/workflows`, `Release`, and the operational parts of `Program.cs` that matter to deployment and packaging.
- Does not own service-level API semantics or persisted data layout except where build or runtime flow depends on them.

## Continue Reading When
- Editing `build.ps1`, `Installer/*.wxs`, `Installer.wixproj`, `.github/workflows/*.yml`, `bundled`, or release packaging behavior.
- Changing frontend-build integration in the backend project, runtime port binding, startup logging, updater flow, or other operational safeguards.
- Working on local packaging, release automation, or bundled dependency versioning.

## Current Structure
- Packaging script: `build.ps1`
- Installer authoring: `Installer`
- Bundled runtime assets: `bundled`
- Release artifacts: `Release`
- Workflow automation: `.github/workflows/build.yml`, `.github/workflows/dep-check.yml`, `.github/workflows/release.yml`
- Frontend build hook: `UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`

## Rules And Conventions
- `[infra:frontend-hook]` Expect the backend build to trigger frontend installation and build by default.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: `UnityLocalizationToolkit-WebUI.csproj` runs `npm install` and `npm run build` before backend build unless `-p:SkipFrontendBuild=true` is supplied.
- `[infra:backend-only-iteration]` Use the backend-only build switch for backend iteration.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: Use `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj -p:SkipFrontendBuild=true` when frontend output does not need to change.
- `[infra:packaging-entrypoint]` Keep local packaging centered on `build.ps1`.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: Full local packaging goes through `.\build.ps1` or `.\build.ps1 -SkipDownload`; it prepares bundled assets, frontend output, `LLMTranslate.dll`, the AOT updater, and release artifacts under `Release/win-x64`.
- `[infra:workflow-alignment]` Keep build and release automation aligned across local scripts and CI.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: `build.ps1`, `build.yml`, and `release.yml` do not automatically call each other, so release-flow changes usually need coordinated updates.
- `[infra:runtime-bind]` Keep runtime and dev operations on `127.0.0.1`.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: The app binds to `127.0.0.1:{port}` from `settings.json`, and the frontend dev proxy should stay on the same loopback convention.
- `[infra:startup-logging]` Preserve the startup path diagnostics.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: Startup logging should continue reporting `CurrentDirectory`, `BaseDirectory`, `ContentRoot`, `WebRoot`, and whether `wwwroot/index.html` exists.
- `[infra:lifecycle-hooks]` Keep the host lifecycle hooks intact when restructuring startup or shutdown.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: `ApplicationStarted` handles deferred AI and updater initialization, and `ApplicationStopping` hides the UI and flushes dirty translation memory.
- `[infra:high-risk-io]` Reuse the existing safety helpers on risky external or archive flows.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: External URLs, ZIP extraction, and file imports should continue flowing through the existing validation and extraction helpers instead of bespoke path handling.
- `[infra:bundled-version-sync]` Sync bundled runtime version references as a single change.
  - Scope: infra
  - Last confirmed: 2026-04-17
  - Notes: If the bundled llama.cpp runtime version changes, update code, scripts, workflows, and release-facing docs together.

## Commands And Checks
- `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
- `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- `.\build.ps1`
- `.\build.ps1 -SkipDownload`

## Common Issues And Handling
- Frontend builds refresh hashed files under `UnityLocalizationToolkit-WebUI/wwwroot/assets`; changed asset filenames are usually expected generated output.
- Workflow file changes may require GitHub credentials with workflow-editing permissions when pushed by automation.

## Recent Relevant Changes
- 2026-04-17: `build`, `ops`, and related operational guidance were consolidated into the standard `infra` sidecar.
