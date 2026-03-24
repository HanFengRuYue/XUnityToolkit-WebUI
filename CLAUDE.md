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

- **后端：** ASP.NET Core Minimal API (.NET 10.0, Windows Forms 用于原生对话框)
- **前端：** Vue 3 + TypeScript + Naive UI + Pinia（位于 `XUnityToolkit-Vue/`）
- **侧边栏状态：** `useSidebarStore`（Pinia + localStorage）管理折叠/宽度；`effectiveWidth` 计算属性在折叠时返回 64px，否则返回自定义宽度；移动端（≤768px）忽略折叠/调整大小；折叠后的导航项是 44×44px 的方形按钮，通过 `margin: 0 auto` 居中；折叠开关位于设置按钮上方
- **AI 翻译页面布局：** 单列布局 — 统计条 → 管线状态 → 设置（可折叠区块卡片，`collapsed.settings`）→ 最近翻译 → 错误；无两列网格
- **实时通信：** 通过单个 `InstallProgressHub` 使用 SignalR（分组：`game-{id}`, `ai-translation`, `logs`, `pre-translation-{gameId}`, `local-llm`, `font-replacement-{gameId}`, `font-generation`, `update`）；`preCacheStatsUpdate` 广播到 `ai-translation` 分组用于预翻译缓存命中/未命中统计；`healthReportReady` 在安装验证步骤完成后广播到 `game-{id}` 分组
- **持久化：** JSON 文件存储在 `%AppData%\XUnityToolkit\`（`library.json`, `settings.json`）；`AppData:Root` 配置键允许为开发/测试覆盖路径；API 密钥使用 DPAPI 加密
- **系统托盘：** NotifyIcon 在专用 STA 线程上运行；`ShowNotification` 通过 `SynchronizationContext.Post` 调度到 STA；`_trayIcon`/`_syncContext` 为 `volatile`
- **无控制台：** `OutputType=WinExe` — 无控制台窗口；不要改回 `Exe`
- **TranslatorEndpoint：** net35 `LLMTranslate.dll` — XUnity.AutoTranslator 自定义端点，将游戏文本转发到 `POST /api/translate`；通过 `[LLMTranslate]` INI 区段配置；在 `Initialize()` 时通过 `WebClient.DownloadStringAsync` 发送连通性 ping（`GET /api/translate/ping?gameId=`）（即发即忘）
- **AI 翻译：** `LlmTranslationService` 调用 LLM API（OpenAI/Claude/Gemini/DeepSeek/Qwen/GLM/Kimi/Custom）；多提供商负载均衡；批量模式由 `SemaphoreSlim` 限制；每游戏统一术语表、翻译记忆、AI 描述；通过 SignalR 实时统计
- **本地 LLM：** `LocalLlmService` 管理 llama-server 进程；通过 DXGI 检测 GPU，WMI 作为后备；llama 二进制文件打包为 ZIP，首次使用时延迟解压；本地模式强制 concurrency=1、batch size=1、禁用术语提取；术语占位符替换 + 后处理在本地模式下仍然有效（跳过系统提示词中的术语以节省上下文 token）
- **资源提取：** `AssetExtractionService` 使用 AssetsTools.NET 从 Unity `.assets` 和 bundle 文件中提取字符串；`PreTranslationService` 批量翻译并写入 XUnity 缓存文件
- **I2 Localization 提取：** `AssetExtractionService.ExtractI2LocalizationTerms` 通过 `mSource.mTerms`+`mLanguages` 字段存在性检测 I2 `LanguageSourceAsset`；使用 `I2:{langCode}:{termKey}` source tag 提取每种语言的术语值；跳过 `IsGameText` 过滤（I2 术语是已知的游戏文本）；对非 I2 MonoBehaviour 回退到通用 `CollectStrings`
- **GameCreator 2 提取：** `AssetExtractionService` 检测 GameCreator Dialogue (`m_Story`+`m_Nodes`)、Quest (`m_Tasks`+`m_Title`/`m_Description`)、Actor (`m_ActorName`/`m_PrimaryActorName`) 和 Stat (`m_Acronym`+`m_Title`) 类型；使用 `GameCreator:{Type}:{Name}` source tag 提取；遍历对话节点树（`m_Content`, `m_ContentChoice`, `m_Values*`）、任务层级和指令子字段；剩余字段回退到通用 `CollectStrings`；`DetectAndLogTemplateVariables` 扫描提取的文本中的 `{Variable}` 模式并记录建议配置 DoNotTranslate 术语
- **VIDE Dialogues 提取：** `AssetExtractionService.TryExtractVideDialogue` 通过 `dID`+`playerDiags` 键检测 TextAsset 中的 VIDE 对话 JSON；解析扁平 JSON，从 `pd_{N}_com_{C}text` 键提取对话文本，从 `pd_{N}_com_{C}charName` 键提取角色名；过滤控制标记（`*` 前缀、`RAND`、`ExtraData`）；source tag 为 `VIDE:{dialogueName}` 和 `VIDE:Character:{dialogueName}`；对非 VIDE TextAsset 回退到通用 `ExtractTextLines`
- **VIDE SOQuotes_UW 提取：** `IsVideQuote` 通过 `quotes`+`dialType` 字段存在性检测；`ExtractVideQuotes` 从 `quotes` 数组中提取所有字符串；source tag 为 `Quote:{assetName}`
- **VIDE SOTraitData_UW 提取：** `IsVideTrait` 通过 `traitName`+`traitType` 字段存在性检测；`ExtractVideTraits` 直接提取 `traitName`、`reqsText`、`effectText`、`flavorText`；仅当 `hoverText` 包含实质性非模板文本时才提取（去除 `{NAME}`/`{REQS}`/`{EFFECT}`/`{FLAVOR}`/`{STATS}`/`{REPS}`/`{TP}`/`{ITEMS}`/`{CLOTHES}` 占位符后检查剩余文本是否 >5 字符）；source tag 为 `Trait:{assetName}` 和 `Trait:HoverText:{assetName}`
- **添加游戏专用提取器：** 模式：在 `ExtractFromAssetsInstance` 中通过字段签名检测 → 专用提取方法配合特定 source tag → 回退到通用 `CollectStrings`；对于 TextAsset JSON 格式，在 `ExtractTextLines` 回退之前的 TextAsset 循环中检测；添加 `[GeneratedRegex]` 匹配键模式；更新 CLAUDE.md 架构部分
- **备份/恢复：** `BackupService` 为每个游戏创建 `BackupManifest` 用于干净卸载；清单位于 `{dataRoot}/backups/{gameId}.json`
- **字体替换：** `FontReplacementService` 使用 AssetsTools.NET 扫描和替换游戏 `.assets` 及 bundle 文件中的 TMP_FontAsset；字段级替换保留 PPtr 引用；自动清除 Addressables CRC；备份位于 `{dataRoot}/font-backups/{gameId}/`；自定义字体位于 `{dataRoot}/custom-fonts/{gameId}/`
- **字体生成：** `TmpFontGeneratorService` 通过 FreeType (`FT_RENDER_MODE_NORMAL`) 渲染抗锯齿位图，然后通过 `DistanceFieldGenerator`（Felzenszwalb EDT）生成 SDF，复刻 Unity 的 FontEngine 方式；支持 SDFAA/SDF8/SDF16/SDF32 渲染模式和上采样（8x/16x/32x）；动态内边距（百分比/像素模式）；自动大小二分搜索；`GradientScale = padding + 1` 注入到 Material 中；RectpackSharp 用于图集打包；多图集支持；`CharacterSetService` 解析可叠加字符集（内置/自定义 TXT/XUnity 翻译文件）；`BuiltinCharsets` 枚举 GB2312/GBK/CJK Common/CJK Full/Japanese；磁盘临时 SDF 位图用于控制内存；生成报告保存为 `.report.json` 附属文件；输出位于 `{dataRoot}/generated-fonts/`
- **BepInEx 日志：** `BepInExLogService` 使用 `FileShare.ReadWrite` 读取 `{GamePath}/BepInEx/LogOutput.log`；AI 分析通过 `LlmTranslationService.CallLlmRawAsync`（不占用信号量）；诊断提示词为预定义中文；日志截断到最后 4000 行供 LLM 上下文使用；`hasBepInEx` 计算包含 `PartiallyInstalled` 状态
- **插件健康检查：** `PluginHealthCheckService` 执行被动的、基于规则的健康检查（不依赖 AI）；第 1 级：文件完整性（doorstop 代理、BepInEx 核心、XUnity 插件、配置、LLMTranslate DLL）；第 2 级：基于日志的检查，通过 `[GeneratedRegex]`（BepInEx 初始化、XUnity 已加载、端点已注册、错误分析）；第 3 级：连通性检查 — `LLMTranslate.dll` 在 `Initialize()` 时发送 `GET /api/translate/ping?gameId=`，`RecordPing`/`HasRecentPing` 跟踪到达情况；`VerifyAsync` 自动启动游戏，等待日志 + ping，然后分析；`VerifyForInstallAsync` 绕过 `_activeVerifications` 守卫供 `InstallOrchestrator` 使用（编排器自己管理并发）；`PluginHealthCard.vue` 只显示有问题的项目（过滤 Healthy），单个"启动验证"按钮（无被动重检按钮），接受可选 `initialReport` prop（提供时跳过被动检查）；API：`GET /api/games/{id}/health-check`, `POST .../health-check/verify`
- **健康检查错误分析：** `CheckLogErrors` 双遍扫描：Pass 1 扫描 `[Error:]` 行匹配通用错误模式（IL2CPP、Harmony、程序集版本、类型加载、插件加载、字体缺失、文件未找到、权限、空引用、网络、XUnity 钩子）；Pass 2 扫描所有行匹配 LLMTranslate 端点专属模式（工具箱连接、空响应、数量不匹配、本地模型、端点禁用、API 失败）——DLL 通过 `Console.WriteLine` 输出，在 BepInEx 中显示为 `[Info/Message:]` 级别而非 `[Error:]`，因此必须独立扫描；`HealthCheckDetail(Category, Excerpt, Suggestion?)` 携带分类诊断；`SafeExcerpt` 剥离日志前缀并用 `Path.GetFileName` 替换绝对路径；每类最多 2 条、总计最多 10 条；添加新错误模式：在 `GeneralErrorPatterns` 或 `EndpointErrorPatterns` 列表中添加 `ErrorPattern` + 对应 `[GeneratedRegex]`
- **健康检查 ↔ DLL 日志依赖：** `CheckEndpointRegistered` 扫描 BepInEx 日志中的 `"LLMTranslate"` 子串 — 需要 DLL 的 `Log()`（无条件）输出初始化 banner；不要将初始化消息限制在 `DebugLog()` 后面，否则健康检查将无法检测到端点
- **在线更新：** `UpdateService` 检查 GitHub Releases 的新版本；基于清单的差异下载（app/wwwroot/bundled-llama/bundled-fonts/bundled-plugins/bundled-misc 组件 ZIP）；`Updater.exe`（AOT，无运行时依赖）处理文件替换和重启；暂存在 `data/update-staging/`；两阶段备份-替换保证原子性；失败时回滚；通过 `AppSettings.ReceivePreReleaseUpdates` 选择预发布版本

## 翻译管线

- **管线分叉流程设计：** 状态仪表板显示水平分叉管线："已接收" → [LLM 分支：排队→翻译中→已完成→术语提取] | [TM 分支：(精确·模糊·模式 inline node)→已命中]；水平分叉布局是设计意图 — 不要替换为平面卡片或垂直自顶向下布局；SVG 覆盖层绘制直角连接路径和动画粒子（`offset-path`）；所有节点包裹在 `NPopover` 中用于悬停提示；节点使用 `flex: 1 1 0` 填充可用宽度；术语提取内联在 LLM 分支流程中（在已完成之后，不是作为单独的 `.branch-sub`）；TM 命中显示为单个 `.tm-node` 配合 `.tm-chips-inline`（不是单独的小芯片）；LLM 完成计数计算为 `totalTranslated - (tmHits + tmFuzzyHits + tmPatternHits)`；TM 分支使用基于 accent 的 `color-mix()` 变体（不是 `--secondary`）以跟随主题强调色，实时 `TranslationStats` 字段（非单独获取的 `TranslationMemoryStats`）
- **多阶段翻译管线：** 第 1 阶段（自然）发送未修改的文本，术语在结构化提示词中 — 无占位符，依赖 LLM 理解；第 2 阶段（占位符）对第 1 阶段未解决的术语应用 `{{G_x}}`/`{{DNT_x}}` 替换；第 3 阶段（强制修正）重翻仍未通过术语审计的片段；各阶段逐步推进 — 每个阶段只处理前序阶段未解决的文本
- **翻译记忆：** `TranslationMemoryService` 提供持久化的每游戏 TM，具有三级匹配（精确 → 动态模式 → Levenshtein 模糊）；在首次 `POST /api/translate` 时延迟加载；作为 `LlmTranslationService` 管线中的第 0 阶段集成（在第 1 阶段之前）；TM 命中需经过 `TermAuditService` 验证；`Add()` 是同步的（仅内存），持久化经过防抖（5 秒延迟）以避免批量翻译时的 I/O 争用；`FlushAsync()` 取消待处理的防抖并立即持久化（由 `PreTranslationService` 在最终批次后调用）；每游戏 `SemaphoreSlim` 锁用于持久化（非全局）；实现 `IDisposable` 用于关闭时刷新；`DynamicPatternService` 检测模板变量并使用 LLM 识别动态文本片段，生成 XUnity `r:` 正则表达式模式；`TermExtractionService` 在第 1 轮预翻译后通过专用 LLM 调用提取术语
- **多轮预翻译：** `PreTranslationService` 支持 2 轮管线；第 1 轮：标准翻译 + 自动术语提取；可选术语审核暂停（5 分钟超时，`AwaitingTermReview` 状态）；第 2 轮：使用新提取的术语重新翻译；动态模式正则追加到 `_PreTranslated_Regex.txt`；结果批量写入 TM
- **统一术语管理：** `TermService` 在 `data/glossaries/{gameId}.json` 存储每游戏术语条目（首次加载时迁移旧版 DNT 条目）；每个 `TermEntry` 有 `Type`（Translate/DoNotTranslate）、`Category`、`Priority`、`CaseSensitive`、`ExactMatch`、`IsRegex`；`TermMatchingService` 处理基于优先级的占位符替换；`TermAuditService` 验证翻译中的术语合规性
- **术语提取服务：** `GlossaryExtractionService`（响应式、热路径、直接写入术语表）和 `TermExtractionService`（批量、预翻译、暂存候选）通过 `LlmResponseParser`、`EndpointSelector`（`SelectBestEndpoint` — 无服务级端点覆盖）、`TermCategoryMapping` 共享 LLM 提取逻辑，但故意是独立服务 — 不同的生命周期、输出和并发模型；不要合并
- **GlossaryExtractionService 重触发守卫：** 在信号量超时时重置 `LastExtractionAt`，绝不重置为 0（会导致每次 `POST /api/translate` 立即重触发）；重置为 `total - interval + 1` 以允许在一个间隔后重试
- **占位符替换顺序：** 术语按优先级排序（高优先），然后按原文长度排序（长优先）；翻译类型术语产生 `{{G_x}}` 占位符，免翻译术语产生 `{{DNT_x}}`；基于优先级的排序确保重要术语在低优先级术语之前占据其范围
- **翻译后处理顺序：** 术语表恢复 → 术语表后处理 → DNT 恢复；**DNT 恢复必须在术语表后处理之后** — 否则 `ApplyGlossaryPostProcess`（在翻译文本中对术语原文执行 `string.Replace`）会将恢复的 DNT 词替换为术语表翻译，破坏免翻译意图
- **术语子串不变量：** 当多个术语存在子串关系时（例如 `SaveSettings.es3` 包含 `Settings`），三层 — `ApplyGlossaryPostProcess`、`TermAuditService`、第 3 阶段强制修正 — 均使用受保护范围/包含逻辑确保较长术语优先；任何新的对术语原文/翻译的 `string.Replace` 必须使用带范围保护的位置替换
- **预计算占位符文本：** 完全被单个占位符替换的文本在 LLM 调用前解析（预计算）；它们绕过 LLM 调用和 `ApplyGlossaryPostProcess`（结果直接来自术语映射），但仍参与术语审计并计入 phase2Pass 统计；`preComputed` 字典跟踪这些索引
- **预翻译缓存优化：** `PreTranslationCacheMonitor` 在 `EnablePreTranslationCache` 开启时跟踪缓存命中/未命中；通过 `EnsureCacheAsync` 在每游戏首次 `POST /api/translate` 时延迟加载（`_loadAttemptedForGameId` + `SemaphoreSlim` 双重检查锁）；`PreTranslationService` 规范化缓存键（富文本标签剥离）并生成带 `sr:` 分割器模式的 `_PreTranslated_Regex.txt`；XUnity 配置针对空白容差优化（`CacheWhitespaceDifferences`, `IgnoreWhitespaceInDialogue`, `MinDialogueChars`）；自定义正则模式存储在 `{dataRoot}/cache/pre-translation-regex/{gameId}.txt`
- **LLM 批量分析限制：** `DynamicPatternService` 和 `TermExtractionService` 最多采样 200 对（`MaxAnalysisPairs`/`MaxExtractionPairs`）以将 LLM 调用上限控制在约 10 次；否则 2500 条翻译 = 127 次顺序调用（10 分钟–2 小时）
- **LLM 批量服务进度：** `DynamicPatternService.AnalyzeDynamicFragmentsAsync` 和 `TermExtractionService.ExtractFromPairsAsync` 接受 `Action<int, int>? onBatchProgress`（done, total）；`PreTranslationService` 在同步回调中使用即发即忘 `_ = BroadcastStatus(...)` 推送 SignalR 更新
- **脚本标签清理：** `ScriptTagService` 从预翻译缓存键和 LLM 输入中剥离游戏特定指令码（例如 `tk,N,text`, `%%,N,text,#BTN`）；`NormalizeForCache` 现有两层：`XUnityTranslationFormat`（富文本）→ `ScriptTagService`（脚本标签）；每游戏规则在 `{dataRoot}/script-tags/{gameId}.json`；全局版本化预设在 `bundled/script-tag-presets.json`，通过 `IsBuiltin` 标志自动更新
- **脚本标签与术语：** 脚本标签预设（`Extract`/`Exclude`）仅在行级别操作；内联占位符保留（例如 `{PC}`, `{M}` 模板变量）需要 DoNotTranslate 术语条目，而非脚本标签规则
- **XUnity 正则翻译：** 缓存文件支持 `r:"pattern"=replacement`（标准正则，默认子串替换，除非用 `^$` 锚定）和 `sr:"pattern"=$1$2`（分割器正则，自动锚定，独立翻译每个捕获组后重新组装）；`sr:` 分组必须是可翻译文本 — 纯数字分组浪费翻译调用；对数字模式使用 `TemplateAllNumberAway=True`

