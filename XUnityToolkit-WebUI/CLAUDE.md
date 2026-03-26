# XUnityToolkit-WebUI 后端

ASP.NET Core 后端。项目概览、API 端点和构建命令请参阅根目录 `CLAUDE.md`。

## 核心

- **无迁移代码：** 项目处于预稳定阶段——不做向后兼容的迁移或旧格式转换
- **关键基础设施服务：** `GameLibraryService`（内存中的游戏库增删改查）、`AppSettingsService`（设置缓存）、`ConfigurationService`（INI 读写/补丁）、`UnityDetectionService`（游戏检测）、`GameImageService`（图标/封面/背景管理）
- `AppSettingsService`：内存缓存；`GetAsync()` 无磁盘 I/O；不要修改返回的对象——使用 `UpdateAsync`/`SaveAsync`
- **数据存储：** 所有运行时数据存储在 `%AppData%\XUnityToolkit\`；`AppDataPaths` 集中管理路径；随附资源（`bundled/`、`wwwroot/`）保留在程序根目录
- **敏感数据加密：** `DpapiProtector`（DPAPI CurrentUser）加密 `ApiEndpointConfig.ApiKey` 和 `SteamGridDbApiKey`；前缀 `ENC:DPAPI:` + Base64；在 `AppSettingsService.ReadAsync`/`WriteAsync` 边界加解密；解密失败时保留密文并创建 `.bak` 备份
- **DI 之前的路径：** `Program.cs` 在 DI 之前读取 `builder.Configuration["AppData:Root"]` 并回退到 `%AppData%\XUnityToolkit\`——必须与 `AppDataPaths._root` 的公式保持同步
- **应用 URL：** `http://127.0.0.1:{port}` 默认 `51821`；**必须使用 `127.0.0.1` 而非 `localhost`**（Unity Mono 会解析为 IPv6 `::1`）；端口可通过 `settings.json` → `aiTranslation.port` 配置（在 `Program.cs` 中 DI 之前读取）
- **静态文件缓存：** `UseStaticFiles` 为 `/assets/*`（Vite 内容哈希）设置 `Cache-Control: public, max-age=31536000, immutable`；根文件（`index.html`、`favicon.ico`）设置 `no-cache`；`MapFallbackToFile` 也为 `index.html` 设置 `no-cache`——两处必须分别配置
- 命名 `HttpClient`：`"LLM"`（120s/200conn）、`"SteamGridDB"`（30s）、`"LocalLlmDownload"`（12h）、`"WebImageSearch"`（15s，浏览器 UA）、`"GitHubUpdate"`（60s）、`"GitHubCdn"`（30s，CDN/Web 请求）
- **Multipart 上传端点：** 必须在 `MapPost` 上链式调用 `.DisableAntiforgery()`——否则 ASP.NET 会拒绝 `multipart/form-data` 请求
- **Updater AOT 约束：** `Updater/` 目标框架为 `net10.0`（非 `-windows`），`PublishAot=true`，`InvariantGlobalization`；不能使用 `JsonSerializer` 反射——必须使用手动字符串拼接生成 JSON；不能使用 WinForms 或 UI 框架
- **Updater 路径遍历验证：** Phase 1（备份）、Phase 2（替换）和 Phase 3（删除）都必须使用 `Path.GetFullPath` + `StartsWith(normalizedAppDir)` 验证目标路径在 appDir 内
- **InformationalVersion 陷阱：** .NET SDK 会附加 `+commitHash` 后缀；版本比较前必须 `Split('+')[0]`
- **GitHub API JSON：** `JsonOptions` 使用 `CamelCase`；GitHub API 返回 snake_case（`tag_name`、`browser_download_url`）——`GitHubRelease`/`GitHubAsset` 模型使用 `[JsonPropertyName]` 覆盖；任何新的 GitHub API 模型属性也必须使用显式 `[JsonPropertyName("snake_case")]`
- **GitHub API 速率限制：** 未认证的 ETag/`If-None-Match` 304 响应**仍然计入** 60 次/小时的限制——只有认证的 304 才豁免；CDN（`objects.githubusercontent.com`）和 Web（`github.com`）请求有独立的、更宽松的限制
- **更新检查三层策略：** CDN（`update-check.json` 资源）→ Atom Feed（`/releases.atom`）→ GitHub API 兜底；提取抛出 `InvalidOperationException` 的方法时（如速率限制），调用方**必须**仍然将 `_status` 设为 Error 并通过 SignalR 广播——否则状态会卡在 `Checking`
- **镜像：** `AppSettings.HfMirrorUrl`；用于模型下载的 HF 主机替换；插件/llama 二进制文件是捆绑的（无运行时 GitHub 下载）
- **触发即忘（Fire-and-forget）：** `Task.Run` 中使用 `CancellationToken.None`；`CancellationTokenSource` 字典用于用户取消
- **触发即忘 SignalR 不变量：** 当触发即忘的 `Task.Run` 包装异步服务调用，且前端通过 SignalR 完成事件重置状态时，端点处理器必须在失败/取消路径也广播完成信号——服务可能仅在成功时广播；否则前端状态会永久卡住；始终将整个主体包装在 `try/catch` 中，并在 `catch` 中广播错误
- **SignalR 错误广播：** 在触发即忘的 `catch (Exception ex)` 块中通过 SignalR 广播错误时，使用静态错误消息（如 `"字体生成失败，请检查字体文件格式"`），不要使用 `ex.Message`——通过 `ILogger` 在服务端记录异常
- **端点返回模式：** 始终使用 `Results.Ok(ApiResult<T>.Ok(...))` 包装返回值——直接返回 `ApiResult<T>`（不用 `Results.Ok()`）会绕过配置的 `JsonSerializerOptions`，可能产生不同的枚举大小写
- **验证错误 HTTP 状态码：** 输入验证失败时始终使用 `Results.BadRequest(ApiResult.Fail(...))`——绝不使用 `Results.Ok(ApiResult.Fail(...))`（对错误返回 200，令客户端困惑）
- **更新端点错误码：** `/api/update/apply` 前置条件失败（活跃操作）→ 409 Conflict；下载/应用服务错误 → 500；`InvalidOperationException` 来自 check → 400
- **HTTP Range 416：** 通过 `Content-Range` 验证完整性；大小不匹配 → 删除并重新开始
- `WebApplication.CreateBuilder()` 之前设置 `Console.OutputEncoding = UTF8`
- P/Invoke：使用 `[DllImport]` 而非 `[LibraryImport]`；重命名方法时 → 搜索所有调用点
- **对话框前景窗口：** `DialogEndpoints.ForceForegroundWindow` 使用 `AttachThreadInput`——不要简化为裸 `SetForegroundWindow`（从后台调用时会静默失败）
- **文件上传安全：** 始终对上传的文件名使用 `Path.GetFileName(file.FileName)`——`Path.Combine` 不能防止恶意文件名的路径穿越
- **每游戏数据清理：** 添加新的每游戏数据目录时，必须在 `DELETE /api/games/{id}` 处理器（`GameEndpoints.cs`）中添加清理 + 如果服务有 `RemoveCache` 则进行缓存驱逐（如 `termService.RemoveCache`、`scriptTagService.RemoveCache`）
- **服务缓存清除：** `TermService.ClearAllCache()`、`ScriptTagService.ClearAllCache()`、`TranslationMemoryService.ClearAllCache()`、`DynamicPatternService.ClearAllCache()`、`TermExtractionService.ClearAllCache()` 清除所有内存缓存；用于设置重置（`POST /api/settings/reset`）和设置导入（`POST /api/settings/import`）；`RemoveCache(gameId)` 清除单个游戏的缓存
- **`ClearAllCache` 释放不变量：** 有每游戏 `ConcurrentDictionary<string, SemaphoreSlim>` 锁的服务必须在 `ClearAllCache()` 中释放所有信号量（遍历 `.Values` → `.Dispose()` → `.Clear()`）；与 `RemoveCache` 模式一致
- **设置重置/导入缓存失效：** `/reset` 和 `/import` 都必须调用全部 7 个失效操作：`settingsService.InvalidateCache()`、`termService.ClearAllCache()`、`scriptTagService.ClearAllCache()`、`tmService.ClearAllCache()`、`dynamicPatternService.ClearAllCache()`、`extractionService.ClearAllCache()`、`cacheMonitor.UnloadCache()`——遗漏任何一个都会留下过期的内存状态
- **设置重置日志挂起：** `POST /api/settings/reset` 在删除数据目录前调用 `FileLoggerProvider.SuspendFileLog()`（释放日志文件句柄），然后在 `finally` 中调用 `ResumeFileLog()`；不要用按子目录删除替代整目录删除（此前导致清理不完整）
- **捆绑文件构建复制：** `bundled/` 中的新文件需要在 `.csproj` 中添加 `<Content CopyToOutputDirectory="PreserveNewest" Link="bundled\...">`——`build.ps1` 仅在发布时运行，`dotnet run` 使用构建输出
- **`ScriptTagService` DI：** `AddSingleton`；遵循 `TermService` 模式（SemaphoreSlim + ConcurrentDictionary 缓存 + 原子文件写入）；预设在 `GetAsync` 中自动更新；保存/自动更新时使编译正则缓存失效
- **`BepInExPluginService` DI：** `AddSingleton`；扫描 `BepInEx/plugins/` 目录列出插件；使用 `MetadataLoadContext` 安全反射读取 DLL 元数据（GUID/Name/Version）——不加载到 AppDomain；toolkit-managed 插件（XUnity、LLMTranslate）通过 `IsToolkitManaged()` 保护，禁止修改/卸载；启用/禁用通过 `.disabled` 文件后缀实现；上传端点需要 `.DisableAntiforgery()`（multipart）；所有端点要求 BepInEx 已安装状态
- **日志级别配置：** `Program.cs` 有两层过滤：`AddFilter("XUnityToolkit_WebUI", LogLevel.Debug)`（ASP.NET 管线）+ `FileLoggerProvider(logsDirectory, LogLevel.Debug)`（提供者）；两者都必须 ≤ 期望级别，否则日志会被静默丢弃
- **静态方法日志模式：** 静态方法无法访问 `ILogger`——要么传递 `ILogger? log = null` 参数并使用 `log?.LogDebug(...)`，要么在有 `logger` 可用的实例方法调用点添加日志；仅需聚合信息（计数、前后对比）时优先使用调用点日志
- 读取日志文件：必须使用 `FileShare.ReadWrite` 以避免 `IOException`
- **C# `[GeneratedRegex]` 与引号：** 原始字符串字面量（`"""..."""`）在正则包含 `"` 时会失败——改用常规转义字符串
- **`Lock` 类型 API（.NET 9+）：** 使用 `_lock.Enter()`/`_lock.Exit()`——不要使用 `Monitor.Enter(_lock)`/`Monitor.Exit(_lock)`（CS9216 警告；`Lock` 不是基于 `object` 的）
- **每游戏锁模式：** 有每游戏数据文件的服务（`TranslationMemoryService`、`DynamicPatternService`）使用 `ConcurrentDictionary<string, SemaphoreSlim>` 进行每游戏加锁——不要对多游戏服务使用单个全局 `SemaphoreSlim`

