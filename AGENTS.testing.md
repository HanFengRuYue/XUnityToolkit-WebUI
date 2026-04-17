# AGENTS.testing.md

## Scope
- Covers verification strategy, test coverage, sequencing of checks, and which commands are expected for different kinds of changes.
- Covers backend tests in `UnityLocalizationToolkit-WebUI.Tests`, frontend type-check and production build checks, and packaging-sensitive validation.
- Does not replace domain guidance for implementation details; use it to choose the right validation depth.

## Continue Reading When
- Deciding whether a change needs `dotnet test`, `dotnet build`, `dotnet publish`, frontend type-check, frontend build, or runtime smoke validation.
- Editing tests or changing code in areas that already have service or infrastructure coverage.
- Working on build or publish flow where command order matters.

## Current Structure
- Test project: `UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`
- Infrastructure coverage: `UnityLocalizationToolkit-WebUI.Tests/Infrastructure`
- Service coverage: `UnityLocalizationToolkit-WebUI.Tests/Services`
- Frontend validation entrypoints: `UnityLocalizationToolkit-Vue/package.json`, `UnityLocalizationToolkit-Vue/vite.config.ts`

## Rules And Conventions
- `[testing:backend-tests]` Use the test project for backend service and infrastructure changes.
  - Scope: testing
  - Last confirmed: 2026-04-17
  - Notes: The current test suite covers infrastructure helpers such as `FileLoggerProvider`, `PathSecurity`, `RuntimePlaceholderProtector`, `TranslationOuterWrapperGuard`, and `TranslationResponseParser`, plus service areas such as AI prompts, endpoint resolution, manual translation, plugin health, pre-translation, translation-editor path resolution, asset writing, and update manifests.
- `[testing:build-when-host-changes]` Run backend build when host or runtime wiring changes.
  - Scope: testing
  - Last confirmed: 2026-04-17
  - Notes: Use `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` for endpoint, startup, host, service-registration, or static-asset-resolution changes.
- `[testing:publish-when-packaging-changes]` Run publish when packaging-sensitive behavior changes.
  - Scope: testing
  - Last confirmed: 2026-04-17
  - Notes: Use `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj` when touching packaged asset lookup, installer-facing output, updater integration, or release flow.
- `[testing:frontend-checks]` Run frontend type-check and build for frontend changes.
  - Scope: testing
  - Last confirmed: 2026-04-17
  - Notes: Use `cd UnityLocalizationToolkit-Vue && npx vue-tsc --build` and `cd UnityLocalizationToolkit-Vue && npm run build` for client-side changes or shared contract changes that affect the web UI.
- `[testing:sequential-staticwebassets]` Keep backend build and test commands sequential.
  - Scope: testing
  - Last confirmed: 2026-04-17
  - Notes: Do not run backend `dotnet build` and `dotnet test` in parallel because `StaticWebAssets` can contend on `obj/.../rpswa.dswa.cache.json`.
- `[testing:real-ui-validation]` Prefer real app validation for visible UI changes.
  - Scope: testing
  - Last confirmed: 2026-04-17
  - Notes: Validate visible UI changes against the backend-served app on `http://127.0.0.1:51821` when practical; use the Vite dev server only when you intentionally need it.

## Commands And Checks
- `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`
- `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- `cd UnityLocalizationToolkit-Vue && npx vue-tsc --build`
- `cd UnityLocalizationToolkit-Vue && npm run build`

## Common Issues And Handling
- If frontend output is regenerated during backend checks, treat `wwwroot/assets` hash changes as expected generated artifacts rather than accidental source edits.
- Choose checks based on the affected surface: tests for logic, build for host wiring, publish for packaging, and frontend build or type-check for UI work.

## Recent Relevant Changes
- 2026-04-17: Validation guidance was split into a dedicated `testing` sidecar so root and domain files can stay focused on implementation rules.