## 实现不变量

- **TranslationStats 计算字段：** `DynamicPatternCount` 和 `ExtractedTermCount` 在 `TranslateEndpoints.cs` 中填充（而非 `LlmTranslationService.GetStats()`），因为 `TermExtractionService` → `LlmTranslationService` 会产生循环 DI 依赖；任何来自依赖 `LlmTranslationService` 的服务的新计算统计必须遵循相同的端点层模式
- **`RecentTranslation.EndpointName` 用于 TM 命中：** 当 `perTextTranslationSource[i]` 不为 null 时在 `PipelineComplete` 中设为 `"翻译记忆"`；防止最近翻译列表中出现空端点名
- **`TranslationMemoryService.GetHitStats()` 元组顺序：** 返回 `(ExactHits, PatternHits, FuzzyHits, Misses)` — 用匹配的名称解构，不要用 `(exact, fuzzy, pattern, misses)`
- **枚举 JSON 大小写：** `TermType` 和 `TermCategory` 使用 `CamelCaseJsonStringEnumConverter<T>`（camelCase：`"translate"`, `"doNotTranslate"`, `"character"`）；所有其他枚举使用默认 PascalCase（`"OpenAI"`, `"NotInstalled"`）— 不要修改全局 `JsonStringEnumConverter` 或为其添加命名策略；**转换器优先级：** 属性级 `[JsonConverter]` > `JsonSerializerOptions.Converters` > 类型级 `[JsonConverter]` — camelCase 枚举必须在 TermEntry **属性**上有 `[JsonConverter]`（不仅是枚举类型上），否则 `Program.cs` 和服务 `JsonOptions` 中的全局 PascalCase `JsonStringEnumConverter` 会覆盖类型级属性
- **TermEntry 反序列化不变量：** 所有从 JSON 反序列化 `List<TermEntry>` 的代码（包括迁移/导入路径）必须使用 `FileHelper.DataJsonOptions` — 没有枚举转换器，`DoNotTranslate` 类型会静默变为 `Translate`（默认枚举值 0）

