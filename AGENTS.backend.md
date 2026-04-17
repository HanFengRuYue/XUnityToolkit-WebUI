# AGENTS.backend.md

## Scope
- Covers server-side and desktop-host code in `UnityLocalizationToolkit-WebUI`, plus compatibility and runtime facts for `TranslatorEndpoint` and `Updater`.
- Covers `Program.cs`, endpoints, services, infrastructure helpers, models, background lifecycle hooks, and host orchestration.
- Does not own packaging workflow details, installer authoring, or verification sequencing beyond backend-specific rules.

## Continue Reading When
- Editing `UnityLocalizationToolkit-WebUI/Program.cs`, `Endpoints`, `Services`, `Infrastructure`, `Models`, or `Hubs`.
- Changing API contracts, runtime startup and shutdown behavior, background services, or path-security-sensitive file handling.
- Touching `TranslatorEndpoint` request or response behavior or `Updater` runtime characteristics.

## Current Structure
- Host entrypoint: `UnityLocalizationToolkit-WebUI/Program.cs`
- API surface: `UnityLocalizationToolkit-WebUI/Endpoints`
- Runtime services: `UnityLocalizationToolkit-WebUI/Services`
- Shared infrastructure: `UnityLocalizationToolkit-WebUI/Infrastructure`
- Shared models and DTOs: `UnityLocalizationToolkit-WebUI/Models`
- Plugin bridge: `TranslatorEndpoint`
- Native updater: `Updater`

## Rules And Conventions
- `[backend:targets]` Preserve the current runtime target split.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: `UnityLocalizationToolkit-WebUI` targets `net10.0-windows`, `TranslatorEndpoint` targets `net35` with `LangVersion=7.3`, and `Updater` targets `net10.0` with `PublishAot=true`.
- `[backend:bind-address]` Keep the app bound to `127.0.0.1`.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: `Program.cs` reads the port from `%AppData%\\UnityLocalizationToolkit\\settings.json` and uses `http://127.0.0.1:{port}`; do not switch runtime defaults to `localhost`.
- `[backend:content-root]` Keep packaged asset resolution pinned to `AppContext.BaseDirectory`.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: `Program.cs` must keep `ContentRootPath` and `WebRootPath` based on `AppContext.BaseDirectory` so packaged runs can still resolve `wwwroot/index.html`.
- `[backend:api-result-shape]` Keep standard JSON endpoints on the `ApiResult` wrapper.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: The normal pattern is `Results.Ok(ApiResult<T>.Ok(...))`; invalid requests should still use proper non-200 results such as `BadRequest` or `NotFound`.
- `[backend:translate-compat]` Treat `/api/translate` as a compatibility endpoint.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: `TranslatorEndpoint/LLMTranslateEndpoint.cs` hardcodes `/api/translate` and `/api/translate/ping`; verify the DLL contract before changing request or response shape.
- `[backend:path-security]` Treat game paths, zip contents, and outbound URLs as untrusted input.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: Reuse `PathSecurity.SafeJoin` for filesystem paths and `PathSecurity.ValidateExternalUrl` for outbound or imported URLs instead of adding ad hoc checks.
- `[backend:async-hot-paths]` Prefer the existing async service flows on hot paths.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: Translation, settings, manual translation, and download flows should not regress into new blocking I/O unless there is a strong reason.
- `[backend:manual-translation-contract]` Keep manual translation routes and summaries on the current contract.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: Manual translation endpoints stay under `/api/games/{id}/manual-translation/*`; asset list queries use `page`, `pageSize`, `valueKind`, `search`, `editableOnly`, and `overriddenOnly`, and status responses expose `kindCounts`.
- `[backend:manual-translation-cache]` Preserve the parsed-project cache behavior.
  - Scope: backend
  - Last confirmed: 2026-04-17
  - Notes: `ManualTranslationService` caches parsed project indexes; list, detail, and override refreshes should reuse cached state, while scan, package import, and game deletion must invalidate it.

## Commands And Checks
- `dotnet run --project UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- `dotnet build UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- `dotnet publish UnityLocalizationToolkit-WebUI/UnityLocalizationToolkit-WebUI.csproj`
- `dotnet test UnityLocalizationToolkit-WebUI.Tests/UnityLocalizationToolkit-WebUI.Tests.csproj`

## Common Issues And Handling
- If startup or packaged launches cannot find frontend assets, inspect `Program.cs` path setup first before changing routing or static-file behavior.
- If a change touches a shared request or response shape, expect follow-up updates across backend models, frontend API types, persistence, and tests.

## Recent Relevant Changes
- 2026-04-17: Backend guidance was rewritten into the standard domain template while preserving the verified host, compatibility, and path-security rules.
