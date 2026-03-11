<script setup lang="ts">
import { ref, h, onMounted, onUnmounted, computed, watch, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton,
  NAlert,
  NIcon,
  NInput,
  NDropdown,
  useMessage,
  useDialog,
} from 'naive-ui'
import {
  GamepadFilled,
  FolderOpenOutlined,
  PlayArrowFilled,
  InfoOutlined,
  FolderOutlined,
  InsertDriveFileOutlined,
  CodeOutlined,
  MemoryOutlined,
  CloudDownloadOutlined,
  TranslateOutlined,
  WarningAmberOutlined,
  ExtensionOutlined,
  ViewInArOutlined,
  SmartToyOutlined,
  MenuBookOutlined,
  DescriptionOutlined,
  PhotoCameraOutlined,
  DriveFileRenameOutlineOutlined,
  CloudUploadOutlined,
  DeleteOutlineOutlined,
  InventoryOutlined,
  FileUploadOutlined,
} from '@vicons/material'
import { useGamesStore } from '@/stores/games'
import { useInstallStore } from '@/stores/install'
import type { Game, XUnityConfig, ModFrameworkType } from '@/api/types'
import { gamesApi, pluginPackageApi, dialogApi } from '@/api/games'
import ConfigPanel from '@/components/config/ConfigPanel.vue'
import { useAutoSave } from '@/composables/useAutoSave'
import { defineAsyncComponent } from 'vue'

const CoverPickerModal = defineAsyncComponent(
  () => import('@/components/library/CoverPickerModal.vue')
)

const route = useRoute()
const router = useRouter()
const gamesStore = useGamesStore()
const installStore = useInstallStore()
const message = useMessage()
const dialog = useDialog()

const gameId = route.params['id'] as string
const game = ref<Game | null>(null)
const config = ref<XUnityConfig | null>(null)
const loading = ref(true)
const aiEndpointInstalled = ref<boolean | null>(null)
const aiEndpointLoading = ref(false)
const aiDescription = ref('')
const showCoverPicker = ref(false)
const packageExporting = ref(false)
const packageImporting = ref(false)

// Name editing state
const editingName = ref(false)
const editNameValue = ref('')

// Icon context menu state
const showIconContextMenu = ref(false)
const iconContextMenuX = ref(0)
const iconContextMenuY = ref(0)
const iconContextMenuOptions = [
  { label: '更换封面', key: 'cover', icon: () => h(NIcon, { size: 16 }, { default: () => h(PhotoCameraOutlined) }) },
  { label: '上传自定义图标', key: 'upload-icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(CloudUploadOutlined) }) },
  { label: '删除自定义图标', key: 'delete-icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(DeleteOutlineOutlined) }) },
]

const iconUrl = computed(() => game.value ? `/api/games/${gameId}/icon?t=${game.value.updatedAt}` : '')

const isInstalled = computed(
  () => game.value?.installState === 'FullyInstalled',
)
const hasBepInEx = computed(
  () =>
    game.value?.installState === 'BepInExOnly' ||
    game.value?.installState === 'FullyInstalled',
)

const otherFrameworks = computed(() =>
  game.value?.detectedFrameworks?.filter((f) => f.framework !== 'BepInEx') ?? [],
)

const frameworkDisplayNames: Record<ModFrameworkType, string> = {
  BepInEx: 'BepInEx',
  MelonLoader: 'MelonLoader',
  IPA: 'IPA / BSIPA',
  ReiPatcher: 'ReiPatcher',
  Sybaris: 'Sybaris',
  UnityInjector: 'UnityInjector',
  Standalone: 'Standalone',
}

// Auto-save for description
const { enable: enableDescAutoSave, disable: disableDescAutoSave } = useAutoSave(
  () => aiDescription.value,
  async () => {
    try {
      await gamesApi.saveDescription(gameId, aiDescription.value || null)
      if (game.value) game.value.aiDescription = aiDescription.value || undefined
    } catch {
      message.error('保存描述失败')
    }
  },
  { debounceMs: 1000 },
)

onMounted(async () => {
  await loadGame()
})

async function loadGame() {
  loading.value = true
  disableDescAutoSave()
  try {
    game.value = await gamesApi.get(gameId)
    if (isInstalled.value) {
      config.value = await gamesApi.getConfig(gameId)
      try {
        const status = await gamesApi.getAiEndpointStatus(gameId)
        aiEndpointInstalled.value = status.installed
      } catch {
        aiEndpointInstalled.value = null
      }
    } else {
      config.value = null
      aiEndpointInstalled.value = null
    }
    // Description is always available (not gated by install state)
    aiDescription.value = game.value?.aiDescription ?? ''
  } catch {
    message.error('加载游戏信息失败')
  } finally {
    loading.value = false
  }
  await nextTick()
  enableDescAutoSave()
}

async function handleInstall() {
  try {
    await installStore.startInstall(gameId, config.value ?? undefined)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '安装失败')
  }
}

function handleUninstall() {
  dialog.warning({
    title: '确认卸载',
    content: '将移除 BepInEx 和 XUnity.AutoTranslator 的所有文件。确定要继续吗？',
    positiveText: '确认卸载',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await installStore.startUninstall(gameId)
      } catch (e) {
        message.error(e instanceof Error ? e.message : '卸载失败')
      }
    },
  })
}