## 安全约定

- **路径遍历：** 对所有 ZIP 解压和用户提供的相对路径使用 `PathSecurity.SafeJoin(root, relative)`；使用 `Path.GetFileName()` 从用户提供的文件名中去除目录部分
- **gameId 验证：** 所有直接将 `id` 用于文件路径的端点（GET/PUT/DELETE）（无 `library.GetByIdAsync` 存在性检查）必须使用 `Guid.TryParse(id, out _)` 验证以防止路径遍历；适用于 TM 统计、dynamic-patterns、term-candidates、extracted-texts、pre-translate/regex 端点
- **SSRF 防护：** 在对任何用户提供的 URL 执行 `HttpClient.GetAsync()` 前使用 `PathSecurity.ValidateExternalUrl(url)`；阻止回环、私有 IP（IPv4+IPv6）、链路本地、`.local`/`.internal` 主机名
- **派生 URL 的 SSRF：** 也要验证从用户可配置基础 URL 构造的 URL（例如 `HfMirrorUrl` + repo path）；必须对最终 URL 而非仅基础 URL 通过 `ValidateExternalUrl` 验证
- **语言代码验证：** `fromLang`/`toLang` 参数以及 INI 来源的语言值（例如 `[General] Language`）用于文件路径时必须验证为简单代码（`^[a-zA-Z0-9_\-]{1,20}$`）以防止路径遍历；同时验证解析后的完整路径通过 `Path.GetFullPath` + `StartsWith` 保持在预期目录内
- **CustomFontPath 验证：** 请求体中的 `FontReplacementRequest.CustomFontPath` 必须在 `FontReplacementEndpoints` 中验证其保持在 `paths.GetCustomFontDirectory(gameId)` 内，然后再传递给服务
- **ExecutableName 验证：** 必须是不含路径分隔符（`/`, `\`）的简单文件名；在 `POST /api/games/` 和 `PUT /api/games/{id}` 中均需验证
- **游戏启动安全：** 在 `Process.Start` 前始终通过 `Path.GetFullPath` + `StartsWith` 验证 `exePath` 在 `GamePath` 内
- **进程参数：** 绝不在未清理引号的情况下将用户输入插入参数字符串；从用于 `-m "..."` 风格参数的用户提供路径中去除 `"`
- **CancellationTokenSource：** 从 `ConcurrentDictionary` 中移除时始终 `Dispose()`；覆盖前先 dispose 旧的 CTS
- **即发即忘端点中的 CTS 所有权：** 生产者（例如 `/replace` 处理程序的 `finally` 块）拥有 disposal；取消端点应只调用 `.Cancel()`，不要调用 `.Dispose()` — 防止取消和完成之间的双重 dispose 竞争
- **进程 disposal：** 在将 `_process` 设为 null 前始终调用 `Process.Dispose()`
- **返回给客户端的错误消息：** 绝不从通用 `catch (Exception)` 块返回 `ex.Message` — 使用安全的静态消息；来自类型化 catch（`HttpRequestException`, `InvalidOperationException`）的 `ex.Message` 可以接受
- **全局异常处理器：** `Program.cs` 中的中间件捕获未处理的 `/api` 异常，服务端记录完整详情，向客户端返回通用错误
- **设置验证：** 在 `SettingsEndpoints` 中对数值设置进行 clamp（MaxConcurrency, Port, ContextSize, LocalContextSize, Temperature, FuzzyMatchThreshold）；添加新的数值型 `AiTranslationSettings` 字段需要添加对应的 `Math.Clamp` 行
- **输入大小限制：** 对列表端点强制最大数量（术语 10000（统一）、翻译文本 500、原始配置 512 KB）；文件上传：字体 50MB、字符集/翻译 10MB、设置导入 100MB
- **正则验证：** 保存前使用 `new Regex(..., timeout: 1s)` 验证术语 `IsRegex` 条目
- **原子文件写入：** 对 JSON 数据文件使用 `FileHelper.WriteJsonAtomicAsync`；对非 JSON 关键文件使用写入临时文件 + `File.Move(overwrite: true)`
- **SignalR 错误消息：** 不要在 `_error` 字段中广播内部文件路径；使用 `Path.GetFileName()` 或通用消息

