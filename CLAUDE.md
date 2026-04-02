# CLAUDE.md

本文件为 Claude Code (claude.ai/code) 在本仓库中工作时提供指导。
另见：`XUnityToolkit-Vue/CLAUDE.md`（前端）和 `XUnityToolkit-WebUI/CLAUDE.md`（后端）了解各包的详细信息。

## 项目概述

XUnityToolkit-WebUI 是一个基于 Web 的工具，用于一键安装 XUnity.AutoTranslator 和 BepInEx 插件到 Unity 游戏中。它由 Vue 3 前端和 ASP.NET Core 后端组成。

## 构建命令

- **前置条件：** .NET 10.0 SDK (preview), Node.js `^20.19.0 || >=22.12.0`

```bash
# 构建后端（也会通过 MSBuild Target 自动构建前端）
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# 运行后端（在 http://127.0.0.1:51821 提供 Web UI 服务）
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj

# 一键本地构建（自包含，win-x64，包含 Updater）
.\build.ps1
.\build.ps1 -SkipDownload    # 跳过资源下载

# 构建前端（输出到 XUnityToolkit-WebUI/wwwroot/）
cd XUnityToolkit-Vue && npm run build

# 构建 TranslatorEndpoint (LLMTranslate.dll)
dotnet build TranslatorEndpoint/TranslatorEndpoint.csproj -c Release

# 前端开发服务器（API 代理到后端）
cd XUnityToolkit-Vue && npm run dev

# 前端类型检查
cd XUnityToolkit-Vue && npx vue-tsc --build
```

## 架构