async function handleOpenFolder() {
  try {
    await gamesApi.openFolder(gameId)
  } catch {
    message.error('打开目录失败')
  }
}

async function handleLaunch() {
  try {
    await gamesApi.launch(gameId)
    message.success('游戏已启动')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '启动游戏失败')
  }
}

function handleRemoveGame() {
  dialog.error({
    title: '移除游戏',
    content: '将从游戏库中移除此游戏（不会删除游戏文件）。确定吗？',
    positiveText: '确认移除',
    negativeText: '取消',
    onPositiveClick: async () => {
      const ok = await gamesStore.removeGame(gameId)
      if (ok) {
        message.success('已移除')
        router.push('/')
      }
    },
  })
}

function startEditName() {
  editNameValue.value = game.value?.name ?? ''
  editingName.value = true
}

async function saveEditName() {
  if (!editingName.value) return
  editingName.value = false
  const name = editNameValue.value.trim()
  if (!name || !game.value || name === game.value.name) return
  try {
    await gamesStore.renameGame(gameId, name)
    game.value!.name = name
    message.success('游戏名称已更新')
  } catch {
    message.error('重命名失败')
  }
}

function cancelEditName() {
  editingName.value = false
}

function handleIconContextMenu(e: MouseEvent) {
  e.preventDefault()
  iconContextMenuX.value = e.clientX
  iconContextMenuY.value = e.clientY
  showIconContextMenu.value = true
}

async function handleIconContextMenuSelect(key: string) {
  showIconContextMenu.value = false
  if (key === 'cover') {
    showCoverPicker.value = true
  } else if (key === 'upload-icon') {
    iconFileInput.value?.click()
  } else if (key === 'delete-icon') {
    try {
      await gamesApi.deleteCustomIcon(gameId)
      await gamesStore.refreshGame(gameId)
      if (game.value) game.value = await gamesApi.get(gameId)
      message.success('自定义图标已删除')
    } catch {
      message.error('删除图标失败')
    }
  }
}

const iconFileInput = ref<HTMLInputElement | null>(null)

async function handleIconFileSelect(e: Event) {
  const input = e.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
    message.error('仅支持 JPEG、PNG 或 WebP 格式')
    return
  }
  if (file.size > 5 * 1024 * 1024) {
    message.error('图片文件不能超过 5 MB')
    return
  }
  try {
    await gamesApi.uploadIcon(gameId, file)
    await gamesStore.refreshGame(gameId)
    if (game.value) game.value = await gamesApi.get(gameId)
    message.success('自定义图标已上传')
  } catch (err) {
    message.error(err instanceof Error ? err.message : '上传图标失败')
  }
}

async function handleInstallAiEndpoint() {
  aiEndpointLoading.value = true
  try {
    const result = await gamesApi.installAiEndpoint(gameId)
    aiEndpointInstalled.value = result.installed
    message.success('AI 翻译引擎已安装')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '安装失败')
  } finally {
    aiEndpointLoading.value = false
  }
}

function handleUninstallAiEndpoint() {
  dialog.warning({
    title: '卸载 AI 翻译引擎',
    content: '将移除游戏内的 AI 翻译引擎 DLL 文件。确定要继续吗？',
    positiveText: '确认卸载',
    negativeText: '取消',
    onPositiveClick: async () => {
      aiEndpointLoading.value = true
      try {
        const result = await gamesApi.uninstallAiEndpoint(gameId)
        aiEndpointInstalled.value = result.installed
        message.success('AI 翻译引擎已卸载')
      } catch (e) {
        message.error(e instanceof Error ? e.message : '卸载失败')
      } finally {
        aiEndpointLoading.value = false
      }
    },
  })
}

