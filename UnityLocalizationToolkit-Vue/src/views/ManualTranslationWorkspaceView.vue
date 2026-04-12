<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NEmpty, NInput, NSpin, NTag, NSwitch, useMessage } from 'naive-ui'
import type {
  Game,
  ManualTranslationAssetContent,
  ManualTranslationAssetEntry,
  ManualTranslationStatus,
} from '@/api/types'
import { gamesApi, manualTranslationApi } from '@/api/games'
import { useFileExplorer } from '@/composables/useFileExplorer'

defineOptions({ name: 'ManualTranslationWorkspaceView' })

const route = useRoute()
const router = useRouter()
const message = useMessage()
const { selectFile } = useFileExplorer()

const gameId = route.params['id'] as string
const game = ref<Game | null>(null)
const status = ref<ManualTranslationStatus | null>(null)
const assets = ref<ManualTranslationAssetEntry[]>([])
const selectedAsset = ref<ManualTranslationAssetEntry | null>(null)
const selectedContent = ref<ManualTranslationAssetContent | null>(null)
const draftValue = ref('')
const search = ref('')
const scope = ref<'all' | 'assets' | 'code'>('all')
const editableOnly = ref(false)
const overriddenOnly = ref(false)
const loading = ref(true)
const scanning = ref(false)
const saving = ref(false)
const applying = ref(false)
const restoring = ref(false)
const packaging = ref(false)

const workspaceStats = computed(() => {
  const total = assets.value.length
  const editable = assets.value.filter((item) => item.editable).length
  const overridden = assets.value.filter((item) => item.overridden).length
  return { total, editable, overridden }
})

const filteredAssets = computed(() => {
  return assets.value.filter((asset) => {
    if (scope.value === 'assets' && !['LooseAssetFile', 'BundleEntry'].includes(asset.storageKind))
      return false
    if (scope.value === 'code' && !['ManagedAssembly', 'ManagedResource', 'Il2CppMetadata', 'NativeBinary'].includes(asset.storageKind))
      return false
    if (editableOnly.value && !asset.editable)
      return false
    if (overriddenOnly.value && !asset.overridden)
      return false

    const query = search.value.trim().toLowerCase()
    if (!query)
      return true

    return (
      asset.displayName?.toLowerCase().includes(query) ||
      asset.objectType.toLowerCase().includes(query) ||
      asset.preview?.toLowerCase().includes(query) ||
      asset.fieldPath?.toLowerCase().includes(query)
    )
  })
})

const isSelectedDirty = computed(() => {
  if (!selectedContent.value)
    return false
  return draftValue.value !== (selectedContent.value.overrideText ?? selectedContent.value.originalText ?? '')
})

watch(
  () => selectedContent.value?.assetId,
  () => {
    draftValue.value = selectedContent.value?.overrideText ?? selectedContent.value?.originalText ?? ''
  },
)

watch([scope, editableOnly, overriddenOnly], async () => {
  if (status.value?.hasScan)
    await loadAssets()
})

let searchTimer: number | undefined
watch(search, () => {
  if (!status.value?.hasScan)
    return
  window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(() => {
    void loadAssets()
  }, 180)
})

onMounted(async () => {
  await loadWorkspace()
})

async function loadWorkspace() {
  loading.value = true
  try {
    const [nextGame, nextStatus] = await Promise.all([
      gamesApi.get(gameId),
      manualTranslationApi.getStatus(gameId),
    ])
    game.value = nextGame
    status.value = nextStatus

    if (nextStatus.hasScan) {
      await loadAssets()
    } else {
      assets.value = []
      selectedAsset.value = null
      selectedContent.value = null
    }
  } catch (error) {
    message.error(error instanceof Error ? error.message : '加载手动翻译工作区失败')
  } finally {
    loading.value = false
  }
}

async function loadAssets() {
  const response = await manualTranslationApi.getAssets(gameId, {
    scope: scope.value === 'all' ? undefined : scope.value,
    search: search.value || undefined,
    editableOnly: editableOnly.value,
    overriddenOnly: overriddenOnly.value,
  })
  assets.value = response.assets

  if (selectedAsset.value) {
    const nextSelected = response.assets.find((item) => item.assetId === selectedAsset.value?.assetId) ?? null
    if (nextSelected) {
      await selectAsset(nextSelected)
    } else {
      selectedAsset.value = null
      selectedContent.value = null
      draftValue.value = ''
    }
  }
}

