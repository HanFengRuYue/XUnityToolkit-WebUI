<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import {
  NIcon,
  NButton,
  NTag,
  NInputNumber,
  NInput,
  NProgress,
  NPopconfirm,
  useMessage,
} from 'naive-ui'
import {
  PlayArrowOutlined,
  StopOutlined,
  DownloadOutlined,
  DeleteOutlined,
  FolderOpenOutlined,
  StorageOutlined,
  CloseOutlined,
  PauseOutlined,
  ExpandMoreOutlined,
  RestoreOutlined,
  WarningAmberOutlined,
  ScienceOutlined,
} from '@vicons/material'
import { useLocalLlmStore } from '@/stores/localLlm'
import { localLlmApi, dialogApi } from '@/api/games'
import type { AiTranslationSettings, BuiltInModelInfo, LocalModelEntry } from '@/api/types'

const DEFAULT_SYSTEM_PROMPT =
  '你是一名专业的游戏文本翻译家。将以下 {from} 文本翻译为 {to}。\n\n' +
  '要求：\n' +
  '1. 仅返回翻译结果的 JSON 数组，保持与输入相同的顺序和数量。不要添加任何解释、说明或 markdown 格式。\n' +
  '2. 不要增加或省略信息，不擅自添加原文中没有的主语、代词或句子。\n' +
  '3. 保持与原文一致的格式：尽量保留行数、标点和特殊符号，仅在必要时做符合目标语言语法的微调。\n' +
  '4. 严格保留所有占位符、控制符和变量名（如 {0}、%s、%d、<b>、</b>、\\n、【SPECIAL_*】等），不要翻译、删除或改动其位置。\n' +
  '5. 若待翻译内容仅为单个字母、数字、符号或空字符串，请原样返回。\n' +
  '6. 翻译准确自然，忠于原文。结合上下文正确使用人称代词和称呼，使对白自然符合游戏语境，不随意改变说话人。\n' +
  '7. 在忠实原文含义的前提下，使译文符合目标语言的表达习惯，并考虑游戏类型和角色性格，力求达到"信、达、雅"。\n\n' +
  '输入示例：["Hello","World"] → 输出：["你好","世界"]'

const props = defineProps<{
  modelValue: AiTranslationSettings
}>()

const emit = defineEmits<{
  'update:modelValue': [value: AiTranslationSettings]
}>()

function updateAiSettings(patch: Partial<AiTranslationSettings>) {
  emit('update:modelValue', { ...props.modelValue, ...patch })
}

const store = useLocalLlmStore()
const message = useMessage()

const gpuLayers = ref(-1)
const contextLength = ref(4096)
const catalogExpanded = ref(false)
const testing = ref(false)

