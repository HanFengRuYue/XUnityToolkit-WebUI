<script setup lang="ts">
import { ref, reactive, h, onMounted, onBeforeUnmount, computed, watch, nextTick } from 'vue'
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
  GetAppOutlined,
  TranslateOutlined,
  WarningAmberOutlined,
  ExtensionOutlined,
  WidgetsOutlined,
  DataObjectOutlined,
  SmartToyOutlined,
  MenuBookOutlined,
  DescriptionOutlined,
  PhotoCameraOutlined,
  DriveFileRenameOutlineOutlined,
  CloudUploadOutlined,
  DeleteOutlineOutlined,
  Inventory2Outlined,
  FileUploadOutlined,
  ImageSearchOutlined,
  WallpaperOutlined,
  FontDownloadOutlined,
  ArticleOutlined,
  ExpandMoreOutlined,
  RefreshOutlined,
} from '@vicons/material'
import { useGamesStore } from '@/stores/games'
import { useInstallStore } from '@/stores/install'
import type { Game, XUnityConfig, ModFrameworkType } from '@/api/types'
import { gamesApi, pluginPackageApi, dialogApi } from '@/api/games'
import ConfigPanel from '@/components/config/ConfigPanel.vue'
import PluginHealthCard from '@/components/health/PluginHealthCard.vue'
import { useAutoSave } from '@/composables/useAutoSave'
import { defineAsyncComponent } from 'vue'

const CoverPickerModal = defineAsyncComponent(
  () => import('@/components/library/CoverPickerModal.vue')
)
const IconPickerModal = defineAsyncComponent(
  () => import('@/components/library/IconPickerModal.vue')
)
const BackgroundPickerModal = defineAsyncComponent(
  () => import('@/components/library/BackgroundPickerModal.vue')
)

const collapsed = reactive({ config: true, description: true, tools: false })

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
const showIconPicker = ref(false)
const showBackgroundPicker = ref(false)
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
  { label: '更换背景', key: 'background', icon: () => h(NIcon, { size: 16 }, { default: () => h(WallpaperOutlined) }) },
  { label: '搜索图标', key: 'web-icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(ImageSearchOutlined) }) },
  { label: '上传自定义图标', key: 'upload-icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(CloudUploadOutlined) }) },
  { label: '删除自定义图标', key: 'delete-icon', icon: () => h(NIcon, { size: 16 }, { default: () => h(DeleteOutlineOutlined) }) },
  { label: '删除背景图', key: 'delete-background', icon: () => h(NIcon, { size: 16 }, { default: () => h(DeleteOutlineOutlined) }) },
]

const iconUrl = computed(() => game.value ? `/api/games/${gameId}/icon?t=${game.value.updatedAt}` : '')
const bgTimestamp = ref(Date.now())
const noBackground = computed(() => game.value?.hasBackground === false)
const backgroundUrl = computed(() => {
  if (!game.value || noBackground.value) return ''
  return `${gamesApi.getBackgroundUrl(gameId)}?t=${bgTimestamp.value}`
})
const heroBgLoaded = ref(false)
const heroScrollY = ref(0)

function onHeroBgLoad() {
  heroBgLoaded.value = true
}

function onHeroBgError() {
  heroBgLoaded.value = false
}

// Parallax scroll for hero background
const scrollContainer = ref<HTMLElement | null>(null)
function onScroll() {
  if (scrollContainer.value) {
    heroScrollY.value = scrollContainer.value.scrollTop
  }
}

onMounted(() => {
  // Find the scroll container (parent .main-content)
  nextTick(() => {
    const el = document.querySelector('.main-content')
    if (el) {
      scrollContainer.value = el as HTMLElement
      el.addEventListener('scroll', onScroll, { passive: true })
    }
  })
})

onBeforeUnmount(() => {
  if (scrollContainer.value) {
    scrollContainer.value.removeEventListener('scroll', onScroll)
  }
})

const isInstalled = computed(
  () => game.value?.installState === 'FullyInstalled',
)
const isInstalling = computed(() => {
  if (!installStore.status) return false
  if (installStore.activeGameId !== gameId) return false
  const step = installStore.status.step
  return step !== 'Idle' && step !== 'Complete' && step !== 'Failed'
})
const installStepLabel = computed(() => {
  const labels: Record<string, string> = {
    DetectingGame: '检测游戏',
    InstallingBepInEx: '安装 BepInEx',
    InstallingXUnity: '安装 XUnity',
    InstallingTmpFont: '安装 TMP 字体',
    InstallingAiTranslation: '部署 AI 翻译引擎',
    GeneratingConfig: '生成配置',
    ApplyingConfig: '应用最佳配置',
    ExtractingAssets: '提取游戏资产',
    VerifyingHealth: '验证插件状态',
  }
  return labels[installStore.status?.step ?? ''] ?? '安装中'
})
const hasBepInEx = computed(
  () =>
    game.value?.installState === 'BepInExOnly' ||
    game.value?.installState === 'FullyInstalled' ||
    game.value?.installState === 'PartiallyInstalled',
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
    content: '将从游戏库中移除此游戏（不会删除游戏文件）。移除后该游戏的术语库、翻译缓存等数据将被清除，重新添加后无法恢复。建议先导出术语库。确定吗？',
    positiveText: '确认移除',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await gamesStore.removeGame(gameId)
        message.success('已移除')
        router.push('/')
      } catch {
        message.error('移除失败')
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
  } else if (key === 'web-icon') {
    showIconPicker.value = true
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
  } else if (key === 'background') {
    showBackgroundPicker.value = true
  } else if (key === 'delete-background') {
    try {
      await gamesApi.deleteBackground(gameId)
      if (game.value) game.value.hasBackground = false
      heroBgLoaded.value = false
      bgTimestamp.value = Date.now()
      message.success('背景图已删除')
    } catch {
      message.error('删除背景图失败')
    }
  }
}

