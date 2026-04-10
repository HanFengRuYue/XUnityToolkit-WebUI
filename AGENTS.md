# AGENTS.md

本文件用于给后续代理提供可直接复用的仓库上下文。当前仓库已经统一改为由根目录 `AGENTS.md` 维护全部项目级、后端级、前端级说明；历史 `CLAUDE.md` 已全部删除，不再作为维护入口。

## 1. 权威文档入口

开始动手前，优先参考这些文件：

- `AGENTS.md`
- `README.md`

规则：

- `AGENTS.md` 是当前唯一维护中的仓库工作手册，所有原 `CLAUDE.md` 内容已并入本文件。
- `README.md` 主要面向产品说明、构建入口和用户侧信息。
- 仓库内不再保留任何 `CLAUDE.md`；如发现重新出现，应视为需要回收进 `AGENTS.md` 的重复文档。
- 若文档与源码冲突，以源码为准，并在改动后同步更新 `AGENTS.md`。

补充说明：

- 仓库内 `.claude/` 目录只有 `scheduled_tasks.lock`，没有额外项目记忆文件。
- 后端与前端原先分散在 `CLAUDE.md`、`XUnityToolkit-WebUI/CLAUDE.md`、`XUnityToolkit-Vue/CLAUDE.md` 的内容，现已统一整理到本文件后半部分的专项章节中。

## 2. 项目概览

XUnityToolkit-WebUI 是一个面向 Unity 游戏汉化/翻译工作流的 Windows 桌面工具，能力包括：

- 一键安装 BepInEx 与 XUnity.AutoTranslator
- 通过 `LLMTranslate.dll` 将游戏文本转发到本地 Web API 做 AI 翻译
- 云端 LLM 与本地 llama.cpp 模式
- 资产提取与预翻译
- TextMesh Pro 字体替换与 SDF 字体生成
- 游戏库管理、封面/图标/背景图管理
- BepInEx 日志分析、插件健康检查
- 更新器与 MSI 安装包

## 3. 仓库结构

顶层关键目录：

- `XUnityToolkit-WebUI/`
  后端主程序。ASP.NET Core Minimal API + WinForms/WebView2 宿主。
- `XUnityToolkit-Vue/`
  前端。Vue 3 + TypeScript + Naive UI + Pinia + Vite。
- `XUnityToolkit-WebUI.Tests/`
  后端测试工程。xUnit，当前覆盖翻译响应解析与运行时占位符保护等关键回归。
- `TranslatorEndpoint/`
  `net35` 的 `LLMTranslate.dll`，供 XUnity.AutoTranslator 调用。
- `Updater/`
  AOT 更新器，负责文件替换、删除、回滚、重启。
- `Installer/`
  WiX 安装器工程。
- `bundled/`
  构建/发布时附带的字体、脚本预设、BepInEx/XUnity/llama 资源。
- `.github/workflows/`
  CI/CD 工作流。

## 4. 技术栈

后端：

- `.NET 10` `net10.0-windows`
- ASP.NET Core Minimal API
- SignalR
- WinForms + WebView2
- AssetsTools.NET
- FreeTypeSharp

前端：

- Vue 3
- TypeScript
- Naive UI
- Pinia
- Vite 8

其他子项目：

- `TranslatorEndpoint`: `net35`, C# 7.3
- `Updater`: `net10.0`, `PublishAot=true`
- `Installer`: WixToolset v6（当前工程为 `WixToolset.Sdk/6.0.2`）

## 5. 常用命令

后端构建：

```bash
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true
dotnet run --project XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj
```

前端：

```bash
cd XUnityToolkit-Vue
npm run dev
npm run build
npx vue-tsc --build
```

测试：

```bash
dotnet test
```

翻译端点：

```bash
dotnet build TranslatorEndpoint/TranslatorEndpoint.csproj -c Release
```

本地完整构建：

```bash
.\build.ps1
.\build.ps1 -SkipDownload
```

重要说明：

- `XUnityToolkit-WebUI.csproj` 默认会在构建前自动执行前端 `npm install` + `npm run build`。
- 前端开发代理到 `http://127.0.0.1:51821`，不要改成 `localhost`。
- 完整 UI 预览优先看后端端口 `51821`，因为它同时承载静态前端和 API。
- 发布产物首页可用性现在由 `build.ps1` 与 `.github/workflows/build.yml` 中的 `Test-FrontendSmoke` 双重守卫；它们会从错误工作目录启动 EXE，并要求 `/` 与 `/api/settings/version` 同时返回 `200`。

## 6. 运行时架构

后端启动入口：

- `XUnityToolkit-WebUI/Program.cs`

主要职责：

- 读取 `settings.json` 中 `aiTranslation.port`，动态决定监听端口，默认 `51821`
- 强制绑定 `http://127.0.0.1:{port}`
- `ContentRootPath` 与 `WebRootPath` 必须固定到 `AppContext.BaseDirectory`，不要依赖当前工作目录；否则更新器、安装器或外部启动器从错误目录拉起时会出现首页 404 但 API 仍可访问
- 注册各类命名 `HttpClient`
- 注册所有核心服务为单例
- 配置 SignalR
- 提供静态文件与 SPA fallback
- 启动日志必须记录 `CurrentDirectory`、`BaseDirectory`、`ContentRoot`、`WebRoot` 以及 `wwwroot/index.html` 是否存在；若入口文件缺失应记录 `Critical`
- 注册全部 Minimal API 端点
- 在 `ApplicationStopping` 时立即隐藏 UI，并刷新脏的翻译记忆
- 在 `ApplicationStarted` 时异步初始化 AI 翻译状态，并自动检查更新

前端入口：

- `XUnityToolkit-Vue/src/main.ts`
- `XUnityToolkit-Vue/src/App.vue`
- `XUnityToolkit-Vue/src/components/layout/AppShell.vue`

前端骨架：

- `RouterView + KeepAlive + Pinia`
- 顶层页面：游戏库、AI 翻译、字体生成、运行日志、设置
- 游戏子页面：配置编辑、资产提取、翻译编辑、术语编辑、字体替换、BepInEx 日志、插件管理

实时通信：

