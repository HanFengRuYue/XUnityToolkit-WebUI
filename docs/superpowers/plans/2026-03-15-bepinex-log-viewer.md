# BepInEx Log Viewer & AI Analysis Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-game sub-page for viewing BepInEx's LogOutput.log with search, filtering, export, and AI-powered diagnostics.

**Architecture:** Backend service reads the log file and calls LLM via existing `CallLlmRawAsync`. Three new API endpoints (read, download, analyze) under `/api/games/{id}/bepinex-log`. Vue 3 sub-page with client-side filtering and `marked` for Markdown rendering.

**Tech Stack:** ASP.NET Core Minimal API, Vue 3 + TypeScript + Naive UI, `marked` (npm), `LlmTranslationService.CallLlmRawAsync`

**Spec:** `docs/superpowers/specs/2026-03-15-bepinex-log-viewer-design.md`

---

## Chunk 1: Backend (Models + Service + Endpoints + Registration)

### Task 1: Create Backend Model

**Files:**
- Create: `XUnityToolkit-WebUI/Models/BepInExLog.cs`

- [ ] **Step 1: Create model file**

```csharp
namespace XUnityToolkit_WebUI.Models;

public record BepInExLogResponse(string Content, long FileSize, DateTime LastModified);
public record BepInExLogAnalysis(string Report, string EndpointName, DateTime AnalyzedAt);
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Models/BepInExLog.cs
git commit -m "feat: 添加 BepInEx 日志查看模型类型"
```

### Task 2: Create BepInExLogService

**Files:**
- Create: `XUnityToolkit-WebUI/Services/BepInExLogService.cs`

**References:**
- `LogEndpoints.cs:48` — `FileShare.ReadWrite` pattern
- `GlossaryExtractionService.cs:208-221` — `ResolveEndpoint` pattern
- `LlmTranslationService.cs:453-469` — `CallLlmRawAsync` signature

- [ ] **Step 1: Create the service file**

```csharp
using System.Text;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

public class BepInExLogService
{
    private readonly AppSettingsService _settingsService;
    private readonly LlmTranslationService _translationService;
    private readonly ILogger<BepInExLogService> _logger;

    private const int MaxAnalysisLines = 4000;

    private const string DiagnosticPrompt = """
        你是一个 BepInEx/XUnity.AutoTranslator 日志分析专家。请分析以下 BepInEx 日志，使用 Markdown 格式输出诊断报告，包含以下部分：

        ## 总体状态
        判断插件是否正常工作（正常 / 部分异常 / 严重错误）

        ## 插件加载状态
        - BepInEx 框架是否成功初始化
        - XUnity.AutoTranslator 是否成功加载
        - LLMTranslate 翻译端点是否已注册
        - 其他已加载的插件列表

        ## 错误与警告摘要
        列出所有 Error 和 Warning 级别的日志条目，按严重程度排序，说明可能的原因

        ## 翻译功能状态
        - 翻译端点是否连接成功
        - 是否有翻译请求和响应记录
        - 翻译是否正常工作

        ## 建议修复措施
        针对发现的问题，给出具体的修复建议

        请直接输出 Markdown 格式的分析报告，不要输出其他内容。
        """;

    public BepInExLogService(
        AppSettingsService settingsService,
        LlmTranslationService translationService,
        ILogger<BepInExLogService> logger)
    {
        _settingsService = settingsService;
        _translationService = translationService;
        _logger = logger;
    }

    public static string GetLogPath(Game game) =>
        Path.Combine(game.GamePath, "BepInEx", "LogOutput.log");

    public async Task<BepInExLogResponse> ReadLogAsync(Game game)
    {
        var logPath = GetLogPath(game);
        if (!File.Exists(logPath))
            throw new FileNotFoundException("BepInEx 日志文件不存在，请确认 BepInEx 已安装且游戏已运行过。", logPath);

        var fi = new FileInfo(logPath);
        var lastModified = fi.LastWriteTimeUtc;
        var fileSize = fi.Length;

        // Read with FileShare.ReadWrite — game may be running
        string content;
        using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(fs, Encoding.UTF8))
        {
            content = await reader.ReadToEndAsync();
        }

        return new BepInExLogResponse(content, fileSize, lastModified);
    }

    public async Task<BepInExLogAnalysis> AnalyzeLogAsync(string logContent, CancellationToken ct)
    {
        var settings = await _settingsService.GetAsync();
        var endpoint = ResolveEndpoint(settings.AiTranslation);

        if (endpoint is null)
            throw new InvalidOperationException("没有可用的 AI 翻译提供商。请先在 AI 翻译设置中配置至少一个启用的端点。");

        // Truncate to last N lines if too long
        var lines = logContent.Split('\n');
        var truncated = lines.Length > MaxAnalysisLines
            ? string.Join('\n', lines[^MaxAnalysisLines..])
            : logContent;

        _logger.LogInformation("开始 AI 分析 BepInEx 日志，使用端点: {Endpoint}，日志行数: {Lines}",
            endpoint.Name, lines.Length);

        var (report, _) = await _translationService.CallLlmRawAsync(
            endpoint, DiagnosticPrompt, truncated, 0.3, ct);

        return new BepInExLogAnalysis(report, endpoint.Name, DateTime.UtcNow);
    }

    private static ApiEndpointConfig? ResolveEndpoint(AiTranslationSettings ai)
    {
        var enabled = ai.Endpoints
            .Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.ApiKey))
            .OrderByDescending(e => e.Priority)
            .ToList();

        return enabled.Count > 0 ? enabled[0] : null;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Services/BepInExLogService.cs
git commit -m "feat: 添加 BepInExLogService 日志读取与 AI 分析服务"
```