## 共享基础设施 (`Infrastructure/`)

- **`FileHelper.DataJsonOptions`：** 标准 `JsonSerializerOptions`（WriteIndented + CamelCase + JsonStringEnumConverter）由所有数据文件服务共享 — 不要声明每服务的 `JsonOptions`；有不同配置的服务（例如 `GameImageService` 不使用 WriteIndented，`UpdateService` 不使用 JsonStringEnumConverter）保持自己的配置
- **`FileHelper.WriteJsonAtomicAsync<T>`：** 原子 JSON 写入（序列化 → `.tmp` → `File.Move`）；包含 `Directory.CreateDirectory`；用于所有每游戏数据文件持久化
- **`LlmResponseParser`：** `ExtractJsonArray(content)` 去除 `<think>` 块 + markdown 围栏 + 提取 `[...]`；`ExtractJsonContent(content)` 用于通用 JSON（数组或对象）；被 `GlossaryExtractionService`、`TermExtractionService`、`LlmTranslationService`、`DynamicPatternService` 使用
- **`EndpointSelector`：** `SelectBestEndpoint(endpoints)` 返回最高优先级的已启用端点；`SelectEndpoint(endpoints, preferredId)` 先尝试指定端点，回退到最佳；被 `GlossaryExtractionService`、`TermExtractionService`、`DynamicPatternService`、`BepInExLogService` 使用
- **`TermCategoryMapping.FromString`：** `Models/TermEntry.cs` 中的共享 `Dictionary<string, TermCategory>`；被 `GlossaryExtractionService` 和 `TermExtractionService` 用于 LLM 响应解析