- 单个 SignalR Hub：`InstallProgressHub`
- 关键分组：
  - `game-{id}`
  - `ai-translation`
  - `logs`
  - `pre-translation-{gameId}`
  - `local-llm`
  - `font-replacement-{gameId}`
  - `font-generation`
  - `update`

## 7. 运行时数据布局

运行时数据根目录：

- 默认：`%AppData%\XUnityToolkit`
- 可通过配置键 `AppData:Root` 覆盖
- 主要路径集中定义在 `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs`
- 更新暂存目录 `update-staging/` 当前由 `XUnityToolkit-WebUI/Services/UpdateService.cs` 直接管理

关键文件/目录：

- `library.json`
- `settings.json`
- `local-llm-settings.json`
- `glossaries/`
- `script-tags/`
- `translation-memory/`
- `dynamic-patterns/`
- `term-candidates/`
- `cache/covers`
- `cache/icons`
- `cache/backgrounds`
- `cache/extracted-texts`
- `cache/pre-translation-regex`
- `models/`
- `llama/`
- `generated-fonts/`
- `font-backups/`
- `custom-fonts/`
  当前字体替换自定义源按 `custom-fonts/<gameId>/ttf/` 与 `custom-fonts/<gameId>/tmp/` 分目录管理
- `backups/`
- `logs/`
- `update-staging/`

安全相关：

- API Key 与 SteamGridDB Key 使用 DPAPI 加密
- JSON 原子写入统一走 `FileHelper.WriteJsonAtomicAsync`
- 路径拼接统一优先使用 `PathSecurity.SafeJoin`
- 外部 URL 校验使用 `PathSecurity.ValidateExternalUrl`

## 8. 后端模块地图

高频关键服务：

- `GameLibraryService`
  游戏库增删改查，落盘到 `library.json`
- `AppSettingsService`
  设置缓存、DPAPI 加解密、读改写
- `ConfigurationService`
  读写 `AutoTranslatorConfig.ini`
- `UnityDetectionService`
  检测 Unity 后端、架构、可执行文件、TextMeshPro 支持
- `InstallOrchestrator`
  安装/卸载编排
- `LlmTranslationService`
  AI 翻译总入口，负责并发、统计、术语、TM、端点调度
- `TranslationMemoryService`
  每游戏翻译记忆，精确/模式/模糊匹配
- `PreTranslationService`
  资产文本批量预翻译、术语提取、多轮流程
- `LocalLlmService`
  管理 llama-server、GPU 检测、模型下载、llama 二进制下载
- `AssetExtractionService`
  资产提取
- `FontReplacementService`
  TMP/TTF 字体扫描与替换
- `TmpFontGeneratorService`
  SDF 字体生成
- `UpdateService`
  更新检查、下载、应用
- `SystemTrayService`
  托盘、窗口显示/隐藏、通知

关键端点目录：

- `XUnityToolkit-WebUI/Endpoints/`

高频端点文件：

- `GameEndpoints.cs`
- `SettingsEndpoints.cs`
- `TranslateEndpoints.cs`
- `FontGenerationEndpoints.cs`
- `FontReplacementEndpoints.cs`
- `ImageEndpoints.cs`
- `AssetEndpoints.cs`
- `LocalLlmEndpoints.cs`
- `UpdateEndpoints.cs`

## 9. 前端模块地图

核心骨架：

- `src/components/layout/AppShell.vue`
- `src/router/index.ts`
- `src/api/client.ts`
- `src/api/games.ts`
- `src/api/types.ts`

高频页面：

- `src/views/GameDetailView.vue`
- `src/views/AiTranslationView.vue`
- `src/views/SettingsView.vue`
- `src/views/AssetExtractionView.vue`
- `src/views/FontGeneratorView.vue`
- `src/views/TermEditorView.vue`
- `src/views/TranslationEditorView.vue`
- `src/views/LibraryView.vue`

高频组件：

- `src/components/settings/LocalAiPanel.vue`
- `src/components/settings/AiTranslationCard.vue`
- `src/components/config/ConfigPanel.vue`
- `src/components/common/FileExplorerModal.vue`

核心 store：

- `src/stores/games.ts`
- `src/stores/theme.ts`
- `src/stores/sidebar.ts`
- `src/stores/install.ts`
- `src/stores/update.ts`

核心 composable：

- `src/composables/useAddGameFlow.ts`
- `src/composables/useAutoSave.ts`
- `src/composables/useFileExplorer.ts`
- `src/composables/useWindowControls.ts`

## 10. 翻译链路速记

在线翻译主链路：

1. 游戏内 XUnity.AutoTranslator 捕获文本
2. `LLMTranslate.dll` 将文本发到 `POST /api/translate`
3. `LlmTranslationService` 处理术语、TM、端点选择、并发控制
4. 结果返回 DLL，再回显到游戏

重要事实：

- `POST /api/translate` 是给 DLL 直接调用的，返回格式不是常规 `ApiResult<T>`
- `LLMTranslate.dll` 目标框架是 `net35`
- `LLMTranslate.dll` 通过 `[LLMTranslate]` INI 区段读取 `ToolkitUrl`、`GameId` 等配置
- `Program.cs` 必须使用 `127.0.0.1`，不要使用 `localhost`
- `LlmTranslationService.TranslateDetailedAsync(...)` 是翻译主链路的权威实现；`TranslateAsync(...)` 只是返回 `Translations` 的轻包装。凡是需要决定“是否允许写入 TM / 术语提取 / 运行时上下文缓存”的调用点，都必须使用详细结果而不是只拿字符串数组
- LLM 返回解析顺序当前固定为：剥离 `<think>` / 代码块包装 → JSON 数组 → 单条 JSON 字符串（仅单条场景）→ 单条纯文本候选（仅单条场景）；批量场景不再接受非结构化原始输出作为译文
- `POST /api/translate` 与 `PreTranslationService` 现在都会过滤 `Persistable == false` 的结果：这些结果可以回显给调用方，但不能进入自动术语提取、运行时上下文缓存或翻译记忆

多阶段翻译：