function formatBytes(bytes: number): string {
  if (bytes >= 1024 * 1024 * 1024) return (bytes / (1024 * 1024 * 1024)).toFixed(1) + ' GB'
  if (bytes >= 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(0) + ' MB'
  return (bytes / 1024).toFixed(0) + ' KB'
}

function formatSpeed(bytesPerSec: number): string {
  if (bytesPerSec >= 1024 * 1024) return (bytesPerSec / (1024 * 1024)).toFixed(1) + ' MB/s'
  return (bytesPerSec / 1024).toFixed(0) + ' KB/s'
}

const allModels = computed(() => store.settings?.models ?? [])
const pausedDownloads = computed(() =>
  (store.settings?.pausedDownloads ?? []).filter(p => !store.downloads.has(p.catalogId)))
const pausedLlamaDownloads = computed(() =>
  (store.settings?.pausedLlamaDownloads ?? []).filter(p => !store.downloads.has(p.downloadId)))

const hasDownloadTasks = computed(() =>
  store.downloads.size > 0 || pausedDownloads.value.length > 0 || pausedLlamaDownloads.value.length > 0)

// llama binary status
const llamaInstalled = computed(() =>
  store.llamaStatus?.backends?.some(b => b.isInstalled) ?? false)

const showLlamaNotInstalled = computed(() =>
  store.llamaStatus !== null && !llamaInstalled.value)

const isLlamaDownloading = computed(() => {
  for (const [key] of store.downloads) {
    if (key.startsWith('llama-')) return true
  }
  return false
})

// Show "LLAMA_NOT_INSTALLED" sentinel as download prompt, not error
const showErrorBanner = computed(() =>
  store.status?.state === 'Failed'
  && !!store.status?.error
  && store.status?.error !== 'LLAMA_NOT_INSTALLED')

const LLAMA_DOWNLOAD_NAMES: Record<string, string> = {
  'llama-cuda': 'llama.cpp CUDA 后端',
  'llama-vulkan': 'llama.cpp Vulkan 后端',
  'llama-cpu': 'llama.cpp CPU 后端',
}

function getDownloadName(downloadId: string): string {
  if (downloadId.startsWith('llama-'))
    return LLAMA_DOWNLOAD_NAMES[downloadId] ?? downloadId
  return store.catalog.find(c => c.id === downloadId)?.name ?? downloadId
}

function isModelDownloaded(catalogId: string): boolean {
  return allModels.value.some(m => m.catalogId === catalogId)
}

function isModelPaused(catalogId: string): boolean {
  return pausedDownloads.value.some(p => p.catalogId === catalogId)
}

// Max GPU VRAM in GB (from DXGI detection)
const maxVramGb = computed(() => {
  if (store.gpus.length === 0) return 0
  const maxBytes = Math.max(...store.gpus.map(g => g.vramBytes))
  return maxBytes / (1024 * 1024 * 1024)
})

function isModelRecommended(item: BuiltInModelInfo): boolean {
  return maxVramGb.value > 0 && item.recommendedVramGb <= maxVramGb.value
}

// Sort catalog: recommended models first (by VRAM desc), then non-recommended (by VRAM asc)
const sortedCatalog = computed(() => {
  const vram = maxVramGb.value
  if (vram <= 0) return store.catalog // No GPU info — keep original order

  return [...store.catalog].sort((a, b) => {
    const aFits = a.recommendedVramGb <= vram
    const bFits = b.recommendedVramGb <= vram
    if (aFits && !bFits) return -1
    if (!aFits && bFits) return 1
    if (aFits && bFits) return b.recommendedVramGb - a.recommendedVramGb // Bigger first
    return a.recommendedVramGb - b.recommendedVramGb // Smaller first
  })
})

function getDownloadProgress(catalogId: string) {
  return store.downloads.get(catalogId)
}

async function handleStart(model: LocalModelEntry) {
  try {
    await store.startServer(model.filePath, gpuLayers.value, contextLength.value)
    message.success('本地 AI 启动中...')
  } catch (e: unknown) {
    message.error(`启动失败: ${e instanceof Error ? e.message : '未知错误'}`)
  }
}

async function handleStop() {
  try {
    await store.stopServer()
    message.success('本地 AI 已停止')
  } catch (e: unknown) {
    message.error(`停止失败: ${e instanceof Error ? e.message : '未知错误'}`)
  }
}

async function handleDownloadLlama() {
  await handleSaveSettings()
  try {
    await localLlmApi.downloadLlama()
    message.info('开始下载 llama.cpp 运行库...')
    store.fetchSettings() // clear paused state
  } catch (e: unknown) {
    message.error(`下载失败: ${e instanceof Error ? e.message : '未知错误'}`)
  }
}

async function handleDownload(catalog: BuiltInModelInfo) {
  await handleSaveSettings()
  try {
    await store.downloadModel(catalog.id)
    message.info(`开始下载 ${catalog.name}...`)
  } catch (e: unknown) {
    message.error(`下载失败: ${e instanceof Error ? e.message : '未知错误'}`)
  }
}

async function handleResumeDownload(catalogId: string) {
  await handleSaveSettings()
  const catalog = store.catalog.find(c => c.id === catalogId)
  try {
    await store.downloadModel(catalogId)
    message.info(`继续下载 ${catalog?.name ?? ''}...`)
    store.fetchSettings() // clear paused state
  } catch (e: unknown) {
    message.error(`恢复下载失败: ${e instanceof Error ? e.message : '未知错误'}`)
  }
}

async function handlePauseDownload(catalogId: string) {
  try {
    if (catalogId.startsWith('llama-')) {
      await localLlmApi.pauseLlamaDownload(catalogId)
    } else {
      await store.pauseDownload(catalogId)
    }
    message.info('下载已暂停')
  } catch { /* ignore */ }
}

async function handleCancelDownload(catalogId: string) {
  try {
    if (catalogId.startsWith('llama-')) {
      const backend = catalogId.replace('llama-', '')
      await localLlmApi.cancelLlamaDownload(catalogId, backend)
      store.downloads.delete(catalogId)
      await store.fetchSettings()
      await store.fetchLlamaStatus()
    } else {
      await store.cancelDownload(catalogId)
    }
    message.info('下载已取消')
  } catch { /* ignore */ }
}

async function handleAddModel() {
  try {
    const filePath = await dialogApi.selectFile('GGUF 模型文件 (*.gguf)|*.gguf')
    if (!filePath) return
    const name = filePath.split(/[\\/]/).pop()?.replace('.gguf', '') ?? 'Custom Model'
    await localLlmApi.addModel(filePath, name)
    await store.fetchModels()
    message.success('模型已添加')
  } catch (e: unknown) {
    message.error(`添加失败: ${e instanceof Error ? e.message : '未知错误'}`)
  }
}

async function handleRemoveModel(model: LocalModelEntry) {
  try {
    await localLlmApi.removeModel(model.id)
    await store.fetchModels()
    message.success('模型已移除')
  } catch (e: unknown) {
    message.error(`移除失败: ${e instanceof Error ? e.message : '未知错误'}`)
  }
}

async function handleTest() {
  testing.value = true
  try {
    const result = await localLlmApi.test()
    if (result.success) {
      message.success(
        `测试成功 (${result.responseTimeMs.toFixed(0)}ms)：${result.translations?.join(', ')}`,
        { duration: 5000 },
      )
    } else {
      message.error(`测试失败：${result.error ?? '未知错误'}`, { duration: 5000 })
    }
  } catch (e: unknown) {
    message.error(`测试失败: ${e instanceof Error ? e.message : '未知错误'}`)
  } finally {
    testing.value = false
  }
}

async function handleSaveSettings() {
  try {
    await localLlmApi.saveSettings({
      gpuLayers: gpuLayers.value,
      contextLength: contextLength.value,
    })
  } catch { /* ignore */ }
}

function handleRestorePrompt() {
  updateAiSettings({ systemPrompt: DEFAULT_SYSTEM_PROMPT })
}

onMounted(async () => {
  await store.connect()
  await Promise.all([
    store.fetchStatus(),
    store.fetchSettings(),
    store.fetchGpus(),
    store.fetchCatalog(),
    store.fetchLlamaStatus(),
  ])
  if (store.settings) {
    gpuLayers.value = store.settings.gpuLayers
    contextLength.value = store.settings.contextLength
  }
})

onUnmounted(() => {
  store.disconnect()
})
</script>

<template>
  <div class="local-ai-panel">
    <!-- llama.cpp Not Installed Banner -->
    <div v-if="showLlamaNotInstalled && !isLlamaDownloading" class="llama-install-banner">
      <div class="llama-install-icon">
        <NIcon :size="24"><WarningAmberOutlined /></NIcon>
      </div>
      <div class="llama-install-content">
        <div class="llama-install-title">llama.cpp 运行库未安装</div>
        <div class="llama-install-desc">
          需要下载 llama.cpp 运行库才能使用本地 AI 推理。
          将根据你的显卡自动下载对应后端
          <template v-if="store.llamaStatus">
            ({{ store.llamaStatus.recommendedBackend }})
          </template>。
        </div>
      </div>
      <NButton type="primary" @click="handleDownloadLlama">
        <template #icon><NIcon><DownloadOutlined /></NIcon></template>
        一键下载
      </NButton>
    </div>

    <!-- Server Status -->
    <div class="status-bar" :class="store.status?.state?.toLowerCase()">
      <div class="status-left">
        <span class="status-dot" :class="store.status?.state?.toLowerCase()"></span>
        <span class="status-text">
          <template v-if="store.status?.state === 'Running'">运行中</template>
          <template v-else-if="store.status?.state === 'Starting'">启动中...</template>
          <template v-else-if="store.status?.state === 'Stopping'">停止中...</template>
          <template v-else-if="store.status?.state === 'Failed'">启动失败</template>
          <template v-else>未启动</template>
        </span>
        <template v-if="store.isRunning && store.status?.loadedModelName">
          <span class="status-model">{{ store.status.loadedModelName }}</span>
          <NTag v-if="store.status?.gpuBackendName" size="small" type="info">
            {{ store.status.gpuBackendName }}
          </NTag>
          <span
            v-if="store.status?.gpuUtilizationPercent != null"
            class="gpu-stats"
          >
            GPU {{ store.status.gpuUtilizationPercent }}%
            &nbsp;|&nbsp;
            VRAM {{ (store.status.gpuVramUsedMb! / 1024).toFixed(1) }}/{{ (store.status.gpuVramTotalMb! / 1024).toFixed(1) }} GB
          </span>
        </template>
        <!-- Backend info when idle -->
        <template v-if="!store.isRunning && !store.isBusy && llamaInstalled && store.llamaStatus">
          <span class="status-backend-info">
            当前后端：{{ store.llamaStatus.recommendedBackend }} (llama.cpp {{ store.llamaStatus.bundledVersion }})
          </span>
        </template>
      </div>
      <div class="status-right">
        <NButton
          v-if="store.isRunning"
          size="small"
          ghost
          :loading="testing"
          @click="handleTest"
        >
          <template #icon><NIcon><ScienceOutlined /></NIcon></template>
          测试
        </NButton>
        <NButton
          v-if="store.isRunning"
          size="small"
          type="error"
          ghost
          :loading="store.status?.state === 'Stopping'"
          @click="handleStop"
        >
          <template #icon><NIcon><StopOutlined /></NIcon></template>
          停止
        </NButton>
      </div>
    </div>

    <!-- Error Display (skip LLAMA_NOT_INSTALLED sentinel) -->
    <div v-if="showErrorBanner" class="error-banner">
      {{ store.status!.error }}
    </div>

    <!-- Launch Settings -->
    <div class="info-section">
      <h3 class="subsection-title">
        <NIcon :size="16"><StorageOutlined /></NIcon>
        推理设置
      </h3>
      <div class="settings-grid">
        <div class="form-row">
          <label class="form-label">GPU 层数</label>
          <NInputNumber
            v-model:value="gpuLayers"
            :min="-1"
            :max="999"
            style="width: 140px"
            @update:value="handleSaveSettings"
          />
          <span class="form-hint">-1 = 全部卸载到 GPU，0 = 仅 CPU</span>
        </div>
        <div class="form-row">
          <label class="form-label">上下文长度</label>
          <NInputNumber
            v-model:value="contextLength"
            :min="512"
            :max="32768"
            :step="512"
            style="width: 140px"
            @update:value="handleSaveSettings"
          />
          <span class="form-hint">较大的上下文需要更多显存</span>
        </div>
      </div>
    </div>

    <!-- Translation Settings (from AiTranslationSettings) -->
    <div class="info-section">
      <h3 class="subsection-title">翻译设置</h3>
      <div class="settings-grid">
        <div class="form-row">
          <label class="form-label">工具箱端口</label>
          <NInputNumber
            :value="modelValue.port"
            @update:value="(v: number | null) => updateAiSettings({ port: v ?? 51821 })"
            :min="1024"
            :max="65535"
            style="width: 140px"
          />
          <span class="form-hint">修改后需重启工具箱生效</span>
        </div>
        <div class="form-row">
          <label class="form-label">温度</label>
          <NInputNumber
            :value="modelValue.temperature"
            @update:value="(v: number | null) => updateAiSettings({ temperature: v ?? 0.3 })"
            :min="0"
            :max="2"
            :step="0.1"
            style="width: 140px"
          />
          <span class="form-hint">较低的值产生更确定的翻译</span>
        </div>
        <div class="form-row">
          <label class="form-label">翻译记忆条数</label>
          <NInputNumber
            :value="modelValue.localContextSize"
            @update:value="(v: number | null) => updateAiSettings({ localContextSize: v ?? 0 })"
            :min="0"
            :max="10"
            style="width: 140px"
          />
          <span class="form-hint">附带的近期翻译对数量，0 为关闭（本地模式最多 10，默认关闭）</span>
        </div>
      </div>

      <div class="form-row" style="margin-top: 8px">
        <label class="form-label">系统提示词</label>
        <div class="prompt-wrapper">
          <NInput
            :value="modelValue.systemPrompt"
            @update:value="(v: string) => updateAiSettings({ systemPrompt: v })"
            type="textarea"
            :rows="3"
            placeholder="指导 LLM 如何翻译"
          />
          <NButton
            size="tiny"
            quaternary
            class="restore-btn"
            @click="handleRestorePrompt"
          >
            <template #icon>
              <NIcon :size="14"><RestoreOutlined /></NIcon>
            </template>
            恢复默认
          </NButton>
        </div>
        <span class="form-hint">使用 {from} 和 {to} 作为语言占位符</span>
      </div>
    </div>

    <!-- My Models -->
    <div class="info-section">
      <div class="info-header">
        <h3 class="subsection-title">我的模型</h3>
        <NButton size="small" @click="handleAddModel">
          <template #icon><NIcon><FolderOpenOutlined /></NIcon></template>
          添加本地模型
        </NButton>
      </div>

      <div v-if="allModels.length === 0" class="empty-hint">
        尚未添加任何模型，请从下方模型库下载或手动添加本地 GGUF 文件。
      </div>

      <div v-else class="model-list">
        <div v-for="model in allModels" :key="model.id" class="model-item">
          <div class="model-info">
            <div class="model-name">{{ model.name }}</div>
            <div class="model-meta">
              <span>{{ formatBytes(model.fileSizeBytes) }}</span>
              <NTag v-if="model.isBuiltIn" size="small">内置</NTag>
            </div>
          </div>
          <div class="model-actions">
            <NButton
              v-if="!store.isRunning || store.status?.loadedModelPath !== model.filePath"
              size="small"
              type="primary"
              ghost
              :disabled="store.isBusy || !llamaInstalled"
              @click="handleStart(model)"
            >
              <template #icon><NIcon><PlayArrowOutlined /></NIcon></template>
              启动
            </NButton>
            <NButton
              v-else
              size="small"
              type="error"
              ghost
              :loading="store.status?.state === 'Stopping'"
              @click="handleStop"
            >
              <template #icon><NIcon><StopOutlined /></NIcon></template>
              停止
            </NButton>
            <NPopconfirm @positive-click="handleRemoveModel(model)">
              <template #trigger>
                <NButton size="small" quaternary type="error">
                  <template #icon><NIcon><DeleteOutlined /></NIcon></template>
                </NButton>
              </template>
              确定移除此模型？{{ model.isBuiltIn ? '（模型文件不会被删除）' : '' }}
            </NPopconfirm>
          </div>
        </div>
      </div>
    </div>

    <!-- Download Tasks (active + paused, includes both model and llama downloads) -->
    <div v-if="hasDownloadTasks" class="info-section">
      <h3 class="subsection-title">
        <NIcon :size="16"><DownloadOutlined /></NIcon>
        下载任务
      </h3>
      <div class="download-task-list">
        <!-- Active downloads -->
        <div v-for="[downloadId, progress] in store.downloads" :key="downloadId" class="download-task-item">
          <div class="download-task-info">
            <div class="download-task-name">
              {{ getDownloadName(downloadId) }}
              <NTag size="small" :bordered="false" :type="progress.useMirror ? 'info' : 'default'">
                {{ progress.useMirror ? '镜像' : '官方' }}
              </NTag>
            </div>
            <NProgress
              type="line"
              :percentage="progress.totalBytes > 0 ? Math.round((progress.bytesDownloaded / progress.totalBytes) * 100) : 0"
              :show-indicator="false"
              :height="6"
            />
            <div class="download-meta">
              <span>{{ formatBytes(progress.bytesDownloaded) }}<template v-if="progress.totalBytes > 0"> / {{ formatBytes(progress.totalBytes) }}</template></span>
              <span>{{ formatSpeed(progress.speedBytesPerSec) }}</span>
            </div>
          </div>
          <div class="download-task-actions">
            <NButton size="small" quaternary @click="handlePauseDownload(downloadId)">
              <template #icon><NIcon><PauseOutlined /></NIcon></template>
            </NButton>
            <NPopconfirm @positive-click="handleCancelDownload(downloadId)">
              <template #trigger>
                <NButton size="small" quaternary type="error">
                  <template #icon><NIcon><CloseOutlined /></NIcon></template>
                </NButton>
              </template>
              确定取消下载？已下载的部分将被删除。
            </NPopconfirm>
          </div>
        </div>

        <!-- Paused model downloads -->
        <div v-for="paused in pausedDownloads" :key="paused.catalogId" class="download-task-item paused">
          <div class="download-task-info">
            <div class="download-task-name">
              {{ getDownloadName(paused.catalogId) }}
              <NTag size="small" type="warning" :bordered="false">已暂停</NTag>
            </div>
            <NProgress
              type="line"
              status="warning"
              :percentage="Math.round((paused.bytesDownloaded / Math.max(paused.totalBytes, 1)) * 100)"
              :show-indicator="false"
              :height="6"
            />
            <div class="download-meta">
              <span>{{ formatBytes(paused.bytesDownloaded) }} / {{ formatBytes(paused.totalBytes) }}</span>
            </div>
          </div>
          <div class="download-task-actions">
            <NButton size="small" type="primary" ghost @click="handleResumeDownload(paused.catalogId)">
              <template #icon><NIcon><PlayArrowOutlined /></NIcon></template>
              继续
            </NButton>
            <NPopconfirm @positive-click="handleCancelDownload(paused.catalogId)">
              <template #trigger>
                <NButton size="small" quaternary type="error">
                  <template #icon><NIcon><CloseOutlined /></NIcon></template>
                </NButton>
              </template>
              确定取消下载？已下载的部分将被删除。
            </NPopconfirm>
          </div>
        </div>

        <!-- Paused llama downloads -->
        <div v-for="paused in pausedLlamaDownloads" :key="paused.downloadId" class="download-task-item paused">
          <div class="download-task-info">
            <div class="download-task-name">
              {{ getDownloadName(paused.downloadId) }}
              <NTag size="small" type="warning" :bordered="false">已暂停</NTag>
            </div>
            <NProgress
              type="line"
              status="warning"
              :percentage="paused.totalBytes > 0 ? Math.round((paused.bytesDownloaded / paused.totalBytes) * 100) : 0"
              :show-indicator="false"
              :height="6"
            />
          </div>
          <div class="download-task-actions">
            <NButton size="small" type="primary" ghost @click="handleDownloadLlama">
              <template #icon><NIcon><PlayArrowOutlined /></NIcon></template>
              继续
            </NButton>
            <NPopconfirm @positive-click="handleCancelDownload(paused.downloadId)">
              <template #trigger>
                <NButton size="small" quaternary type="error">
                  <template #icon><NIcon><CloseOutlined /></NIcon></template>
                </NButton>
              </template>
              确定取消下载？已下载的部分将被删除。
            </NPopconfirm>
          </div>
        </div>
      </div>
    </div>

    <!-- Model Catalog (collapsible) -->
    <div class="info-section">
      <div class="info-header clickable" @click="catalogExpanded = !catalogExpanded">
        <h3 class="subsection-title">
          模型库
          <NTag size="small" :bordered="false">{{ store.catalog.length }} 个模型</NTag>
        </h3>
        <NIcon :size="18" class="collapse-icon" :class="{ expanded: catalogExpanded }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>

      <template v-if="catalogExpanded">
        <p class="section-desc">推荐的翻译模型，点击下载按钮一键获取。<template v-if="maxVramGb > 0">检测到显存 {{ maxVramGb.toFixed(0) }} GB，已按兼容性排序。</template></p>
        <div class="catalog-list">
          <div v-for="item in sortedCatalog" :key="item.id" class="catalog-item" :class="{ recommended: isModelRecommended(item) }">
            <div class="catalog-info">
              <div class="catalog-name">
                {{ item.name }}
                <NTag v-if="isModelRecommended(item)" size="small" type="success" :bordered="false">推荐</NTag>
              </div>
              <div class="catalog-desc">{{ item.description }}</div>
              <div class="catalog-tags">
                <NTag v-for="tag in item.tags" :key="tag" size="small">{{ tag }}</NTag>
                <span class="catalog-size">{{ formatBytes(item.fileSizeBytes) }}</span>
                <span class="catalog-vram">{{ item.recommendedVramGb }}GB+ 显存</span>
              </div>
            </div>
            <div class="catalog-action">
              <template v-if="getDownloadProgress(item.id)">
                <NTag size="small" type="info">下载中</NTag>
              </template>
              <template v-else-if="isModelDownloaded(item.id)">
                <NTag type="success" size="small">已下载</NTag>
              </template>
              <template v-else-if="isModelPaused(item.id)">
                <NTag type="warning" size="small">已暂停</NTag>
              </template>
              <template v-else>
                <NButton size="small" @click="handleDownload(item)">
                  <template #icon><NIcon><DownloadOutlined /></NIcon></template>
                  下载
                </NButton>
              </template>
            </div>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.local-ai-panel {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

/* llama Install Banner */
.llama-install-banner {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  padding: 18px 20px;
  background: color-mix(in srgb, var(--warning) 6%, var(--bg-subtle));
  border: 1px solid color-mix(in srgb, var(--warning) 20%, var(--border));
  border-radius: var(--radius-md);
}

.llama-install-icon {
  color: var(--warning);
  flex-shrink: 0;
  margin-top: 2px;
}

.llama-install-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.llama-install-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-1);
}