### Task 3: Create BepInExLogEndpoints

**Files:**
- Create: `XUnityToolkit-WebUI/Endpoints/BepInExLogEndpoints.cs`

**References:**
- `LogEndpoints.cs` — MapGroup + file download pattern
- `FontReplacementEndpoints.cs` — per-game endpoint group pattern

- [ ] **Step 1: Create the endpoints file**

```csharp
using XUnityToolkit_WebUI.Models;
using XUnityToolkit_WebUI.Services;

namespace XUnityToolkit_WebUI.Endpoints;

public static class BepInExLogEndpoints
{
    public static void MapBepInExLogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/games/{id}/bepinex-log");

        // GET / — read full log content + metadata
        group.MapGet("/", async (string id, GameLibraryService library, BepInExLogService logService) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var logPath = BepInExLogService.GetLogPath(game);
            if (!File.Exists(logPath))
                return Results.NotFound(ApiResult.Fail("BepInEx 日志文件不存在，请确认 BepInEx 已安装且游戏已运行过。"));

            try
            {
                var response = await logService.ReadLogAsync(game);
                return Results.Ok(ApiResult<BepInExLogResponse>.Ok(response));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResult.Fail($"读取日志失败: {ex.Message}"));
            }
        });

        // GET /download — download raw log file
        group.MapGet("/download", async (string id, GameLibraryService library) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var logPath = BepInExLogService.GetLogPath(game);
            if (!File.Exists(logPath))
                return Results.NotFound(ApiResult.Fail("BepInEx 日志文件不存在"));

            // Stream with FileShare.ReadWrite
            var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Results.File(fs, "text/plain", "LogOutput.log");
        });

        // POST /analyze — AI analysis of log content
        group.MapPost("/analyze", async (string id, GameLibraryService library,
            BepInExLogService logService, CancellationToken ct) =>
        {
            var game = await library.GetByIdAsync(id);
            if (game is null)
                return Results.NotFound(ApiResult.Fail("游戏不存在"));

            var logPath = BepInExLogService.GetLogPath(game);
            if (!File.Exists(logPath))
                return Results.NotFound(ApiResult.Fail("BepInEx 日志文件不存在"));

            try
            {
                var logResponse = await logService.ReadLogAsync(game);
                var analysis = await logService.AnalyzeLogAsync(logResponse.Content, ct);
                return Results.Ok(ApiResult<BepInExLogAnalysis>.Ok(analysis));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResult.Fail(ex.Message));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResult.Fail($"AI 分析失败: {ex.Message}"));
            }
        });
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-WebUI/Endpoints/BepInExLogEndpoints.cs
git commit -m "feat: 添加 BepInEx 日志 API 端点（读取、下载、AI 分析）"
```

### Task 4: Register Service and Endpoints in Program.cs