- Phase 0：TM 查找
- Phase 1：自然翻译
- Phase 2：术语/DNT 占位符替换
- Phase 3：强制修正
- 在 Phase 1 / Phase 2 进入 LLM 之前，运行时 `SPECIAL_*` 占位符会先替换成内部 `{{XU_RT_n}}` 占位符；LLM 返回后会做宽松恢复，但最终必须逐字回到源文本中的原始 token
- `SPECIAL_*` 运行时占位符同时覆盖半角 `[SPECIAL_01]` 与全角 `【SPECIAL_01】`；括号样式、大小写、数量和位置都必须与输入完全一致。任一环节校验失败时，整段安全回退原文
- Phase 0 的 TM 命中也必须经过同样的运行时占位符 round-trip 校验；历史坏缓存命中要视为 miss，不能继续复用

翻译记忆：

- 每游戏持久化
- 顺序：精确 → 动态模式 → 模糊
- 写入是同步加入内存，持久化有 5 秒防抖
- 关闭时会强制刷新脏数据

术语系统：

- 统一术语表替代旧的 glossary/do-not-translate 分离设计
- `TermService` 管理每游戏术语
- `TermMatchingService` 做命中与占位符处理
- `TermAuditService` 做阶段合规审查

脚本标签：

- `ScriptTagService` 负责清理与缓存归一化
- 与预翻译缓存、术语和翻译记忆关系紧密，修改时要注意 `NormalizeForCache` 调用点一致性

## 11. 本地 LLM 速记

主服务：

- `XUnityToolkit-WebUI/Services/LocalLlmService.cs`

关键点：

- GPU 检测优先 DXGI，WMI 兜底
- 后端选择逻辑：NVIDIA→CUDA，AMD/Intel→Vulkan，无显卡→CPU
- 本地模式强制更保守的并发与批处理
- llama.cpp 版本当前固定为 `b8580`
- 下载模型支持 HuggingFace 与 ModelScope
- llama 二进制和模型是两个概念：
  - llama 运行时二进制：`bundled/llama/` 或用户下载
  - 模型文件：`%AppData%\XUnityToolkit\models`

版本同步点：

- `build.ps1`
- `.github/workflows/build.yml`
- `LocalLlmService.LlamaVersion`
- 文档描述

## 12. 字体与资源处理速记

字体替换：

- `FontReplacementService`
- 支持扫描并替换 TMP_FontAsset
- 也支持扫描 Unity Legacy `Font` 资源；支持直接替换 `dynamicEmbedded` 类型的 TTF/OTF，也支持把依赖 `FontNames` 的 `osFallback` / 名称映射动态字体转为内嵌字体
- 带 `CharacterRects` 的静态图集字体以及模式不明的 Legacy `Font` 会明确标记为不支持；`osFallback` 转内嵌时默认保留原 `FontNames` 作为缺字兜底
- Legacy `Font.m_FontData` 在不少游戏里是 `vector -> Array -> char` 结构，字段 `Value` 可能为 `null`；扫描和替换时判断字节长度要优先看 `m_FontData["Array"].Children.Count`，不能只依赖 `AsByteArray`
- TTF 替换写回后必须立即重新读取目标 `Font` 并验证 `FontDataSize ==` 目标字节长度且 `TtfMode == dynamicEmbedded`；验证失败要视为替换失败并回滚该字体
- 自定义替换源按游戏隔离，目录为 `custom-fonts/<gameId>/ttf/` 与 `custom-fonts/<gameId>/tmp/`，支持累计上传多个源，不再按类型整类覆盖
- 替换请求按逐字体 `sourceId` 传递，允许同一次操作里为不同 TMP / TTF 字体选择不同默认源或自定义源
- 状态接口会返回 `availableSources` 与 `usedSources`；备份清单中的 `ReplacedFontEntry` 会记录 `SourceId` 与 `SourceDisplayName`
- `GET /font-replacement/status` 当前主要返回备份、来源和外部还原状态；其中 `ReplacedFonts` 来自 `manifest.json` 摘要，不是实时重扫结果。需要当前 `ttfMode` / `fontDataSize` 时，以 `POST /font-replacement/scan` 为准
- 替换前会建立备份，恢复依赖备份清单和哈希

字体生成：

- `TmpFontGeneratorService`
- 基于 `FreeTypeSharp + EDT`
- 输出用于 TMP 的 SDF 字体资源

资产提取：

- `AssetExtractionService`
- 使用 AssetsTools.NET
- 目标是提取可翻译文本，供预翻译与缓存生成

## 13. 更新与发布速记

本地构建脚本：

- `build.ps1`

作用：

- 下载 bundled 资源
- 提取 XUnity 依赖 DLL 到 `TranslatorEndpoint/libs`
- 构建前端
- 构建 `LLMTranslate.dll`
- 构建 AOT `Updater`
- 发布到 `Release/win-x64`

CI：

- `.github/workflows/build.yml`
- `.github/workflows/release.yml`
- `.github/workflows/dep-check.yml`

重要事实：

- CI 逻辑与 `build.ps1` 是两份并行维护的实现，改构建流程时必须双改
- CI 不直接调用 `build.ps1`
- 更新器是增量更新的关键组件，含备份、替换、删除、回滚逻辑
- 首页静态资源可用性 smoke check 现在也是双维护实现；若调整启动端口解析、静态资源目录、启动方式或首页健康检查，必须同时更新 `build.ps1` 与 `.github/workflows/build.yml` 中的 `Test-FrontendSmoke`
- MSI 由 WiX 生成，且不同 edition 构建时必须注意清理 `Installer/obj/...`

## 14. 关键约束与不变量

通用：