async function handleExportPackage() {
  packageExporting.value = true
  try {
    const url = pluginPackageApi.getExportUrl(gameId)
    const resp = await fetch(url, { method: 'POST' })
    if (!resp.ok) {
      const text = await resp.text()
      let msg = `HTTP ${resp.status}`
      try { const json = JSON.parse(text); if (json.error) msg = json.error } catch { /* ignore */ }
      throw new Error(msg)
    }
    const blob = await resp.blob()
    const disposition = resp.headers.get('content-disposition')
    let fileName = '汉化包.zip'
    if (disposition) {
      // Extract filename from content-disposition, handling both filename= and filename*=UTF-8''
      const utf8Match = disposition.match(/filename\*=UTF-8''(.+?)(?:;|$)/i)
      const plainMatch = disposition.match(/filename="?(.+?)"?(?:;|$)/i)
      const raw = utf8Match?.[1] ?? plainMatch?.[1]
      if (raw) fileName = decodeURIComponent(raw)
    }
    const a = document.createElement('a')
    a.href = URL.createObjectURL(blob)
    a.download = fileName
    a.click()
    setTimeout(() => URL.revokeObjectURL(a.href), 1000)
    message.success('汉化包已生成')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '生成汉化包失败')
  } finally {
    packageExporting.value = false
  }
}

async function handleImportPackage() {
  packageImporting.value = true
  try {
    const filePath = await dialogApi.selectFile('ZIP 压缩包 (*.zip)|*.zip')
    if (!filePath) return
    await pluginPackageApi.importPackage(gameId, filePath)
    message.success('汉化包导入成功')
    await loadGame()
  } catch (e) {
    message.error(e instanceof Error ? e.message : '导入汉化包失败')
  } finally {
    packageImporting.value = false
  }
}

function handleUninstallFramework(framework: ModFrameworkType) {
  dialog.warning({
    title: `卸载 ${frameworkDisplayNames[framework]}`,
    content: `将移除 ${frameworkDisplayNames[framework]} 框架及其关联的 XUnity 插件文件。确定要继续吗？`,
    positiveText: '确认卸载',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        const updated = await gamesApi.uninstallFramework(gameId, framework)
        game.value = updated
        await gamesStore.refreshGame(gameId)
        message.success(`${frameworkDisplayNames[framework]} 已卸载`)
      } catch (e) {
        message.error(e instanceof Error ? e.message : '卸载失败')
      }
    },
  })
}

const stopWatch = watch(
  () => installStore.status?.step,
  async (step) => {
    if (step === 'Complete') {
      await loadGame()
      await gamesStore.refreshGame(gameId)
    }
  },
)
onUnmounted(() => stopWatch())
</script>

