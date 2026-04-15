# AGENTS.notes.md

<!-- agents-maintainer:auto:start -->
## Read This When
- Needing extra durable project notes that do not fit a more specific topic.

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
- This repository is a Windows-first Unity localization toolkit: XUnity setup, cloud and local LLM translation, asset extraction and pretranslation, font replacement or generation, plus plugin and log tooling.
- Primary subprojects are `UnityLocalizationToolkit-WebUI/`, `UnityLocalizationToolkit-Vue/`, `UnityLocalizationToolkit-WebUI.Tests/`, `TranslatorEndpoint/`, `Updater/`, and `Installer/`.
- The repo currently relies on root `AGENTS.md` plus root topic sidecars; there are no nested AGENTS files yet. Add nested files only when a subtree gains its own repeat-use workflow.
- Git collaboration docs and conventional commit subjects are intentionally Chinese-facing in repository docs and scripts; AGENTS files stay English.
- The game detail area treats XUnity and manual translation as parallel workflows. Changes to shared status, summaries, or delete or cleanup logic should keep both workspaces consistent instead of letting one silently overwrite the other.
<!-- agents-maintainer:manual:end -->