- 返回 `ApiResult` 的端点要用 `Results.Ok(ApiResult<T>.Ok(...))`
- 输入验证失败时应返回 `BadRequest`，不要返回 200 + fail body
- 新增每游戏数据文件时，必须同步补充删除逻辑与缓存清理
- 新增设置字段时，要检查后端默认值、TS 类型、前端默认值、保存逻辑是否同步
- 提交到 Git 与 GitHub 的提交标题、提交正文、PR 标题、PR 描述、评审回复等协作文案统一使用中文，保持与仓库既有历史风格一致；仅在引用外部专有名词、接口名或命令时保留必要英文
- Git 提交标题默认使用 `type: 中文摘要` 单行格式，`type` 统一使用仓库和更新面板已识别的英文小写类型：`feat`、`fix`、`docs`、`refactor`、`perf`、`ci`、`chore`、`style`、`test`；不要写成中文类型、emoji、无类型标题、`[标签] 标题`、`type(scope):` 或其他自造前缀
- Git 提交标题中的冒号必须使用半角 `:` 且后跟一个空格；摘要要直接描述本次主要改动结果，保持中文、具体、可读，避免“更新一下”“修一点问题”“若干调整”这类空泛表述
- 工具箱内更新内容当前由 `.github/workflows/build.yml` 通过 `git log --pretty=format:"- %s (\`%h\`)" --no-merges` 生成，再由 `XUnityToolkit-Vue/src/views/SettingsView.vue` 按 `- type: description (hash)` 解析；想让更新面板正确显示类型徽标和文本时，提交标题必须遵循上述格式，且不要把关键变更信息只写进提交正文或 merge commit 标题
- Git 提交正文用于补充背景、约束、风险与验证结论，统一中文书写；正文应围绕“为什么改 / 改了什么 / 需要注意什么”展开，不要套英文模板、AI 套话或与实际改动不符的泛化总结
- 版本相关提交沿用仓库既有风格：正式发布使用 `feat: 发布 vX.Y`，纯版本号抬升使用 `chore: 版本号提升至 vX.Y`

后端：

- 不要从头重建 `AutoTranslatorConfig.ini`，而是使用补丁式修改
- `AppSettingsService.GetAsync()` 返回缓存对象，不要原地乱改，优先 `UpdateAsync`/`SaveAsync`
- 热路径避免磁盘 I/O，尤其 `POST /api/translate`
- `GameId` 用作文件路径时必须校验 GUID
- 用户提供的 URL 在真正请求前必须走 SSRF 校验
- `Program.cs` 中首页静态资源根目录必须锚定到 `AppContext.BaseDirectory`，不要让 `wwwroot` 跟随 `Environment.CurrentDirectory`

前端：

- 顶层缓存页面依赖 `KeepAlive`，涉及 SignalR、定时器、window 监听器时必须同时处理 `onActivated`/`onDeactivated`/`onBeforeUnmount`
- 不要直接从页面修改 store 的内部状态，优先走 actions
- 自动保存页面切换/刷新数据时，遵循：
  `disable -> load/assign -> nextTick -> enable`
- UI 改动后，至少执行：
  - `npx vue-tsc --build`
  - `npm run build`
- 若是明显视觉改动，优先补做一次实际 UI 验证

## 15. 高频同步点

以下改动非常容易漏：

- `InstallStep`
  需要同步后端模型、TS 类型、安装进度 UI、状态文案映射
- `AppSettings`
  需要同步：
  - `Models/AppSettings.cs`
  - `src/api/types.ts`
  - 相关 store 的读写
  - `SettingsView.vue`
- `AiTranslationSettings`
  需要同步：
  - C# 模型
  - TS 类型
  - AI 翻译页
  - 设置页默认值
  - 后端 `Math.Clamp`
  - 默认系统提示词文案；当前后端默认值与 `XUnityToolkit-Vue/src/constants/prompts.ts` 必须同时要求 `[SPECIAL_01]` / `【SPECIAL_01】` 按输入原样保留
- `LocalLlmSettings`
  需要同步：
  - C# 模型
  - `LocalLlmEndpoints`
  - TS 类型
  - `LocalAiPanel.vue`
- 新 per-game 缓存/目录
  需要同步：
  - `AppDataPaths.cs`
  - 删除逻辑
  - 导出排除列表
  - 清缓存逻辑

## 16. 推荐阅读顺序

当需要接手某一块时，建议优先看：

安装与游戏接入：

- `XUnityToolkit-WebUI/Endpoints/GameEndpoints.cs`
- `XUnityToolkit-WebUI/Services/InstallOrchestrator.cs`
- `XUnityToolkit-WebUI/Services/UnityDetectionService.cs`
- `XUnityToolkit-WebUI/Services/BepInExInstallerService.cs`
- `XUnityToolkit-WebUI/Services/XUnityInstallerService.cs`

在线翻译与预翻译：

- `XUnityToolkit-WebUI/Endpoints/TranslateEndpoints.cs`
- `XUnityToolkit-WebUI/Services/LlmTranslationService.cs`
- `XUnityToolkit-WebUI/Services/TranslationMemoryService.cs`
- `XUnityToolkit-WebUI/Services/PreTranslationService.cs`
- `XUnityToolkit-WebUI/Services/TermService.cs`

本地模型：

- `XUnityToolkit-WebUI/Endpoints/LocalLlmEndpoints.cs`
- `XUnityToolkit-WebUI/Services/LocalLlmService.cs`
- `XUnityToolkit-Vue/src/components/settings/LocalAiPanel.vue`

字体相关：

- `XUnityToolkit-WebUI/Endpoints/FontReplacementEndpoints.cs`
- `XUnityToolkit-WebUI/Endpoints/FontGenerationEndpoints.cs`
- `XUnityToolkit-WebUI/Services/FontReplacementService.cs`
- `XUnityToolkit-WebUI/Services/TmpFontGeneratorService.cs`

前端主交互：

- `XUnityToolkit-Vue/src/components/layout/AppShell.vue`
- `XUnityToolkit-Vue/src/views/GameDetailView.vue`
- `XUnityToolkit-Vue/src/views/AiTranslationView.vue`
- `XUnityToolkit-Vue/src/views/SettingsView.vue`
- `XUnityToolkit-Vue/src/api/games.ts`
- `XUnityToolkit-Vue/src/api/types.ts`

构建/发布/更新：

- `build.ps1`
- `.github/workflows/build.yml`
- `Updater/Program.cs`
- `Installer/Package.wxs`

## 17. 本次仓库检查结论

截至本次整理时，已确认：

