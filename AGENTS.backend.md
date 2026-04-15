# AGENTS.backend.md

<!-- agents-maintainer:auto:start -->
## Read This When
- Touching APIs, services, desktop host code, runtime orchestration, or server-side logic.

## Relevant Paths
- `UnityLocalizationToolkit-WebUI`
- `UnityLocalizationToolkit-WebUI.Tests`
- `TranslatorEndpoint`
- `Updater`

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
- The main host project is `UnityLocalizationToolkit-WebUI` on `.NET 10` (`net10.0-windows`); `TranslatorEndpoint` targets `net35` / C# 7.3; `Updater` targets `net10.0` with `PublishAot=true`.
- Keep the app bound to `http://127.0.0.1:{port}`. The port comes from `settings.json` (`aiTranslation.port`, default `51821`). Do not switch runtime or generated config URLs to `localhost`.
- `Program.cs` must keep `ContentRootPath` and `WebRootPath` pinned to `AppContext.BaseDirectory` so packaged launches still resolve `wwwroot/index.html`.
- Standard JSON endpoints should continue returning `Results.Ok(ApiResult<T>.Ok(...))`; invalid requests should use proper non-200 results such as `BadRequest`.
- `POST /api/translate` is a special compatibility endpoint for `LLMTranslate.dll`; verify the DLL contract before changing its request or response shape.
- Prefer existing async service flows over new blocking I/O on hot paths, especially translation, settings, and asset workflows.
- Treat `GameId` and other path fragments as untrusted input: validate GUIDs where expected, use `PathSecurity.SafeJoin` for filesystem paths, and `PathSecurity.ValidateExternalUrl` for outbound URLs.
- Manual translation endpoints stay under `/api/games/{id}/manual-translation/*`. Asset list queries use `page`, `pageSize`, `valueKind=All|Text|Code|Image|Font|Binary`, `search`, `editableOnly`, and `overriddenOnly`; do not reintroduce the old `scope=assets|code` contract.
- `ManualTranslationStatus` exposes `kindCounts`; frontend filters and summary cards should reuse that response instead of recounting the full asset set client-side.
- `ManualTranslationService` caches parsed project indexes. List/detail/override refreshes should reuse cached state; `scan`, package import, and game deletion must invalidate it.
- Do not run `dotnet build UnityLocalizationToolkit-WebUI/...` and `dotnet test UnityLocalizationToolkit-WebUI.Tests/...` in parallel because `StaticWebAssets` can contend on `obj/.../rpswa.dswa.cache.json`.
<!-- agents-maintainer:manual:end -->