<template>
  <!-- Loading -->
  <div v-if="loading" class="detail-loading">
    <div class="loading-spinner"></div>
  </div>

  <div v-else-if="game" class="detail-page">
    <!-- Back Navigation & Title -->
    <div class="detail-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push('/')">
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
          <path d="M12 5L7 10L12 15" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        <span>返回</span>
      </button>
      <div class="header-actions">
        <NButton tertiary type="error" size="small" @click="handleRemoveGame">
          从库中移除
        </NButton>
      </div>
    </div>

    <!-- Game Title -->
    <div class="game-title-section" style="animation-delay: 0.05s">
      <div class="title-icon" @click="showCoverPicker = true" @contextmenu="handleIconContextMenu" title="左键更换封面 / 右键更多操作">
        <img
          :src="iconUrl"
          :alt="game.name"
          class="title-icon-img"
          @error="($event.target as HTMLImageElement).style.display = 'none'; ($event.target as HTMLImageElement).nextElementSibling?.classList.add('visible')"
        />
        <div class="title-icon-fallback">
          <NIcon :size="28" color="var(--text-3)">
            <GamepadFilled />
          </NIcon>
        </div>
        <div class="title-icon-edit">
          <NIcon :size="14" color="#fff">
            <PhotoCameraOutlined />
          </NIcon>
        </div>
      </div>
      <h1 v-if="!editingName" class="game-title" @dblclick="startEditName">{{ game.name }}</h1>
      <NInput
        v-else
        v-model:value="editNameValue"
        class="game-title-input"
        size="large"
        autofocus
        @keyup.enter="saveEditName"
        @keyup.escape="cancelEditName"
        @blur="saveEditName"
      />
      <button v-if="!editingName" class="edit-name-btn" title="重命名" @click="startEditName">
        <NIcon :size="16" color="var(--text-3)">
          <DriveFileRenameOutlineOutlined />
        </NIcon>
      </button>
      <div class="title-status">
        <template v-if="game.isUnityGame">
          <span class="status-indicator" :class="{
            'status-installed': isInstalled,
            'status-partial': hasBepInEx && !isInstalled,
            'status-none': !hasBepInEx,
          }"></span>
          <span class="status-text">
            {{ isInstalled ? '已安装' : hasBepInEx ? '仅 BepInEx' : '未安装' }}
          </span>
        </template>
        <template v-else>
          <span class="status-text" style="color: var(--text-3)">非 Unity</span>
        </template>
      </div>
    </div>

    <!-- Game Info Card -->
    <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><InfoOutlined /></NIcon>
          </span>
          游戏信息
        </h2>
        <div class="header-btn-group">
          <NButton size="small" @click="handleOpenFolder">
            <template #icon><NIcon :size="16"><FolderOpenOutlined /></NIcon></template>
            打开目录
          </NButton>
          <NButton size="small" @click="handleLaunch">
            <template #icon><NIcon :size="16"><PlayArrowFilled /></NIcon></template>
            运行游戏
          </NButton>
        </div>
      </div>

      <div class="info-grid">
        <div class="info-card">
          <div class="info-card-icon folder">
            <NIcon :size="18"><FolderOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">路径</span>
            <span class="info-value mono">{{ game.gamePath }}</span>
          </div>
        </div>
        <div class="info-card">
          <div class="info-card-icon file">
            <NIcon :size="18"><InsertDriveFileOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">可执行文件</span>
            <span class="info-value" :class="{ muted: !game.executableName }">
              {{ game.executableName || '未知' }}
            </span>
          </div>
        </div>
        <template v-if="game.isUnityGame">
          <div class="info-card">
            <div class="info-card-icon unity">
              <NIcon :size="18"><ViewInArOutlined /></NIcon>
            </div>
            <div class="info-card-content">
              <span class="info-label">Unity 版本</span>
              <span class="info-value" :class="{ muted: !game.detectedInfo }">
                {{ game.detectedInfo?.unityVersion || '未知' }}
              </span>
            </div>
          </div>
          <div class="info-card">
            <div class="info-card-icon code">
              <NIcon :size="18"><CodeOutlined /></NIcon>
            </div>
            <div class="info-card-content">
              <span class="info-label">脚本后端</span>
              <span class="info-value" :class="{ muted: !game.detectedInfo }">
                {{ game.detectedInfo?.backend || '未知' }}
              </span>
            </div>
          </div>
          <div class="info-card">
            <div class="info-card-icon arch">
              <NIcon :size="18"><MemoryOutlined /></NIcon>
            </div>
            <div class="info-card-content">
              <span class="info-label">架构</span>
              <span class="info-value" :class="{ muted: !game.detectedInfo }">
                {{ game.detectedInfo?.architecture || '未知' }}
              </span>
            </div>
          </div>
        </template>
        <div v-if="!game.isUnityGame" class="info-card">
          <div class="info-card-icon">
            <NIcon :size="18"><InfoOutlined /></NIcon>
          </div>
          <div class="info-card-content">
            <span class="info-label">类型</span>
            <span class="info-value muted">非 Unity 游戏</span>
          </div>
        </div>
      </div>
    </div>

    <!-- Detected Frameworks Card (non-BepInEx) -->
    <div v-if="otherFrameworks.length > 0" class="section-card" style="animation-delay: 0.15s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon warning">
            <NIcon :size="16"><WarningAmberOutlined /></NIcon>
          </span>
          检测到的模组框架
        </h2>
      </div>
      <NAlert type="warning" style="margin-bottom: 16px">
        检测到其他模组框架，建议卸载后再安装 BepInEx 版本，避免插件冲突。
      </NAlert>
      <div class="framework-list">
        <div v-for="fw in otherFrameworks" :key="fw.framework" class="framework-item">
          <div class="framework-info">
            <NIcon :size="16" color="var(--warning)"><ExtensionOutlined /></NIcon>
            <span class="framework-name">{{ frameworkDisplayNames[fw.framework] }}</span>
            <span v-if="fw.version" class="framework-version">v{{ fw.version }}</span>
            <span v-if="fw.hasXUnityPlugin" class="framework-xunity">
              含 XUnity{{ fw.xUnityVersion ? ` v${fw.xUnityVersion}` : '' }}
            </span>
          </div>
          <NButton size="small" type="error" ghost @click="handleUninstallFramework(fw.framework)">
            卸载
          </NButton>
        </div>
      </div>
    </div>

    <!-- Install Management Card (Unity only) -->
    <div v-if="game.isUnityGame" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.2s' : '0.15s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon download">
            <NIcon :size="16"><CloudDownloadOutlined /></NIcon>
          </span>
          安装管理
        </h2>
        <NButton v-if="isInstalled" type="error" @click="handleUninstall" ghost size="small">
          卸载插件
        </NButton>
      </div>

      <NAlert
        v-if="game.detectedInfo?.backend === 'IL2CPP'"
        type="warning"
        style="margin-bottom: 20px"
      >
        检测到 IL2CPP 游戏，将使用 BepInEx 6 (预发布版)。首次启动游戏可能需要 30-90 秒生成互操作程序集。
      </NAlert>

      <!-- Uninstalled State -->
      <div v-if="!isInstalled" class="install-cta-horizontal">
        <div class="cta-left">
          <div class="cta-visual">
            <svg class="cta-icon" width="40" height="40" viewBox="0 0 40 40" fill="none">
              <rect x="4" y="4" width="32" height="32" rx="8" stroke="currentColor" stroke-width="1.5" opacity="0.2"/>
              <path d="M20 12V24M20 24L14 18M20 24L26 18" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
              <path d="M12 28H28" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            </svg>
          </div>
          <div class="cta-text">
            <span class="cta-title">准备安装翻译插件</span>
            <span class="cta-desc">将自动下载并安装 BepInEx 框架和 XUnity.AutoTranslator 翻译插件</span>
          </div>
        </div>
        <NButton
          type="primary"
          size="large"
          :disabled="!game.detectedInfo"
          @click="handleInstall"
          class="install-button"
        >
          <template #icon>
            <svg width="18" height="18" viewBox="0 0 18 18" fill="none">
              <path d="M9 3V12M9 12L5 8M9 12L13 8M3 15H15" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
          </template>
          一键安装
        </NButton>
      </div>

      <!-- Installed State -->
      <div v-else class="installed-info-horizontal">
        <div class="version-card">
          <div class="version-card-header">
            <svg width="18" height="18" viewBox="0 0 18 18" fill="none">
              <rect x="2" y="2" width="14" height="14" rx="3" stroke="currentColor" stroke-width="1.2" opacity="0.5"/>
              <path d="M6 9L8 11L12 7" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            <span class="version-card-label">BepInEx</span>
          </div>
          <span class="version-card-value">{{ game.installedBepInExVersion }}</span>
          <span class="version-card-desc">模组框架</span>
        </div>
        <div class="version-card">
          <div class="version-card-header">
            <svg width="18" height="18" viewBox="0 0 18 18" fill="none">
              <rect x="2" y="2" width="14" height="14" rx="3" stroke="currentColor" stroke-width="1.2" opacity="0.5"/>
              <path d="M6 9L8 11L12 7" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            <span class="version-card-label">XUnity.AutoTranslator</span>
          </div>
          <span class="version-card-value">{{ game.installedXUnityVersion }}</span>
          <span class="version-card-desc">翻译插件</span>
        </div>
        <div class="installed-hint-inline">
          如需重新安装，请先卸载当前版本
        </div>
      </div>
    </div>

    <!-- Translation Config Card (Unity only, separate full-width card) -->
    <div v-if="game.isUnityGame" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.25s' : '0.2s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon translate">
            <NIcon :size="16"><TranslateOutlined /></NIcon>
          </span>
          翻译配置
        </h2>
      </div>
      <ConfigPanel
        :config="config"
        :disabled="!isInstalled"
        :game-id="gameId"
      />
    </div>

    <!-- AI Translation Engine Card (only when fully installed) -->
    <div v-if="game.isUnityGame && isInstalled" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.3s' : '0.25s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon ai">
            <NIcon :size="16"><SmartToyOutlined /></NIcon>
          </span>
          AI 翻译引擎
        </h2>
        <div v-if="aiEndpointInstalled !== null" class="ai-status-badge" :class="{ installed: aiEndpointInstalled }">
          <span class="ai-status-dot"></span>
          {{ aiEndpointInstalled ? '已安装' : '未安装' }}
        </div>
      </div>

      <div class="ai-endpoint-content">
        <div class="ai-endpoint-desc">
          <p v-if="aiEndpointInstalled">
            AI 翻译引擎已部署到游戏内，启动游戏后可通过工具箱的 AI 翻译功能实时翻译游戏文本。
          </p>
          <p v-else>
            安装 AI 翻译引擎后，游戏将通过工具箱连接 LLM 进行实时翻译。需要在 AI 翻译页面配置 API Key。
          </p>
        </div>
        <div class="ai-endpoint-actions">
          <NButton
            v-if="!aiEndpointInstalled"
            type="primary"
            :loading="aiEndpointLoading"
            @click="handleInstallAiEndpoint"
          >
            <template #icon><NIcon :size="16"><SmartToyOutlined /></NIcon></template>
            安装 AI 翻译引擎
          </NButton>
          <NButton
            v-else
            type="error"
            ghost
            :loading="aiEndpointLoading"
            @click="handleUninstallAiEndpoint"
          >
            卸载引擎
          </NButton>
        </div>
      </div>
    </div>

    <!-- Asset Extraction & Pre-Translation Card -->
    <div v-if="game.isUnityGame && isInstalled" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.35s' : '0.3s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><ViewInArOutlined /></NIcon>
          </span>
          资产提取与预翻译
        </h2>
        <NButton size="small" type="primary" @click="router.push(`/games/${gameId}/asset-extraction`)">
          打开
        </NButton>
      </div>
      <p class="asset-extraction-desc">
        从游戏资产中提取文本，自动检测游戏语言，并使用 AI 进行预翻译。预翻译的结果会写入翻译缓存，游戏启动时即可使用。
      </p>
    </div>

    <!-- Translation Editor Card -->
    <div v-if="game.isUnityGame && isInstalled" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.4s' : '0.35s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><DriveFileRenameOutlineOutlined /></NIcon>
          </span>
          译文编辑器
        </h2>
        <NButton size="small" type="primary" @click="router.push(`/games/${gameId}/translation-editor`)">
          打开
        </NButton>
      </div>
      <p class="asset-extraction-desc">
        可视化编辑游戏的自动翻译文件，修改、新增或删除翻译条目，支持导入和导出翻译文件。
      </p>
    </div>

    <!-- Glossary Editor Card -->
    <div v-if="game.isUnityGame && isInstalled" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.45s' : '0.4s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><MenuBookOutlined /></NIcon>
          </span>
          AI 翻译术语库
        </h2>
        <NButton size="small" type="primary" @click="router.push(`/games/${gameId}/glossary-editor`)">
          打开
        </NButton>
      </div>
      <p class="asset-extraction-desc">
        管理该游戏的专用术语表，术语会自动注入翻译提示词并对结果进行后处理替换，支持导入和导出。
      </p>
    </div>

    <!-- AI Description Card (available for all Unity games, even before install) -->
    <div v-if="game.isUnityGame" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.5s' : '0.45s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><DescriptionOutlined /></NIcon>
          </span>
          游戏背景描述
        </h2>
      </div>
      <p class="ai-description-hint">
        为该游戏填写背景信息（类型、世界观、角色等），AI 翻译时会自动参考，提升翻译的准确性和一致性。
      </p>
      <NInput
        v-model:value="aiDescription"
        type="textarea"
        :rows="4"
        placeholder="例如：这是一款日式 RPG 游戏，背景设定在中世纪奇幻世界。主角是一名年轻的剑士，同伴包括女法师艾拉和精灵弓手雷恩..."
      />
    </div>

    <!-- Plugin Package Card -->
    <div v-if="game.isUnityGame" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.55s' : '0.5s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><InventoryOutlined /></NIcon>
          </span>
          汉化包
        </h2>
      </div>
      <p class="asset-extraction-desc">
        将当前游戏的全部插件和翻译文件打包为 ZIP 汉化补丁，方便分享给其他玩家。其他玩家解压到游戏目录即可使用。导入汉化包会覆盖同名文件。
      </p>
      <div class="pkg-actions">
        <NButton
          type="primary"
          :loading="packageExporting"
          :disabled="!isInstalled"
          @click="handleExportPackage"
        >
          <template #icon><NIcon><InventoryOutlined /></NIcon></template>
          生成汉化包
        </NButton>
        <NButton
          :loading="packageImporting"
          @click="handleImportPackage"
        >
          <template #icon><NIcon><FileUploadOutlined /></NIcon></template>
          导入汉化包
        </NButton>
      </div>
    </div>

    <!-- Icon Context Menu -->
    <NDropdown
      trigger="manual"
      :show="showIconContextMenu"
      :options="iconContextMenuOptions"
      :x="iconContextMenuX"
      :y="iconContextMenuY"
      placement="bottom-start"
      @clickoutside="showIconContextMenu = false"
      @select="handleIconContextMenuSelect"
    />

    <!-- Hidden file input for icon upload -->
    <input
      ref="iconFileInput"
      type="file"
      accept="image/jpeg,image/png,image/webp"
      style="display: none"
      @change="handleIconFileSelect"
    />

    <!-- Cover Picker Modal -->
    <CoverPickerModal
      v-if="showCoverPicker"
      :show="showCoverPicker"
      :game="game"
      @update:show="showCoverPicker = $event"
      @saved="loadGame()"
    />
  </div>
