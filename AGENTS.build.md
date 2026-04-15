# AGENTS.build.md

<!-- agents-maintainer:auto:start -->
## Read This When
- Touching build scripts, packaging, CI, code generation, or dependency installation workflow.

## Relevant Paths
- `.github`
- `Installer`
- `Release`
- `Updater`
- `build.ps1`

## Commands / Constraints
- Relevant command: `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Relevant command: `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Relevant command: `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- Keep notes specific to this topic and move path-local rules into closer directory AGENTS.md files when possible.

## Verification
- Run `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
- Run `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
- Run `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when it is part of the touched workflow.
<!-- agents-maintainer:auto:end -->

## Manual Notes
<!-- agents-maintainer:manual:start -->
- Common backend loop:
  - `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
  - `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
  - `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
  - `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`
- Common frontend loop:
  - `cd UnityLocalizationToolkit-Vue`
  - `npm run dev`
  - `npx vue-tsc --build`
  - `npm run build`
- `UnityLocalizationToolkit-WebUI.csproj` runs frontend `npm install` and `npm run build` by default. Use `-p:SkipFrontendBuild=true` for backend-only iteration.
- Do not run backend build and backend test in parallel. `StaticWebAssets` can contend on `obj/.../rpswa.dswa.cache.json`.
- Full local packaging goes through `.\build.ps1` or `.\build.ps1 -SkipDownload`. The script prepares bundled assets, frontend output, `LLMTranslate.dll`, the AOT updater, and release artifacts under `Release/win-x64`.
- Keep `build.ps1`, `.github/workflows/build.yml`, and `.github/workflows/release.yml` aligned. CI does not simply shell out to `build.ps1`, so release-flow changes usually need updates in both places.
- If the bundled llama.cpp runtime version changes, sync the version references in code, the build script, CI, and release-facing docs in the same change.
- Frontend builds refresh hashed files under `UnityLocalizationToolkit-WebUI/wwwroot/assets`. Changed asset filenames are usually expected generated output, not accidental noise.
- Workflow file edits require GitHub credentials with workflow-editing permissions when changes are pushed by automation.
<!-- agents-maintainer:manual:end -->
