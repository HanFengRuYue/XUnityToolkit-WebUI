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
- 后端主程序是 `UnityLocalizationToolkit-WebUI`，目标框架为 `.NET 10` `net10.0-windows`；`TranslatorEndpoint` 是 `net35` / C# 7.3，`Updater` 是 `net10.0` 且 `PublishAot=true`。
- `Program.cs` 必须继续绑定 `http://127.0.0.1:{port}`，端口来自 `settings.json` 的 `aiTranslation.port`，默认 `51821`。不要改成 `localhost`。
- `ContentRootPath` 和 `WebRootPath` 必须锚定 `AppContext.BaseDirectory`，不能跟随 `Environment.CurrentDirectory`。否则独立启动、更新器或安装器拉起时会出现首页 `404` 但 API 仍可访问的状态。
- 返回 `ApiResult<T>` 的接口统一使用 `Results.Ok(ApiResult<T>.Ok(...))`；参数错误返回 `BadRequest`，不要返回 `200 + fail body`。
- `POST /api/translate` 是给 `LLMTranslate.dll` 直接调用的特殊接口，不走常规 `ApiResult<T>` 包装；修改响应格式前必须先核对 DLL 兼容性。
- 热路径避免阻塞式 I/O，尤其是 `POST /api/translate`。修改 `AppSettingsService` 时优先走 `UpdateAsync` / `SaveAsync`，不要原地乱改 `GetAsync()` 返回的缓存对象。
- 任何把 `GameId` 用作路径片段的逻辑都要先校验为 GUID；外部 URL 在真正请求前必须经过 `PathSecurity.ValidateExternalUrl`。
- 手动翻译后端接口统一挂在 `/api/games/{id}/manual-translation/*`。`GET /assets` 已改为服务端分页，支持 `page`、`pageSize`、`valueKind=All|Text|Code|Image|Font|Binary`、`search`、`editableOnly`、`overriddenOnly`，不要再引入旧的 `scope=assets|code` 语义。
- `ManualTranslationStatus` 现在固定返回 `kindCounts(all/text/code/image/font/binary)`。前端统计和筛选必须复用这个结果，不要再把全量资产拉回本地自己数。
- `ManualTranslationService` 会按游戏缓存已解析的 `asset-index.json`。列表、详情、保存覆盖后的刷新都应复用缓存；`scan`、`import-package` 和删除游戏数据时必须失效缓存。
- 手动翻译列表默认排序保持为：`overridden desc -> valueKind(Text, Code, Image, Font, Binary) -> displayName/preview -> assetFile -> pathId`。改排序前先确认不会破坏大资产集的可读性。
- 后端回归测试放在 `UnityLocalizationToolkit-WebUI.Tests`。`dotnet build UnityLocalizationToolkit-WebUI/...` 和 `dotnet test UnityLocalizationToolkit-WebUI.Tests/...` 不要并行跑，否则 `StaticWebAssets` 会争用 `obj/.../rpswa.dswa.cache.json`。
<!-- agents-maintainer:manual:end -->
