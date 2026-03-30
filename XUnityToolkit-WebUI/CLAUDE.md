# XUnityToolkit-WebUI 后端

ASP.NET Core 后端。项目概览、API 端点和构建命令请参阅根目录 `CLAUDE.md`。

## 命令

```bash
dotnet build XUnityToolkit-WebUI.csproj                       # 构建（自动触发前端构建）
dotnet build XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true  # 仅构建后端
dotnet run --project XUnityToolkit-WebUI.csproj                # 运行（http://127.0.0.1:51821）
```

## 核心

- **无迁移代码：** 项目处于预稳定阶段——不做向后兼容的迁移或旧格式转换
- **关键基础设施服务：** `GameLibraryService`（内存中的游戏库增删改查）、`AppSettingsService`（设置缓存）、`ConfigurationService`（INI 读写/补丁）、`UnityDetectionService`（游戏检测）、`GameImageService`（图标/封面/背景管理）
- `AppSettingsService`：内存缓存；`GetAsync()` 无磁盘 I/O；不要修改返回的对象——使用 `UpdateAsync`/`SaveAsync`；**启动延迟加载：** 设置初始化（`LlmTranslationService.Enabled` + 清理过期本地端点）在 `ApplicationStarted` 回调中异步执行（不再阻塞 `app.Run()` 之前的启动路径）
- **数据存储：** 所有运行时数据存储在 `%AppData%\XUnityToolkit\`；`AppDataPaths` 集中管理路径；随附资源（`bundled/`、`wwwroot/`）保留在程序根目录
- **敏感数据加密：** `DpapiProtector`（DPAPI CurrentUser）加密 `ApiEndpointConfig.ApiKey` 和 `SteamGridDbApiKey`；前缀 `ENC:DPAPI:` + Base64；在 `AppSettingsService.ReadAsync`/`WriteAsync` 边界加解密；解密失败时保留密文并创建 `.bak` 备份
- **DI 之前的路径：** `Program.cs` 在 DI 之前读取 `builder.Configuration["AppData:Root"]` 并回退到 `%AppData%\XUnityToolkit\`——必须与 `AppDataPaths._root` 的公式保持同步
- **应用 URL：** `http://127.0.0.1:{port}` 默认 `51821`；**必须使用 `127.0.0.1` 而非 `localhost`**（Unity Mono 会解析为 IPv6 `::1`）；端口可通过 `settings.json` → `aiTranslation.port` 配置（在 `Program.cs` 中 DI 之前读取）
- **静态文件缓存：** `UseStaticFiles` 为 `/assets/*`（Vite 内容哈希）设置 `Cache-Control: public, max-age=31536000, immutable`；根文件（`index.html`、`favicon.ico`）设置 `no-cache`；`MapFallbackToFile` 也为 `index.html` 设置 `no-cache`——两处必须分别配置
- 命名 `HttpClient`：`"LLM"`（120s/200conn）、`"SteamGridDB"`（30s）、`"LocalLlmDownload"`（12h）、`"WebImageSearch"`（15s，浏览器 UA）、`"GitHubUpdate"`（60s）、`"GitHubCdn"`（30s，CDN/Web 请求）
- **Multipart 上传端点：** 必须在 `MapPost` 上链式调用 `.DisableAntiforgery()`——否则 ASP.NET 会拒绝 `multipart/form-data` 请求
- **Updater AOT 约束：** `Updater/` 目标框架为 `net10.0`（非 `-windows`），`PublishAot=true`，`InvariantGlobalization`；不能使用 `JsonSerializer` 反射——必须使用手动字符串拼接生成 JSON；不能使用 WinForms 或 UI 框架
- **Updater 路径遍历验证：** Phase 1（备份）、Phase 2（替换）和 Phase 3（删除）都必须使用 `Path.GetFullPath` + `StartsWith(normalizedAppDir)` 验证目标路径在 appDir 内
- **Updater `normalizedAppDir` 构造：** 必须 `Path.GetFullPath(appDir).TrimEnd(separators) + DirectorySeparatorChar`——`AppContext.BaseDirectory` 始终带尾部 `\`，`GetFullPath` 保留它，不做 `TrimEnd` 会产生双反斜杠导致所有 `StartsWith` 检查失败（文件替换静默跳过）；`UpdateService` 传给 Updater 的 `appDir` 也需 `TrimEnd` 以兼容旧版 Updater
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

- **文件上传安全：** 始终对上传的文件名使用 `Path.GetFileName(file.FileName)`——`Path.Combine` 不能防止恶意文件名的路径穿越
- **每游戏数据清理：** 添加新的每游戏数据目录时，必须在 `DELETE /api/games/{id}` 处理器（`GameEndpoints.cs`）中添加清理 + 如果服务有 `RemoveCache` 则进行缓存驱逐（如 `termService.RemoveCache`、`scriptTagService.RemoveCache`、`cacheMonitor.UnloadCache()`）
- **服务缓存清除：** `TermService.ClearAllCache()`、`ScriptTagService.ClearAllCache()`、`TranslationMemoryService.ClearAllCache()`、`DynamicPatternService.ClearAllCache()`、`TermExtractionService.ClearAllCache()` 清除所有内存缓存；用于设置重置（`POST /api/settings/reset`）和设置导入（`POST /api/settings/import`）；`RemoveCache(gameId)` 清除单个游戏的缓存
- **`ClearAllCache` 释放不变量：** 有每游戏 `ConcurrentDictionary<string, SemaphoreSlim>` 锁的服务必须在 `ClearAllCache()` 中释放所有信号量（遍历 `.Values` → `.Dispose()` → `.Clear()`）；与 `RemoveCache` 模式一致
- **设置重置/导入缓存失效：** `/reset` 和 `/import` 都必须调用全部 7 个失效操作：`settingsService.InvalidateCache()`、`termService.ClearAllCache()`、`scriptTagService.ClearAllCache()`、`tmService.ClearAllCache()`、`dynamicPatternService.ClearAllCache()`、`extractionService.ClearAllCache()`、`cacheMonitor.UnloadCache()`——遗漏任何一个都会留下过期的内存状态
- **设置重置日志挂起：** `POST /api/settings/reset` 在删除数据目录前调用 `FileLoggerProvider.SuspendFileLog()`（释放日志文件句柄），然后在 `finally` 中调用 `ResumeFileLog()`；不要用按子目录删除替代整目录删除（此前导致清理不完整）
- **捆绑文件构建复制：** `bundled/` 中的新文件需要在 `.csproj` 中添加 `<Content CopyToOutputDirectory="PreserveNewest" Link="bundled\...">`——`build.ps1` 仅在发布时运行，`dotnet run` 使用构建输出
- **`ScriptTagService` DI：** `AddSingleton`；遵循 `TermService` 模式（SemaphoreSlim + ConcurrentDictionary 缓存 + 原子文件写入）；预设在 `GetAsync` 中自动更新；保存/自动更新时使编译正则缓存失效
- **`BepInExPluginService` DI：** `AddSingleton`；扫描 `BepInEx/plugins/` 目录列出插件；使用 `MetadataLoadContext` 安全反射读取 DLL 元数据（GUID/Name/Version）——不加载到 AppDomain；`RuntimeDlls` 为 `Lazy<string[]>` 静态缓存（避免循环内重复枚举运行时目录）；toolkit-managed 插件（XUnity、LLMTranslate）通过 `IsToolkitManaged()` 保护，禁止修改/卸载；启用/禁用通过 `.disabled` 文件后缀实现；上传端点需要 `.DisableAntiforgery()`（multipart）；所有端点要求 BepInEx 已安装状态
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
- **`ExtractChunkAsync` CancellationToken 不变量：** `Parallel.ForEachAsync` 的 per-iteration `ct` 必须传递到 `ExtractChunkAsync` → `CallLlmRawAsync`——不要用 `CancellationToken.None`（否则 LLM 调用在应用关闭/游戏删除时不可取消）
- **TermEntry 模型：** `Type`（Translate/DoNotTranslate）、`Original`、`Translation`、`Category`、`Description`、`IsRegex`、`CaseSensitive`、`ExactMatch`、`Priority`
- **关键：** 设置检查（异步）必须在缓冲排空之前——否则禁用时翻译对会丢失
- `TryTriggerExtraction` 是同步的（热路径）；异步工作延迟到 `DrainAndExtractAsync`
- DLL 必须在 `POST /api/translate` 中发送 `gameId`——需要 INI 中的 `[LLMTranslate] GameId`
- **所有翻译路径**都必须调用 `BufferTranslation` + `TryTriggerExtraction`，以 `!string.IsNullOrEmpty(gameId)` 且 `!isLocalMode` 作为条件守卫

## 并发与性能

- **`TermMatchingService` 正则缓存：** `MatchesAnyExact`/`MatchesAnyRegex` 通过静态 `ConcurrentDictionary<(string, RegexOptions), Regex>` 缓存编译后的 Regex——热路径上不要每次调用 `new Regex()`
- **`JsonSerializerOptions` 静态化：** 不要在方法体内 `new JsonSerializerOptions { ... }`——构造开销大（初始化转换器缓存）；使用 `FileHelper.DataJsonOptions` 或声明 `static readonly` 字段
- **热路径：** `POST /api/translate` 接收 100+ 请求/秒；所有 I/O 必须使用内存缓存，绝不每请求访问磁盘
- `SemaphoreSlim`：每批一个槽位；`EnsureSemaphore` 延迟 Dispose 3 分钟；60s 超时 → 503；**关键：** 信号量等待和 LLM 调用放在不同的 `try` 块中；**本地模式批处理拆分：** `TranslateLocalSequentialAsync` 单次获取信号量后循环逐条调用 `CallOpenAiCompatRawAsync`，使 `_translating` 显示 1（而非批大小）；系统提示词缓存 + 节流广播
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
- **`await using` 提前关闭：** 当流必须在后续代码读取文件之前关闭时，使用块作用域 `{ await using var s = ...; }` 而非调用 `s.Close()` 或 `s.DisposeAsync()`（与 `await using` 隐式 dispose 冗余，且造成双重释放）
- **SignalR 下载进度节流：** 所有基于流的下载循环（`UpdateService`、`LocalLlmService`）必须使用 `Stopwatch` 节流 SignalR 广播（200ms 间隔）；循环结束后执行最终广播确保 100% 进度可见
- **插件并发：** DLL 10×10 = 100 条文本；Mono >15 连接会死锁——改用批处理
- **XUnity HTTP：** Mono `DefaultConnectionLimit` = 2 → `FindServicePoint(uri).ConnectionLimit`；无 `Connection: close`（CLOSE_WAIT bug）
- **预翻译：** 对每批 10 条使用 `Parallel.ForEachAsync`（本地模式：批=5，并行度=1）；CAS 节流 200ms 进度；`catch (OperationCanceledException) when (ct.IsCancellationRequested)` 守卫防止 HTTP `TaskCanceledException` 中止整个操作——在 `Parallel.ForEachAsync` 主体中**始终使用 `when` 守卫**
- **顺序 LLM 循环中的 `when` 守卫：** `DynamicPatternService`/`TermExtractionService` 批处理循环也需要 `catch (OperationCanceledException) when (ct.IsCancellationRequested)`——裸 `catch (OperationCanceledException) { throw; }` 会捕获 HTTP 超时的 `TaskCanceledException` 并在单次超时时中止所有剩余批次
- **LLM 重试：** `TranslateBatchAsync` 对瞬态错误（`TaskCanceledException`、`HttpRequestException`、`TimeoutException`）最多重试 2 次，指数退避（2s、4s）；重试循环在信号量获取块内；`IsTransientError` 辅助方法判断可重试性
- **TM 去抖动持久化：** `TranslationMemoryService.Add()` 是同步的（仅内存）；持久化通过 `ScheduleDebouncedPersist` 去抖动 5s；`FlushAsync` 在批处理完成后立即持久化；每游戏 `SemaphoreSlim` 锁（非全局）

## AI 翻译上下文

- **批处理模式：** 整批作为一次 LLM 调用；JSON 数组 I/O
- **翻译记忆：** 每游戏易失；云端 `ContextSize`（10，最大 100），本地 `LocalContextSize`（3，最大 10）
- **游戏描述：** `Game.AiDescription`；`_descriptionCache` 在 `PUT /description` 时失效；截断到 500 字符
- **多阶段管线：** Phase 0（TM 查找）→ Phase 1（自然模式，仅云端）→ Phase 2（占位符替换）→ Phase 3（强制纠正）；`TermAuditService` 在阶段间验证合规性；`NaturalTranslationMode` 和 `TermAuditEnabled` 控制阶段
- **翻译记忆 Phase 0：** 在 `TranslateAsync` 中插入于 Phase 1 之前；`phase0Resolved` HashSet 追踪 TM 已解决的索引；Phase 1/2/3 过滤循环必须跳过这些索引；`perTextTranslationSource` 数组追踪每条文本的 `"tmExact"`/`"tmFuzzy"`/`"tmPattern"`；TM 命中在接受前经过 `TermAuditService` 验证
- **SystemPrompt 顺序：** 模板 → 描述 → 术语 → 记忆 → [文本]
- **添加 SystemPrompt 段落：** 新参数必须贯穿：`TranslateAsync` → `TranslateBatchAsync` → `CallProviderAsync` → 全部 8 个提供商分支 → `Call*Async` → `BuildSystemPrompt`；同时更新 `TranslateLocalSequentialAsync`（直接调用 `CallOpenAiCompatRawAsync`，绕过标准链）和 `TestTranslateAsync`（传 `null`）
- **ParseTranslationArray：** 先去除 `<think>...</think>` 再提取 JSON 数组（处理无围栏情况）
- **`CallOpenAiCompatRawAsync` 可选采样参数：** 接受 `double? minP, double? repeatPenalty, int? maxTokens` 可选参数；`CallOpenAiCompatAsync` 在 `ep.ApiKey == "local"` 时自动传递 `AiTranslationSettings.LocalMinP/LocalRepeatPenalty` 和基于输入长度估算的 max_tokens；`CallLlmRawAsync` 不传递这些参数（保持通用）
- **`CallLlmRawAsync`：** `Task<(string content, long tokens)> CallLlmRawAsync(ApiEndpointConfig endpoint, string systemPrompt, string userContent, double temperature, CancellationToken ct)`——返回元组，必须解构 `var (content, _) = await ...`；无信号量的公共任意 LLM 调用方法；被 `GlossaryExtractionService`、`BepInExLogService`、`DynamicPatternService`、`TermExtractionService` 使用；端点选择：`OrderByDescending(e => e.Priority)`（值越高越优先，与 `CalculateScore` 一致）
- **测试与翻译端点的差异：** `TestTranslateAsync` 直接使用请求体中的端点；`TranslateAsync` 通过 `settingsService.GetAsync()` 读取已存储的设置。设置未持久化时测试可能通过但翻译失败——调试"没有可用的AI提供商"错误时始终检查两条路径
- **Gemini 认证：** 使用 `x-goog-api-key` HTTP 头，不要用 URL 查询参数 `?key=`——URL 参数会在异常消息、日志和 HTTP 跟踪中泄露
- **占位符旁路：** 当整个输入文本是单个占位符（`{{G_x}}`/`{{DNT_x}}`）时，直接预计算结果并跳过 LLM 调用——LLM 对保留占位符不可靠；预计算结果跳过 `ApplyGlossaryPostProcess` 但仍经过术语审计（计入 phase2Pass 统计）；`preComputed` 字典追踪这些索引
- **提示词中的术语：** 在云端模式下，所有翻译类型的术语条目（包括非正则）都保留在系统提示词中，即使使用了占位符——不要过滤为仅正则。在本地模式下，当匹配的术语数 ≤20 时包含术语表（提升术语遵守率），超过 20 时 `promptGlossary` 设为 `null` 以节省上下文 token；术语执行仍依赖占位符替换（非正则）和 `ApplyGlossaryPostProcess`（正则 + 兜底）
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
- **I2 Localization 提取：** `ExtractI2LocalizationTerms` 通过 `mSource.mTerms`+`mLanguages` 字段存在性检测 I2 `LanguageSourceAsset`；使用 `I2:{langCode}:{termKey}` source tag；跳过 `IsGameText` 过滤；对非 I2 MonoBehaviour 回退到通用 `CollectStrings`
- **VIDE Dialogues 提取：** `TryExtractVideDialogue` 通过 `dID`+`playerDiags` 键检测 TextAsset 中的 VIDE 对话 JSON；从 `pd_{N}_com_{C}text` 提取对话，从 `pd_{N}_com_{C}charName` 提取角色名；过滤控制标记；source tag 为 `VIDE:{dialogueName}` 和 `VIDE:Character:{dialogueName}`
- **VIDE SOQuotes_UW 提取：** `IsVideQuote` 通过 `quotes`+`dialType` 检测；`ExtractVideQuotes` 从 `quotes` 数组提取；source tag 为 `Quote:{assetName}`
- **VIDE SOTraitData_UW 提取：** `IsVideTrait` 通过 `traitName`+`traitType` 检测；提取 `traitName`、`reqsText`、`effectText`、`flavorText`；`hoverText` 仅在去除模板占位符后剩余文本 >5 字符时提取；source tag 为 `Trait:{assetName}`
- **添加游戏专用提取器：** 模式：在 `ExtractFromAssetsInstance` 中通过字段签名检测 → 专用提取方法配合特定 source tag → 回退到通用 `CollectStrings`；对于 TextAsset JSON 格式，在 `ExtractTextLines` 回退之前的 TextAsset 循环中检测；添加 `[GeneratedRegex]` 匹配键模式；更新 CLAUDE.md 架构部分
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
- **CharacterSetService：** 单例；依赖 `AppDataPaths`、`GameLibraryService`；`ResolveCharactersAsync` 合并内置 + 自定义 TXT + 翻译文件来源；来自 `BuiltinCharsets.SupersetOf` 的超集警告；翻译文件路径从游戏 INI `[Files] OutputFile` + `[General] Language` 解析；`UseAllFontCharacters` 模式接受 `IReadOnlySet<int>? preEnumeratedFontChars`，直接使用字体 cmap 字符（不添加额外字符集）
- **始终包含的字符集：** `ResolveCharactersAsync` 在普通模式下无条件合并 `Ascii()` + `CommonPunctuation()` 到每个生成的字体中；`CommonPunctuation()` 覆盖 CJK 符号与标点（U+3000–U+303F）、CJK 兼容形式（U+FE30–U+FE4F）、全角 ASCII（U+FF01–U+FF60）和部分通用标点（破折号、引号、省略号）；添加新的始终包含范围：在 `BuiltinCharsets.cs` 的 `CommonPunctuation()` 中添加 + 在 `TmpFontGeneratorService.GenerateAsync` 的兜底路径中镜像
- **`UseAllFontCharacters` 不变量：** 此模式下**不得**添加 `Ascii()`/`CommonPunctuation()` — 字体 cmap 已包含其支持的所有字符，额外添加字体不支持的字符会导致生成时出现"缺失字形"；`EnumerateFontCharacters`（`FontGenerationEndpoints.cs` 中的 unsafe 静态方法）通过 `FT_Get_First_Char`/`FT_Get_Next_Char` 枚举 cmap（循环终止条件：`glyphIndex == 0`）；cmap 扫描对大字体（60K+ 字形）可能耗时较长，端点层须用 `Task.Run` 包裹避免阻塞请求线程
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
- **性能参数：** `--flash-attn --cont-batching -ub 1024` 默认启用；CPU 模式额外设置 `-t/-tb` 为 CPU 核心数；KV cache 量化通过 `LocalLlmSettings.KvCacheType`（默认 `q8_0`，可选 `f16`/`q4_0`）
- **本地推理采样参数：** 本地端点（`ApiKey=="local"`）的请求附加 `min_p`（`AiTranslationSettings.LocalMinP`，默认 0.05）、`repeat_penalty`（`LocalRepeatPenalty`，默认 1.0）、`max_tokens`（基于输入长度估算）
- **本地串行循环优化：** `TranslateLocalSequentialAsync` 在批量翻译时单次获取信号量 + 缓存系统提示词 + 节流 SignalR 广播（替代原先 per-text 的 `TranslateBatchAsync` 调用）
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
- **`SystemTrayService` STA 线程初始化顺序：** `RunTrayLoop()` 中必须按顺序调用：`SetHighDpiMode(PerMonitorV2)` → `EnableVisualStyles()` → `SetCompatibleTextRenderingDefault(false)`；`SetHighDpiMode` 必须在任何窗口创建之前
- **`WebViewWindow` 生命周期：** 由 `SystemTrayService` 在 STA 线程上创建；`_mainWindow` 仅在 `InitializeAsync()` + `Show()` 成功后赋值（防止暴露未就绪窗口）；`Kestrel ready` 通过 `TaskCompletionSource` + `lifetime.ApplicationStarted` 协调；`Application.Run()` 保持无参调用（不传 Form）以支持 WebView2 不可用的回退路径

## Web 图片搜索

- **`WebImageSearchService`：** 抓取 Bing 和 Google 的图片搜索结果；无需 API 密钥
- **SSRF 防护：** `ValidateImageUrl` 在服务端下载前拒绝非 http(s)、环回地址、私有 IP
- **Content-Type 验证：** 保存下载的图片前必须检查 `IsAllowedContentType`

## WebView2 详情

- **启动优化：** `RunTrayLoop()` 在 STA 线程上启动 `CoreWebView2Environment.CreateAsync`（存为 `_preCreatedEnvTask`），与 Kestrel 启动并行运行（节省 300-2000ms）；**必须在 STA 线程上调用**——从 MTA（`StartAsync`）调用会导致 `RPC_E_CHANGED_MODE`；`WebViewWindow` 构造函数接收 `Task<CoreWebView2Environment>?`；`InitializeAsync` 中 `await _envTask` 使用预创建的环境；`Icon` 在 `BuildTrayIcon` 中缓存一次
- **快速关闭：** `OnFormClosing` 在 `ApplicationExitCall` 路径上通过 `Controls.Remove(_webView)` 分离控件，跳过慢速 `_webView.Dispose()`（~500-1000ms Chromium 子进程关闭）；浏览器子进程在宿主进程退出时通过 IPC 通道断开自动终止
- **DPI 缩放陷阱：** `MinimumSize` 在 PerMonitorV2 下是逻辑像素（150% DPI 时物理最小 750×600），CSS 媒体查询使用 CSS 像素——高 DPI 下差异显著；添加/修改响应式断点时必须考虑 DPI 缩放后的 CSS 视口；WebView2 无边框窗口的窗口控制按钮必须在所有视口尺寸下可访问
- **关闭超时：** `HostOptions.ShutdownTimeout = 1s`；浏览器的 SignalR WebSocket 连接 + `withAutomaticReconnect()` 会导致 Kestrel 排空延迟——缩短超时强制中止连接；不要移除或大幅增大此配置
- **WebView2 窗口：** `WebViewWindow`（无边框 `FormBorderStyle.None`，`MinimumSize` 500×400）；`IsNonClientRegionSupportEnabled` 启用 CSS `app-region: drag`；`WebMessageReceived`/`PostWebMessageAsString` 通信桥处理最小化/最大化/关闭命令；`WM_NCHITTEST` 边缘调整大小；`WM_GETMINMAXINFO` 约束最大化边界到工作区域；回退到系统浏览器；用户数据目录 `{AppData}/webview2-cache/`；`DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE=33)` 启用 Windows 11 圆角

## BepInEx 日志与健康检查

- **BepInEx 日志：** `BepInExLogService` 使用 `FileShare.ReadWrite` 读取 `{GamePath}/BepInEx/LogOutput.log`；AI 分析通过 `LlmTranslationService.CallLlmRawAsync`（不占用信号量）；诊断提示词为预定义中文；日志截断到最后 4000 行供 LLM 上下文使用；`hasBepInEx` 计算包含 `PartiallyInstalled` 状态
- **插件健康检查：** `PluginHealthCheckService` 执行被动的、基于规则的健康检查（不依赖 AI）；第 1 级：文件完整性；第 2 级：基于日志的检查（`[GeneratedRegex]`）；第 3 级：连通性检查——DLL 发送 `GET /api/translate/ping?gameId=`；`VerifyAsync` 自动启动游戏，等待日志 + ping，分析；`VerifyForInstallAsync` 绕过 `_activeVerifications` 守卫供编排器使用；`PluginHealthCard.vue` 只显示有问题的项目，接受可选 `initialReport` prop
- **健康检查错误分析：** `CheckLogErrors` 双遍扫描：Pass 1 扫描 `[Error:]` 行匹配通用错误模式（IL2CPP、Harmony、程序集版本等）；Pass 2 扫描所有行匹配 LLMTranslate 端点专属模式——DLL 通过 `Console.WriteLine` 输出为 `[Info/Message:]` 级别；`HealthCheckDetail(Category, Excerpt, Suggestion?)` 携带分类诊断；`SafeExcerpt` 剥离日志前缀并用 `Path.GetFileName` 替换绝对路径；每类最多 2 条、总计最多 10 条；添加新错误模式：在 `GeneralErrorPatterns` 或 `EndpointErrorPatterns` 列表中添加 `ErrorPattern` + 对应 `[GeneratedRegex]`
- **插件健康验证前置条件：** `POST .../health-check/verify` 需要 `FullyInstalled` 或 `PartiallyInstalled` 状态；`BepInExOnly` 时验证必定超时
- **健康检查 ↔ DLL 日志依赖：** `CheckEndpointRegistered` 扫描 BepInEx 日志中的 `"LLMTranslate"` 子串——需要 DLL 的 `Log()` 输出初始化 banner；不要将初始化消息限制在 `DebugLog()` 后面

## 文件浏览器

- **`FileExplorerModal.vue`** 在 `App.vue` 全局挂载一次（`defineAsyncComponent`）；`useFileExplorer()` composable 使用模块级 reactive 单例提供 `selectFile()`/`selectFolder()` → `Promise<string | null>`；返回服务端文件路径
- **`QuickAccessHelper`**（静态类，`Services/`）通过 Shell COM（STA 线程）枚举 `shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}`，结果缓存在 `volatile` 静态字段；COM 对象必须逐级 `Marshal.ReleaseComObject`
- **`POST /api/filesystem/read-text`：** 读取文本文件内容（最大 10MB），供前端导入功能使用；所有文件上传场景均提供 `*-from-path` 端点变体（接受 `UploadFromPathRequest { FilePath }`），与 multipart 上传端点并存

## 备份/恢复

- **`BackupService`：** 为每个游戏创建 `BackupManifest` 用于干净卸载；清单位于 `{dataRoot}/backups/{gameId}.json`

## LLAMA 运行时下载

- **`LocalLlmService.DownloadLlamaAsync`：** 从 GitHub 最新发行版下载 `bundled-llama.zip` 到 `{BaseDir}/bundled/llama/`；通过 `llamaDownloadProgress` SignalR 事件广播到 `local-llm` 分组；`POST /api/local-llm/llama-download` 触发即发即忘下载；前端 `LocalAiPanel.vue` 在 llama 未安装时显示下载横幅

## 翻译管线详情

- **多轮预翻译：** `PreTranslationService` 支持 2 轮管线；第 1 轮：标准翻译 + 自动术语提取；可选术语审核暂停（5 分钟超时，`AwaitingTermReview` 状态）；第 2 轮：使用新提取的术语重新翻译；动态模式正则追加到 `_PreTranslated_Regex.txt`；结果批量写入 TM
- **统一术语管理：** `TermService` 在 `data/glossaries/{gameId}.json` 存储每游戏术语条目（首次加载时迁移旧版 DNT 条目）；每个 `TermEntry` 有 `Type`（Translate/DoNotTranslate）、`Category`、`Priority`、`CaseSensitive`、`ExactMatch`、`IsRegex`；`TermMatchingService` 处理基于优先级的占位符替换；`TermAuditService` 验证翻译中的术语合规性
- **术语提取服务：** `GlossaryExtractionService`（响应式、热路径）和 `TermExtractionService`（批量、预翻译）通过 `LlmResponseParser`、`EndpointSelector`、`TermCategoryMapping` 共享逻辑，但故意是独立服务——不要合并
- **GlossaryExtractionService 重触发守卫：** 在信号量超时时重置 `LastExtractionAt`；使用 `Math.Max(1L, total - interval + 1)` 而非 `Math.Max(0, ...)`
- **占位符替换顺序：** 术语按优先级排序（高优先），然后按原文长度排序（长优先）；翻译类型术语产生 `{{G_x}}`，免翻译术语产生 `{{DNT_x}}`
- **翻译后处理顺序：** 术语表恢复 → 术语表后处理 → DNT 恢复；**DNT 恢复必须在术语表后处理之后** — 否则 `ApplyGlossaryPostProcess` 会破坏免翻译意图
- **预翻译缓存优化：** `PreTranslationCacheMonitor` 跟踪缓存命中/未命中；`PreTranslationService` 规范化缓存键并生成 `sr:` 分割器模式的 `_PreTranslated_Regex.txt`；XUnity 配置针对空白容差优化
- **LLM 批量分析限制：** `DynamicPatternService` 和 `TermExtractionService` 最多采样 200 对以将 LLM 调用上限控制在约 10 次
- **LLM 批量服务进度：** `AnalyzeDynamicFragmentsAsync` 和 `ExtractFromPairsAsync` 接受 `Action<int, int>? onBatchProgress`（done, total）；`PreTranslationService` 用即发即忘推送 SignalR 更新
- **脚本标签清理：** `ScriptTagService` 从预翻译缓存键和 LLM 输入中剥离游戏特定指令码；`NormalizeForCache` 两层：`XUnityTranslationFormat` → `ScriptTagService`；每游戏规则在 `{dataRoot}/script-tags/{gameId}.json`；全局预设在 `bundled/script-tag-presets.json`
- **脚本标签与术语：** 脚本标签预设仅在行级别操作；内联占位符保留（如 `{PC}`）需要 DoNotTranslate 术语条目
- **XUnity 正则翻译：** 缓存文件支持 `r:"pattern"=replacement`（标准正则）和 `sr:"pattern"=$1$2`（分割器正则）；`sr:` 分组必须是可翻译文本；对数字模式使用 `TemplateAllNumberAway=True`

## 实现不变量

- **TranslationStats 计算字段：** `DynamicPatternCount` 和 `ExtractedTermCount` 在 `TranslateEndpoints.cs` 中填充（而非 `LlmTranslationService.GetStats()`），因为 `TermExtractionService` → `LlmTranslationService` 会产生循环 DI 依赖；任何来自依赖 `LlmTranslationService` 的服务的新计算统计必须遵循相同的端点层模式
- **`RecentTranslation.EndpointName` 用于 TM 命中：** 当 `perTextTranslationSource[i]` 不为 null 时在 `PipelineComplete` 中设为 `"翻译记忆"`；防止最近翻译列表中出现空端点名
- **`TranslationMemoryService.GetHitStats()` 元组顺序：** 返回 `(ExactHits, PatternHits, FuzzyHits, Misses)` — 用匹配的名称解构，不要用 `(exact, fuzzy, pattern, misses)`
- **枚举 JSON 大小写：** `TermType` 和 `TermCategory` 使用 `CamelCaseJsonStringEnumConverter<T>`（camelCase）；所有其他枚举使用默认 PascalCase — 不要修改全局 `JsonStringEnumConverter`；**转换器优先级：** 属性级 `[JsonConverter]` > `JsonSerializerOptions.Converters` > 类型级 `[JsonConverter]` — camelCase 枚举必须在 TermEntry **属性**上有 `[JsonConverter]`
- **TermEntry 反序列化不变量：** 所有从 JSON 反序列化 `List<TermEntry>` 的代码必须使用 `FileHelper.DataJsonOptions` — 没有枚举转换器，`DoNotTranslate` 类型会静默变为 `Translate`

## 安全约定

- **路径遍历：** 对所有 ZIP 解压和用户提供的相对路径使用 `PathSecurity.SafeJoin(root, relative)`；使用 `Path.GetFileName()` 从用户提供的文件名中去除目录部分
- **gameId 验证：** 所有直接将 `id` 用于文件路径的端点必须使用 `Guid.TryParse(id, out _)` 验证以防止路径遍历
- **SSRF 防护：** 在对任何用户提供的 URL 执行 `HttpClient.GetAsync()` 前使用 `PathSecurity.ValidateExternalUrl(url)`；阻止回环、私有 IP、链路本地、`.local`/`.internal` 主机名
- **派生 URL 的 SSRF：** 也要验证从用户可配置基础 URL 构造的 URL；必须对最终 URL 验证
- **外部 API 源 URL 的 SSRF：** 来自外部 API 响应的 URL 也必须通过 `ValidateExternalUrl` 验证
- **语言代码验证：** `fromLang`/`toLang` 用于文件路径时必须验证为简单代码（`^[a-zA-Z0-9_\-]{1,20}$`）；同时验证完整路径通过 `Path.GetFullPath` + `StartsWith` 保持在预期目录内
- **CustomFontPath 验证：** 必须验证保持在 `paths.GetCustomFontDirectory(gameId)` 内
- **ExecutableName 验证：** 必须是不含路径分隔符的简单文件名；在 `POST /api/games/` 和 `PUT /api/games/{id}` 中均需验证
- **游戏启动安全：** `Process.Start` 前始终通过 `Path.GetFullPath` + `StartsWith` 验证 `exePath` 在 `GamePath` 内
- **进程参数：** 绝不在未清理引号的情况下将用户输入插入参数字符串
- **CancellationTokenSource：** 从 `ConcurrentDictionary` 中移除时始终 `Dispose()`；覆盖前先 dispose 旧的 CTS
- **即发即忘端点中的 CTS 所有权：** 生产者拥有 disposal；取消端点应只调用 `.Cancel()`，不要 `.Dispose()`
- **进程 disposal：** 在将 `_process` 设为 null 前始终调用 `Process.Dispose()`
- **返回给客户端的错误消息：** 绝不从通用 `catch (Exception)` 块返回 `ex.Message` — 使用安全的静态消息
- **全局异常处理器：** `Program.cs` 中的中间件捕获未处理的 `/api` 异常，向客户端返回通用错误
- **设置验证：** 在 `SettingsEndpoints` 中对数值设置进行 clamp；添加新的数值型字段需要添加对应的 `Math.Clamp`
- **输入大小限制：** 术语 10000、翻译文本 500、原始配置 512 KB；文件上传：字体 50MB、字符集/翻译 10MB、设置导入 100MB
- **正则验证：** 保存前使用 `new Regex(..., timeout: 1s)` 验证术语 `IsRegex` 条目
- **原子文件写入：** 对 JSON 数据文件使用 `FileHelper.WriteJsonAtomicAsync`；对非 JSON 关键文件使用写入临时文件 + `File.Move(overwrite: true)`
- **SignalR 错误消息：** 不要在 `_error` 字段中广播内部文件路径；使用 `Path.GetFileName()` 或通用消息

## 共享基础设施 (`Infrastructure/`)

- **`FileHelper.DataJsonOptions`：** 标准 `JsonSerializerOptions`（WriteIndented + CamelCase + JsonStringEnumConverter）由所有数据文件服务共享 — 不要声明每服务的 `JsonOptions`
- **`FileHelper.WriteJsonAtomicAsync<T>`：** 原子 JSON 写入（序列化 → `.tmp` → `File.Move`）；包含 `Directory.CreateDirectory`
- **`LlmResponseParser`：** `ExtractJsonArray(content)` 去除 `<think>` 块 + markdown 围栏 + 提取 `[...]`；`ExtractJsonContent(content)` 用于通用 JSON
- **`EndpointSelector`：** `SelectBestEndpoint(endpoints)` 返回最高优先级的已启用端点；`SelectEndpoint(endpoints, preferredId)` 先尝试指定端点，回退到最佳
- **`TermCategoryMapping.FromString`：** `Models/TermEntry.cs` 中的共享 `Dictionary<string, TermCategory>`
- **`GameProcessHelper.KillGameProcessAsync`：** `Infrastructure/GameProcessHelper.cs`；处理通过 shell 启动的游戏进程关闭——按可执行文件名 + 游戏目录匹配真实进程；kill 后延迟 1 秒确保文件句柄释放