## 前端共享工具

- **`src/utils/format.ts`：** `formatBytes(bytes)` 和 `formatSpeed(bytesPerSec)` — 共享格式化；不要在组件中声明局部的 `formatBytes`/`formatSize`
- **`src/constants/prompts.ts`：** `DEFAULT_SYSTEM_PROMPT` — 共享默认系统提示词常量；不要在组件中重复

## 代码约定

- **Git commit 消息：** 标题和正文必须用中文书写（conventional commit 前缀如 `feat:`/`fix:`/`ci:`/`docs:` 可保持英文）
- **目标框架：** net10.0-windows
- **根命名空间：** XUnityToolkit_WebUI
- **可空引用类型：** 已启用；**隐式 using：** 已启用
- **AssetsTools.NET 数组访问：** 数组字段结构为 `field → "Array" child → actual elements`；使用 `GetArrayElements()` 辅助方法（在 `AssetExtractionService` 中）解包；不要直接迭代数组字段的 `.Children` 来期望获得元素
- **通知：** 所有用户通知必须通过后端 `SystemTrayService.ShowNotification` — 不要在前端使用浏览器 `Notification` API

## API 端点

- **游戏：** `GET/POST /api/games/`, `GET/DELETE /api/games/{id}`, `POST .../add-with-detection`, `PUT /api/games/{id}`（重命名）, `POST .../detect`, `POST .../open-folder`, `POST .../launch`
- **TMP 字体：** `GET/POST/DELETE /api/games/{id}/tmp-font` — 检查/安装/卸载内置 TMP 字体（与游戏 Unity 版本匹配）；由 `ConfigPanel.vue` 使用
- **框架：** `DELETE /api/games/{id}/framework/{framework}`
- **安装：** `POST /api/games/{id}/install`, `DELETE .../install`（卸载）, `GET .../status`, `POST .../cancel`
- **图标：** `GET /api/games/{id}/icon`（自定义 > exe 图标）, `POST .../icon/upload`, `DELETE .../icon/custom`, `POST .../icon/{search,grids,select}`（SteamGridDB）, `POST .../icon/web-search`, `POST .../icon/web-select`
- **封面：** `GET .../cover`, `POST .../cover/upload`（5MB）, `POST .../cover/{search,grids,select,steam-search,steam-select,web-search,web-select}`, `DELETE .../cover`
- **背景：** `GET .../background`, `POST .../background/upload`（10MB）, `POST .../background/{search,heroes,select,steam-search,steam-select,web-search,web-select}`, `DELETE .../background`
- **配置：** `GET/PUT /api/games/{id}/config`（PUT 时 PatchAsync 读-修改-写）, `GET/PUT .../config/raw`
- **设置：** `GET/PUT /api/settings`, `GET .../version`, `POST .../reset`（删除整个 `paths.Root` 目录，使所有服务缓存失效，重建目录）, `GET .../data-path`, `POST .../export`（ZIP，**非 ApiResult**）, `POST .../import`（multipart ZIP）, `POST .../open-data-folder`
- **对话框：** `POST /api/dialog/{select-folder,select-file}`
- **AI 翻译：** `POST /api/translate`（**非 ApiResult** — DLL 直接调用；前端必须使用原始 `fetch`）, `GET /api/translate/stats`, `GET /api/translate/cache-stats`, `POST /api/translate/test`, `GET /api/translate/ping?gameId=`（来自 LLMTranslate.dll 的连通性 ping）
- **AI 控制：** `POST /api/ai/toggle`, `GET /api/ai/models?provider=&apiBaseUrl=&apiKey=`, `GET /api/ai/extraction/stats`
- **本地 LLM：** `GET/PUT /api/local-llm/settings`（PUT 仅合并 gpuLayers/contextLength）, `GET .../status`, `GET .../gpus`, `POST .../gpus/refresh`, `GET .../catalog`, `GET .../llama-status`, `POST .../test`（需要 Running 状态）, `POST .../start`, `POST .../stop`, `.../download`（模型）+ `/pause` + `/cancel` 变体, `GET .../models`, `POST .../models/add`, `DELETE .../models/{id}`
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
- **"Endpoint" 与 "Provider"：** "translation endpoint" = `LLMTranslate.dll`；"provider" = `ApiEndpointConfig` LLM API 配置