.llama-install-desc {
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.5;
}


/* Status Bar */
.status-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 18px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: all 0.3s ease;
}

.status-bar.running {
  background: color-mix(in srgb, var(--success) 5%, var(--bg-subtle));
  border-color: color-mix(in srgb, var(--success) 20%, var(--border));
}

.status-bar.failed {
  background: color-mix(in srgb, var(--danger) 5%, var(--bg-subtle));
  border-color: color-mix(in srgb, var(--danger) 20%, var(--border));
}

.status-bar.starting,
.status-bar.stopping {
  background: color-mix(in srgb, var(--warning) 5%, var(--bg-subtle));
  border-color: color-mix(in srgb, var(--warning) 20%, var(--border));
}

.status-left {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-3);
  flex-shrink: 0;
}

.status-dot.running {
  background: var(--success);
  animation: pulse-dot 2s ease-in-out infinite;
}

.status-dot.starting,
.status-dot.stopping {
  background: var(--warning);
  animation: pulse-dot 1s ease-in-out infinite;
}

.status-dot.failed {
  background: var(--danger);
}

@keyframes pulse-dot {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}

.status-text {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-1);
}

.status-model {
  font-size: 13px;
  color: var(--text-2);
  padding-left: 8px;
  border-left: 1px solid var(--border);
}