async function selectAsset(asset: ManualTranslationAssetEntry) {
  selectedAsset.value = asset
  selectedContent.value = await manualTranslationApi.getAssetContent(gameId, asset.assetId)
}

async function handleScan() {
  scanning.value = true
  try {
    await manualTranslationApi.scan(gameId)
    message.success('手动翻译索引已刷新')
    await loadWorkspace()
  } catch (error) {
    message.error(error instanceof Error ? error.message : '扫描失败')
  } finally {
    scanning.value = false
  }
}

async function handleSaveOverride() {
  if (!selectedAsset.value)
    return
  saving.value = true
  try {
    await manualTranslationApi.saveOverride(gameId, selectedAsset.value.assetId, draftValue.value, 'manual')
    message.success('覆盖内容已保存')
    await loadAssets()
    await selectAsset(selectedAsset.value)
  } catch (error) {
    message.error(error instanceof Error ? error.message : '保存覆盖失败')
  } finally {
    saving.value = false
  }
}

async function handleDeleteOverride() {
  if (!selectedAsset.value)
    return
  saving.value = true
  try {
    await manualTranslationApi.deleteOverride(gameId, selectedAsset.value.assetId)
    message.success('覆盖内容已删除')
    await loadAssets()
    await selectAsset(selectedAsset.value)
  } catch (error) {
    message.error(error instanceof Error ? error.message : '删除覆盖失败')
  } finally {
    saving.value = false
  }
}

async function handleApply() {
  applying.value = true
  try {
    const result = await manualTranslationApi.apply(gameId)
    message.success(`已写回 ${result.appliedOverrides} 项覆盖，修改 ${result.modifiedFiles} 个文件`)
    if (result.warnings.length > 0)
      message.warning(result.warnings[0]!)
    await loadWorkspace()
  } catch (error) {
    message.error(error instanceof Error ? error.message : '应用覆盖失败')
  } finally {
    applying.value = false
  }
}

async function handleRestore() {
  restoring.value = true
  try {
    await manualTranslationApi.restore(gameId)
    message.success('已从手动翻译备份恢复原始文件')
    await loadWorkspace()
  } catch (error) {
    message.error(error instanceof Error ? error.message : '恢复失败')
  } finally {
    restoring.value = false
  }
}

async function handleBuildPackage() {
  packaging.value = true
  try {
    const response = await fetch(manualTranslationApi.buildPackageUrl(gameId), { method: 'POST' })
    if (!response.ok) {
      const text = await response.text()
      throw new Error(text || '构建补丁包失败')
    }
    const blob = await response.blob()
    const disposition = response.headers.get('content-disposition')
    const fileName = disposition?.match(/filename="?([^"]+)"?/)?.[1] ?? 'manual-translation.zip'
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = fileName
    anchor.click()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
    message.success('手动翻译补丁包已生成')
  } catch (error) {
    message.error(error instanceof Error ? error.message : '构建补丁包失败')
  } finally {
    packaging.value = false
  }
}

async function handleImportPackage() {
  try {
    const zipPath = await selectFile({
      title: '选择手动翻译补丁包',
      filters: [{ label: 'ZIP 压缩包', extensions: ['.zip'] }],
    })
    if (!zipPath)
      return
    packaging.value = true
    await manualTranslationApi.importPackage(gameId, zipPath)
    message.success('补丁包已导入')
    await loadWorkspace()
  } catch (error) {
    message.error(error instanceof Error ? error.message : '导入补丁包失败')
  } finally {
    packaging.value = false
  }
}

function handleExportSelected() {
  if (!selectedAsset.value)
    return
  window.open(manualTranslationApi.exportAssetUrl(gameId, selectedAsset.value.assetId), '_blank')
}
</script>

