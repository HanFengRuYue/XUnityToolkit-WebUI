# AGENTS.md

本文件用于给后续代理提供可直接复用的仓库上下文。它不是对 `README.md` 和各级 `CLAUDE.md` 的逐字复制，而是面向“接手开发/排障/重构”的工作手册。

## 1. 权威文档入口

开始动手前，优先参考这些文件：

- `README.md`
- `CLAUDE.md`
- `XUnityToolkit-WebUI/CLAUDE.md`
- `XUnityToolkit-Vue/CLAUDE.md`

若 AGENTS 与上述文件冲突，以源码和这些维护文档为准，并在改动后同步更新 AGENTS。

补充说明：

- 仓库内 `.claude/` 目录只有 `scheduled_tasks.lock`，没有额外项目记忆文件。
- 本仓库的维护说明主要沉淀在上述三个 `CLAUDE.md` 中。

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
- `Installer`: WixToolset v4

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

## 6. 运行时架构

后端启动入口：

- `XUnityToolkit-WebUI/Program.cs`

主要职责：

- 读取 `settings.json` 中 `aiTranslation.port`，动态决定监听端口，默认 `51821`
- 强制绑定 `http://127.0.0.1:{port}`
- 注册各类命名 `HttpClient`
- 注册所有核心服务为单例
- 配置 SignalR
- 提供静态文件与 SPA fallback
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
- 路径集中定义在 `XUnityToolkit-WebUI/Infrastructure/AppDataPaths.cs`

关键文件/目录：

- `library.json`
- `settings.json`
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
- `backups/`
- `logs/`

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

多阶段翻译：

- Phase 0：TM 查找
- Phase 1：自然翻译
- Phase 2：术语/DNT 占位符替换
- Phase 3：强制修正

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
- 也支持 TTF 资源替换
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
- MSI 由 WiX 生成，且不同 edition 构建时必须注意清理 `Installer/obj/...`

## 14. 关键约束与不变量

通用：

- 返回 `ApiResult` 的端点要用 `Results.Ok(ApiResult<T>.Ok(...))`
- 输入验证失败时应返回 `BadRequest`，不要返回 200 + fail body
- 新增每游戏数据文件时，必须同步补充删除逻辑与缓存清理
- 新增设置字段时，要检查后端默认值、TS 类型、前端默认值、保存逻辑是否同步

后端：

- 不要从头重建 `AutoTranslatorConfig.ini`，而是使用补丁式修改
- `AppSettingsService.GetAsync()` 返回缓存对象，不要原地乱改，优先 `UpdateAsync`/`SaveAsync`
- 热路径避免磁盘 I/O，尤其 `POST /api/translate`
- `GameId` 用作文件路径时必须校验 GUID
- 用户提供的 URL 在真正请求前必须走 SSRF 校验

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
- 根目录尚无 `AGENTS.md`，本文件为首次补充
- `.claude/` 中没有额外项目说明
- 构建链路、前后端入口、运行时数据目录、更新器、翻译端点均已做过静态核对

## 18. 维护建议

后续如有下列变更，请同步更新本文件：

- 新增顶层子项目
- 调整构建/发布流程
- 修改运行时数据目录布局
- 大幅重构翻译链路、本地 LLM、字体链路、更新链路
- 引入新的高频同步点或新的全局不变量