## INI 配置

- **绝不从头生成 XUnity 配置**——始终使用 `PatchAsync` 读改写
- 配置文件名：`AutoTranslatorConfig.ini`，位于 `BepInEx/config/`
- PatchAsync null 语义：`null` = 跳过，`""` = 清除值
- `ApplyOptimalDefaultsAsync` 设置 `[General] Language = zh`、`OverrideFont = Microsoft YaHei`、`Endpoint = LLMTranslate` 及 `FallbackEndpoint = GoogleTranslateV2`
- **敏感节（导出时需脱敏）：** `GoogleLegitimate`、`BingLegitimate`、`Baidu`、`Yandex`、`DeepLLegitimate`、`PapagoTranslate`、`LingoCloud`、`Watson`、`Custom`、`LecPowerTranslator15`、`ezTrans`

## 术语提取

- `GlossaryExtractionService`：缓冲 → 触发 → 排空 → LLM 提取 → 过滤 DNT 术语 → 合并；依赖 `TermService` 排除 DoNotTranslate 类型的术语（通过提示词暗示和合并前的硬过滤两种方式）；chunk 处理使用 `Parallel.ForEachAsync(MaxDegreeOfParallelism: 3)` 匹配信号量容量——不要用 `Task.WhenAll` 同时启动所有 chunk（会导致大量信号量超时和无效重排队）
- **TermEntry 模型：** `Type`（Translate/DoNotTranslate）、`Original`、`Translation`、`Category`、`Description`、`IsRegex`、`CaseSensitive`、`ExactMatch`、`Priority`
- **关键：** 设置检查（异步）必须在缓冲排空之前——否则禁用时翻译对会丢失
- `TryTriggerExtraction` 是同步的（热路径）；异步工作延迟到 `DrainAndExtractAsync`
- DLL 必须在 `POST /api/translate` 中发送 `gameId`——需要 INI 中的 `[LLMTranslate] GameId`
- **所有翻译路径**都必须调用 `BufferTranslation` + `TryTriggerExtraction`，以 `!string.IsNullOrEmpty(gameId)` 且 `!isLocalMode` 作为条件守卫