**Files:**
- Modify: `XUnityToolkit-WebUI/Program.cs:132` (DI registration) and `XUnityToolkit-WebUI/Program.cs:195` (endpoint mapping)

- [ ] **Step 1: Add DI registration**

After line 132 (`builder.Services.AddSingleton<CharacterSetService>();`), add:

```csharp
builder.Services.AddSingleton<BepInExLogService>();
```

- [ ] **Step 2: Add endpoint mapping**

After line 195 (`app.MapLocalLlmEndpoints();`), add:

```csharp
app.MapBepInExLogEndpoints();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build XUnityToolkit-WebUI/XUnityToolkit-WebUI.csproj -p:SkipFrontendBuild=true`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-WebUI/Program.cs
git commit -m "feat: 注册 BepInExLogService 和 API 端点"
```

## Chunk 2: Frontend (Types + API + Route + View + Entry Point)

### Task 5: Add TypeScript Types

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/types.ts` (append at end, before final empty line)

- [ ] **Step 1: Add types at end of `types.ts`**

After the `CharsetInfo` interface (line ~561), add:

```typescript
// ── BepInEx Log ──

export interface BepInExLogResponse {
  content: string
  fileSize: number
  lastModified: string
}

export interface BepInExLogAnalysis {
  report: string
  endpointName: string
  analyzedAt: string
}
```

- [ ] **Step 2: Type-check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add XUnityToolkit-Vue/src/api/types.ts
git commit -m "feat: 添加 BepInEx 日志 TypeScript 类型定义"
```

### Task 6: Add API Client Functions

**Files:**
- Modify: `XUnityToolkit-Vue/src/api/games.ts`

- [ ] **Step 1: Add import for new types**

In the import statement at line 2, add `BepInExLogResponse` and `BepInExLogAnalysis` to the type imports.

- [ ] **Step 2: Add `bepinexLogApi` export after `logsApi` (line ~260)**

```typescript
export const bepinexLogApi = {
  get: (id: string) => api.get<BepInExLogResponse>(`/api/games/${id}/bepinex-log`),
  analyze: (id: string) => api.post<BepInExLogAnalysis>(`/api/games/${id}/bepinex-log/analyze`, {}),
  getDownloadUrl: (id: string) => `/api/games/${id}/bepinex-log/download`,
}
```

- [ ] **Step 3: Type-check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No errors

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/api/games.ts
git commit -m "feat: 添加 BepInEx 日志 API 客户端函数"
```

### Task 7: Install `marked` and Add Route

**Files:**
- Modify: `XUnityToolkit-Vue/package.json` (via npm install)
- Modify: `XUnityToolkit-Vue/src/router/index.ts`

- [ ] **Step 1: Install marked**

Run: `cd XUnityToolkit-Vue && npm install marked`

- [ ] **Step 2: Add route in `router/index.ts`**

After the font-replacement route (line 48), add:

```typescript
{
  path: '/games/:id/bepinex-log',
  name: 'bepinex-log',
  component: () => import('@/views/BepInExLogView.vue'),
  meta: { depth: 3 },
},
```

- [ ] **Step 3: Type-check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: May warn about missing BepInExLogView.vue (ok, created next task)

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/package.json XUnityToolkit-Vue/package-lock.json XUnityToolkit-Vue/src/router/index.ts
git commit -m "feat: 安装 marked 依赖并添加 BepInEx 日志路由"
```

### Task 8: Create BepInExLogView.vue

**Files:**
- Create: `XUnityToolkit-Vue/src/views/BepInExLogView.vue`

**References:**
- `LogView.vue` — log display patterns
- `GameDetailView.vue` — back navigation pattern
- Blob download pattern from `GameDetailView.vue:432`

- [ ] **Step 1: Create the view component**

```vue
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NIcon, NInput, NSelect, NSpin, useMessage } from 'naive-ui'
import {
  ArrowBackOutlined,
  RefreshOutlined,
  FileDownloadOutlined,
  AutoFixHighOutlined,
} from '@vicons/material'
import { bepinexLogApi } from '@/api/games'
import type { BepInExLogAnalysis } from '@/api/types'
import { marked } from 'marked'

defineOptions({ name: 'BepInExLogView' })

const route = useRoute()
const router = useRouter()
const message = useMessage()