### 同步点

- **InstallStep 枚举：** 在 4 处同步：`Models/InstallationStatus.cs`, `src/api/types.ts`, `InstallProgressDrawer.vue`, `InstallOrchestrator.cs`；还要更新 `GameDetailView.vue` 的 `installStepLabel` 映射
- **添加 AppSettings 字段：** 在 4 处同步：`Models/AppSettings.cs`, `src/api/types.ts`, store 的 `loadPreferences`/`savePreferences`, `SettingsView.vue`
- **添加 AiTranslationSettings 字段：** 在 3 处同步：`Models/AiTranslationSettings.cs`, `src/api/types.ts`, `AiTranslationView.vue`（`DEFAULT_AI_TRANSLATION` + 管线设置 UI）；实时设置（`TermAuditEnabled`, `NaturalTranslationMode`, `EnableTranslationMemory`, `FuzzyMatchThreshold`）在 `AiTranslationView.vue` 中显示；仅预翻译设置（`EnableLlmPatternAnalysis`, `EnableMultiRoundTranslation`, `EnableAutoTermExtraction`, `AutoApplyExtractedTerms`）在 `AssetExtractionView.vue` 中显示
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
- **添加 BuiltInModelInfo 字段：** 在 2 处同步：`Models/LocalLlmSettings.cs`, `src/api/types.ts`；在 `LocalAiPanel.vue` 中显示
- **LocalLlmDownloadProgress 字段：** 在 2 处同步：`Models/LocalLlmSettings.cs`, `src/api/types.ts`；在 `LocalAiPanel.vue` 中显示
- **DataPathInfo：** 在 2 处同步：`Endpoints/SettingsEndpoints.cs`（record）, `src/api/types.ts`
- **添加 UnityGameInfo 字段：** 在 2 处同步：`Models/UnityGameInfo.cs`, `src/api/types.ts`；安装编排器 Step 1 始终重新检测（`DetectAsync`），新字段自动生效无需额外处理
- **添加 AppDataPaths 目录：** 还要更新 `SettingsEndpoints.cs` `/export` 端点中的导出排除列表，如果新目录包含大型/可重新生成/机器特定的数据；`translation-memory/`、`dynamic-patterns/`、`term-candidates/` 被排除在导出之外（可重新生成）
- **PluginHealthReport/HealthCheckItem/HealthCheckDetail 字段：** 在 2 处同步：`Models/PluginHealth.cs`, `src/api/types.ts`；在 `PluginHealthCard.vue` 中显示
- **日志级别同步点：** `Program.cs` `AddFilter` + `FileLoggerProvider` 构造函数 `minLevel` + 前端 `LogView.vue` `selectedLevels` + `levelDefs` — 修改日志级别阈值时四处必须一致