<template>
  <div class="manual-workspace-page">
    <div class="workspace-hero">
      <div class="workspace-copy">
        <span class="workspace-eyebrow">UnityLocalizationToolkit v5.0</span>
        <h1>手动翻译工作台</h1>
        <p>
          直接索引 Unity 资产、脚本字符串和 IL2CPP 文本承载内容，把覆盖差异写回游戏文件，同时保留原有的 XUnity 实时翻译工作流。
        </p>
      </div>
      <div class="workspace-switch">
        <button class="switch-chip" @click="router.push(`/games/${gameId}`)">XUnity 工作区</button>
        <button class="switch-chip active">手动翻译</button>
      </div>
    </div>

    <div v-if="loading" class="workspace-loading">
      <NSpin size="large" />
    </div>

    <template v-else>
      <div class="workspace-toolbar">
        <div class="toolbar-left">
          <div class="workspace-title">
            <strong>{{ game?.name }}</strong>
            <span>{{ status?.state ?? 'notScanned' }}</span>
          </div>
          <div class="toolbar-stats">
            <span>总资源 {{ status?.assetCount ?? 0 }}</span>
            <span>可编辑 {{ status?.editableAssetCount ?? 0 }}</span>
            <span>覆盖 {{ status?.overrideCount ?? 0 }}</span>
          </div>
        </div>
        <div class="toolbar-actions">
          <NButton secondary :loading="scanning" @click="handleScan">重新扫描</NButton>
          <NButton secondary :loading="packaging" @click="handleImportPackage">导入补丁包</NButton>
          <NButton secondary :loading="packaging" @click="handleBuildPackage">构建补丁包</NButton>
        </div>
      </div>

      <div v-if="!status?.hasScan" class="workspace-empty">
        <NEmpty description="还没有建立手动翻译索引">
          <template #extra>
            <NButton type="primary" :loading="scanning" @click="handleScan">开始扫描游戏资源</NButton>
          </template>
        </NEmpty>
      </div>

      <div v-else class="workspace-shell">
        <aside class="workspace-rail">
          <div class="rail-section">
            <span class="rail-label">浏览范围</span>
            <div class="rail-pills">
              <button class="rail-pill" :class="{ active: scope === 'all' }" @click="scope = 'all'">全部</button>
              <button class="rail-pill" :class="{ active: scope === 'assets' }" @click="scope = 'assets'">资源</button>
              <button class="rail-pill" :class="{ active: scope === 'code' }" @click="scope = 'code'">代码</button>
            </div>
          </div>

          <div class="rail-section">
            <span class="rail-label">筛选</span>
            <NInput v-model:value="search" placeholder="搜索名称、字段或内容" />
            <label class="rail-toggle">
              <span>只看可编辑项</span>
              <NSwitch v-model:value="editableOnly" />
            </label>
            <label class="rail-toggle">
              <span>只看已覆盖项</span>
              <NSwitch v-model:value="overriddenOnly" />
            </label>
          </div>

          <div class="rail-section rail-summary">
            <span class="rail-label">当前结果</span>
            <div class="summary-metric">
              <strong>{{ workspaceStats.total }}</strong>
              <span>已加载资源</span>
            </div>
            <div class="summary-metric">
              <strong>{{ workspaceStats.editable }}</strong>
              <span>可直接编辑</span>
            </div>
            <div class="summary-metric">
              <strong>{{ workspaceStats.overridden }}</strong>
              <span>已存在覆盖</span>
            </div>
          </div>
        </aside>

        <section class="workspace-results">
          <header class="results-header">
            <div>
              <span class="results-kicker">Asset Browser</span>
              <h2>资源与代码索引</h2>
            </div>
            <span class="results-count">{{ filteredAssets.length }} 项</span>
          </header>
          <div class="results-list">
            <button
              v-for="asset in filteredAssets"
              :key="asset.assetId"
              class="asset-row"
              :class="{ active: selectedAsset?.assetId === asset.assetId }"
              @click="selectAsset(asset)"
            >
              <div class="asset-row-copy">
                <strong>{{ asset.displayName || asset.objectType }}</strong>
                <span>{{ asset.objectType }} · {{ asset.assetFile }}</span>
                <p>{{ asset.preview || asset.fieldPath || '无预览内容' }}</p>
              </div>
              <div class="asset-row-tags">
                <NTag size="small" :bordered="false">{{ asset.storageKind }}</NTag>
                <NTag v-if="asset.editable" type="success" size="small" :bordered="false">可编辑</NTag>
                <NTag v-if="asset.overridden" type="warning" size="small" :bordered="false">已覆盖</NTag>
              </div>
            </button>
          </div>
        </section>

        <section class="workspace-inspector">
          <template v-if="selectedAsset && selectedContent">
            <header class="inspector-header">
              <div>
                <span class="results-kicker">Inspector</span>
                <h2>{{ selectedAsset.displayName || selectedAsset.objectType }}</h2>
              </div>
              <div class="inspector-actions">
                <NButton secondary size="small" @click="handleExportSelected">导出</NButton>
                <NButton
                  v-if="selectedAsset.overridden"
                  tertiary
                  size="small"
                  :loading="saving"
                  @click="handleDeleteOverride"
                >
                  删除覆盖
                </NButton>
              </div>
            </header>

            <div class="inspector-meta">
              <span>{{ selectedAsset.assetFile }}</span>
              <span v-if="selectedAsset.fieldPath">{{ selectedAsset.fieldPath }}</span>
              <span v-if="selectedAsset.editHint">{{ selectedAsset.editHint }}</span>
            </div>

            <div class="inspector-panels">
              <div class="text-panel">
                <span class="panel-label">原始内容</span>
                <pre>{{ selectedContent.originalText || '该条目没有可直接渲染的文本内容。' }}</pre>
              </div>
              <div class="text-panel editable">
                <span class="panel-label">覆盖内容</span>
                <NInput
                  v-model:value="draftValue"
                  type="textarea"
                  :autosize="{ minRows: 12, maxRows: 24 }"
                  :disabled="!selectedAsset.editable"
                  placeholder="这里会保存手动翻译覆盖文本"
                />
              </div>
            </div>
          </template>

          <div v-else class="workspace-empty inline-empty">
            <NEmpty description="从左侧选择一项资源或代码字符串后，这里会显示差异编辑器。" />
          </div>
        </section>
      </div>

      <footer v-if="status?.hasScan" class="workspace-dock">
        <div class="dock-copy">
          <strong>应用与回滚</strong>
          <span>默认直接写回游戏文件；每次应用前都会先生成可恢复备份。</span>
        </div>
        <div class="dock-actions">
          <NButton :disabled="!selectedAsset?.editable || !isSelectedDirty" :loading="saving" @click="handleSaveOverride">
            保存覆盖
          </NButton>
          <NButton secondary :loading="restoring" @click="handleRestore">回滚</NButton>
          <NButton type="primary" :loading="applying" @click="handleApply">应用到游戏</NButton>
        </div>
      </footer>
    </template>
  </div>