## 并发与性能

- **热路径：** `POST /api/translate` 接收 100+ 请求/秒；所有 I/O 必须使用内存缓存，绝不每请求访问磁盘
- `SemaphoreSlim`：每批一个槽位；`EnsureSemaphore` 延迟 Dispose 3 分钟；60s 超时 → 503；**关键：** 信号量等待和 LLM 调用放在不同的 `try` 块中；**本地模式批处理拆分：** `TranslateAsync` 循环单文本的 `TranslateBatchAsync` 调用，使 `_translating` 显示 1（而非批大小）
- **信号量释放守卫：** 释放信号量的 `finally` 块必须检查 `if (semaphoreAcquired)`——超时/取消后无条件 `Release()` 会损坏信号量计数；统计计数器如 `_translating` 同理
- **热路径缓存：** 绝不在热路径上调用 `GameLibraryService.GetByIdAsync`；使用 `ConcurrentDictionary` + 显式失效
- `BroadcastStats`：CAS 节流 200ms；完成/错误时使用 `force: true`，以及所有 `TranslateBatchAsync` 状态转换（`_queued++/--`、`_translating++/--`）——非强制广播会被静默丢弃，因为它们触发时正处于调用方广播的节流窗口内，导致中间管线状态对前端不可见
- **统计计数器单位：** `_queued` 和 `_translating` 按 API 请求计数（每次 `TranslateBatchAsync` 调用 ±1）；`_totalReceived` 和 `_totalTranslated` 按单条文本计数；`TranslationStats` 中的 `MaxConcurrency` 反映 `_currentMaxConcurrency`
- **`TranslationStats.Queued` 是计算字段：** `max(0, TotalReceived - TotalTranslated - _totalFailedTexts)`——不是从 `_queued` 读取；`_queued` 仅内部用于信号量级别的跟踪/日志；`_totalFailedTexts` 追踪失败的 `TranslateAsync` 调用中的文本数以保持队列平衡；`TranslateAsync` 使用 `bool completed` 标志 + `finally` 块确保在所有失败路径上递增 `_totalFailedTexts`
- **RecordError：** `LlmTranslationService.RecordError` 是唯一的记录点——端点的 catch 不得重复计数
- `volatile` vs `Volatile.Read`：不要混用；`DateTime?` → `long` ticks + `Interlocked`；async 不能有 `ref`/`in`/`out` → 包装类
- **`Volatile.Read` 一致性：** 当字段使用 `Volatile.Read`/`Volatile.Write` 时，所有访问点都必须使用它们——单次直接读取（如统计快照中）会绕过内存屏障，可能返回过期值
- **ConcurrentDictionary 迭代安全性：** 在并发修改时迭代 `.Keys`（如 `ClearAllCache` 与 `Add` 插入同时发生）可能遗漏条目；使用 `.Keys.ToList()` 在修改前创建快照
- **CTS 原子交换：** 用可空 `CancellationTokenSource` 字段守卫"同一时间只有一个操作"时，使用 `Interlocked.CompareExchange(ref _cts, newCts, null)`——不要用 `if (_cts is not null) throw; _cts = new...`（TOCTOU 竞态）
- **去抖动中的 CTS 替换：** 在 `ConcurrentDictionary` 中替换 CTS 时，先写入新 CTS，然后再取消/释放旧的——避免并发代码找不到任何条目的窗口期
- **CTS 去抖动原子交换：** `_dict.GetValueOrDefault(key)` + `_dict[key] = newCts` 不是线程安全的——并发调用者可能孤立一个 CTS（泄漏后台任务）或双重释放旧 CTS；使用 `GetOrAdd`/`TryUpdate` CAS 循环模式
- **CTS finally 中的原子清理：** 使用 `Interlocked.Exchange(ref _cts, null)?.Dispose()`——不要用 `var cts = _cts; _cts = null; cts?.Dispose()`（非原子的读-置空-释放允许并发 `.Cancel()` 在已释放的 CTS 上执行）
- **`await using` 提前关闭：** 当流必须在后续代码读取文件之前关闭时，使用块作用域 `{ await using var s = ...; }` 而非调用 `s.Close()`（与 `await using` 隐式 dispose 冗余）
- **插件并发：** DLL 10×10 = 100 条文本；Mono >15 连接会死锁——改用批处理
- **XUnity HTTP：** Mono `DefaultConnectionLimit` = 2 → `FindServicePoint(uri).ConnectionLimit`；无 `Connection: close`（CLOSE_WAIT bug）
- **预翻译：** 对每批 10 条使用 `Parallel.ForEachAsync`（本地模式：批=1，并行度=1）；CAS 节流 200ms 进度；`catch (OperationCanceledException) when (ct.IsCancellationRequested)` 守卫防止 HTTP `TaskCanceledException` 中止整个操作——在 `Parallel.ForEachAsync` 主体中**始终使用 `when` 守卫**
- **顺序 LLM 循环中的 `when` 守卫：** `DynamicPatternService`/`TermExtractionService` 批处理循环也需要 `catch (OperationCanceledException) when (ct.IsCancellationRequested)`——裸 `catch (OperationCanceledException) { throw; }` 会捕获 HTTP 超时的 `TaskCanceledException` 并在单次超时时中止所有剩余批次
- **LLM 重试：** `TranslateBatchAsync` 对瞬态错误（`TaskCanceledException`、`HttpRequestException`、`TimeoutException`）最多重试 2 次，指数退避（2s、4s）；重试循环在信号量获取块内；`IsTransientError` 辅助方法判断可重试性
- **TM 去抖动持久化：** `TranslationMemoryService.Add()` 是同步的（仅内存）；持久化通过 `ScheduleDebouncedPersist` 去抖动 5s；`FlushAsync` 在批处理完成后立即持久化；每游戏 `SemaphoreSlim` 锁（非全局）