const gameId = computed(() => route.params.id as string)

// State
const logContent = ref('')
const fileSize = ref(0)
const lastModified = ref('')
const loading = ref(false)
const analyzing = ref(false)
const analysisResult = ref<BepInExLogAnalysis | null>(null)
const searchQuery = ref('')
const levelFilter = ref<string>('All')

// Level filter options
const levelOptions = [
  { label: '全部', value: 'All' },
  { label: 'Info', value: 'Info' },
  { label: 'Warning', value: 'Warning' },
  { label: 'Error', value: 'Error' },
]

// BepInEx log line format: [Level  : Source] Message
// Level can be: Info, Warning, Error, Fatal, Debug, Message
const logLevelRegex = /^\[(Info|Warning|Error|Fatal|Debug|Message)\s*:/i

interface LogLine {
  text: string
  level: string
}

// Parse log lines with level detection
const parsedLines = computed<LogLine[]>(() => {
  if (!logContent.value) return []
  const raw = logContent.value.split('\n')
  const result: LogLine[] = []
  let currentLevel = 'Info'

  for (const line of raw) {
    const match = line.match(logLevelRegex)
    if (match) {
      currentLevel = match[1]
    }
    result.push({ text: line, level: currentLevel })
  }
  return result
})

// Filtered lines
const filteredLines = computed(() => {
  let lines = parsedLines.value

  // Level filter (Error includes Fatal)
  if (levelFilter.value !== 'All') {
    const filterVal = levelFilter.value.toLowerCase()
    lines = lines.filter((l) => {
      const lvl = l.level.toLowerCase()
      if (filterVal === 'error') return lvl === 'error' || lvl === 'fatal'
      return lvl === filterVal
    })
  }

  // Search filter
  if (searchQuery.value) {
    const q = searchQuery.value.toLowerCase()
    lines = lines.filter((l) => l.text.toLowerCase().includes(q))
  }

  return lines
})

// Level to CSS class
function levelClass(level: string): string {
  switch (level.toLowerCase()) {
    case 'error':
    case 'fatal':
      return 'log-error'
    case 'warning':
      return 'log-warning'
    default:
      return 'log-info'
  }
}

// File size formatting
function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

// Load log
async function loadLog() {
  loading.value = true
  try {
    const resp = await bepinexLogApi.get(gameId.value)
    logContent.value = resp.content
    fileSize.value = resp.fileSize
    lastModified.value = resp.lastModified
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '加载日志失败'
    message.error(msg)
  } finally {
    loading.value = false
  }
}

// Export / download
async function handleExport() {
  try {
    const url = bepinexLogApi.getDownloadUrl(gameId.value)
    const resp = await fetch(url)
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`)
    const blob = await resp.blob()
    const blobUrl = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = blobUrl
    a.download = 'LogOutput.log'
    a.click()
    setTimeout(() => URL.revokeObjectURL(blobUrl), 1000)
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : '导出失败'
    message.error(msg)
  }
}

// AI analysis
async function handleAnalyze() {
  analyzing.value = true
  analysisResult.value = null
  try {
    const result = await bepinexLogApi.analyze(gameId.value)
    analysisResult.value = result
  } catch (e: unknown) {
    const msg = e instanceof Error ? e.message : 'AI 分析失败'
    message.error(msg)
  } finally {
    analyzing.value = false
  }
}

// Render markdown
function renderMarkdown(md: string): string {
  return marked.parse(md, { async: false })
}

// Highlight search matches in text
function highlightText(text: string): string {
  if (!searchQuery.value) return escapeHtml(text)
  const escaped = escapeHtml(text)
  const q = escapeHtml(searchQuery.value)
  const regex = new RegExp(`(${q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi')
  return escaped.replace(regex, '<mark class="search-highlight">$1</mark>')
}

function escapeHtml(str: string): string {
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

onMounted(() => {
  loadLog()
})
</script>

<template>
  <div class="log-viewer-page">
    <!-- Header -->
    <div class="page-header">
      <NButton text @click="router.back()">
        <template #icon><NIcon :size="20"><ArrowBackOutlined /></NIcon></template>
      </NButton>
      <h1 class="page-title">BepInEx 日志</h1>
      <span v-if="fileSize" class="file-meta">
        {{ formatFileSize(fileSize) }}
        <template v-if="lastModified"> · {{ new Date(lastModified).toLocaleString() }}</template>
      </span>
    </div>

    <!-- Toolbar -->
    <div class="toolbar">
      <NInput
        v-model:value="searchQuery"
        placeholder="搜索日志..."
        clearable
        size="small"
        class="search-input"
      />
      <NSelect
        v-model:value="levelFilter"
        :options="levelOptions"
        size="small"
        class="level-select"
      />
      <NButton size="small" @click="loadLog" :loading="loading">
        <template #icon><NIcon><RefreshOutlined /></NIcon></template>
        刷新
      </NButton>
      <NButton size="small" @click="handleExport">
        <template #icon><NIcon><FileDownloadOutlined /></NIcon></template>
        导出
      </NButton>
      <NButton size="small" type="primary" @click="handleAnalyze" :loading="analyzing">
        <template #icon><NIcon><AutoFixHighOutlined /></NIcon></template>
        AI 分析
      </NButton>
    </div>

    <!-- Log Content -->
    <div v-if="loading && !logContent" class="loading-container">
      <NSpin size="large" />
    </div>
    <div v-else-if="!logContent && !loading" class="empty-state">
      BepInEx 日志文件为空或不存在。请确认游戏已运行过至少一次。
    </div>
    <div v-else class="log-content">
      <div class="log-lines">
        <div
          v-for="(line, idx) in filteredLines"
          :key="idx"
          class="log-line"
          :class="levelClass(line.level)"
          v-html="highlightText(line.text)"
        />
      </div>
      <div v-if="filteredLines.length === 0 && logContent" class="empty-filter">
        没有匹配的日志条目
      </div>
    </div>

    <!-- AI Analysis Result -->
    <div v-if="analyzing" class="analysis-section">
      <div class="analysis-header">
        <h2 class="section-title">AI 诊断分析</h2>
      </div>
      <div class="analysis-loading">
        <NSpin size="medium" />
        <span>正在分析日志...</span>
      </div>
    </div>
    <div v-else-if="analysisResult" class="analysis-section">
      <div class="analysis-header">
        <h2 class="section-title">AI 诊断分析</h2>
        <span class="analysis-meta">
          由 {{ analysisResult.endpointName }} 生成 · {{ new Date(analysisResult.analyzedAt).toLocaleString() }}
        </span>
      </div>
      <div class="analysis-content markdown-body" v-html="renderMarkdown(analysisResult.report)" />
    </div>
  </div>
</template>

<style scoped>
.log-viewer-page {
  max-width: 1200px;
}

.page-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}

.page-title {
  font-size: 20px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
}

.file-meta {
  font-size: 13px;
  color: var(--text-3);
  margin-left: auto;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.search-input {
  width: 240px;
}

.level-select {
  width: 120px;
}

.loading-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 200px;
}

.empty-state,
.empty-filter {
  text-align: center;
  padding: 48px 24px;
  color: var(--text-3);
  font-size: 14px;
}

.log-content {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 8px;
  overflow: auto;
  max-height: 600px;
  font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
  font-size: 12px;
  line-height: 1.5;
}

.log-lines {
  padding: 12px 16px;
}

.log-line {
  white-space: pre-wrap;
  word-break: break-all;
  padding: 1px 0;
}

.log-line.log-error {
  color: #e74c3c;
}

.log-line.log-warning {
  color: #e6a23c;
}

.log-line.log-info {
  color: var(--text-2);
}

/* Search highlight */
.log-line :deep(.search-highlight) {
  background: rgba(255, 213, 79, 0.4);
  border-radius: 2px;
  padding: 0 1px;
}

/* Analysis section */
.analysis-section {
  margin-top: 20px;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 20px;
}

.analysis-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}

.section-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
}

.analysis-meta {
  font-size: 12px;
  color: var(--text-3);
}

.analysis-loading {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 24px;
  justify-content: center;
  color: var(--text-3);
}

.analysis-content {
  color: var(--text-1);
  line-height: 1.7;
}

/* Markdown body styles */
.markdown-body :deep(h1),
.markdown-body :deep(h2),
.markdown-body :deep(h3) {
  color: var(--text-1);
  margin-top: 16px;
  margin-bottom: 8px;
}

.markdown-body :deep(h2) {
  font-size: 16px;
  border-bottom: 1px solid var(--border);
  padding-bottom: 6px;
}

.markdown-body :deep(h3) {
  font-size: 14px;
}

.markdown-body :deep(ul),
.markdown-body :deep(ol) {
  padding-left: 20px;
  margin: 8px 0;
}

.markdown-body :deep(li) {
  margin: 4px 0;
}

.markdown-body :deep(code) {
  background: var(--bg-subtle);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 13px;
}

.markdown-body :deep(pre) {
  background: var(--bg-subtle);
  padding: 12px 16px;
  border-radius: 6px;
  overflow-x: auto;
}

.markdown-body :deep(p) {
  margin: 8px 0;
}

.markdown-body :deep(strong) {
  color: var(--text-1);
}

/* Responsive */
@media (max-width: 768px) {
  .toolbar {
    flex-direction: column;
    align-items: stretch;
  }

  .search-input,
  .level-select {
    width: 100%;
  }

  .log-content {
    max-height: 400px;
  }
}
</style>
```

- [ ] **Step 2: Type-check**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit`
Expected: No errors

- [ ] **Step 3: Build frontend**

Run: `cd XUnityToolkit-Vue && npm run build`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add XUnityToolkit-Vue/src/views/BepInExLogView.vue
git commit -m "feat: 添加 BepInEx 日志查看页面（搜索、过滤、导出、AI 分析）"
```

### Task 9: Add Entry Point in GameDetailView

**Files:**
- Modify: `XUnityToolkit-Vue/src/views/GameDetailView.vue`

- [ ] **Step 1: Update `hasBepInEx` computed to include `PartiallyInstalled`**

At `GameDetailView.vue:157-161`, change:

```typescript
const hasBepInEx = computed(
  () =>
    game.value?.installState === 'BepInExOnly' ||
    game.value?.installState === 'FullyInstalled',
)
```

to:

```typescript
const hasBepInEx = computed(
  () =>
    game.value?.installState === 'BepInExOnly' ||
    game.value?.installState === 'FullyInstalled' ||
    game.value?.installState === 'PartiallyInstalled',
)
```

- [ ] **Step 2: Add `ArticleOutlined` icon import**

In the `@vicons/material` import block (lines 13-40), add `ArticleOutlined`.

- [ ] **Step 3: Add BepInEx log card in template**

Insert after the Font Replacement card (after line 948, before the AI Description card). This new card has animation delay `0.55s`/`0.5s`. Then shift the remaining cards:
- AI Description card: `0.6s`/`0.55s` (was `0.55s`/`0.5s`)
- Plugin Package card: `0.65s`/`0.6s` (was `0.6s`/`0.55s`)

New card HTML:

```html
<!-- BepInEx Log Card -->
<div v-if="hasBepInEx" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.55s' : '0.5s' }">
  <div class="section-header">
    <h2 class="section-title">
      <span class="section-icon">
        <NIcon :size="16"><ArticleOutlined /></NIcon>
      </span>
      BepInEx 日志
    </h2>
    <NButton size="small" type="primary" @click="router.push(`/games/${gameId}/bepinex-log`)">
      打开
    </NButton>
  </div>
  <p class="asset-extraction-desc">
    查看 BepInEx 运行日志，检查插件加载状态和翻译引擎工作情况。支持日志搜索、过滤、导出和 AI 智能分析。
  </p>
</div>
```

- [ ] **Step 4: Type-check and build**

Run: `cd XUnityToolkit-Vue && npx vue-tsc --noEmit && npm run build`
Expected: No errors

- [ ] **Step 5: Commit**

```bash
git add XUnityToolkit-Vue/src/views/GameDetailView.vue
git commit -m "feat: 在游戏详情页添加 BepInEx 日志入口卡片"
```

### Task 10: Update CLAUDE.md API Endpoints

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add BepInEx Log endpoints to the API Endpoints section**

In the API Endpoints section of CLAUDE.md, after the Font Replacement line, add:

```markdown
- **BepInEx Log:** `GET /api/games/{id}/bepinex-log`, `GET .../download` (**not ApiResult**), `POST .../analyze`
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: 在 CLAUDE.md 中添加 BepInEx 日志 API 端点文档"
```