</template>

<style scoped>
.detail-page {
  /* Full width - no max-width restriction */
}

.detail-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 300px;
}

.loading-spinner {
  width: 36px;
  height: 36px;
  border: 2px solid var(--border);
  border-top-color: var(--accent);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

/* ===== Header ===== */
.detail-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.back-button {
  display: flex;
  align-items: center;
  gap: 6px;
  background: none;
  border: none;
  color: var(--text-2);
  cursor: pointer;
  padding: 8px 12px 8px 8px;
  border-radius: var(--radius-sm);
  font-size: 14px;
  font-family: var(--font-body);
  transition: all 0.2s ease;
}

.back-button:hover {
  background: var(--bg-muted);
  color: var(--text-1);
}

.back-button:hover svg {
  transform: translateX(-2px);
}

.back-button svg {
  transition: transform 0.2s ease;
}

/* ===== Game Title ===== */
.game-title-section {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 28px;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.title-icon {
  width: 56px;
  height: 56px;
  flex-shrink: 0;
  border-radius: 14px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  position: relative;
  box-shadow: var(--shadow-card);
  cursor: pointer;
  transition: border-color 0.2s ease;
}

.title-icon:hover {
  border-color: var(--accent-border);
}

.title-icon-edit {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.5);
  opacity: 0;
  transition: opacity 0.2s ease;
  border-radius: 13px;
}

.title-icon:hover .title-icon-edit {
  opacity: 1;
}

.title-icon-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  image-rendering: -webkit-optimize-contrast;
  border-radius: 13px;
}