.status-backend-info {
  font-size: 12px;
  color: var(--text-3);
  font-family: var(--font-mono);
  padding-left: 8px;
  border-left: 1px solid var(--border);
}

.gpu-stats {
  font-size: 12px;
  color: var(--text-3);
  font-family: var(--font-mono);
  padding-left: 8px;
  border-left: 1px solid var(--border);
}

.status-right {
  display: flex;
  gap: 8px;
}

/* Error Banner */
.error-banner {
  padding: 12px 16px;
  background: color-mix(in srgb, var(--danger) 6%, transparent);
  border: 1px solid color-mix(in srgb, var(--danger) 15%, transparent);
  border-radius: var(--radius-md);
  color: var(--danger);
  font-size: 13px;
  word-break: break-word;
}

/* Info Sections */
.info-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.info-section.compact {
  gap: 8px;
}

.info-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.info-header.clickable {
  cursor: pointer;
  user-select: none;
  padding: 4px 0;
}

.info-header.clickable:hover .subsection-title {
  color: var(--accent);
}

.collapse-icon {
  color: var(--text-3);
  transition: transform 0.2s ease;
}

.collapse-icon.expanded {
  transform: rotate(180deg);
}

.subsection-title {
  font-family: var(--font-display);
  font-size: 15px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  transition: color 0.2s ease;
}