### 构建

- `dotnet build` 自动运行前端；跳过使用 `-p:SkipFrontendBuild=true`
- `build.ps1`：本地构建 — 下载内置资源 → 提取 XUnity 引用 DLL → 更新 classdata.tpk → 前端 → TranslatorEndpoint → Updater (AOT) → 发布到 `Release/win-x64/`；`-SkipDownload` 跳过资源下载；无清单/组件 ZIP、无 MSI（CI `build.yml` 独立处理完整发布构建）；清理：删除 `web.config`、`*.pdb`、`*.staticwebassets.endpoints.json`
- **版本号：** `build.ps1` 通过 `-p:InformationalVersion` 自动生成 `3.4.{YYYYMMDDHHmm}`（CI 使用 `3.4.` 前缀）；**必须使用 `InformationalVersion` 而非 `Version`** — `Version` 设置 `AssemblyVersion`（UInt16 最大 65535），时间戳会溢出
- **多文件发布：** 已移除 `PublishSingleFile`；已移除 `ExcludeFromSingleFile` target；LibCpp2IL.dll 在多文件模式下自然工作
- **附属程序集：** `SatelliteResourceLanguages=en` 从发布输出中剥离所有语言文件夹（cs/de/fr/ja/ko 等）；WinForms 附属资源未使用（UI 是 Vue，原生对话框使用操作系统本地化）
- **数据路径：** 始终为 `%AppData%\XUnityToolkit\`（无便携模式）；`AppData:Root` 配置键允许为开发/测试覆盖
- **AppDataPaths 配置回写：** 在 `Program.cs` 中修改 `appDataRoot` 来源后，**必须**执行 `builder.Configuration["AppData:Root"] = appDataRoot` — 否则 `AppDataPaths`（通过 DI 读取 `IConfiguration`）不会获取新值
- **内置资源：** `bundled/{bepinex5,bepinex6,xunity,llama}/` — BepInEx/XUnity 通过 API 自动检测最新版本；llama.cpp 固定在 b8416（更改 build.ps1/build.yml 中的 `$llamaTag`）；CUDA 12.4；发布后复制
- **TMP 字体：** `bundled/fonts/`（在 git 中跟踪）；发布构建使用 `build.ps1` 发布后 `Copy-Item`
- **TMP 字体兼容性：** 不是所有游戏都使用 TextMeshPro；`UnityGameInfo.HasTextMeshPro`（`bool?`：`true`=有TMP DLL, `false`=Mono 无 TMP, `null`=IL2CPP 或未知）控制安装条件；Mono 游戏通过检查 `{GameName}_Data/Managed/` 中含 `TextMeshPro` 的 DLL 判断；XUnity 日志 `"Cannot use fallback font because it is not supported in this version"` 标志 TMP 不可用
- **PowerShell ZIP：** 不要使用 `Compress-Archive`（在 PowerShell 7.5.5 上已损坏 — 模块加载错误）；使用 `[System.IO.Compression.ZipFile]` 代替
- **更新清单：** 每次发布生成 `manifest-{rid}.json`，包含 SHA256 哈希；组件 ZIP：`app-{rid}.zip`、`wwwroot.zip`、`bundled-llama.zip`、`bundled-fonts.zip`、`bundled-plugins.zip`、`bundled-misc.zip`
- 构建前停止后端：`taskkill //f //im XUnityToolkit-WebUI.exe`
- 默认系统提示词：中文，7 条规则；`{from}`/`{to}` 会被替换；`{0}` 等为字面量
- 日志：`{dataRoot}/logs/XUnityToolkit_YYYY-MM-DD_HH-mm-ss.log`；500 条环形缓冲区 + `LogBroadcast`
- 截图清理：测试后删除项目根目录的 `*.png` 和 `.playwright-mcp/`