## AI 翻译上下文

- **批处理模式：** 整批作为一次 LLM 调用；JSON 数组 I/O
- **翻译记忆：** 每游戏易失；云端 `ContextSize`（10，最大 100），本地 `LocalContextSize`（0，最大 10）
- **游戏描述：** `Game.AiDescription`；`_descriptionCache` 在 `PUT /description` 时失效；截断到 500 字符
- **多阶段管线：** Phase 0（TM 查找）→ Phase 1（自然模式，仅云端）→ Phase 2（占位符替换）→ Phase 3（强制纠正）；`TermAuditService` 在阶段间验证合规性；`NaturalTranslationMode` 和 `TermAuditEnabled` 控制阶段
- **翻译记忆 Phase 0：** 在 `TranslateAsync` 中插入于 Phase 1 之前；`phase0Resolved` HashSet 追踪 TM 已解决的索引；Phase 1/2/3 过滤循环必须跳过这些索引；`perTextTranslationSource` 数组追踪每条文本的 `"tmExact"`/`"tmFuzzy"`/`"tmPattern"`；TM 命中在接受前经过 `TermAuditService` 验证
- **SystemPrompt 顺序：** 模板 → 描述 → 术语 → 记忆 → [文本]
- **添加 SystemPrompt 段落：** 新参数必须贯穿：`TranslateAsync` → `TranslateBatchAsync` → `CallProviderAsync` → 全部 8 个提供商分支 → `Call*Async` → `BuildSystemPrompt`；同时更新 `TestTranslateAsync`（传 `null`）
- **ParseTranslationArray：** 先去除 `<think>...</think>` 再提取 JSON 数组（处理无围栏情况）
- **`CallLlmRawAsync`：** `Task<(string content, long tokens)> CallLlmRawAsync(ApiEndpointConfig endpoint, string systemPrompt, string userContent, double temperature, CancellationToken ct)`——返回元组，必须解构 `var (content, _) = await ...`；无信号量的公共任意 LLM 调用方法；被 `GlossaryExtractionService`、`BepInExLogService`、`DynamicPatternService`、`TermExtractionService` 使用；端点选择：`OrderByDescending(e => e.Priority)`（值越高越优先，与 `CalculateScore` 一致）
- **测试与翻译端点的差异：** `TestTranslateAsync` 直接使用请求体中的端点；`TranslateAsync` 通过 `settingsService.GetAsync()` 读取已存储的设置。设置未持久化时测试可能通过但翻译失败——调试"没有可用的AI提供商"错误时始终检查两条路径
- **Gemini 认证：** 使用 `x-goog-api-key` HTTP 头，不要用 URL 查询参数 `?key=`——URL 参数会在异常消息、日志和 HTTP 跟踪中泄露
- **占位符旁路：** 当整个输入文本是单个占位符（`{{G_x}}`/`{{DNT_x}}`）时，直接预计算结果并跳过 LLM 调用——LLM 对保留占位符不可靠；预计算结果跳过 `ApplyGlossaryPostProcess` 但仍经过术语审计（计入 phase2Pass 统计）；`preComputed` 字典追踪这些索引
- **提示词中的术语：** 在云端模式下，所有翻译类型的术语条目（包括非正则）都保留在系统提示词中，即使使用了占位符——不要过滤为仅正则。在本地模式下，`promptTerms` 设为 `null` 以节省上下文 token；术语执行仅依赖占位符替换（非正则）和 `ApplyGlossaryPostProcess`（正则 + 兜底）
- **术语提示词标注：** `AppendTermAnnotation` 格式化 Category（`GetCategoryLabel` → 角色/地点/物品/技能/组织/通用）+ Description；Phase 1 使用全角 `（）`，Phase 2 使用半角 `()`；两个阶段都在发送给 LLM 的术语列表中包含类别和描述
- **`AppendTermAnnotation` 括号格式：** 全角直接追加 `（`；半角直接追加 `(`——不要在 `(` 前插入前导空格；两种都以匹配的右括号结束
- **术语子串保护：** `ApplyGlossaryPostProcess`、Phase 3 强制纠正和 `TermAuditService` 都使用受保护区间追踪，防止较短术语破坏较长术语翻译中的子串（如 `Settings`→`设置` 不得替换 `SaveSettings.es3`→`SaveSettings.es3` 中的 `Settings`）；`TermAuditService` 按最长优先排序术语，跳过被更长已通过术语包含的短术语审计；任何对术语原文/译文进行 `string.Replace` 的新代码必须遵循此模式
- **空翻译守卫：** LLM 可能对不可翻译文本（插件名、缩写）返回 `""`；XUnity.AutoTranslator 将空翻译视为错误——5 次连续错误触发翻译器自动关闭；`TranslateAsync` 在翻译为空/空白时必须回退到原文