</template>

<style scoped>
.manual-workspace-page {
  min-height: 100%;
  padding: 32px;
  background:
    radial-gradient(circle at top right, rgba(255, 183, 77, 0.16), transparent 24%),
    linear-gradient(180deg, #0d1117 0%, #121821 100%);
  color: var(--text-1);
}

.workspace-hero {
  display: flex;
  justify-content: space-between;
  gap: 24px;
  align-items: flex-end;
  padding-bottom: 28px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.workspace-copy {
  max-width: 720px;
}

.workspace-eyebrow,
.results-kicker {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
  letter-spacing: 0.14em;
  text-transform: uppercase;
  color: rgba(255, 196, 110, 0.78);
}

.workspace-copy h1,
.results-header h2,
.inspector-header h2 {
  margin: 10px 0 0;
  font-size: clamp(28px, 4vw, 44px);
  line-height: 1.05;
}

.workspace-copy p {
  margin: 14px 0 0;
  max-width: 640px;
  color: var(--text-2);
  font-size: 15px;
  line-height: 1.7;
}

.workspace-switch {
  display: inline-flex;
  gap: 10px;
}

.switch-chip,
.rail-pill {
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: transparent;
  color: var(--text-2);
  padding: 10px 14px;
  border-radius: 999px;
  cursor: pointer;
  transition: 160ms ease;
}

.switch-chip.active,
.rail-pill.active {
  background: rgba(255, 196, 110, 0.14);
  border-color: rgba(255, 196, 110, 0.44);
  color: #ffe2a5;
}

.workspace-loading,
.workspace-empty {
  min-height: 320px;
  display: grid;
  place-items: center;
}

.workspace-toolbar,
.workspace-dock {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 18px;
  margin-top: 24px;
}

.workspace-title {
  display: flex;
  gap: 12px;
  align-items: baseline;
}

.workspace-title span,
.toolbar-stats,
.dock-copy span,
.inspector-meta span {
  color: var(--text-3);
}

.toolbar-stats,
.inspector-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  font-size: 13px;
}

.toolbar-actions,
.dock-actions,
.inspector-actions {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.workspace-shell {
  display: grid;
  grid-template-columns: 248px minmax(320px, 1.05fr) minmax(360px, 0.95fr);
  gap: 24px;
  margin-top: 24px;
  min-height: 680px;
}

.workspace-rail,
.workspace-results,
.workspace-inspector {
  min-height: 0;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 24px;
  backdrop-filter: blur(18px);
}

.workspace-rail {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.rail-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.rail-label,
.panel-label {
  font-size: 12px;
  letter-spacing: 0.12em;
  text-transform: uppercase;
  color: rgba(255, 255, 255, 0.46);
}

.rail-pills {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.rail-toggle {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  color: var(--text-2);
}

.rail-summary {
  margin-top: auto;
}

.summary-metric {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 0;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
}

.summary-metric strong {
  font-size: 28px;
}

.workspace-results,
.workspace-inspector {
  display: flex;
  flex-direction: column;
}

.results-header,
.inspector-header {
  display: flex;
  justify-content: space-between;
  gap: 18px;
  align-items: flex-start;
  padding: 24px 24px 16px;
}

.results-count {
  font-size: 13px;
  color: rgba(255, 255, 255, 0.54);
}

.results-list {
  overflow: auto;
  padding: 0 12px 12px;
}

.asset-row {
  width: 100%;
  text-align: left;
  background: transparent;
  border: 1px solid transparent;
  border-radius: 18px;
  padding: 16px;
  display: flex;
  justify-content: space-between;
  gap: 16px;
  cursor: pointer;
  transition: 160ms ease;
}

.asset-row:hover,
.asset-row.active {
  border-color: rgba(255, 196, 110, 0.32);
  background: rgba(255, 196, 110, 0.06);
}

.asset-row-copy {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.asset-row-copy strong {
  font-size: 15px;
}

.asset-row-copy span,
.asset-row-copy p {
  margin: 0;
  color: var(--text-3);
}

.asset-row-copy p {
  display: -webkit-box;
  overflow: hidden;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.asset-row-tags {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
}

.workspace-inspector {
  overflow: hidden;
}

.inspector-panels {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16px;
  padding: 0 24px 24px;
  overflow: auto;
}

.text-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.text-panel pre {
  margin: 0;
  padding: 16px;
  min-height: 180px;
  border-radius: 18px;
  background: rgba(0, 0, 0, 0.22);
  color: #dfe7f3;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: 'Cascadia Mono', 'JetBrains Mono', monospace;
  font-size: 13px;
  line-height: 1.7;
}

.workspace-dock {
  position: sticky;
  bottom: 0;
  margin-top: 24px;
  padding: 18px 20px;
  border-radius: 20px;
  background: rgba(5, 8, 12, 0.86);
  border: 1px solid rgba(255, 255, 255, 0.08);
  backdrop-filter: blur(14px);
}

.dock-copy {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.inline-empty {
  flex: 1;
}

@media (max-width: 1400px) {
  .workspace-shell {
    grid-template-columns: 220px minmax(280px, 1fr);
  }

  .workspace-inspector {
    grid-column: 1 / -1;
  }
}

@media (max-width: 960px) {
  .manual-workspace-page {
    padding: 20px;
  }

  .workspace-hero,
  .workspace-toolbar,
  .workspace-dock {
    flex-direction: column;
    align-items: stretch;
  }

  .workspace-shell {
    grid-template-columns: 1fr;
  }
}
</style>