- 仓库当前存在完整的项目级说明文档，且与源码主干大体一致
- 根目录 `AGENTS.md` 已成为统一维护入口，原三份 `CLAUDE.md` 的内容已完成合并并删除旧文件
- `.claude/` 中没有额外项目说明
- 构建链路、前后端入口、运行时数据目录、更新器、翻译端点均已做过静态核对

## 18. 维护建议

后续如有下列变更，请同步更新本文件：

- 新增顶层子项目
- 调整构建/发布流程
- 修改运行时数据目录布局
- 大幅重构翻译链路、本地 LLM、字体链路、更新链路
- 引入新的高频同步点或新的全局不变量

## 19. 接口矩阵补充

以下接口族原先分散记录在历史 `CLAUDE.md` 中，现统一收敛到此：

- 游戏管理：`GET/POST /api/games`、`GET/DELETE /api/games/{id}`、`POST /api/games/add-with-detection`、`POST /api/games/batch-add`、`PUT /api/games/{id}`、`POST /api/games/{id}/detect`、`POST /api/games/{id}/open-folder`、`POST /api/games/{id}/launch`
- TMP 字体：`GET/POST/DELETE /api/games/{id}/tmp-font`
- 安装与状态：`POST /api/games/{id}/install`、`DELETE /api/games/{id}/install`、`GET /api/games/{id}/status`、`POST /api/games/{id}/cancel`
- 图标、封面、背景：均提供 `upload`、`*-from-path`、SteamGridDB、网页搜索、选择、删除等配套端点
- 配置：`GET/PUT /api/games/{id}/config`、`GET/PUT /api/games/{id}/config/raw`
- 应用设置：`GET/PUT /api/settings`、`GET /api/settings/version`、`POST /api/settings/reset`、`POST /api/settings/export`、`POST /api/settings/import`、`POST /api/settings/import-from-path`、`POST /api/settings/open-data-folder`
- 文件浏览器：`GET /api/filesystem/drives`、`GET /api/filesystem/quick-access`、`POST /api/filesystem/list`、`POST /api/filesystem/read-text`
- AI 翻译：`POST /api/translate`、`GET /api/translate/stats`、`GET /api/translate/cache-stats`、`POST /api/translate/test`、`GET /api/translate/ping`
- AI 控制与模型：`POST /api/ai/toggle`、`GET /api/ai/models`、`GET /api/ai/extraction/stats`
- 本地 LLM：`GET/PUT /api/local-llm/settings`、`GET /api/local-llm/status`、`GET /api/local-llm/gpus`、`POST /api/local-llm/gpus/refresh`、`GET /api/local-llm/catalog`、`GET /api/local-llm/llama-status`、`POST /api/local-llm/test`、`POST /api/local-llm/start`、`POST /api/local-llm/stop`、下载/暂停/取消模型、下载/取消 llama 运行时
- AI 端点、术语、描述：`/api/games/{id}/ai-endpoint`、`/api/games/{id}/terms`、`/api/games/{id}/description`
- 兼容层：`/api/games/{id}/glossary`、`/api/games/{id}/do-not-translate` 保留兼容旧调用，但底层统一走 `TermService`
- 翻译记忆与动态模式：`/api/games/{id}/translation-memory`、`/api/games/{id}/dynamic-patterns`、`/api/games/{id}/term-candidates`
- 脚本标签：`GET /api/script-tag-presets`、`GET/PUT /api/games/{id}/script-tags`
- 资源提取与预翻译：`POST /api/games/{id}/extract-assets`、`GET/DELETE /api/games/{id}/extracted-texts`、`POST /api/games/{id}/pre-translate`、`GET /api/games/{id}/pre-translate/status`、`POST /api/games/{id}/pre-translate/cancel`、`GET/PUT /api/games/{id}/pre-translate/regex`
- 翻译编辑器：`GET/PUT /api/games/{id}/translation-editor`、`POST /api/games/{id}/translation-editor/import`、`GET /api/games/{id}/translation-editor/export`
- 字体替换：`POST /api/games/{id}/font-replacement/scan`、`POST /api/games/{id}/font-replacement/replace`、`POST /api/games/{id}/font-replacement/restore`、`GET /api/games/{id}/font-replacement/status`、`POST /api/games/{id}/font-replacement/upload`、`POST /api/games/{id}/font-replacement/upload-from-path`、`POST /api/games/{id}/font-replacement/cancel`、`DELETE /api/games/{id}/font-replacement/custom-fonts/{sourceId}`
- 字体替换上传端点现在要求显式区分 `kind={ttf|tmp}`；状态端点会返回默认源/自定义源列表与已使用源摘要；替换请求中的 `fonts[]` 需要携带逐字体 `sourceId`
- `POST /api/games/{id}/font-replacement/scan` 是字体当前资源状态的权威来源；`GET /api/games/{id}/font-replacement/status` 主要基于 `manifest.json` 汇总替换状态，不返回实时重扫后的 `ttfMode` / `fontDataSize`
- 字体生成：上传、生成、状态、取消、下载、历史、删除、安装 TMP 字体、字符集预览/上传、报告查询均由 `/api/font-generation/*` 提供
- BepInEx 日志与健康：`/api/games/{id}/bepinex-log`、`/api/games/{id}/health-check`
- 插件管理与插件包：`/api/games/{id}/plugins`、`/api/games/{id}/plugin-package/export`、`/api/games/{id}/plugin-package/import`
- 日志与更新：`/api/logs*`、`/api/update/*`
- 所有 `multipart/form-data` 上传端点都必须显式 `.DisableAntiforgery()`
- 所有 `*-from-path` 端点都必须与对应 multipart 端点保持相同业务校验与副作用，不允许只做“简化版实现”
- `POST /api/translate` 直接服务于 `LLMTranslate.dll`，不是 `ApiResult<T>`，前端若直接调用必须使用原始 `fetch` 处理响应
- 常规 JSON 端点统一使用 `Results.Ok(ApiResult<T>.Ok(...))` 或 `Results.BadRequest(ApiResult.Fail(...))`，不要直接返回裸对象

## 20. 同步点与模型补充