.section-desc {
  font-size: 13px;
  color: var(--text-2);
  margin: 0;
}

.empty-hint {
  padding: 20px;
  text-align: center;
  color: var(--text-3);
  font-size: 13px;
  background: var(--bg-subtle);
  border: 1px dashed var(--border);
  border-radius: var(--radius-md);
}

/* Settings Grid */
.settings-grid {
  display: flex;
  gap: 20px;
  flex-wrap: wrap;
}

.form-row {
  display: flex;
  flex-direction: column;
  gap: 6px;
  flex: 1;
  min-width: 200px;
}

.form-label {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-2);
}

.form-hint {
  font-size: 12px;
  color: var(--text-3);
}


/* Prompt Wrapper */
.prompt-wrapper {
  position: relative;
}

.restore-btn {
  position: absolute;
  right: 8px;
  top: 8px;
  opacity: 0.6;
  transition: opacity 0.2s;
}

.restore-btn:hover {
  opacity: 1;
}

/* Model List */
.model-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.model-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 18px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease;
}

.model-item:hover {
  border-color: var(--border-hover);
}

.model-info {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  flex: 1;
}

.model-name {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-1);
}

.model-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  color: var(--text-3);
}

.model-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
}

/* Download Tasks */
.download-task-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.download-task-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 18px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
}

