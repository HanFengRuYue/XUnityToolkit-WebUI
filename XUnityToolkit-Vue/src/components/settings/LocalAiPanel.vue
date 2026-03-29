<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, onActivated, onDeactivated } from 'vue'
import {
  NIcon,
  NButton,
  NTag,
  NInputNumber,
  NInput,
  NSlider,
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
  ScienceOutlined,
} from '@vicons/material'
import { useLocalLlmStore } from '@/stores/localLlm'
import { localLlmApi } from '@/api/games'
import { useFileExplorer } from '@/composables/useFileExplorer'
import type { AiTranslationSettings, BuiltInModelInfo, LocalModelEntry } from '@/api/types'
import { DEFAULT_SYSTEM_PROMPT } from '@/constants/prompts'
import { formatBytes, formatSpeed } from '@/utils/format'

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
const { selectFile } = useFileExplorer()
const message = useMessage()

const gpuLayers = ref(-1)
const contextLength = ref(4096)
const kvCacheType = ref('q8_0')
const catalogExpanded = ref(false)
const testing = ref(false)


const allModels = computed(() => store.settings?.models ?? [])
const pausedDownloads = computed(() =>
  (store.settings?.pausedDownloads ?? []).filter(p => !store.downloads.has(p.catalogId)))

const hasDownloadTasks = computed(() =>
  store.downloads.size > 0 || pausedDownloads.value.length > 0)

// llama binary status
const llamaInstalled = computed(() =>
  store.llamaStatus?.backends?.some(b => b.isInstalled) ?? false)

const showErrorBanner = computed(() =>
  store.status?.state === 'Failed' && !!store.status?.error)

function getDownloadName(downloadId: string): string {
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
    await store.pauseDownload(catalogId)
    message.info('下载已暂停')
  } catch { /* ignore */ }
}

async function handleCancelDownload(catalogId: string) {
  try {
    await store.cancelDownload(catalogId)
    message.info('下载已取消')
  } catch { /* ignore */ }
}