const iconFileInput = ref<HTMLInputElement | null>(null)

async function handleIconSaved() {
  await gamesStore.refreshGame(gameId)
  if (game.value) game.value = await gamesApi.get(gameId)
}

function handleBackgroundSaved() {
  if (game.value) game.value.hasBackground = true
  bgTimestamp.value = Date.now()
  heroBgLoaded.value = false
}

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

async function handleReinstallAiEndpoint() {
  aiEndpointLoading.value = true
  try {
    const result = await gamesApi.installAiEndpoint(gameId)
    aiEndpointInstalled.value = result.installed
    message.success('AI 翻译引擎已重装')
  } catch (e) {
    message.error(e instanceof Error ? e.message : '重装失败')
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
onBeforeUnmount(() => stopWatch())
</script>

<template>
  <!-- Loading -->
  <div v-if="loading" class="detail-loading">
    <div class="loading-spinner"></div>
  </div>

  <div v-else-if="game" class="detail-page">
    <!-- Hero Background -->
    <div class="hero-backdrop" :class="{ 'hero-visible': heroBgLoaded }">
      <img
        v-if="!noBackground"
        :src="backgroundUrl"
        class="hero-bg-img"
        alt=""
        :style="{ transform: `translateY(${heroScrollY * 0.3}px) scale(1.05)` }"
        @load="onHeroBgLoad"
        @error="onHeroBgError"
      />
      <div class="hero-gradient-overlay"></div>
      <div class="hero-vignette"></div>
    </div>

    <!-- Hero Header (nav bar over the background) -->
    <div class="hero-nav" style="animation-delay: 0s">
      <button class="back-button" :class="{ 'on-hero': heroBgLoaded }" @click="router.push('/')">
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
          <path d="M12 5L7 10L12 15" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        <span>返回</span>
      </button>
      <div class="hero-nav-actions">
        <button class="hero-action-btn" :title="heroBgLoaded ? '更换背景' : '设置背景'" @click="showBackgroundPicker = true">
          <NIcon :size="16"><WallpaperOutlined /></NIcon>
        </button>
        <button class="hero-action-btn" title="从库中移除" @click="handleRemoveGame">
          <NIcon :size="16"><DeleteOutlineOutlined /></NIcon>
        </button>
      </div>
    </div>

    <!-- Hero Title Banner -->
    <div class="hero-title-banner" :class="{ 'has-hero': heroBgLoaded }" style="animation-delay: 0.05s">
      <div class="title-icon" @click="showIconPicker = true" @contextmenu="handleIconContextMenu" title="左键更换图标 / 右键更多操作">
        <img
          :src="iconUrl"
          :alt="game.name"
          class="title-icon-img"
          @error="($event.target as HTMLImageElement).style.display = 'none'; ($event.target as HTMLImageElement).nextElementSibling?.classList.add('visible')"
        />
        <div class="title-icon-fallback">
          <NIcon :size="36" color="var(--text-3)">
            <GamepadFilled />
          </NIcon>
        </div>
        <div class="title-icon-edit">
          <NIcon :size="16" color="#fff">
            <PhotoCameraOutlined />
          </NIcon>
        </div>
      </div>
      <div class="title-text-group">
        <div class="title-name-row">
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
            <NIcon :size="16" color="currentColor">
              <DriveFileRenameOutlineOutlined />
            </NIcon>
          </button>
        </div>
        <div class="title-meta-row">
          <div class="title-status" :class="{ 'on-hero': heroBgLoaded }">
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
        <div class="header-actions">
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
              <NIcon :size="18"><WidgetsOutlined /></NIcon>
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
            <NIcon :size="16"><GetAppOutlined /></NIcon>
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

      <!-- Installing State -->
      <div v-if="!isInstalled && isInstalling" class="install-cta-horizontal installing">
        <div class="cta-left">
          <div class="cta-visual installing">
            <svg class="cta-icon-spin" width="28" height="28" viewBox="0 0 28 28" fill="none">
              <circle cx="14" cy="14" r="11" stroke="currentColor" stroke-width="2.5" opacity="0.2"/>
              <path d="M14 3A11 11 0 0 1 25 14" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"/>
            </svg>
          </div>
          <div class="cta-text">
            <span class="cta-title">正在安装翻译插件</span>
            <span class="cta-desc">{{ installStepLabel }}... · 关闭此面板不会中断安装</span>
          </div>
        </div>
        <NButton
          type="primary"
          size="large"
          @click="installStore.isDrawerOpen = true"
          class="install-button"
        >
          查看进度
        </NButton>
      </div>

      <!-- Uninstalled State -->
      <div v-else-if="!isInstalled" class="install-cta-horizontal">
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
            <span class="cta-desc">将自动安装 BepInEx 框架和 XUnity.AutoTranslator 翻译插件</span>
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

    <!-- Plugin Health Check Card -->
    <PluginHealthCard
      v-if="game.isUnityGame && hasBepInEx && !isInstalling"
      :game-id="gameId"
      :initial-report="installStore.activeGameId === gameId ? installStore.healthReport : null"
      :style="{ animationDelay: otherFrameworks.length > 0 ? '0.25s' : '0.2s' }"
    />

    <!-- Translation Config Card (Unity only, separate full-width card) -->
    <div v-if="game.isUnityGame" class="section-card" :class="{ 'is-collapsed': collapsed.config }" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.3s' : '0.25s' }">
      <div class="section-header collapsible" @click="collapsed.config = !collapsed.config">
        <h2 class="section-title">
          <span class="section-icon translate">
            <NIcon :size="16"><TranslateOutlined /></NIcon>
          </span>
          翻译配置
        </h2>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.config }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>
      <div class="section-body" :class="{ collapsed: collapsed.config }">
        <div class="section-body-inner">
          <ConfigPanel
            :config="config"
            :disabled="!isInstalled"
            :game-id="gameId"
          />
        </div>
      </div>
    </div>

    <!-- AI Translation Engine Card (only when fully installed) -->
    <div v-if="game.isUnityGame && isInstalled" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.35s' : '0.3s' }">
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
          <template v-else>
            <NButton
              ghost
              :loading="aiEndpointLoading"
              @click="handleReinstallAiEndpoint"
            >
              <template #icon><NIcon :size="16"><RefreshOutlined /></NIcon></template>
              重装引擎
            </NButton>
            <NButton
              type="error"
              ghost
              :loading="aiEndpointLoading"
              @click="handleUninstallAiEndpoint"
            >
              卸载引擎
            </NButton>
          </template>
        </div>
      </div>
    </div>

    <!-- Game Tools Card -->
    <div v-if="game.isUnityGame && isInstalled" class="section-card" :class="{ 'is-collapsed': collapsed.tools }" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.4s' : '0.35s' }">
      <div class="section-header collapsible" @click="collapsed.tools = !collapsed.tools">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><ExtensionOutlined /></NIcon>
          </span>
          游戏工具
        </h2>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.tools }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>
      <div class="section-body" :class="{ collapsed: collapsed.tools }">
        <div class="section-body-inner">
          <div class="tool-list">
            <div class="tool-item" @click="router.push(`/games/${gameId}/asset-extraction`)">
              <div class="tool-item-icon">
                <NIcon :size="18"><DataObjectOutlined /></NIcon>
              </div>
              <div class="tool-item-content">
                <span class="tool-item-title">资产提取与预翻译</span>
                <span class="tool-item-desc">从游戏资产中提取文本，使用 AI 进行预翻译</span>
              </div>
              <svg class="tool-item-arrow" width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M6 4L10 8L6 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
            <div class="tool-item" @click="router.push(`/games/${gameId}/translation-editor`)">
              <div class="tool-item-icon">
                <NIcon :size="18"><DriveFileRenameOutlineOutlined /></NIcon>
              </div>
              <div class="tool-item-content">
                <span class="tool-item-title">译文编辑器</span>
                <span class="tool-item-desc">可视化编辑翻译条目，支持导入和导出翻译文件</span>
              </div>
              <svg class="tool-item-arrow" width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M6 4L10 8L6 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
            <div class="tool-item" @click="router.push(`/games/${gameId}/term-editor`)">
              <div class="tool-item-icon">
                <NIcon :size="18"><MenuBookOutlined /></NIcon>
              </div>
              <div class="tool-item-content">
                <span class="tool-item-title">AI 翻译术语库</span>
                <span class="tool-item-desc">管理专用术语表，自动注入翻译提示词</span>
              </div>
              <svg class="tool-item-arrow" width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M6 4L10 8L6 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
            <div class="tool-item" @click="router.push(`/games/${gameId}/font-replacement`)">
              <div class="tool-item-icon">
                <NIcon :size="18"><FontDownloadOutlined /></NIcon>
              </div>
              <div class="tool-item-content">
                <span class="tool-item-title">字体替换</span>
                <span class="tool-item-desc">修改游戏资产中的 TMP 字体，支持扫描和还原</span>
              </div>
              <svg class="tool-item-arrow" width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M6 4L10 8L6 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
            <div v-if="hasBepInEx" class="tool-item" @click="router.push(`/games/${gameId}/bepinex-log`)">
              <div class="tool-item-icon">
                <NIcon :size="18"><ArticleOutlined /></NIcon>
              </div>
              <div class="tool-item-content">
                <span class="tool-item-title">BepInEx 日志</span>
                <span class="tool-item-desc">查看运行日志，支持搜索、过滤和 AI 分析</span>
              </div>
              <svg class="tool-item-arrow" width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M6 4L10 8L6 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
            <div v-if="hasBepInEx" class="tool-item" @click="router.push(`/games/${gameId}/plugin-manager`)">
              <div class="tool-item-icon">
                <NIcon :size="18"><WidgetsOutlined /></NIcon>
              </div>
              <div class="tool-item-content">
                <span class="tool-item-title">插件管理</span>
                <span class="tool-item-desc">安装、卸载和管理第三方 BepInEx 插件</span>
              </div>
              <svg class="tool-item-arrow" width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M6 4L10 8L6 12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- AI Description Card (available for all Unity games, even before install) -->
    <div v-if="game.isUnityGame" class="section-card" :class="{ 'is-collapsed': collapsed.description }" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.45s' : '0.4s' }">
      <div class="section-header collapsible" @click="collapsed.description = !collapsed.description">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><DescriptionOutlined /></NIcon>
          </span>
          游戏背景描述
        </h2>
        <NIcon :size="18" class="collapse-chevron" :class="{ expanded: !collapsed.description }">
          <ExpandMoreOutlined />
        </NIcon>
      </div>
      <div class="section-body" :class="{ collapsed: collapsed.description }">
        <div class="section-body-inner">
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
      </div>
    </div>

    <!-- Plugin Package Card -->
    <div v-if="game.isUnityGame" class="section-card" :style="{ animationDelay: otherFrameworks.length > 0 ? '0.5s' : '0.45s' }">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><Inventory2Outlined /></NIcon>
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
          <template #icon><NIcon><Inventory2Outlined /></NIcon></template>
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

    <!-- Icon Picker Modal -->
    <IconPickerModal
      v-if="showIconPicker && game"
      :show="showIconPicker"
      :game="game"
      @update:show="showIconPicker = $event"
      @saved="handleIconSaved"
    />

    <!-- Background Picker Modal -->
    <BackgroundPickerModal
      v-if="showBackgroundPicker && game"
      :show="showBackgroundPicker"
      :game="game"
      @update:show="showBackgroundPicker = $event"
      @saved="handleBackgroundSaved"
    />
  </div>