- `InstallStep`、`UpdateInfo`、`VersionInfo`、`DataPathInfo`、`BatchAddResult`、`UnityGameInfo`、`FileExplorer`、`FontReplacement`、`FontGeneration`、`PluginHealth`、`BepInExPlugin`、`LocalLlmSettings`、`BuiltInModelInfo`、`LlamaStatus` 等模型，新增字段时都必须同时同步 C# 模型、TS 类型、相关 API、对应前端页面
- 字体替换链路改动时，要一起核对 `FontReplacementRequest.Fonts[].SourceId`、`ReplacementSource` / `ReplacementSourceSet`、`FontReplacementStatus.AvailableSources` / `UsedSources`、`ReplacedFontEntry.SourceId` / `SourceDisplayName`，并同步 `FontReplacement.cs`、`src/api/types.ts`、`FontReplacementView.vue`、`FontReplacementEndpoints.cs`、`FontReplacementService.cs`
- 涉及 Legacy `Font` 的 TTF 分析或写回时，还要一起核对 `AnalyzeTtfFont`、`GetByteArrayLength`、`SetByteArrayContents`、写后重读验证日志、`GetStatusAsync` 和前端状态文案；`scan` 与 `status` 的语义不要混用
- `SettingsView.vue` 的默认 `AppSettings`、`AiTranslationView.vue` 的 `DEFAULT_AI_TRANSLATION`、后端 `AppSettings`/`AiTranslationSettings` 默认值必须保持一致
- 数值型设置新增字段时，要同步后端的 `Math.Clamp` 逻辑，否则前端与后端会出现边界不一致
- `TermEntry` 的 `Type`/`Category`/`Source`、`ScriptTagRule`/`ScriptTagConfig`、`TranslationStats`/`RecentTranslation`/`TranslationError`、`PreTranslationStatus`/`PreTranslationCacheStats` 都属于容易漏同步的高频模型
- 新增每游戏目录时，除了 `AppDataPaths.cs`，还要同步 `DELETE /api/games/{id}` 清理逻辑、缓存驱逐、设置导出排除列表、必要时的设置导入重建逻辑
- `RecordError`、`NormalizeForCache`、`ApplicationStopping` 回调、日志级别过滤、SignalR 事件名与阶段名，都属于“改一处必须全链路核对”的同步点
- 翻译解析契约、运行时占位符保护与 `Persistable` 过滤属于新的高频同步点；凡是新增翻译调用方或缓存写入点，都要核对是否错误接收了非结构化回退结果
- `build.ps1` 与 `.github/workflows/build.yml` 不是同一实现的不同入口，而是两份并行维护脚本；流程、版本号、资源来源、构建 edition 发生变化时必须双改
- 若变更首页可用性、静态资源目录、启动端口或启动方式，还要同步更新两处 `Test-FrontendSmoke`，确保“错误工作目录启动时 `/` 与 `/api/settings/version` 都返回 `200`”这个回归守卫不失效
- Git 提交标题规范、`.github/workflows/build.yml` 中 `### Changelog` 的生成逻辑，以及 `XUnityToolkit-Vue/src/views/SettingsView.vue` 的 `typeLabels` / 正则解析属于联动点；若调整提交格式、更新内容展示样式或 changelog 生成方式，必须同时核对这三处，且注意 `--no-merges` 会让 merge commit 不进入工具箱更新列表
- `llama.cpp` 版本更新需要同时同步 `build.ps1`、`build.yml`、`LocalLlmService.LlamaVersion`、下载资源命名模式、README/本手册说明

## 21. 后端专项补充

### 21.1 运行与架构细节

- 项目当前不做向后兼容迁移或旧格式自动转换；默认按“预稳定阶段、可以清晰断代”的思路维护
- `Program.cs` 必须在 DI 之前读取 `AppData:Root` 并回写到 `builder.Configuration["AppData:Root"]`，否则 `AppDataPaths` 看到的是旧值
- 静态资源缓存策略必须区分 `/assets/*` 与 `index.html`/`favicon.ico`；SPA fallback 也必须显式设置 `no-cache`
- 命名 `HttpClient` 已经承载超时、连接数、UA 和用途假设；新增外部网络调用优先复用命名客户端，不要随处 `new HttpClient()`
- `Updater/` 是 AOT 工程，不允许依赖 WinForms、反射式 JSON 序列化或 `Microsoft.Win32.Registry` 的常规托管封装；涉及注册表时应使用 P/Invoke

### 21.2 TranslatorEndpoint 与配置链路

- `TranslatorEndpoint` 目标为 `net35`，并依赖 `build.ps1` 从 XUnity 包里提取 `libs/` 引用 DLL
- `[LLMTranslate]` INI 区段的 `ToolkitUrl`、`GameId` 等值由 `POST /api/games/{id}/ai-endpoint`、`InstallOrchestrator` 和 DLL 初始化共同维护，修改其约定必须三处同改
- 不要从零重写 `AutoTranslatorConfig.ini`；统一通过 `ConfigurationService.PatchAsync` 做补丁式修改
- `PatchAsync` 中 `null` 表示跳过字段，空字符串表示清空字段，这个语义不能改
- 默认最优配置会写入 `Language=zh`、`OverrideFont=Microsoft YaHei`、`Endpoint=LLMTranslate` 等值；若调整默认配置，必须同时核对安装链路和文档说明
- `LLMTranslate.dll` 的日志分为始终输出的 `Log()` 和仅在 `DebugMode` 下输出的 `DebugLog()`，不要把关键初始化和错误信息放进 `DebugLog()`

### 21.3 AI 翻译、术语与缓存

- 在线翻译主链路仍然是 Phase 0 TM 查找、Phase 1 自然翻译、Phase 2 术语/DNT 占位符替换、Phase 3 强制修正
- `TranslationMemoryService` 的写入先落内存，持久化走防抖；热路径上不要引入额外磁盘 I/O
- `GlossaryExtractionService` 与 `TermExtractionService` 共享解析与分类逻辑，但故意保持两个独立服务；不要因为“看起来重复”而强行合并
- 所有翻译路径都必须在满足条件时调用 `BufferTranslation` + `TryTriggerExtraction`，否则术语提取统计会失真
- `ScriptTagService.NormalizeForCache` 是缓存归一化的唯一入口；涉及预翻译缓存、动态模式或脚本标签的变更都要核对调用点
- `TranslationStats.Queued` 是推导值，不等于内部 `_queued`；TM 命中和失败文本统计也有各自独立含义，不能混用
- `RecentTranslation.EndpointName` 在 TM 命中场景下需要显式写成“翻译记忆”，否则前端最近翻译列表会出现空白端点名