async function handleAddModel() {
  try {
    const filePath = await selectFile({
      title: '选择 GGUF 模型文件',
      filters: [{ label: 'GGUF 模型', extensions: ['.gguf'] }],
    })
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
  if (gpuLayers.value === null || contextLength.value === null) return
  try {
    await localLlmApi.saveSettings({
      gpuLayers: gpuLayers.value,
      contextLength: contextLength.value,
      kvCacheType: kvCacheType.value,
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
    kvCacheType.value = store.settings.kvCacheType ?? 'q8_0'
  }
})

onActivated(async () => {
  await store.connect()
  await store.fetchStatus()
})

onDeactivated(() => {
  store.disconnect()
})

onBeforeUnmount(() => {
  store.disconnect()
})
</script>

<template>
  <div class="local-ai-panel">
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

    <!-- LLAMA Download Banner -->
    <div v-if="!llamaInstalled && !store.llamaDownload" class="llama-download-banner">
      <div class="llama-download-info">
        <NIcon :size="20"><DownloadOutlined /></NIcon>
        <div>
          <div class="llama-download-title">llama.cpp 引擎未安装</div>
          <div class="llama-download-desc">本地 AI 翻译需要 llama.cpp 推理引擎。可从 GitHub 发行版在线下载。</div>
        </div>
      </div>
      <NButton type="primary" @click="store.downloadLlama()">
        <template #icon><NIcon><DownloadOutlined /></NIcon></template>
        下载 llama.cpp
      </NButton>
    </div>

    <div v-if="store.llamaDownload && !store.llamaDownload.done && !store.llamaDownload.error" class="llama-download-banner downloading">
      <div class="llama-download-progress-info">
        <span>正在下载 llama.cpp 引擎...</span>
        <span class="llama-download-stats">
          {{ formatBytes(store.llamaDownload.bytesDownloaded) }}
          <template v-if="store.llamaDownload.totalBytes > 0">
            / {{ formatBytes(store.llamaDownload.totalBytes) }}
          </template>
          <template v-if="store.llamaDownload.speedBytesPerSec > 0">
            &middot; {{ formatSpeed(store.llamaDownload.speedBytesPerSec) }}
          </template>
        </span>
      </div>
      <NProgress
        type="line"
        :percentage="store.llamaDownload.totalBytes > 0
          ? Math.round(store.llamaDownload.bytesDownloaded / store.llamaDownload.totalBytes * 100)
          : 0"
        :show-indicator="false"
        style="margin: 8px 0"
      />
      <NButton size="small" quaternary @click="store.cancelLlamaDownload()">
        <template #icon><NIcon><CloseOutlined /></NIcon></template>
        取消
      </NButton>
    </div>

    <div v-if="store.llamaDownload?.error" class="llama-download-banner error">
      <span>{{ store.llamaDownload.error }}</span>
      <NButton size="small" type="primary" @click="store.retryLlamaDownload()">
        重试
      </NButton>
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
          <label class="form-label">上下文长度（{{ contextLength }}）</label>
          <NSlider
            v-model:value="contextLength"
            :min="512"
            :max="32768"
            :step="512"
            :tooltip="true"
            @update:value="handleSaveSettings"
          />
          <span class="form-hint">较大的上下文需要更多显存</span>
        </div>
        <div class="form-row">
          <label class="form-label">KV Cache 量化</label>
          <NSelect
            v-model:value="kvCacheType"
            :options="[
              { label: 'f16（最高精度）', value: 'f16' },
              { label: 'q8_0（推荐，省约 50% 显存）', value: 'q8_0' },
              { label: 'q4_0（省约 75% 显存）', value: 'q4_0' },
            ]"
            style="width: 260px"
            @update:value="handleSaveSettings"
          />
          <span class="form-hint">量化 KV Cache 可减少显存占用，q8_0 几乎无质量损失</span>
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
          <label class="form-label">温度（{{ modelValue.temperature }}）</label>
          <NSlider
            :value="modelValue.temperature"
            @update:value="(v: number) => updateAiSettings({ temperature: v })"
            :min="0"
            :max="2"
            :step="0.1"
            :tooltip="true"
            :format-tooltip="(v: number) => v.toFixed(1)"
          />
          <span class="form-hint">较低的值产生更确定的翻译</span>
        </div>
        <div class="form-row">
          <label class="form-label">翻译记忆条数（{{ modelValue.localContextSize }}）</label>
          <NSlider
            :value="modelValue.localContextSize"
            @update:value="(v: number) => updateAiSettings({ localContextSize: v })"
            :min="0"
            :max="10"
            :step="1"
            :tooltip="true"
          />
          <span class="form-hint">附带的近期翻译对数量，0 为关闭（本地模式最多 10）</span>
        </div>
        <div class="form-row">
          <label class="form-label">min_p（{{ modelValue.localMinP?.toFixed(2) ?? '0.05' }}）</label>
          <NSlider
            :value="modelValue.localMinP ?? 0.05"
            @update:value="(v: number) => updateAiSettings({ localMinP: v })"
            :min="0"
            :max="1"
            :step="0.01"
            :tooltip="true"
            :format-tooltip="(v: number) => v.toFixed(2)"
          />
          <span class="form-hint">动态裁剪低概率 token，较高值加速生成但可能降低多样性</span>
        </div>
        <div class="form-row">
          <label class="form-label">重复惩罚（{{ modelValue.localRepeatPenalty?.toFixed(1) ?? '1.0' }}）</label>
          <NSlider
            :value="modelValue.localRepeatPenalty ?? 1.0"
            @update:value="(v: number) => updateAiSettings({ localRepeatPenalty: v })"
            :min="0.5"
            :max="2"
            :step="0.1"
            :tooltip="true"
            :format-tooltip="(v: number) => v.toFixed(1)"
          />
          <span class="form-hint">1.0 = 无惩罚（翻译场景推荐），大于 1 会惩罚重复用词</span>
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
              <NTag size="small" :bordered="false" :type="progress.useModelScope ? 'success' : progress.useMirror ? 'info' : 'default'">
                {{ progress.useModelScope ? 'ModelScope' : progress.useMirror ? '镜像' : '官方' }}
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

.llama-download-banner {
  padding: 14px 16px;
  background: color-mix(in srgb, var(--accent) 6%, transparent);
  border: 1px solid color-mix(in srgb, var(--accent) 15%, transparent);
  border-radius: var(--radius-md);
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.llama-download-banner .llama-download-info {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  color: var(--accent);
}
.llama-download-banner .llama-download-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-1);
}
.llama-download-banner .llama-download-desc {
  font-size: 13px;
  color: var(--text-2);
  margin-top: 2px;
}
.llama-download-banner .llama-download-progress-info {
  display: flex;
  justify-content: space-between;
  font-size: 13px;
  color: var(--text-2);
}
.llama-download-banner .llama-download-stats {
  font-size: 12px;
  color: var(--text-3);
}
.llama-download-banner.downloading {
  background: color-mix(in srgb, var(--accent) 4%, transparent);
}
.llama-download-banner.error {
  background: color-mix(in srgb, var(--danger) 6%, transparent);
  border-color: color-mix(in srgb, var(--danger) 15%, transparent);
  color: var(--danger);
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
  font-size: 13px;
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

  .form-row :deep(.n-input-number) {
    width: 100% !important;
  }

  .status-bar {
    flex-direction: column;
    align-items: stretch;
    gap: 10px;
  }

  .status-right {
    justify-content: flex-end;
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

  .info-header {
    flex-wrap: wrap;
    gap: 10px;
  }
}
</style>