### 更新器与 MSI 安装程序

- **更新器：** `Updater/Updater.csproj` (net10.0, PublishAot)；仅 win-x64；`--data-dir` CLI 参数将日志/备份路径指向 `paths.Root`
- **更新器 AOT P/Invoke：** `DllImport`/`const`/`static readonly` 不能在顶级语句中使用 — 必须包装在 `partial class Program` 中；不能使用 `Microsoft.Win32.Registry` — 必须直接 P/Invoke advapi32.dll
- **MSI 安装程序：** `Installer/Installer.wixproj` (WixToolset.Sdk)；每用户安装到 `%LocalAppData%\Programs\`；`build.ps1` 从发布输出自动生成 `Installer/Generated/HarvestedFiles.wxs`；MSI 版本：`{(YYYY-2024)*12+MM}.{DD}.{HH*60+mm}`（所有段在 MSI 限制内：major<256, minor<256, build<65536）
- **MSI + 更新器共存：** Updater.exe 在增量更新后通过 P/Invoke（AOT 安全）同步 HKCU Uninstall 键中的 `DisplayVersion`/`InstallDate`
- **MSI 注册表键：** 由 MSI 写入（`Components.wxs`），由 `Updater/Program.cs` 读取（MsiProductCode, InstallDir）；`DataPath` 键由 MSI 写入仅用于 `RemoveFolderEx` 清理 — 应用不再读取它；键路径：`HKCU\Software\XUnityToolkit`
- **安装程序许可证：** `Installer/License.rtf` 必须与项目根目录 `LICENSE` 匹配（版权持有人、许可类型）

### WiX 注意事项

- **保留属性：** `PublishDir` 和 `SourceDir` 是 MSBuild/WiX SDK 的保留属性，会被静默覆盖；使用自定义名称（例如 `AppPublishDir`）并通过 `-p:AppPublishDir=...` 传递
- **路径解析：** WiX 相对于 `.wixproj` 目录解析 `Source` 路径，不是相对于 CWD；在 `.wixproj` 中使用 `IsPathRooted` 处理绝对和相对路径输入；不要在 WiX 构建中设置 `-p:OutputPath`（会干扰文件解析）
- **每用户 ICE 错误：** 每用户安装（`Scope="perUser"`）触发 ICE 误报；`SuppressValidation=true` 跳过所有 ICE 检查（也加快构建）
- **WixUI 变量覆盖：** `WixUILicenseRtf` 等必须是 `.wxs` 中的 `<WixVariable>`，不是 `.wxl` 中的 `<String>` — 本地化字符串在 WiX v5 中不适用于 WixUI 变量覆盖
- **MSI 代码页：** MSI 数据库代码页默认为 1252（西方）；MSI 内部字符串（例如 `DowngradeErrorMessage`）中的中文字符会导致 WIX0311 错误；对 MSI 级别的字符串使用英文
- **v5 元素语法：** `<String>` 使用 `Value` 属性（非内部文本）；`<Publish>` 使用 `Condition` 属性（非内部文本）；内部文本在 WiX v5 中已过时
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

### 杂项

- **gitignore：** `docs/` 被 gitignore；提交规格/计划文档时使用 `git add -f`
- **gitignore 取反：** `bundled/`（目录模式）阻止子级取反；使用 `bundled/*`（通配符）以允许 `!bundled/fonts/` 和 `!bundled/script-tag-presets.json`