</template>

<style scoped>
.detail-page {
  position: relative;
}

/* ===== Hero Background ===== */
.hero-backdrop {
  position: absolute;
  top: -24px;
  left: -28px;
  right: -28px;
  height: 420px;
  overflow: hidden;
  opacity: 0;
  transition: opacity 0.8s cubic-bezier(0.22, 1, 0.36, 1);
  pointer-events: none;
  z-index: 0;
}

.hero-backdrop.hero-visible {
  opacity: 1;
}

.hero-bg-img {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: center 25%;
  filter: brightness(0.65) saturate(1.1);
  will-change: transform;
}

.hero-gradient-overlay {
  position: absolute;
  inset: 0;
  background:
    linear-gradient(
      to bottom,
      transparent 0%,
      transparent 30%,
      color-mix(in srgb, var(--bg-root) 50%, transparent) 65%,
      var(--bg-root) 100%
    );
}

.hero-vignette {
  position: absolute;
  inset: 0;
  box-shadow: inset 0 0 100px 30px color-mix(in srgb, var(--bg-root) 35%, transparent);
}

/* Ensure all content floats above the hero */
.hero-nav,
.hero-title-banner,
.section-card {
  position: relative;
  z-index: 1;
}

/* ===== Section Card Acrylic ===== */
.section-card {
  background: color-mix(in srgb, var(--bg-card) 82%, transparent);
  backdrop-filter: blur(20px) saturate(1.2);
  -webkit-backdrop-filter: blur(20px) saturate(1.2);
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

/* ===== Hero Nav ===== */
.hero-nav {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.hero-nav-actions {
  display: flex;
  gap: 6px;
  align-items: center;
}

.hero-action-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border-radius: 10px;
  border: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(0, 0, 0, 0.25);
  backdrop-filter: blur(16px);
  -webkit-backdrop-filter: blur(16px);
  color: rgba(255, 255, 255, 0.6);
  cursor: pointer;
  transition: all 0.2s ease;
}

.hero-action-btn:hover {
  background: rgba(0, 0, 0, 0.4);
  border-color: rgba(255, 255, 255, 0.15);
  color: #fff;
  transform: scale(1.08);
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

.back-button.on-hero {
  color: rgba(255, 255, 255, 0.75);
  text-shadow: 0 1px 4px rgba(0, 0, 0, 0.4);
}

.back-button:hover {
  background: rgba(255, 255, 255, 0.08);
  color: var(--text-1);
}

.back-button.on-hero:hover {
  color: #fff;
}

.back-button:hover svg {
  transform: translateX(-2px);
}

.back-button svg {
  transition: transform 0.2s ease;
}

/* ===== Hero Title Banner ===== */
.hero-title-banner {
  display: flex;
  align-items: flex-end;
  gap: 20px;
  padding-top: 60px;
  padding-bottom: 24px;
  margin-bottom: 20px;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.hero-title-banner.has-hero {
  padding-top: 100px;
  padding-bottom: 28px;
  margin-bottom: 24px;
}

.title-text-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  flex: 1;
}

.title-name-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.title-meta-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.title-icon {
  width: 80px;
  height: 80px;
  flex-shrink: 0;
  border-radius: 18px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  position: relative;
  box-shadow: var(--shadow-card);
  cursor: pointer;
  transition: all 0.25s ease;
}

.hero-title-banner.has-hero .title-icon {
  box-shadow: 0 6px 28px rgba(0, 0, 0, 0.5), 0 0 0 1px rgba(255, 255, 255, 0.1);
  border-color: rgba(255, 255, 255, 0.1);
}

.title-icon:hover {
  border-color: var(--accent-border);
  transform: scale(1.04);
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
  border-radius: 17px;
}

.title-icon:hover .title-icon-edit {
  opacity: 1;
}

.title-icon-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  image-rendering: -webkit-optimize-contrast;
  border-radius: 17px;
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
  font-size: 28px;
  font-weight: 700;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.03em;
  cursor: default;
  line-height: 1.2;
}

.hero-title-banner.has-hero .game-title {
  color: #fff;
  text-shadow: 0 2px 8px rgba(0, 0, 0, 0.5), 0 1px 2px rgba(0, 0, 0, 0.3);
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
  color: var(--text-3);
}

.hero-title-banner.has-hero .edit-name-btn {
  color: rgba(255, 255, 255, 0.5);
}

.title-name-row:hover .edit-name-btn {
  opacity: 1;
}

.edit-name-btn:hover {
  background: rgba(255, 255, 255, 0.08);
}

.title-status {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 4px 12px;
  border-radius: 20px;
  background: var(--bg-subtle-hover);
  border: 1px solid var(--border);
  flex-shrink: 0;
}

.title-status.on-hero {
  background: rgba(0, 0, 0, 0.3);
  border-color: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
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
  font-size: 12px;
  font-weight: 500;
  color: var(--text-2);
}

.hero-title-banner.has-hero .status-text {
  color: rgba(255, 255, 255, 0.7);
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

.section-icon.warning {
  background: color-mix(in srgb, var(--warning) 10%, transparent);
  color: var(--warning);
}

/* .header-actions is defined globally in main.css */

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

/* Installing state */
.cta-visual.installing {
  background: color-mix(in srgb, var(--accent) 12%, transparent);
  border-color: var(--accent-border);
  animation: none;
}

.cta-icon-spin {
  color: var(--accent);
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
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
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

/* ===== AI Description ===== */

.ai-description-hint {
  margin: 0 0 12px;
  font-size: 13px;
  color: var(--text-2);
  line-height: 1.6;
}

/* ===== Tool List ===== */
.tool-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.tool-item {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 14px 16px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  cursor: pointer;
  transition: all 0.2s ease;
}

.tool-item:hover {
  border-color: var(--accent-border);
  background: var(--bg-subtle-hover);
  transform: translateX(4px);
}

.tool-item-icon {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--radius-sm);
  background: var(--accent-soft);
  color: var(--accent);
  flex-shrink: 0;
}

.tool-item-content {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.tool-item-title {
  font-size: 14px;
  font-weight: 500;
  color: var(--text-1);
}

.tool-item-desc {
  font-size: 12px;
  color: var(--text-3);
  line-height: 1.4;
}

.tool-item-arrow {
  color: var(--text-3);
  flex-shrink: 0;
  opacity: 0;
  transform: translateX(-4px);
  transition: all 0.2s ease;
}

.tool-item:hover .tool-item-arrow {
  opacity: 1;
  transform: translateX(0);
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .hero-backdrop {
    top: -20px;
    left: -20px;
    right: -20px;
    height: 320px;
  }

  .hero-title-banner {
    padding-top: 40px;
    gap: 14px;
  }

  .hero-title-banner.has-hero {
    padding-top: 70px;
  }

  .title-icon {
    width: 64px;
    height: 64px;
    border-radius: 14px;
  }

  .game-title {
    font-size: 22px;
    word-break: break-word;
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
  .hero-backdrop {
    top: -16px;
    left: -12px;
    right: -12px;
    height: 250px;
  }

  .hero-title-banner {
    padding-top: 30px;
    gap: 12px;
  }

  .hero-title-banner.has-hero {
    padding-top: 50px;
  }

  .title-icon {
    width: 56px;
    height: 56px;
    border-radius: 12px;
  }

  .section-header {
    flex-wrap: wrap;
    gap: 10px;
  }

  .header-actions {
    gap: 6px;
    width: 100%;
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