- **后端：** ASP.NET Core Minimal API (.NET 10.0, Windows Forms 用于系统托盘和 WebView2 窗口)；详见 `XUnityToolkit-WebUI/CLAUDE.md`
- **前端：** Vue 3 + TypeScript + Naive UI + Pinia（位于 `XUnityToolkit-Vue/`）；详见 `XUnityToolkit-Vue/CLAUDE.md`
- **实时通信：** 通过单个 `InstallProgressHub` 使用 SignalR（分组：`game-{id}`, `ai-translation`, `logs`, `pre-translation-{gameId}`, `local-llm`, `font-replacement-{gameId}`, `font-generation`, `update`）
- **持久化：** JSON 文件存储在 `%AppData%\XUnityToolkit\`（`library.json`, `settings.json`）；`AppData:Root` 配置键允许覆盖路径；API 密钥使用 DPAPI 加密
- **TranslatorEndpoint：** net35 `LLMTranslate.dll` — XUnity.AutoTranslator 自定义端点，将游戏文本转发到 `POST /api/translate`；通过 `[LLMTranslate]` INI 区段配置
- **AI 翻译：** `LlmTranslationService` 调用 LLM API（OpenAI/Claude/Gemini/DeepSeek/Qwen/GLM/Kimi/Custom）；多提供商负载均衡；每游戏统一术语表、翻译记忆、AI 描述；通过 SignalR 实时统计
- **本地 LLM：** `LocalLlmService` 管理 llama-server 进程；通过 DXGI/WMI 检测 GPU；本地模式强制 concurrency=1、batch size=1、禁用术语提取
- **模块化版本：** Full（自包含+LLAMA）、No-LLAMA（自包含）、Lite（框架依赖）；通过 `-p:Edition=full|no-llama|lite` 传递；`EditionInfo`（`Infrastructure/EditionInfo.cs`）在构建时注入
- **资源提取：** `AssetExtractionService` 使用 AssetsTools.NET 提取字符串；支持 I2 Localization、GameCreator 2、VIDE 等专用提取器
- **字体：** `FontReplacementService` 扫描和替换 TMP_FontAsset；`TmpFontGeneratorService` 通过 FreeType + Felzenszwalb EDT 生成 SDF 字体
- **在线更新：** `UpdateService` 检查 GitHub Releases；基于清单的差异下载；`Updater.exe`（AOT）处理文件替换和重启

## 翻译管线

- **管线分叉流程设计：** 状态仪表板显示水平分叉管线："已接收" → [LLM 分支：排队→翻译中→已完成→术语提取] | [TM 分支：(精确·模糊·模式)→已命中]；水平分叉布局是设计意图 — 不要替换为平面卡片或垂直自顶向下布局；LLM 完成计数 = `totalTranslated - (tmHits + tmFuzzyHits + tmPatternHits)`；TM 分支使用基于 accent 的 `color-mix()` 变体；**管线动画 `active` 不变量：** 连接线的 `active` 条件必须使用瞬时流量指标（如 `queued + translating > 0`），不能使用累计计数器 — 累计值只增不减，会导致动画永远播放
- **多阶段翻译管线：** Phase 0（TM 查找）→ Phase 1（自然模式）→ Phase 2（占位符 `{{G_x}}`/`{{DNT_x}}` 替换）→ Phase 3（强制修正）；各阶段逐步推进；详见 `XUnityToolkit-WebUI/CLAUDE.md` 的"AI 翻译上下文"和"翻译管线详情"
- **翻译记忆：** `TranslationMemoryService` 提供持久化的每游戏 TM（精确 → 动态模式 → Levenshtein 模糊）；TM 命中需经过 `TermAuditService` 验证；`Add()` 同步（仅内存），持久化经过防抖（5 秒）
- **统一术语管理：** `TermService` 在 `data/glossaries/{gameId}.json` 存储每游戏术语条目；`TermMatchingService` 处理占位符替换；`TermAuditService` 验证合规性

## 代码约定

- **Git commit 消息：** 标题和正文必须用中文书写（conventional commit 前缀如 `feat:`/`fix:`/`ci:`/`docs:` 可保持英文）
- **目标框架：** net10.0-windows
- **根命名空间：** XUnityToolkit_WebUI
- **可空引用类型：** 已启用；**隐式 using：** 已启用
- **AssetsTools.NET 数组访问：** 数组字段结构为 `field → "Array" child → actual elements`；使用 `GetArrayElements()` 辅助方法（在 `AssetExtractionService` 中）解包；不要直接迭代数组字段的 `.Children` 来期望获得元素
- **通知：** 所有用户通知必须通过后端 `SystemTrayService.ShowNotification` — 不要在前端使用浏览器 `Notification` API

## API 端点

- **游戏：** `GET/POST /api/games/`, `GET/DELETE /api/games/{id}`, `POST .../add-with-detection`, `POST .../batch-add`（批量添加子目录游戏）, `PUT /api/games/{id}`（重命名）, `POST .../detect`, `POST .../open-folder`, `POST .../launch`
- **TMP 字体：** `GET/POST/DELETE /api/games/{id}/tmp-font` — 检查/安装/卸载内置 TMP 字体（与游戏 Unity 版本匹配）；由 `ConfigPanel.vue` 使用
- **框架：** `DELETE /api/games/{id}/framework/{framework}`
- **安装：** `POST /api/games/{id}/install`, `DELETE .../install`（卸载）, `GET .../status`, `POST .../cancel`
- **图标：** `GET /api/games/{id}/icon`（自定义 > exe 图标）, `POST .../icon/upload`, `DELETE .../icon/custom`, `POST .../icon/{search,grids,select}`（SteamGridDB）, `POST .../icon/web-search`, `POST .../icon/web-select`
- **封面：** `GET .../cover`, `POST .../cover/upload`（5MB）, `POST .../cover/{search,grids,select,steam-search,steam-select,web-search,web-select}`, `DELETE .../cover`
- **背景：** `GET .../background`, `POST .../background/upload`（10MB）, `POST .../background/{search,heroes,select,steam-search,steam-select,web-search,web-select}`, `DELETE .../background`
- **配置：** `GET/PUT /api/games/{id}/config`（PUT 时 PatchAsync 读-修改-写）, `GET/PUT .../config/raw`
- **设置：** `GET/PUT /api/settings`, `GET .../version`, `POST .../reset`（删除整个 `paths.Root` 目录，使所有服务缓存失效，重建目录）, `GET .../data-path`, `POST .../export`（ZIP，**非 ApiResult**）, `POST .../import`（multipart ZIP）, `POST .../open-data-folder`
- **文件浏览器：** `GET /api/filesystem/drives`, `GET /api/filesystem/quick-access`, `POST /api/filesystem/list`, `POST /api/filesystem/read-text`（前端文件浏览器弹窗用于替代 Windows 原生对话框）
- **Path-based 上传：** 所有文件上传端点均有 `*-from-path` 变体，接受 `UploadFromPathRequest { FilePath }` JSON 请求体，用于内置文件浏览器场景；multipart 端点保留用于拖拽上传；涉及：`font-generation/upload-from-path`、`font-replacement/upload-from-path`、`cover/upload-from-path`、`background/upload-from-path`、`icon/upload-from-path`、`settings/import-from-path`、`charset/upload-custom-from-path`、`charset/upload-translation-from-path`
- **`*-from-path` 端点对等性：** `*-from-path` 变体必须与对应的 multipart 端点包含相同的业务逻辑（如迁移、验证）——`settings/import-from-path` 已同步 DNT 迁移逻辑
- **AI 翻译：** `POST /api/translate`（**非 ApiResult** — DLL 直接调用；前端必须使用原始 `fetch`）, `GET /api/translate/stats`, `GET /api/translate/cache-stats`, `POST /api/translate/test`, `GET /api/translate/ping?gameId=`（来自 LLMTranslate.dll 的连通性 ping）
- **AI 控制：** `POST /api/ai/toggle`, `GET /api/ai/models?provider=&apiBaseUrl=&apiKey=`, `GET /api/ai/extraction/stats`
- **本地 LLM：** `GET/PUT /api/local-llm/settings`（PUT 仅合并 gpuLayers/contextLength/kvCacheType）, `GET .../status`, `GET .../gpus`, `POST .../gpus/refresh`, `GET .../catalog`, `GET .../llama-status`, `POST .../test`（需要 Running 状态）, `POST .../start`, `POST .../stop`, `.../download`（模型）+ `/pause` + `/cancel` 变体, `GET .../models`, `POST .../models/add`, `DELETE .../models/{id}`, `POST .../llama-download`（下载 llama 二进制）, `POST .../llama-download/cancel`
- **AI 端点：** `GET/POST/DELETE /api/games/{id}/ai-endpoint` — 管理 `LLMTranslate.dll`；POST 还会修补 INI 中的 `[LLMTranslate] ToolkitUrl` + `GameId`
- **术语：** `GET/PUT /api/games/{id}/terms` — 统一术语 CRUD（替代独立的 glossary/DNT）；`POST /api/games/{id}/terms/import-from-game` — 跨游戏导入 | **描述：** `GET/PUT .../description`
- **术语表（兼容）：** `GET/PUT /api/games/{id}/glossary` — 旧版兼容层，通过 TermService 读写
- **免翻译（兼容）：** `GET/PUT /api/games/{id}/do-not-translate` — 旧版兼容层，通过 TermService 读写
- **翻译记忆：** `GET/DELETE /api/games/{id}/translation-memory`（统计/清除）, `GET/DELETE .../dynamic-patterns`, `GET/POST/DELETE .../term-candidates`（通过 POST 应用，`{ originals: string[] }`，也恢复暂停的预翻译）
- **脚本标签：** `GET /api/script-tag-presets`, `GET/PUT /api/games/{id}/script-tags`
- **资源提取：** `POST .../extract-assets`, `GET/DELETE .../extracted-texts`
- **预翻译：** `POST .../pre-translate`, `GET .../pre-translate/status`, `POST .../pre-translate/cancel`, `GET/PUT .../pre-translate/regex`
- **翻译编辑器：** `GET/PUT .../translation-editor`, `POST .../import`, `GET .../export`（**非 ApiResult**）
- **字体替换：** `POST .../font-replacement/scan`, `POST .../replace`, `POST .../restore`, `GET .../status`, `POST .../upload`, `POST .../cancel`, `DELETE .../font-replacement/custom-font?type={ttf|tmp}`
- **BepInEx 日志：** `GET /api/games/{id}/bepinex-log`, `GET .../download`（**非 ApiResult**）, `POST .../analyze`
- **插件健康：** `GET /api/games/{id}/health-check`, `POST .../health-check/verify`
- **BepInEx 插件：** `GET /api/games/{id}/plugins`, `POST .../plugins/install`（本地路径）, `POST .../plugins/upload`（multipart, 50MB）, `DELETE .../plugins?relativePath=`（卸载）, `POST .../plugins/toggle`（启用/禁用）, `GET .../plugins/config?configFile=`
- **字体生成：** `POST /api/font-generation/upload`（multipart, 50MB）, `POST .../generate`, `GET .../status`, `POST .../cancel`, `GET .../download/{fileName}`（**非 ApiResult**）, `GET .../history`, `DELETE .../{fileName}`, `POST .../install-tmp-font/{gameId}`（安装到 `BepInEx/Font/` + 修补 INI）, `GET .../charsets`, `POST .../charset/preview`, `POST .../charset/upload-custom`, `POST .../charset/upload-translation`, `GET .../report/{fileName}`
- **插件包：** `POST .../plugin-package/export`（ZIP，**非 ApiResult**）, `POST .../import`
- **日志：** `GET /api/logs?count=`, `GET .../history?lines=`, `GET .../download`（**非 ApiResult**）
- **更新：** `GET /api/update/check`, `GET .../status`, `POST .../download`, `POST .../cancel`, `POST .../apply`, `POST .../dismiss`
- **ApiResult 模式：** `ApiResult<T>.Ok(data)` / `ApiResult.Ok()`；请求记录在 Endpoints 文件底部

## 开发说明

### TranslatorEndpoint

- 目标 net35 (C# 7.3)；`Microsoft.NETFramework.ReferenceAssemblies.net35` NuGet
- **引用 DLL：** `TranslatorEndpoint/libs/` 由 `build.ps1` 从内置 XUnity ZIP 自动提取；已提交的文件作为后备
- `DisableSpamChecks()` 移除稳定化等待；`SetTranslationDelay(float)` 最小 0.1 秒；v5.4.3+ 可用
- `GetOrCreateSetting` 读取已有 INI；更改 DLL 默认值不会影响已安装的游戏 — 使用 `PatchSectionAsync`
- **双级日志：** `Log()` 始终输出关键信息（初始化 banner、配置、错误、请求/完成摘要）；`DebugLog()` 受 `DebugMode` 控制用于详细信息（文本预览、响应数据、ServicePoint 配置）；`DebugMode` 默认为 `false` — 不要在生产环境中默认为 `true`
- **`EscapeJsonString` 代理对处理：** 补充平面字符（U+10000+）在 .NET 中是代理对；循环中检测 `char.IsHighSurrogate` + `char.IsLowSurrogate` 配对后直接追加两个 char；孤立代理用 `\uXXXX` 转义
- **"Endpoint" 与 "Provider"：** "translation endpoint" = `LLMTranslate.dll`；"provider" = `ApiEndpointConfig` LLM API 配置

### 同步点

- **InstallStep 枚举：** 在 4 处同步：`Models/InstallationStatus.cs`, `src/api/types.ts`, `InstallProgressDrawer.vue`, `InstallOrchestrator.cs`；还要更新 `GameDetailView.vue` 的 `installStepLabel` 映射
- **添加 AppSettings 字段：** 在 4 处同步：`Models/AppSettings.cs`, `src/api/types.ts`, store 的 `loadPreferences`/`savePreferences`, `SettingsView.vue`；`SettingsView.vue` 中 `ref<AppSettings>({...})` 的初始值必须与 `AppSettings.cs` 的默认值一致
- **添加 AiTranslationSettings 字段：** 在 4 处同步：`Models/AiTranslationSettings.cs`, `src/api/types.ts`, `AiTranslationView.vue`（`DEFAULT_AI_TRANSLATION`）, `SettingsView.vue`（默认值）；实时设置（`TermAuditEnabled`, `NaturalTranslationMode`, `EnableTranslationMemory`, `FuzzyMatchThreshold`）在 `AiTranslationView.vue` 中显示；本地模式专属设置（`LocalMinP`, `LocalRepeatPenalty`, `LocalContextSize`）在 `LocalAiPanel.vue` 中显示；仅预翻译设置（`EnableLlmPatternAnalysis`, `EnableMultiRoundTranslation`, `EnableAutoTermExtraction`, `AutoApplyExtractedTerms`）在 `AssetExtractionView.vue` 中显示；数值型字段需在 `SettingsEndpoints.cs` 中添加 `Math.Clamp`
- **添加 TermEntry 字段：** 在 2 处同步：`Models/TermEntry.cs`, `src/api/types.ts`；包含 `Source`（`TermSource` 枚举，PascalCase JSON）
- **ScriptTagRule/ScriptTagConfig 字段：** 在 2 处同步：`Models/ScriptTagRule.cs` + `Models/ScriptTagConfig.cs` ↔ `src/api/types.ts`
- **每游戏数据清理（脚本标签）：** `GameEndpoints.cs` 中的 `DELETE /api/games/{id}` 必须删除 `scriptTagFile` + 调用 `scriptTagService.RemoveCache`
- **每游戏数据清理（术语）：** `GameEndpoints.cs` 中的 `DELETE /api/games/{id}` 必须删除术语表文件 + 调用 `termService.RemoveCache`
- **每游戏数据清理（翻译记忆）：** `DELETE /api/games/{id}` 必须删除 TM 文件 + 调用 `translationMemoryService.RemoveCache`、`dynamicPatternService.RemoveCache`、`termExtractionService.RemoveCache`
- **`NormalizeForCache` 调用点：** 3 处必须全部使用 `ScriptTagService.NormalizeForCache(gameId, text)`：`WriteTranslationCacheAsync`、`LoadCache`、`RecordTexts`
- **添加预设规则：** 更新 `bundled/script-tag-presets.json`，递增 `version`
- **添加 TranslationStats/RecentTranslation/TranslationError 字段：** 在 3 处同步：`Models/TranslationStats.cs`, `src/api/types.ts`, `AiTranslationView.vue`（显示 + recent-meta 区域）；包含术语审计统计和每文本术语元数据（`HasTerms`, `HasDnt`, `TermAuditResult`）、`TranslationMemoryHits`、`TranslationMemoryFuzzyHits`、`TranslationMemoryPatternHits`、`TranslationMemoryMisses`、`MaxConcurrency`、`DynamicPatternCount`、`ExtractedTermCount`
- **翻译记忆显示：** TM 命中统计显示在管线分叉中（不是单独的卡片）；实时数据来自 `TranslationStats.TranslationMemoryHits/FuzzyHits/PatternHits`，通过 SignalR（无单独 TM 统计 API — `translationMemoryApi` 已作为死代码移除）
- **PreTranslationCacheStats 字段：** 在 2 处同步：`Models/TranslationStats.cs`, `src/api/types.ts`；在 `AiTranslationView.vue` 中显示
- **PreTranslationStatus 字段：** 在 2 处同步：`Models/AssetExtraction.cs`, `src/api/types.ts`；在 `AssetExtractionView.vue` 中显示；包含 `CurrentRound`、`CurrentPhase`、`PhaseProgress`、`PhaseTotal`、`ExtractedTermCount`、`DynamicPatternCount`
- **`RecordError` 调用点：** `LlmTranslationService.RecordError` 被以下位置调用：内部（`TranslateAsync` 提前退出）、外部（`TranslateEndpoints.cs` catch 块）— 签名更改必须同时更新两处
- **字体生成模型：** 在 `Models/FontGeneration.cs` ↔ `src/api/types.ts` 之间同步 `CharacterSetConfig`/`FontGenerationReport`/`CharsetInfo`；在 `TmpFontGeneratorService` ↔ `FontGeneratorView.vue` phaseLabels 之间同步阶段值；在 `BuiltinCharsets` ↔ 前端 checkbox 值之间同步字符集 ID
- **字体替换模型：** 在 `Models/FontReplacement.cs` ↔ `src/api/types.ts` ↔ `FontReplacementView.vue` 之间同步 `FontInfo`/`FontReplacementStatus`
- **TMP 字体模型：** 在 `Endpoints/GameEndpoints.cs` ↔ `src/api/types.ts` 之间同步 `TmpFontStatus`；API 方法在 `src/api/games.ts` 中
- 前端状态生命周期：`GameDetailView.loadGame()` 在 `isInstalled=false` 时重置状态
- 安装 store 的 `operationType` 跟踪安装与卸载
- **`[LLMTranslate]` INI 配置：** 在 3 处写入 — `POST /ai-endpoint`、`InstallOrchestrator`、DLL `Initialize`
- **更新状态模型：** `Models/UpdateInfo.cs` → `src/api/types.ts` → `src/stores/update.ts` → `SettingsView.vue`
- **AppSettings.ReceivePreReleaseUpdates：** 在 4 处同步：`Models/AppSettings.cs`, `src/api/types.ts`, `SettingsView.vue`（设置默认值 + NSwitch）
- **添加 LocalLlmSettings 用户配置字段：** 在 3 处同步：`Models/LocalLlmSettings.cs`, `Endpoints/LocalLlmEndpoints.cs`（`UpdateLocalLlmSettingsRequest`）, `src/api/types.ts`；UI 在 `LocalAiPanel.vue`；API 在 `src/api/games.ts`（`localLlmApi.saveSettings`）；`UpdateUserSettingsAsync` 合并字段
- **添加 BuiltInModelInfo 字段：** 在 2 处��步：`Models/LocalLlmSettings.cs`, `src/api/types.ts`；在 `LocalAiPanel.vue` 中显示
- **添加 BuiltInModelCatalog 条目：** 仅需编辑 `Models/LocalLlmSettings.cs` 的 `BuiltInModelCatalog.Models` 列表——前端自动渲染新条目，无需任何前端修改；`ModelScopeRepo`/`ModelScopeFile` 可为 null（不是所有 HuggingFace 模型都有 ModelScope 镜像）
- **LocalLlmDownloadProgress 字段：** 在 2 处同步：`Models/LocalLlmSettings.cs`, `src/api/types.ts`；在 `LocalAiPanel.vue` 中显示
- **VersionInfo：** 在 2 处同步：`Endpoints/SettingsEndpoints.cs`（record）, `src/api/types.ts`；包含 `Version`、`Edition`
- **LlamaDownloadProgress 字段：** 在 2 处同步：`Models/LocalLlmSettings.cs`, `src/api/types.ts`
- **LlamaStatus 字段：** 在 3 处同步：`Models/LocalLlmSettings.cs`, `src/api/types.ts`, `LocalAiPanel.vue`（更新横幅）；包含 `InstalledVersion`、`NeedsUpdate`（仅 non-Full 版本为 true）
- **Edition 构建参数：** 在 2 处同步：`build.ps1`（`-Edition` 参数）和 `build.yml`（editions 数组）——必须手动同步
- **清单命名约定：** `manifest-{rid}.json`（full）、`manifest-{rid}-no-llama.json`、`manifest-{rid}-lite.json`
- **`bundled/llama/` 删除列表排除：** `UpdateService` 中的关键不变量——非 full 版本不得删除用户下载的 llama 文件
- **DataPathInfo：** 在 2 处同步：`Endpoints/SettingsEndpoints.cs`（record）, `src/api/types.ts`
- **FileExplorer 模型：** 在 2 处同步：`Models/FileExplorer.cs`, `src/api/types.ts`；包含 `QuickAccessEntry`、`ReadTextResponse`；`UploadFromPathRequest` 为所有 path-based 上传端点共享的请求记录
- **BatchAddResult/BatchSkippedItem 字段：** 在 2 处同步：`Models/BatchAddModels.cs`, `src/api/types.ts`；`GameEndpoints.cs` 中 `DetectAndAddAsync` 是 `add-with-detection` 和 `batch-add` 的共享检测+添加逻辑
- **添加 UnityGameInfo 字段：** 在 2 处同步：`Models/UnityGameInfo.cs`, `src/api/types.ts`；安装编排器 Step 1 始终重新检测（`DetectAsync`），新字段自动生效无需额外处理
- **添加 AppDataPaths 目录：** 还要更新 `SettingsEndpoints.cs` `/export` 端点中的导出排除列表，如果新目录包含大型/可重新生成/机器特定的数据；`translation-memory/`、`dynamic-patterns/`、`term-candidates/` 被排除在导出之外（可重新生成）
- **PluginHealthReport/HealthCheckItem/HealthCheckDetail 字段：** 在 2 处同步：`Models/PluginHealth.cs`, `src/api/types.ts`；在 `PluginHealthCard.vue` 中显示
- **BepInExPlugin 字段：** 在 2 处同步：`Models/BepInExPlugin.cs`, `src/api/types.ts`；API 方法在 `src/api/games.ts`；在 `PluginManagerView.vue` 中显示
- **日志级别同步点：** `Program.cs` `AddFilter` + `FileLoggerProvider` 构造函数 `minLevel` + 前端 `LogView.vue` `selectedLevels` + `levelDefs` — 修改日志级别阈值时四处必须一致
- **`ApplicationStopping` 回调同步点：** `Program.cs` 注册两个关闭回调：`SystemTrayService.HideUIImmediately()`（立即隐藏 UI）+ `TranslationMemoryService.FlushAllDirtyWithTimeout(3s)`（刷新脏 TM）；添加新的需要关闭时清理的服务应在此处添加回调
- **更新 llama.cpp 版本：** 在 4 处同步：`build.ps1`（`$llamaTag`）、`.github/workflows/build.yml`（`$llamaTag`）、`LocalLlmService.cs`（`LlamaVersion`）、根 `CLAUDE.md`（内置资源描述）；`build.ps1` 和 `build.yml` 在下载后自动写入 `bundled/llama/version.txt`（无需手动同步）；同时更新 CUDA 资产 pattern（build.ps1 + build.yml）和用户文档（README.md）；资产命名需通过 `gh api repos/ggml-org/llama.cpp/releases/tags/{tag}` 确认实际 CUDA 版本号（如 `cuda-13.1` 而非 `cuda-13`）

### 构建

- `dotnet build` 自动运行前端；跳过使用 `-p:SkipFrontendBuild=true`
- `build.ps1`：本地构建 — 下载内置资源 → 提取 XUnity 引用 DLL → 更新 classdata.tpk → 前端 → TranslatorEndpoint → Updater (AOT) → 发布到 `Release/win-x64/`；`-SkipDownload` 跳过资源下载；无清单/组件 ZIP、无 MSI（CI `build.yml` 独立处理完整发布构建）；清理：删除 `web.config`、`*.pdb`、`*.staticwebassets.endpoints.json`
- **版本号：** `build.ps1` 通过 `-p:InformationalVersion` 自动生成 `4.3.{YYYYMMDDHHmm}`（CI 使用 `4.3.` 前缀）；**必须使用 `InformationalVersion` 而非 `Version`** — `Version` 设置 `AssemblyVersion`（UInt16 最大 65535），时间戳会溢出
- **多文件发布：** 已移除 `PublishSingleFile`；已移除 `ExcludeFromSingleFile` target；LibCpp2IL.dll 在多文件模式下自然工作
- **附属程序集：** `SatelliteResourceLanguages=en` 从发布输出中剥离所有语言文件夹（cs/de/fr/ja/ko 等）；WinForms 附属资源未使用（UI 是 Vue，原生对话框已移除）
- **MSB3277 屏蔽：** `<NoWarn>MSB3277</NoWarn>` — WebView2.Wpf.dll 引用 WindowsBase 5.0.0.0 与 .NET 10 的 4.0.0.0 冲突；.NET Core 运行时自动统一，警告无害
- **自托管字体：** `XUnityToolkit-Vue/public/fonts/` 存放 Lexend、DM Sans、JetBrains Mono 的 WOFF2 文件；`src/assets/fonts.css` 声明 `@font-face`（由 `main.css` 导入）；**绝不引入 Google Fonts CDN**——桌面应用不应依赖外部 CDN，且 `fonts.googleapis.com` 在国内被墙会阻塞渲染
- **Vite vendor 分包：** `vite.config.ts` 中 `rolldownOptions.output.codeSplitting.groups` 将 vue/naive-ui/signalr 拆分为独立 chunk（使用 `test` 正则匹配 `node_modules` 路径）；`chunkSizeWarningLimit: 750`；`onwarn` 过滤 SignalR ESM `/*#__PURE__*/` 注释警告；Vite 8 使用 Rolldown 引擎（替代 Rollup + esbuild），不要使用 `rollupOptions` 或 `manualChunks` 对象形式（已移除）
- **数据路径：** 始终为 `%AppData%\XUnityToolkit\`（无便携模式）；`AppData:Root` 配置键允许为开发/测试覆盖
- **AppDataPaths 配置回写：** 在 `Program.cs` 中修改 `appDataRoot` 来源后，**必须**执行 `builder.Configuration["AppData:Root"] = appDataRoot` — 否则 `AppDataPaths`（通过 DI 读取 `IConfiguration`）不会获取新值
- **内置资源：** `bundled/{bepinex5,bepinex6,xunity,llama}/` — BepInEx/XUnity 通过 API 自动检测最新版本；llama.cpp 固定在 b8580（更改 build.ps1/build.yml 中的 `$llamaTag`）；CUDA 13.1；发布后复制
- **TMP 字体：** `bundled/fonts/`（在 git 中跟踪）；发布构建使用 `build.ps1` 发布后 `Copy-Item`
- **TMP 字体兼容性：** 不是所有游戏都使用 TextMeshPro；`UnityGameInfo.HasTextMeshPro`（`bool?`：`true`=有TMP DLL, `false`=Mono 无 TMP, `null`=IL2CPP 或未知）控制安装条件；Mono 游戏通过检查 `{GameName}_Data/Managed/` 中含 `TextMeshPro` 的 DLL 判断；XUnity 日志 `"Cannot use fallback font because it is not supported in this version"` 标志 TMP 不可用
- **PowerShell ZIP：** 不要使用 `Compress-Archive`（在 PowerShell 7.5.5 上已损坏 — 模块加载错误）；使用 `[System.IO.Compression.ZipFile]` 代替
- **WiX MSI 多版本构建陷阱：** 在循环中为多个 edition 构建 MSI 时，必须在每次迭代前清理 `Installer/obj/{Platform}/{Configuration}/`（不仅是 `bin/`）——WiX SDK 在 `obj/` 中缓存 `.wixobj`、`.cab` 和中间 MSI，增量构建会复用上一版本的产物导致所有 MSI 内容相同；只清理 `obj/{Platform}/{Configuration}/` 而非整个 `obj/`，以保留 NuGet restore 缓存
- **更新清单：** 每次发布生成 `manifest-{rid}.json`，包含 SHA256 哈希；组件 ZIP：`app-{rid}.zip`、`wwwroot.zip`、`bundled-llama.zip`、`bundled-fonts.zip`、`bundled-plugins.zip`、`bundled-misc.zip`
- 构建前停止后端：`taskkill //f //im XUnityToolkit-WebUI.exe`
- 默认系统提示词：中文，7 条规则；`{from}`/`{to}` 会被替换；`{0}` 等为字面量
- 日志：`{dataRoot}/logs/XUnityToolkit_YYYY-MM-DD_HH-mm-ss.log`；500 条环形缓冲区 + `LogBroadcast`
- 截图清理：测试后删除项目根目录的 `*.png` 和 `.playwright-mcp/`
- **UI 预览：** 使用后端端口 51821 预览 UI（后端同时提供静态前端文件）；5173 是 Vite dev server（需代理 API），不适合独立预览完整功能
- **BepInEx 插件安装双路径：** `POST .../plugins/install`（本地路径，前端文件浏览器选择）用于本机场景；`POST .../plugins/upload`（multipart 上传）用于远程访问场景；目前 upload 的 API 客户端已实现（`bepinexPluginApi.upload`）但前端无 UI 入口