## 预翻译缓存监控

- **懒加载：** `EnsureCacheAsync(gameId, toLang, ct)` 从 `POST /api/translate` 调用；每游戏读取一次 `_PreTranslated.txt`；`_loadAttemptedForGameId` 防止重复尝试（即使文件不存在）；`UnloadCache()` 重置尝试追踪
- **热路径守卫：** 初始加载后，`EnsureCacheAsync` 是单次 `volatile` 读取——稳态时无锁竞争
- **依赖：** `GameLibraryService` + `AppSettingsService`（都有内存缓存）；`GetByIdAsync` 仅在游戏切换时调用，非每请求调用
- **IDisposable：** `PreTranslationCacheMonitor` 实现 `IDisposable`——关闭时释放 `_summaryTimer` 和 `_loadLock`

## 资源提取（AssetsTools.NET）

- `MonoCecilTempGenerator`/`Cpp2IlTempGenerator` 都在 `AssetsTools.NET.Extra` 中；IL2CPP：完全限定名 `AssetsTools.NET.Cpp2IL.Cpp2IlTempGenerator`
- **`classdata.tpk`：** 作为资源嵌入；由 `build.ps1` 从 [AssetRipper/Tpk](https://github.com/AssetRipper/Tpk) CI 自动更新（需要 `gh` CLI）；已提交的文件作为兜底
- `LoadAssetsFile()` 持有文件句柄——每次迭代必须 `UnloadAssetsFile()`
- **Bundle 文件：** `LoadBundleFile(path, true)` → 遍历 DirectoryInfos（跳过 `.resource`/`.resS`）→ `LoadAssetsFileFromBundle` → `UnloadBundleFile`
- **TypeTree 兜底：** bundle 通常嵌入类型树——检查 `afile.Metadata.TypeTreeEnabled`
- 安装流程自动提取 → 检测语言 → 补丁 `[General] FromLanguage` → 缓存；失败不阻塞安装
- **语言检测：** 基于比例；拉丁 >80% → 立即判定英语；非拉丁需要 ≥50 字符且占总量 ≥2%；日语需要假名占 CJK+假名的 ≥5%；默认为 `"en"`（非 `"ja"`）；`IsGameText` 是启发式排除过滤器
- **`IsGameText` 路径过滤：** 路径检测需要无空格启发式——Unix 路径使用 `text.Contains('/') && text.Contains('.') && !text.Contains(' ')`；Windows 路径使用 `text.Contains(":\\")`；带 `/` 和 `.` 的自然文本（如富文本关闭标签 `</b>`、带句号的对话）保留；反斜杠路径同样要求无空格
- **`CollectStrings` 深度：** 递归限制为 20（支持 GameCreator 2 等深度嵌套框架）
- **TextAsset JSON 检测：** 对于将结构化数据存储为 JSON TextAsset 的游戏（如 VIDE 对话），在 TextAsset 提取循环中添加检测（返回 `List<ExtractedText>?` 的 `TryExtract*`），在通用 `ExtractTextLines` 兜底之前；使用 `System.Text.Json.JsonDocument` 解析；返回 `null` 则回退到通用提取
- **GameCreator 2 支持：** `DetectGameCreatorDialogueType` 识别 Dialogue/Quest/Actor/Stat 类型；类型特定的提取使用 `GameCreator:{Type}:{Name}` 来源标签；`DetectAndLogTemplateVariables` 扫描 `{Variable}` 模式（如 `{PC}`、`{M}`）并记录 DNT 术语建议
- XUnity 缓存格式：`encoded_original=encoded_translation`；转义 `\\`、`\n`、`\r`、`\=`；`XUnityTranslationFormat` 静态类
- **OutputFile 中的 `{Lang}`：** 用 `config.TargetLanguage` 替换；防范路径穿越

## 字体替换（AssetsTools.NET）

- **TMP_FontAsset 检测：** 具有 `m_Version` + `m_GlyphTable` 字段的 MonoBehaviour = v1.1.0（支持）；`m_fontInfo` + `m_glyphInfoList` = v1.0.0（不支持）
- **字体扫描类数据库兜底：** 当 `LoadClassDatabaseFromPackage` 失败且无嵌入类型树时，跳过 MonoBehaviour 扫描但仍尝试 `AssetClassID.Font` 扫描——TTF Font 资源使用原始 classId 匹配（`GetAssetsOfType`），在格式版本 >= 16 时无需类数据库即可工作；`GetBaseField` 仍可能失败（每资源捕获）；同时扫描 `{GameName}_Data/` 顶层（不仅是 `StreamingAssets/`）的 `*.bundle` 文件
- **安全深拷贝：** `DeepCopyFieldValues` 按字段名匹配递归复制值，保留目标的类型树结构以实现跨 TMP 版本兼容；`RebuildArrayEntries` 从目标的 `TemplateField` 创建新数组条目以避免序列化不匹配；除数据字段外还复制 `m_AtlasPopulationMode` 和 `m_IsMultiAtlasTexturesEnabled`；不要触碰 PPtr 字段（`material`、`m_SourceFontFile`、`m_FallbackFontAssetTable`）；`m_AtlasTextures` 由多图集逻辑单独处理
- **Bundle 备份优化：** `EnsureBackup` 在文件句柄释放后使用 `File.Move`（同卷上瞬时完成）；`EnsureBackupCopy`（File.Copy）用于句柄可能打开的零散文件；通过 `Path.GetPathRoot` 检测是否同卷
- **多图集替换：** `ReplaceSingleFont` 通过 `List<SourceAtlasPage>` 读取所有源图集页；替换现有目标纹理；为额外页创建新 Texture2D 资源（通过 `AssetInfos.Max(PathId) + 1` 获取全局唯一 PathId）；更新 `m_AtlasTextures` PPtr 数组
- **纹理替换：** `AssetsTools.NET.Texture` v3.0.2（最新）；API 使用 `TextureFile.ReadTextureFile()` → `SetPictureData()`/`FillPictureData()` → `WriteTo()`；替换嵌入纹理后清除 `m_StreamData`（path/offset/size）
- **新 Texture2D 陷阱：** `ValueBuilder.DefaultValueFieldFromTemplate` 创建值全为零的字段；`TextureFile.ReadTextureFile(zeroedField)` → m_MipCount=0，m_TextureDimension=0——Unity 不会渲染；必须 `ReadTextureFile(existingTexture)` 继承元数据，然后通过 `SetPictureData` + `WriteTo` 覆盖格式/数据/尺寸
- **`TextureFile` 字段覆盖范围：** `WriteTo` 写入全部 31 个纹理字段；`SetPictureData` 仅设置 5 个（width、height、data、completeImageSize、streamData）——其他元数据（m_MipCount、m_TextureSettings、m_IsReadable、m_TextureDimension、m_ImageCount、m_ColorSpace）必须来自 `ReadTextureFile` 源
- **SDF 图集纹理元数据：** 生成 SDF 图集纹理时，必须显式设置 `m_MipCount=1, m_MipMap=false, m_StreamingMipmaps=false, m_IsReadable=true`——模板纹理可能启用了 mipmap，继承这些设置会导致黑色条纹伪影（SDF 距离值在 mipmap 平均降采样时不能正确缩小）
- **`AssetFileInfo.Create`（v>=16）：** `TypeIdOrIndex` = 原始 classId（非索引）；不会添加 TypeTreeType——元数据中必须已有该 classId 的类型树条目
- **Bundle 写入模式：** 修改资源 → `DirectoryInfos[i].SetNewData(afile)` → 写入未压缩的 `.tmp` → 重新读取 → `Pack(writer, LZ4)` → 删除 tmp → 移动到原位
- **Addressables CRC：** 在 `catalog.json` 中正则置零 `"Crc"\s*:\s*\d+`；删除 `catalog.hash`；`catalog.bundle` 包含带 JSON 的 TextAsset
- **备份命名：** 从游戏根目录的相对路径，分隔符替换为 `_`（如 `XXX_Data_sharedassets0.assets`）
- **外部恢复检测：** 清单中存储 SHA256 哈希值，在 `GET .../status` 时比较；将哈希计算包装在 `Task.Run` 中（文件可能有数百 MB）
- **自定义字体自动解析：** `ReplaceFontsAsync` 先检查 `data/custom-fonts/{gameId}/`，再回退到捆绑字体；`GetStatusAsync` 返回 `CustomTtfFileName`/`CustomTmpFileName` 供前端显示；`DELETE .../custom-font?type={ttf|tmp}` 清除特定类型，无参数时清除全部
- **`FontReplacementService` DI：** 依赖 `TmpFontService`、`BundledAssetPaths`、`AppDataPaths`；`BundledAssetPaths` 用于 TTF 捆绑字体路径解析（优先于 `Assembly.GetExecutingAssembly().Location`，后者在单文件发布中会失败）
- **TTF 替换：** `ReplaceSingleTtfFont` 替换 `AssetClassID.Font` 资源中的 `m_FontData` 字节数组；保留所有布局元数据（`m_FontSize`、`m_LineSpacing` 等）
- **捆绑 TTF：** `bundled/fonts/SourceHanSansCN-Regular.ttf`（约 10MB）用作 TTF 字体的默认替换源
- **自定义字体共存：** `data/custom-fonts/{gameId}/` 可同时存放 AssetBundle（用于 TMP）和 TTF/OTF（用于 TTF）；通过扩展名区分文件；上传端点使用魔术字节（`00 01 00 00` TTF，`4F 54 54 4F` OTF）进行格式检测
- **TMP 源过滤：** `ReplaceFontsAsync` 自定义字体自动解析按扩展名过滤（从 TMP 源中排除 `.ttf`/`.otf`，从 TTF 源中排除非 TTF）
- **创建新数组条目：** `ValueBuilder.DefaultValueFieldFromTemplate(prototype.TemplateField)` 从现有条目的模板创建新字段实例；使用第一个数组子元素作为原型，逐条目克隆，设置值，然后赋值 `array.Children = newList`

## 字体生成（FreeTypeSharp + Felzenszwalb EDT）

- **SDF 管线（忠实于 Unity）：** `FT_RENDER_MODE_NORMAL` → AA 位图 → `DistanceFieldGenerator.GenerateSdf()`（Felzenszwalb EDT）→ 带 padding 的 SDF；复制 Unity 的 FontEngine 方法；不要使用 `FT_RENDER_MODE_SDF`（基于轮廓，与 Unity 产生不同结果）
- **`DistanceFieldGenerator`：** 静态类；**关键：`outside` 字段必须初始化为 0（非 INF）**——padding 区域在字形外部；`inside` 字段初始化为 INF；v≥1 的像素必须显式设置 `outside=INF`；SDFAA 使用边缘居中的亚像素播种 `(0.5-v)²`/`(v-0.5)²`（边缘在 v=0.5 处；不要使用 `v²`/`(1-v)²`）；SDF8/16/32 在阈值 128 处使用二值初始化；Felzenszwalb 1D→2D 抛物线包络；上采样模式使用双线性降采样；**归一化必须除以 `padding * upsampling`**；参考：TinySDF（`gridInner.fill(0)`、`gridOuter.fill(INF)`）
- **渲染模式：** SDFAA（1x，默认）、SDF8（8x）、SDF16（16x）、SDF32（32x 上采样）；`AtlasRenderMode` 枚举：SDFAA=4165、SDF8=4168、SDF16=4169、SDF32=4170；约束：`samplingSize × upsampling ≤ 16384`
- **Padding：** 动态计算——百分比模式 `(int)(samplingSize * percent / 100)` 或像素模式；最小值 1；`GradientScale = padding + 1` 注入到 Material `m_SavedProperties.m_Floats` 中；`m_AtlasPadding` 必须匹配
- **SDF 位图包含 padding：** `GlyphData` 中的 `BitmapWidth/Height` 是含 padding 的尺寸；打包直接使用含 padding 的大小（无双重 padding）；`GlyphRect` 引用内部字形区域（`AtlasX + padding`，不含 padding 的宽度）；`UsedGlyphRects` 引用完整含 padding 的区域
- **自动大小：** `AutoSizeSamplingSize` 二分搜索（15 次迭代）；采样 200 个字形估算面积；初始最大值 = `sqrt(atlasArea / count) × 3`；百分比模式时重新计算 padding
- **FreeTypeSharp：** v3.1.0 原始 unsafe P/Invoke（捆绑 FreeType 2.13.2）；csproj 中需要 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`；`FT_LOAD` 是枚举不是 int——直接使用 `FT_LOAD.FT_LOAD_NO_BITMAP | FT_LOAD.FT_LOAD_NO_HINTING`（无需强制转换）
- **图集 Y 轴：** FreeType 位图是自上而下的（Y=0 在顶部）；Unity Texture2D/TMP GlyphRect 是自下而上的（Y=0 在底部）；图集字节必须行翻转，GlyphRect Y 需转换（`atlasHeight - y - height`）后才能注入
- **`TmpFontService.ResolveFontFile` 调用方：** 游戏安装传完整版本（`"2022.3.62f3"`），字体生成传主版本号（`"6000"`）；`ParseMajorVersion` 必须同时处理两种格式（带点和不带点）
- **多图集：** `PackGlyphs` 先尝试单页；二分搜索 + 90% 安全边际计算每页最大字形数；`RectanglePacker.Pack` 溢出时不会抛异常——必须检查返回的 `bounds` 是否超过图集尺寸
- **async/unsafe 冲突：** `GenerateCore` 是 `unsafe` 的（FreeType 指针）——通过 `CharacterSetService.ResolveCharactersAsync` 的字符解析必须在 `GenerateAsync` 中 `Task.Run` 之前完成，绝不在 `GenerateCore` 内部
- **`HashSet<int>` 而非 `HashSet<char>`：** CJK Extension B（U+20000+）需要辅助平面支持；`char` 是 16 位的；整个管线使用 `int` 码点；`StringInfo.GetTextElementEnumerator()` 处理代理对
- **磁盘临时 SDF 位图：** 对于大字符集（70K+ 字符），SDF 位图在渲染期间保存到 `data/font-generation/temp/{sessionId}/`，合成时逐页读回，在 `finally` 中清理
- **CharacterSetService：** 单例；依赖 `AppDataPaths`、`GameLibraryService`；`ResolveCharactersAsync` 合并内置 + 自定义 TXT + 翻译文件来源；来自 `BuiltinCharsets.SupersetOf` 的超集警告；翻译文件路径从游戏 INI `[Files] OutputFile` + `[General] Language` 解析
- **始终包含的字符集：** `ResolveCharactersAsync` 无条件合并 `Ascii()` + `CommonPunctuation()` 到每个生成的字体中；`CommonPunctuation()` 覆盖 CJK 符号与标点（U+3000–U+303F）、CJK 兼容形式（U+FE30–U+FE4F）、全角 ASCII（U+FF01–U+FF60）和部分通用标点（破折号、引号、省略号）；添加新的始终包含范围：在 `BuiltinCharsets.cs` 的 `CommonPunctuation()` 中添加 + 在 `TmpFontGeneratorService.GenerateAsync` 的兜底路径中镜像
- **零大小字形 GlyphRect：** `BitmapWidth==0 || BitmapHeight==0` 的字形（空格、控制字符）必须使 GlyphRect 为 `{0,0,0,0}`——`BitmapWidth - 2*padding` 会产生负值，TMP 会拒绝；零大小字形跳过图集打包但包含在 GlyphTable/CharacterTable 中以提供有效度量
- **FT_Load/Render 失败追踪：** `FT_Load_Glyph`/`FT_Render_Glyph` 失败必须将字符添加到 `missingChars`——裸 `continue` 会静默丢失字符，使其既不在字体中也不在报告中
- **安装为 TMP 字体：** `POST /install-tmp-font/{gameId}` 将生成的 bundle 复制到 `{GamePath}/BepInEx/Font/{fontName}`（保留实际名称，去掉 `.bundle` 扩展名）并补丁 `[Behaviour] FallbackFontTextMeshPro`；使用 `TmpFontService.InstallCustomFont`
- **`TmpFontService` API：** `InstallFont(gamePath, gameInfo)` 返回 `string?` 配置路径（如 `"BepInEx/Font/SourceHanSans_U2022"`），不可用时返回 null；`InstallCustomFont(gamePath, srcPath, destFileName)` 用于生成字体的静态方法；`RemoveFont` 删除 `BepInEx/Font/` 中的所有文件；无 `FontFileName`/`ConfigValue` 常量
- **`FallbackFontTextMeshPro` 配置：** 不在 `ApplyOptimalDefaultsAsync` 默认值中；由 `InstallOrchestrator` 在字体安装后和 `POST /{id}/tmp-font` 端点设置

## 本地 LLM

- GPU 检测：`DxgiGpuDetector`（DXGI，64 位 VRAM，PCI VendorId）为主；WMI 兜底（uint32 上限 4GB）
- 后端选择：NVIDIA→CUDA，AMD/Intel→Vulkan，无→CPU；二进制文件在 `{dataRoot}/llama/{cuda,vulkan,cpu}/`
- **捆绑二进制文件：** ZIP 在 `bundled/llama/`；首次 `POST /start` 时通过 `ExtractLlamaZipAsync` 懒提取
- **模型下载：** `.downloading` 临时文件 + HTTP Range；`_pauseRequests` 区分暂停和取消；双源：HuggingFace（`/resolve/main/`）或 ModelScope（`/models/{repo}/resolve/master/`——注意是 `master` 非 `main`）；`_downloadModelScopeState` 追踪活跃源；清理时检查两个源的临时文件
- **ModelScope URL：** `https://modelscope.cn/models/{owner}/{repo}/resolve/master/{file}`——支持 Range 头用于断点续传；公开模型无需认证
- **GPU 监控：** CUDA 运行时每 3 秒轮询 nvidia-smi
- **推理已禁用：** `--reasoning-budget 0` 防止 `<think>` 块
- 端点自动注册为 `Custom` 提供商，Priority=8；稳定的 `EndpointId`
- **端点生命周期：** `RegisterEndpointAsync` 在 `StartAsync`（健康检查后）时设置 `Enabled=true`；`UnregisterEndpointAsync` 在 `StopAsync` 时设置 `Enabled=false`；`Program.cs` 中的**启动清理**强制将 `ApiKey=="local"` 的端点设为 `Enabled=false`（本地 LLM 在全新启动时绝不运行；防范崩溃孤立状态）
- **翻译门控：** `POST /api/translate` 注入 `LocalLlmService`，当 `ActiveMode=="local" && !IsRunning` 时阻止请求（503）；防止 XUnity 错误累积（5 次连续错误 → 翻译器关闭）
- 设置：`data/local-llm-settings.json`；镜像设置统一在 `AppSettings` 中

## BepInEx 安装（捆绑式）

- **Mono 游戏：** BepInEx 5 来自 `bundled/bepinex5/`（x64 + x86）
- **IL2CPP 游戏：** BepInEx 6 BE 来自 `bundled/bepinex6/`（x64 + x86）；支持 IL2CPP 元数据 v31+
- `BepInExInstallerService.InstallAsync` 是简单的 ZIP 解压；版本从文件名解析

## 插件包（导出/导入）

- `PluginPackageService`：将 BepInEx + XUnity 导出为 ZIP（最大压缩）
- **INI 脱敏：** 清空敏感节的值；`[LLMTranslate]` 仅清空 `GameId`
- **ZIP 文件名：** `{脱敏后的游戏名}_{目标语言}_{yyyy-MM-dd}.zip`
- **带字体替换的导入：** ZIP 中的 `_font_replacement_manifest.json` 标记文件；导入必须在解压前备份目标资源文件（ZIP 会覆盖原始文件）；更新清单中的 `OriginalPath` 以匹配导入游戏的目录

## DI 与提供者注意事项

- 预构造的 `ILoggerProvider`：使用 `AddSingleton<T>(_ => instance)` 避免双重释放
- `IHubContext<T>.SendAsync` 是扩展方法——需要 `using Microsoft.AspNetCore.SignalR;`
- **`SystemTrayService` DI：** `AddSingleton` + `AddHostedService(sp => sp.GetRequired...)` 用于注入 + 托管

## Web 图片搜索

- **`WebImageSearchService`：** 抓取 Bing 和 Google 的图片搜索结果；无需 API 密钥
- **SSRF 防护：** `ValidateImageUrl` 在服务端下载前拒绝非 http(s)、环回地址、私有 IP
- **Content-Type 验证：** 保存下载的图片前必须检查 `IsAllowedContentType`