.title-icon-fallback {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  opacity: 0;
}

.title-icon-fallback.visible {
  opacity: 1;
}

.game-title {
  font-family: var(--font-display);
  font-size: 30px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.03em;
  cursor: default;
}

.game-title-input {
  flex: 1;
  min-width: 0;
}

.edit-name-btn {
  background: none;
  border: none;
  cursor: pointer;
  padding: 6px;
  border-radius: var(--radius-sm);
  opacity: 0;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.game-title-section:hover .edit-name-btn {
  opacity: 1;
}

.edit-name-btn:hover {
  background: var(--bg-muted);
}

.title-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 5px 14px;
  border-radius: 20px;
  background: var(--bg-subtle-hover);
  border: 1px solid var(--border);
  flex-shrink: 0;
}

.status-indicator {
  width: 8px;
  height: 8px;
  border-radius: 50%;
}

.status-installed {
  background: var(--success);
  box-shadow: 0 0 10px rgba(52, 211, 153, 0.5);
  animation: pulse 2s ease-in-out infinite;
}

.status-partial {
  background: var(--warning);
  box-shadow: 0 0 8px rgba(251, 191, 36, 0.4);
}

.status-none {
  background: var(--text-3);
}

.status-text {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-2);
}

/* ===== Section Card ===== */
.section-card {
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 24px;
  margin-bottom: 16px;
  animation: slideUp 0.5s var(--ease-out) backwards;
  transition: border-color 0.3s ease, background 0.3s ease, box-shadow 0.3s ease;
  box-shadow: var(--shadow-card-rest);
}