### 21.4 性能、并发与 SignalR

- 热路径避免 `GameLibraryService.GetByIdAsync` 等磁盘或大对象访问，优先使用缓存
- `SemaphoreSlim` 相关逻辑要区分“是否成功获取信号量”再释放，避免超时后误 `Release()`
- `BroadcastStats` 的节流与 `force: true` 语义是前端实时流水线显示的前提，不能随意删减
- KeepAlive 或触发即忘的后台任务，如果依赖 SignalR 结束事件来复位前端状态，那么失败、取消路径也必须广播完成/错误事件
- 向前端返回的错误消息要避免泄露内部绝对路径、异常堆栈或敏感配置；详细信息只写服务器日志

### 21.5 资源、字体、WebView2 与周边服务

- `AssetExtractionService` 使用 AssetsTools.NET，数组字段访问统一遵循 `field -> "Array" -> elements` 模式
- TTF 字体替换支持 `dynamicEmbedded` 的 Unity Legacy `Font`，也支持将 `osFallback` / 名称映射动态字体原位转成内嵌字体；`staticAtlas` 与 `unknown` 仍统一扫描但拒绝替换
- Legacy `Font.m_FontData` 的单字节元素既可能是 `UInt8` 也可能是 `Int8/char`；写回数组项时要按 `AssetValueType` 选择 `AsByte` 或 `AsSByte`，否则会在 `SetNewData` 时触发有符号溢出
- `TmpFontGeneratorService` 基于 FreeTypeSharp 与 Felzenszwalb EDT 生成 SDF；生成出的 atlas、padding、gradient scale、render mode 之间有强耦合，不要局部改一个字段
- `WebImageSearchService` 通过网页抓取提供图片搜索；所有 URL 在真正请求之前必须先走 SSRF 校验，保存前还要校验内容类型
- `WebViewWindow`、`SystemTrayService`、WebView2 预热、加载 overlay、快速隐藏 UI、关闭超时等机制都属于桌面宿主层不变量，改动前要完整回看历史实现
- `WebViewWindow.InitializeAsync()` 现在会先探测 `GET /`，并且只在首页首次 `NavigationCompleted` 成功后才隐藏原生 loading overlay；首页探测或首屏导航失败时必须保留 overlay 并给出明确错误，不能直接暴露系统 404 页面
- `Updater/Program.cs` 在成功重启和回滚重启两条路径里都必须保持 `WorkingDirectory = appDir`，否则可能出现 API 正常但首页因 `wwwroot` 解析到错误目录而 404
- `QuickAccessHelper` 使用 Shell COM 且要求 STA 线程；相关 COM 对象必须逐级 `Marshal.ReleaseComObject`
- `BepInExLogService`、`PluginHealthCheckService`、`BepInExPluginService` 都依赖文件共享读或被动分析模式，不要在这些路径里引入“加载用户 DLL 到当前进程”这类高风险操作

### 21.6 安全约定补充

- `gameId`、语言代码、用户上传文件名、可执行文件名、导入 ZIP 内部路径都必须做路径穿越和格式校验
- `PathSecurity.SafeJoin` 与 `PathSecurity.ValidateExternalUrl` 是路径安全和 SSRF 防护的统一入口；不要自行复制一套近似实现
- `POST /api/settings/reset`、`POST /api/settings/import` 会引发跨服务缓存失效，相关服务新增缓存后必须并入这两条路径
- JSON 数据文件统一走 `FileHelper.WriteJsonAtomicAsync`；非 JSON 关键文件也应采用 `.tmp + move` 的原子落盘模式

## 22. 前端专项补充

### 22.1 基础约定

- 使用 Vue 3 Composition API 和 `<script setup lang="ts">`
- API 调用统一从 `@/api/client` 或 `@/api/games` 进入，不要直接散落 axios 实现
- 生产路径不要留下 `console.*`
- 共享工具优先放在 `src/composables/`、`src/utils/`、`src/constants/`，不要在页面内重复定义一份几乎相同的 helper
- `gamesStore.launchGame(id)` 是所有启动游戏入口的唯一调用点，不要绕开它直调 API

### 22.2 设计系统与样式

- 项目使用自托管字体，禁止引入 Google Fonts CDN
- 主题模式由 `useThemeStore` 控制，`resolvedTheme` 才是渲染依据；`mode` 可能是 `system`
- 共享布局和卡片类名以 `main.css` 为准，例如 `.page-title`、`.section-card`、`.section-header`、`.header-actions`、`.table-container`
- 顶层页面和游戏子页面的标题、回退按钮、section card 样式有明确约定，不要在每个页面重新发明一套
- 这是桌面应用而不是全宽网页，默认窗口宽度扣掉侧边栏和卡片内边距后，经常只剩“中等内容宽度”；双列卡片、表单和设置面板不要只依赖 `768px` 这类移动端断点，优先使用 `repeat(auto-fit, minmax(...))`、`minmax(0, 1fr)` 或补充中间断点，确保默认窗口大小下不裁切
- `section-card`、折叠区正文和局部 grid/flex 列在需要收缩时通常都要显式 `min-width: 0`；`src/assets/main.css` 已为共享卡片容器补了这层约束，新增类似 `ConfigPanel.vue`、`FontGeneratorView.vue` 的双列布局时要一起检查 Naive UI 输入控件和外层容器是否真正允许收缩
- 避免无必要的内联样式；除一次性的 `animation-delay` 等极少数情况外，统一落到作用域 CSS
- 颜色和边框一律使用设计系统变量，不要发明不存在的 CSS 变量名

### 22.3 KeepAlive、SignalR 与资源生命周期