.download-task-item.paused {
  border-color: color-mix(in srgb, var(--warning) 25%, var(--border));
}

.download-task-info {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.download-task-name {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-1);
  display: flex;
  align-items: center;
  gap: 8px;
}

.download-task-actions {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
}

/* Catalog */
.catalog-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.catalog-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 20px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease;
}

.catalog-item:hover {
  border-color: var(--border-hover);
}

.catalog-item.recommended {
  border-color: color-mix(in srgb, var(--success) 25%, var(--border));
}

.catalog-info {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  flex: 1;
}

.catalog-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--text-1);
}

.catalog-desc {
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.5;
}

.catalog-tags {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.catalog-size,
.catalog-vram {
  font-size: 11px;
  color: var(--text-3);
  font-family: var(--font-mono);
}

.catalog-action {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

/* Download Progress */
.download-meta {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  color: var(--text-3);
  font-family: var(--font-mono);
}


@media (max-width: 768px) {
  .settings-grid {
    flex-direction: column;
    gap: 16px;
  }

  .catalog-item {
    flex-direction: column;
    align-items: stretch;
  }

  .catalog-action {
    justify-content: flex-end;
  }

  .model-item {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }

  .model-actions {
    justify-content: flex-end;
  }

  .download-task-item {
    flex-direction: column;
    align-items: stretch;
    gap: 12px;
  }

  .download-task-actions {
    justify-content: flex-end;
  }

  .llama-install-banner {
    flex-direction: column;
  }

}
</style>