.section-card:hover {
  border-color: var(--border-hover);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
}

.section-title {
  font-family: var(--font-display);
  font-size: 17px;
  font-weight: 600;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.01em;
  display: flex;
  align-items: center;
  gap: 10px;
}

.section-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 8px;
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

.section-icon.warning {
  background: color-mix(in srgb, var(--warning) 10%, transparent);
  color: var(--warning);
}

.header-btn-group {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  align-items: center;
}

/* ===== Info Grid (card-style items) ===== */
.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
  gap: 12px;
}

.info-card {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 14px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease, background 0.2s ease;
}

.info-card:hover {
  border-color: var(--border-hover);
  background: var(--bg-subtle-hover);
}

.info-card-icon {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--radius-sm);
  flex-shrink: 0;
  background: var(--accent-soft);
  color: var(--accent);
}


.info-card-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.info-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.info-value {
  font-size: 14px;
  color: var(--text-1);
}

.info-value.mono {
  font-family: var(--font-mono);
  font-size: 12px;
  color: var(--text-2);
  word-break: break-all;
  line-height: 1.5;
}

.info-value.muted {
  color: var(--text-3);
}

/* ===== Install CTA Horizontal (Uninstalled State) ===== */
.install-cta-horizontal {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
}

.install-cta-horizontal .cta-left {
  display: flex;
  align-items: center;
  gap: 16px;
}