- 顶层页面通过 `KeepAlive` 缓存，因此涉及 SignalR、window 监听器、定时器的页面与子组件都要同时处理 `onActivated`、`onDeactivated`、`onBeforeUnmount`
- `HubConnection` 不要在模块顶层创建，统一在生命周期钩子内创建和销毁
- `onBeforeUnmount` 是默认清理钩子，不要改成 `onUnmounted`
- 自动保存页面在加载外部数据时统一采用 `disable -> load/assign -> nextTick -> enable`
- `AiTranslationView`、`SettingsView` 这类共享 `AppSettings` 的 KeepAlive 页面，重新激活时必须重新从后端加载，避免旧副本覆盖新改动

### 22.4 Naive UI 与常见实现陷阱

- `NInputNumber` 的 `@update:value` 会发出 `number | null`，保存时要显式处理 `null`
- `NDialogOptions.onPositiveClick` 返回 Promise 会阻塞对话框关闭；耗时操作通常用 fire-and-forget 更合适
- `NDataTable` 的排序、虚拟滚动、`row-key` 唯一性、弹性列 `minWidth` 都有历史坑，表格变更前要核对现有模式
- `NColorPicker` 使用自定义触发器时应走 `#trigger` 槽，并锁定 `hex` 模式
- `NUpload`、`NEllipsis` 在 flex 容器里有额外包裹层时，需要用 `:deep()` 或外层容器修正布局
- `NTabs type="segment"`、折叠面板、按钮渐变边框等视觉模式已有历史实现，改动前先复用现有样式结构

### 22.5 页面组织与交互模式

- 顶层页面加路由、导航、缓存名；游戏子页面加 `/games/:id/...` 路由和 `GameDetailView` 入口按钮，不要塞进主导航
- `TermEditorView` 是统一术语编辑页，替代早期独立 glossary/do-not-translate 页面；相关新能力优先接到这里
- 文件浏览器由 `FileExplorerModal.vue` 全局挂载一次，`useFileExplorer()` 通过 Promise 返回选中的服务器路径
- 背景图、封面图、图标、网页图片搜索、游戏详情 hero 视觉、视差滚动、缓存失效时间戳，都有现成模式，不要局部重写
- 涉及复杂多步骤交互时，优先抽成 composable，例如 `useAddGameFlow`、`useAutoSave`、`useWindowControls`

## 23. 构建、发布、CI/CD 补充

- `dotnet build` 会自动触发前端构建；仅构建后端时用 `-p:SkipFrontendBuild=true`
- `build.ps1` 负责本地完整构建，但不负责生成所有 CI 产物；CI 逻辑在 workflow 内独立实现
- 版本号应通过 `InformationalVersion` 传递，不要滥用 `Version` 导致 `AssemblyVersion` 溢出问题
- 当前发布是多文件模式，不再使用单文件发布；相关 `ExcludeFromSingleFile` 历史逻辑已失效
- `SatelliteResourceLanguages=en`、WiX 的 `obj` 清理、PowerShell ZIP、组件 ZIP、manifest 命名、edition 构建参数等都已成为既定约束
- WiX/MSI 细节包括：每用户安装、`MajorUpgrade` 调度、可选删除 `%AppData%\XUnityToolkit`、注册表同步、许可证文本一致性、中文字符串本地化方式、`SuppressValidation=true`
- CI 包含可复用的 `build.yml`、发布 `release.yml`、依赖巡检 `dep-check.yml`
- GitHub Actions 中的 AOT 发布、`$GITHUB_OUTPUT` 多行写法、`gh release create --notes-file`、detached HEAD 场景下回写 `main` 等问题都属于既有坑位
- `dep-check.yml` 只跟踪 BepInEx 与 XUnity；`llama.cpp` 版本目前仍然固定人工维护

## 24. 旧文档状态

- 历史 `CLAUDE.md` 已全部删除
- 原先三份 `CLAUDE.md` 的有效内容已并入本 `AGENTS.md`
- 后续若有人重新添加 `CLAUDE.md`，应默认视为重复文档并回收至 `AGENTS.md`

## 25. 插件健康状态补充

- `PluginHealthCheckService` 现已改为 settings-aware 的异步检查流程：被动检查 `GET /api/games/{id}/health-check` 与主动验证 `POST /api/games/{id}/health-check/verify` 都统一走 `CheckAsync(...)`
- 插件健康状态不再只靠 BepInEx 日志猜测原因；检查顺序调整为：文件完整性 -> 工具箱 AI 状态 -> 日志归类 -> 验证后的工具箱连通性
- 当工具箱侧存在可明确识别的问题时，后端会新增 `toolboxAiState` 健康项，前端展示名称为“工具箱 AI 翻译”，状态固定为 `Warning`
- `toolboxAiState` 当前覆盖三类场景：
  - `AiTranslation.Enabled == false`
  - 当前没有任何可用端点，判定规则与 `LlmTranslationService` 保持一致：`Enabled && ApiKey 非空`
  - 当前 `ActiveMode == "local"` 且 `LocalLlmService.IsRunning == false`
- 对于 `toolboxAiState`，详情文案必须直接指出工具箱侧问题，不允许再使用“可能不完全兼容 XUnity”这类兼容性描述
- 日志错误归类已拆分为两层：
  - 真正的 XUnity 兼容性/Hook 异常：只在没有明确工具箱 AI 阻塞证据时才给出“当前游戏版本可能不完全兼容 XUnity”
  - 泛化的翻译失败：如 `Failed: 'Continue'`、`Failed: 'Credits'`、`Cannot translate`、`AutoTranslator failed`，在存在工具箱 AI 问题时必须归因到工具箱侧，不得误标为 XUnity 兼容性问题
- `PluginHealthReport`、`HealthCheckItem`、`HealthCheckDetail` 的 JSON 结构本次未扩展；兼容性要求是只新增 `checks[].id = toolboxAiState`，不要再额外添加新字段
- `PluginHealthCard.vue` 目前对异常项使用固定排序：
  - `toolboxAiState` 最先显示
  - `logErrors` 次之
  - 其他异常项保持原始顺序
- 插件健康状态属于文件共享读 + 被动分析路径；允许读取 `settings.json`、本地 LLM 运行状态和 `BepInEx/LogOutput.log`，但不要引入任何会加载用户插件 DLL、修改游戏文件、或为分析而重写配置文件的实现