### 更新器与 MSI 安装程序

- **更新器：** `Updater/Updater.csproj` (net10.0, PublishAot)；仅 win-x64；`--data-dir` CLI 参数将日志/备份路径指向 `paths.Root`
- **更新器 AOT P/Invoke：** `DllImport`/`const`/`static readonly` 不能在顶级语句中使用 — 必须包装在 `partial class Program` 中；不能使用 `Microsoft.Win32.Registry` — 必须直接 P/Invoke advapi32.dll
- **更新器 AOT 构建环境：** `PublishAot` 需要 Visual Studio C++ 工具链（`vswhere.exe` + MSVC linker）；仅测试逻辑时可用 `dotnet publish -p:PublishAot=false --self-contained`
- **更新器测试方法：** 创建模拟目录（app dir + staging dir + delete list），不指定 `--pid` 运行 Updater，验证文件被正确替换/删除；注意 `--app-dir` 须测试带和不带尾部 `\` 两种情况
- **MSI 安装程序：** `Installer/Installer.wixproj` (WixToolset.Sdk)；每用户安装到 `%LocalAppData%\Programs\`；`build.ps1` 从发布输出自动生成 `Installer/Generated/HarvestedFiles.wxs`；MSI 版本：`{(YYYY-2024)*12+MM}.{DD}.{HH*60+mm}`（所有段在 MSI 限制内：major<256, minor<256, build<65536）
- **MSI + 更新器共存：** Updater.exe 在增量更新后通过 P/Invoke（AOT 安全）同步 HKCU Uninstall 键中的 `DisplayVersion`/`InstallDate`
- **MSI MajorUpgrade 调度：** `Schedule="afterInstallExecute"` — 新版先安装再卸载旧版；`CleanupAppData` 组件（GUID `D4E5F6A7-...`）保留为空壳（仅 CleanupMarker 注册表值），用于 MSI 组件引用计数防止旧版 `RemoveFolderEx` 在升级时删除数据目录；不要移除该组件或更改其 GUID
- **MSI 注册表键：** 由 MSI 写入（`Components.wxs`），由 `Updater/Program.cs` 读取（MsiProductCode, InstallDir）；`DataPath` 键由 MSI 写入，`CleanupMarker` 键为 `CleanupAppData` 组件 KeyPath；键路径：`HKCU\Software\XUnityToolkit`
- **MSI 卸载可选删除数据：** 卸载时 `UninstallConfirmDlg` 提供 checkbox（默认不勾选）让用户选择是否删除 `%AppData%\XUnityToolkit\`；不勾选则保留（安全默认）；升级时不弹出（`NOT UPGRADINGPRODUCTCODE` 条件）；静默卸载时也可通过 `msiexec /x {ProductCode} DELETE_USER_DATA=1` 触发删除
- **安装程序许可证：** `Installer/License.rtf` 必须与项目根目录 `LICENSE` 匹配（版权持有人、许可类型）

### WiX 注意事项

- **保留属性：** `PublishDir` 和 `SourceDir` 是 MSBuild/WiX SDK 的保留属性，会被静默覆盖；使用自定义名称（例如 `AppPublishDir`）并通过 `-p:AppPublishDir=...` 传递
- **路径解析：** WiX 相对于 `.wixproj` 目录解析 `Source` 路径，不是相对于 CWD；在 `.wixproj` 中使用 `IsPathRooted` 处理绝对和相对路径输入；不要在 WiX 构建中设置 `-p:OutputPath`（会干扰文件解析）
- **每用户 ICE 错误：** 每用户安装（`Scope="perUser"`）触发 ICE 误报；`SuppressValidation=true` 跳过所有 ICE 检查（也加快构建）
- **WixUI 变量覆盖：** `WixUILicenseRtf` 等必须是 `.wxs` 中的 `<WixVariable>`，不是 `.wxl` 中的 `<String>` — 本地化字符串在 WiX 中不适用于 WixUI 变量覆盖
- **MSI 代码页：** MSI 数据库代码页默认为 1252（西方）；MSI 内部字符串（例如 `DowngradeErrorMessage`）中的中文字符会导致 WIX0311 错误；对 MSI 级别的字符串使用英文
- **元素语法：** `<String>` 使用 `Value` 属性（非内部文本）；`<Publish>` 使用 `Condition` 属性（非内部文本）；内部文本已过时
- **DefaultLanguage 输出路径：** 设置 `<DefaultLanguage>zh-CN</DefaultLanguage>` 导致 MSI 输出到文化子文件夹（例如 `bin/x64/Release/zh-CN/`）；`build.ps1` 使用 `-Recurse` 查找 MSI
- **WiX UI 扩展：** `WixToolset.UI.wixext` 附带内置 `zh-CN` 本地化；只需自定义 `.wxl` 用于应用特定字符串（启动复选框文本、许可证路径）；`.wxs` 中的中文文本必须使用 `!(loc.StringId)` 以避免代码页错误
- **构建产物清理：** WiX 在 `OutputPath` 中生成 `.wixpdb` 文件；移动 MSI 后必须清理，否则它们会污染发布 ZIP
- **CI 共享 PowerShell 函数：** 提取到独立 `.ps1` 文件（例如 `Installer/Generate-InstallerWxs.ps1`）；`build.ps1` 和 CI 均通过 `. ./path/to/script.ps1` 引用

### CI/CD

- GitHub Actions；`build.yml`（可复用）、`release.yml`（标签 `v*`）、`dep-check.yml`（每日更新检查 → 自动预发布）
- **CI 中的 .NET 10 preview：** `setup-dotnet@v5` 配合 `dotnet-quality: 'preview'`
- **CI 并行构建：** `build.yml` 使用 PowerShell `Start-Job` 实现步骤内并行 — npm ci 在资源下载期间后台运行；前端/TranslatorEndpoint 并行构建（Updater AOT 作为单独步骤运行 — AOT 发布需要自己的 restore，`--no-restore` 会出错）；主 ZIP 在组件 ZIP 创建期间后台创建
- **CI NuGet 缓存：** `actions/cache@v4` 作用于 `~/.nuget/packages`，以 `hashFiles('**/*.csproj')` 为键；并行构建前显式执行 `dotnet restore`，后续所有 dotnet 命令使用 `--no-restore`
- **CI 组件 ZIP：** 使用 `ZipFileExtensions.CreateEntryFromFile` 直接带路径前缀 — 不要使用 `Copy-Item` 创建临时包装目录（对大型内置资源浪费 I/O）
- **CI 版本跟踪：** `.github/deps.json` 存储最后已知版本；`dep-check.yml` 与上游比较
- **CI 不能调用 `build.ps1`** — `Wait-Exit` 在非交互模式下阻塞；工作流内联复制逻辑；变更必须在 `build.ps1` 和 `build.yml` 之间手动同步
- **CI `Download-Asset` 临时文件：** 必须使用 `.downloading` 临时文件 + `Move-Item` 模式（匹配 `build.ps1` 的 `Download-IfMissing`）以防止中断的下载在重试时被当作缓存
- **dep-check.yml** 仅跟踪 BepInEx 5/6 和 XUnity 版本 — llama.cpp 是固定版本，不自动检查
- **CI 陷阱 — AOT 与 `--no-restore`：** `dotnet publish` 配合 AOT 需要自己的 restore 阶段来填充 `PrivateSdkAssemblies` ItemGroup；独立 `dotnet restore` + `--no-restore` publish 会失败，报 `PrivateSdkAssemblies is required`
- **CI 陷阱 — `$GITHUB_OUTPUT`：** 多行值会破坏格式；使用 heredoc（`key<<EOF`）或 `jq -c` 处理 JSON
- **CI 陷阱 — `gh release create --notes`：** `${{ }}` 中的反引号会变成 bash 命令替换；改用 `--notes-file`
- **CI 陷阱 — tag push detached HEAD：** release workflow 由 tag push 触发时处于 detached HEAD；"Update README download links" 步骤必须先 `git checkout main` 再 commit/push

### 杂项

- **gitignore：** `docs/` 被 gitignore；提交规格/计划文档时使用 `git add -f`
- **gitignore 取反：** `bundled/`（目录模式）阻止子级取反；使用 `bundled/*`（通配符）以允许 `!bundled/fonts/` 和 `!bundled/script-tag-presets.json`