.install-cta-horizontal .cta-text {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.install-cta-horizontal .cta-title {
  font-family: var(--font-display);
  font-size: 15px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.01em;
}

.install-cta-horizontal .cta-desc {
  font-size: 13px;
  color: var(--text-3);
  line-height: 1.5;
}

/* ===== Installed State Horizontal ===== */
.installed-info-horizontal {
  display: flex;
  align-items: stretch;
  gap: 12px;
}

.installed-info-horizontal .version-card {
  flex: 1;
  min-width: 0;
}

.installed-hint-inline {
  display: flex;
  align-items: center;
  font-size: 12px;
  color: var(--text-3);
  padding: 0 12px;
  border-left: 1px solid var(--border);
  white-space: nowrap;
  flex-shrink: 0;
}

/* ===== Framework List ===== */
.framework-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.framework-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  gap: 12px;
}

.framework-info {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.framework-name {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-1);
}

.framework-version {
  font-family: var(--font-mono);
  font-size: 12px;
  color: var(--text-2);
}

.framework-xunity {
  font-size: 11px;
  color: var(--warning);
  background: rgba(251, 191, 36, 0.08);
  padding: 2px 8px;
  border-radius: 10px;
  border: 1px solid rgba(251, 191, 36, 0.15);
}

/* ===== CTA Visual ===== */
.cta-visual {
  width: 52px;
  height: 52px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 14px;
  background: var(--accent-soft);
  border: 1px solid var(--accent-border);
  color: var(--accent);
  animation: breathe 3s ease-in-out infinite;
  flex-shrink: 0;
}

.install-button {
  position: relative;
  flex-shrink: 0;
}

.version-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 14px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease;
}

.version-card:hover {
  border-color: var(--accent-border);
}

.version-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--accent);
}

.version-card-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-2);
  letter-spacing: 0.01em;
}

.version-card-value {
  font-family: var(--font-mono);
  font-size: 18px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.02em;
}

.version-card-desc {
  font-size: 11px;
  color: var(--text-3);
  letter-spacing: 0.02em;
}


/* ===== AI Endpoint Section ===== */
.ai-status-badge {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  border-radius: 20px;
  font-size: 12px;
  font-weight: 500;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  color: var(--text-3);
}

.ai-status-badge.installed {
  background: rgba(52, 211, 153, 0.08);
  border-color: rgba(52, 211, 153, 0.2);
  color: #34d399;
}

.ai-status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--text-3);
}

.ai-status-badge.installed .ai-status-dot {
  background: #34d399;
  box-shadow: 0 0 6px rgba(52, 211, 153, 0.5);
}

.ai-endpoint-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 20px;
}

.ai-endpoint-desc {
  flex: 1;
  min-width: 0;
}

.ai-endpoint-desc p {
  margin: 0;
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.6;
}

.asset-extraction-desc {
  margin: 0;
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.6;
}

.pkg-actions {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-top: 12px;
}

.ai-endpoint-actions {
  flex-shrink: 0;
}

/* ===== AI Description ===== */

.ai-description-hint {
  margin: 0 0 12px;
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.6;
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .game-title-section {
    flex-wrap: wrap;
    gap: 12px;
  }

  .game-title {
    font-size: 22px;
    flex: 1;
    min-width: 0;
    word-break: break-word;
  }

  .title-status {
    flex-shrink: 0;
  }

  .section-card {
    padding: 16px;
  }

  .info-grid {
    grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
    gap: 10px;
  }

  .version-card-value {
    font-size: 15px;
  }

  .install-cta-horizontal {
    flex-direction: column;
    align-items: stretch;
    text-align: center;
  }

  .install-cta-horizontal .cta-left {
    flex-direction: column;
    align-items: center;
  }

  .installed-info-horizontal {
    flex-direction: column;
  }

  .installed-hint-inline {
    border-left: none;
    border-top: 1px solid var(--border);
    padding: 12px 0 0;
    justify-content: center;
  }

  .ai-endpoint-content {
    flex-direction: column;
    align-items: stretch;
    text-align: center;
  }
}

@media (max-width: 480px) {
  .section-header {
    flex-wrap: wrap;
    gap: 10px;
  }

  .header-btn-group {
    gap: 6px;
    width: 100%;
  }

  .game-title-section {
    gap: 10px;
  }

  .title-icon {
    width: 44px;
    height: 44px;
    border-radius: 10px;
  }

  .game-title {
    font-size: 20px;
  }

  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }

  .info-grid {
    grid-template-columns: 1fr;
  }

  .install-cta-horizontal .install-button {
    width: 100%;
  }
}
</style>
