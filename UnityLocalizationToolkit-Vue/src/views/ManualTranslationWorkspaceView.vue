<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NAlert, NButton, NEmpty, NIcon, NInput, NSelect, NSpin, NSwitch, NTag, useMessage } from 'naive-ui'
import {
  ArticleOutlined,
  DataObjectOutlined,
  FileDownloadOutlined,
  FontDownloadOutlined,
  GridViewRound,
  Inventory2Outlined,
  RefreshOutlined,
  SaveOutlined,
  SearchOutlined,
  TranslateOutlined,
  ViewListRound,
  WallpaperOutlined,
} from '@vicons/material'
import type {
  Game,
  ManualTranslationApplyResult,
  ManualTranslationAssetContent,
  ManualTranslationAssetEntry,
  ManualTranslationAssetFilterValueKind,
  ManualTranslationAssetValueKind,
  ManualTranslationStatus,
} from '@/api/types'
import { gamesApi, manualTranslationApi } from '@/api/games'
import GameWorkspaceHero from '@/components/manual/GameWorkspaceHero.vue'
import ManualTranslationFontPanel from '@/components/manual/ManualTranslationFontPanel.vue'
import {
  DEFAULT_MANUAL_KIND,
  DEFAULT_MANUAL_PAGE_SIZE,
  MANUAL_KIND_OPTIONS,
  MANUAL_PAGE_SIZE_OPTIONS,
  formatManualWorkspaceState,
  getManualKindCount,
  getManualKindDescription,
  getManualKindLabel,
  getManualStorageKindLabel,
  getManualValueKindTagType,
} from '@/components/manual/manualTranslationMeta'
import { useFileExplorer } from '@/composables/useFileExplorer'

defineOptions({ name: 'ManualTranslationWorkspaceView' })

interface RouteState {
  search: string
  valueKind: ManualTranslationAssetFilterValueKind
  editableOnly: boolean
  overriddenOnly: boolean
  page: number
  pageSize: number
  assetId: string
}

interface DetailFact {
  label: string
  value: string
  mono?: boolean
}

type AssetListMode = 'list' | 'gallery'

const IMAGE_LIST_MODE_STORAGE_KEY = 'ult.manual-workspace.image-list-mode'
const RAIL_WIDTH_STORAGE_KEY = 'ult.manual-workspace.rail-width'
const DEFAULT_RAIL_WIDTH = 420
const MIN_RAIL_WIDTH = 340
const MAX_RAIL_WIDTH = 620
const STACKED_BREAKPOINT = 1280
const TOP_CHROME_HIDE_SCROLL = 64
const TOP_CHROME_SHOW_SCROLL = 24
const TOP_CHROME_SCROLL_DELTA = 12

const route = useRoute()
const router = useRouter()
const message = useMessage()
const { selectFile } = useFileExplorer()

const gameId = computed(() => route.params.id as string)

const loading = ref(true)
const listLoading = ref(false)
const contentLoading = ref(false)
const scanning = ref(false)
const saving = ref(false)
const applying = ref(false)
const restoring = ref(false)

const game = ref<Game | null>(null)
const status = ref<ManualTranslationStatus | null>(null)
const assets = ref<ManualTranslationAssetEntry[]>([])
const selectedAsset = ref<ManualTranslationAssetEntry | null>(null)
const selectedContent = ref<ManualTranslationAssetContent | null>(null)
const totalAssets = ref(0)
const draftValue = ref('')
const search = ref('')
const valueKind = ref<ManualTranslationAssetFilterValueKind>(DEFAULT_MANUAL_KIND)
const editableOnly = ref(false)
const overriddenOnly = ref(false)
const currentPage = ref(1)
const pageSize = ref(DEFAULT_MANUAL_PAGE_SIZE)
const assetListMode = ref<AssetListMode>(readImageListModePreference())
const railWidth = ref(readRailWidthPreference())
const viewportWidth = ref(typeof window === 'undefined' ? STACKED_BREAKPOINT : window.innerWidth)
const workspaceHeight = ref<number | null>(null)
const isDraggingRail = ref(false)
const isTopChromeHidden = ref(false)
const assetScroller = ref<HTMLElement | null>(null)
const inspectorBody = ref<HTMLElement | null>(null)
const workspacePage = ref<HTMLElement | null>(null)
const workspaceCanvas = ref<HTMLElement | null>(null)
const failedThumbs = ref<Record<string, true>>({})
const originalPreviewFailed = ref(false)
const overridePreviewFailed = ref(false)
const previewRetryToken = ref(0)

let searchTimer: ReturnType<typeof window.setTimeout> | undefined
let railDragStartX = 0
let railDragStartWidth = DEFAULT_RAIL_WIDTH
let workspaceLayoutFrame: number | null = null
let workspaceResizeObserver: ResizeObserver | null = null
let workspaceScrollHost: HTMLElement | null = null
let workspaceSurfaceScrollSources: HTMLElement[] = []
const scrollTopBySource = new WeakMap<HTMLElement, number>()

const totalPages = computed(() => Math.max(1, Math.ceil(totalAssets.value / pageSize.value)))
const isTextLike = computed(() => selectedAsset.value?.valueKind === 'Text' || selectedAsset.value?.valueKind === 'Code')
const isImageAsset = computed(() => selectedAsset.value?.valueKind === 'Image')
const isFontAsset = computed(() => selectedAsset.value?.valueKind === 'Font')
const isGalleryMode = computed(() => valueKind.value === 'Image' && assetListMode.value === 'gallery')
const hasEditableSelection = computed(() => Boolean(selectedAsset.value?.editable && isTextLike.value))
const shouldStackWorkspace = computed(() => viewportWidth.value <= STACKED_BREAKPOINT)
const canResizeRail = computed(() => !shouldStackWorkspace.value)
const workspaceCanvasStyle = computed(() =>
  !shouldStackWorkspace.value && workspaceHeight.value
    ? { height: `${workspaceHeight.value}px` }
    : undefined,
)
const railShellStyle = computed(() =>
  !shouldStackWorkspace.value
    ? { width: `${railWidth.value}px` }
    : undefined,
)
const workspaceHeroTone = computed<'success' | 'warning' | 'default'>(() => {
  if (!status.value?.hasScan)
    return 'default'
  if (status.value.overrideCount > 0)
    return 'warning'
  return 'success'
})
const workspaceHeroStatusLabel = computed(() =>
  status.value?.hasScan ? formatManualWorkspaceState(status.value.state) : '未扫描',
)
const workspaceHeroSubtitle = computed(() =>
  `当前正在浏览${getManualKindLabel(valueKind.value)}资源。左侧支持按类型、覆盖状态和关键字筛选，右侧固定显示当前资源的详情与编辑器。`,
)
const kindSummaryCards = computed(() =>
  MANUAL_KIND_OPTIONS.map(option => ({
    ...option,
    count: getManualKindCount(option.value, status.value?.kindCounts ?? null),
  })),
)
const currentRangeText = computed(() => {
  if (!totalAssets.value)
    return '当前筛选没有命中的资源'

  const start = (currentPage.value - 1) * pageSize.value + 1
  const end = Math.min(currentPage.value * pageSize.value, totalAssets.value)
  return `当前显示 ${start}-${end} / ${totalAssets.value}`
})
const isSelectedDirty = computed(() => {
  if (!isTextLike.value)
    return false

  const currentValue = selectedContent.value?.overrideText ?? selectedContent.value?.originalText ?? ''
  return draftValue.value !== currentValue
})
const selectedInspectorMeta = computed(() =>
  selectedAsset.value ? getAssetListMetaLine(selectedAsset.value) : '从左侧选择一个资源',
)
const selectedAssetFacts = computed<DetailFact[]>(() => {
  if (!selectedAsset.value)
    return []

  const asset = selectedAsset.value
  const facts: DetailFact[] = [
    { label: '资源文件', value: asset.assetFile, mono: true },
    { label: '存储位置', value: getManualStorageKindLabel(asset.storageKind) },
  ]

  if (asset.bundleEntry)
    facts.push({ label: 'Bundle 条目', value: asset.bundleEntry, mono: true })
  if (asset.relativePath)
    facts.push({ label: '相对路径', value: asset.relativePath, mono: true })
  if (asset.fieldPath)
    facts.push({ label: '字段路径', value: asset.fieldPath, mono: true })
  if (asset.codeLocation)
    facts.push({ label: '代码定位', value: asset.codeLocation, mono: true })
  if (asset.pathId !== undefined)
    facts.push({ label: 'Path ID', value: String(asset.pathId), mono: true })
  if (asset.valueKind === 'Image' && asset.width && asset.height)
    facts.push({ label: '贴图尺寸', value: `${asset.width} × ${asset.height}` })

  return facts
})
const originalImageUrl = computed(() => {
  if (!selectedAsset.value)
    return ''

  const stamp = [
    status.value?.scannedAt ?? 'scan',
    selectedAsset.value.assetId,
    selectedAsset.value.overrideUpdatedAt ?? 'original',
    previewRetryToken.value,
  ].join(':')
  return `${manualTranslationApi.getImagePreviewUrl(gameId.value, selectedAsset.value.assetId, 'Original', 'Full')}&ts=${encodeURIComponent(stamp)}`
})
const overrideImageUrl = computed(() => {
  if (!selectedAsset.value?.overridden)
    return ''

  const stamp = [
    status.value?.scannedAt ?? 'scan',
    selectedAsset.value.assetId,
    selectedAsset.value.overrideUpdatedAt ?? 'override',
    previewRetryToken.value,
  ].join(':')
  return `${manualTranslationApi.getImagePreviewUrl(gameId.value, selectedAsset.value.assetId, 'Override', 'Full')}&ts=${encodeURIComponent(stamp)}`
})

onMounted(async () => {
  await loadWorkspace()
  await syncFromRoute()
  await nextTick()
  attachWorkspaceObservers()
  scheduleWorkspaceLayout()
  window.addEventListener('resize', scheduleWorkspaceLayout, { passive: true })
})

onBeforeUnmount(() => {
  if (searchTimer)
    window.clearTimeout(searchTimer)
  teardownRailResize()
  detachWorkspaceObservers()
  if (workspaceLayoutFrame !== null)
    window.cancelAnimationFrame(workspaceLayoutFrame)
  window.removeEventListener('resize', scheduleWorkspaceLayout)
})

watch(() => route.fullPath, () => {
  void syncFromRoute()
})

watch(search, (value) => {
  if (searchTimer)
    window.clearTimeout(searchTimer)

  searchTimer = window.setTimeout(() => {
    const nextValue = value.trim()
    if (nextValue === parseRouteState().search)
      return

    void replaceRouteState({ search: nextValue, page: 1, assetId: '' })
  }, 220)
})

function clampRailWidth(width: number) {
  return Math.min(MAX_RAIL_WIDTH, Math.max(MIN_RAIL_WIDTH, width))
}

function readImageListModePreference(): AssetListMode {
  if (typeof window === 'undefined')
    return 'list'

  return window.localStorage.getItem(IMAGE_LIST_MODE_STORAGE_KEY) === 'gallery' ? 'gallery' : 'list'
}

function readRailWidthPreference() {
  if (typeof window === 'undefined')
    return DEFAULT_RAIL_WIDTH

  const raw = Number(window.localStorage.getItem(RAIL_WIDTH_STORAGE_KEY) ?? DEFAULT_RAIL_WIDTH)
  return Number.isFinite(raw) ? clampRailWidth(raw) : DEFAULT_RAIL_WIDTH
}

function persistImageListMode(mode: AssetListMode) {
  assetListMode.value = mode
  if (typeof window !== 'undefined')
    window.localStorage.setItem(IMAGE_LIST_MODE_STORAGE_KEY, mode)
}

function persistRailWidth(width: number) {
  railWidth.value = clampRailWidth(width)
  if (typeof window !== 'undefined')
    window.localStorage.setItem(RAIL_WIDTH_STORAGE_KEY, String(railWidth.value))
}

function setTopChromeHidden(hidden: boolean) {
  if (isTopChromeHidden.value === hidden)
    return

  isTopChromeHidden.value = hidden
  scheduleWorkspaceLayout()
}

function syncTopChromeVisibility(scrollTop: number, delta = 0, forceVisible = false) {
  const nextScrollTop = Math.max(0, scrollTop)

  if (forceVisible) {
    setTopChromeHidden(false)
    return
  }

  if (nextScrollTop <= TOP_CHROME_SHOW_SCROLL) {
    setTopChromeHidden(false)
    return
  }

  if (nextScrollTop > TOP_CHROME_HIDE_SCROLL && delta >= TOP_CHROME_SCROLL_DELTA) {
    setTopChromeHidden(true)
    return
  }

  if (delta <= -TOP_CHROME_SCROLL_DELTA)
    setTopChromeHidden(false)
}

function syncScrollSourceState() {
  for (const source of [workspaceScrollHost, assetScroller.value, inspectorBody.value]) {
    if (source)
      scrollTopBySource.set(source, source.scrollTop)
  }
}

function scheduleWorkspaceLayout() {
  if (typeof window === 'undefined')
    return

  viewportWidth.value = window.innerWidth

  if (workspaceLayoutFrame !== null)
    window.cancelAnimationFrame(workspaceLayoutFrame)

  workspaceLayoutFrame = window.requestAnimationFrame(() => {
    workspaceLayoutFrame = null
    updateWorkspaceLayout()
  })
}

function updateWorkspaceLayout() {
  if (typeof window === 'undefined') {
    workspaceHeight.value = null
    return
  }

  viewportWidth.value = window.innerWidth

  if (shouldStackWorkspace.value || !workspaceCanvas.value) {
    workspaceHeight.value = null
    return
  }

  const rect = workspaceCanvas.value.getBoundingClientRect()
  const available = Math.floor(window.innerHeight - rect.top - 24)
  workspaceHeight.value = Math.max(320, available)
}

function attachWorkspaceObservers() {
  detachWorkspaceObservers()

  if (!workspacePage.value)
    return

  if (typeof ResizeObserver !== 'undefined') {
    workspaceResizeObserver = new ResizeObserver(() => {
      scheduleWorkspaceLayout()
    })
    workspaceResizeObserver.observe(workspacePage.value)
  }

  workspaceScrollHost = workspacePage.value.closest('.main-content') as HTMLElement | null
  workspaceSurfaceScrollSources = [assetScroller.value, inspectorBody.value].filter((source): source is HTMLElement => Boolean(source))
  syncScrollSourceState()
  syncTopChromeVisibility(workspaceScrollHost?.scrollTop ?? 0, 0, true)
  workspaceScrollHost?.addEventListener('scroll', handleWorkspaceScroll, { passive: true })
  for (const source of workspaceSurfaceScrollSources)
    source.addEventListener('scroll', handleWorkspaceSurfaceScroll, { passive: true })
}

function detachWorkspaceObservers() {
  workspaceResizeObserver?.disconnect()
  workspaceResizeObserver = null

  if (workspaceScrollHost) {
    workspaceScrollHost.removeEventListener('scroll', handleWorkspaceScroll)
    workspaceScrollHost = null
  }

  for (const source of workspaceSurfaceScrollSources)
    source.removeEventListener('scroll', handleWorkspaceSurfaceScroll)
  workspaceSurfaceScrollSources = []
}

function handleWorkspaceScroll() {
  const scrollTop = workspaceScrollHost?.scrollTop ?? 0
  const lastScrollTop = workspaceScrollHost ? (scrollTopBySource.get(workspaceScrollHost) ?? scrollTop) : scrollTop
  if (workspaceScrollHost)
    scrollTopBySource.set(workspaceScrollHost, scrollTop)
  syncTopChromeVisibility(scrollTop, scrollTop - lastScrollTop)
  scheduleWorkspaceLayout()
}

function handleWorkspaceSurfaceScroll(event: Event) {
  const target = event.target
  if (!(target instanceof HTMLElement))
    return

  if (target !== assetScroller.value && target !== inspectorBody.value)
    return

  const scrollTop = target.scrollTop
  const lastScrollTop = scrollTopBySource.get(target) ?? scrollTop
  scrollTopBySource.set(target, scrollTop)
  syncTopChromeVisibility(scrollTop, scrollTop - lastScrollTop)
  scheduleWorkspaceLayout()
}

function handleRailResizeStart(event: MouseEvent) {
  if (!canResizeRail.value || event.button !== 0)
    return

  event.preventDefault()
  isDraggingRail.value = true
  railDragStartX = event.clientX
  railDragStartWidth = railWidth.value
  document.addEventListener('mousemove', handleRailResizeMove)
  document.addEventListener('mouseup', handleRailResizeEnd)
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
}

function handleRailResizeMove(event: MouseEvent) {
  if (!isDraggingRail.value)
    return

  railWidth.value = clampRailWidth(railDragStartWidth + event.clientX - railDragStartX)
}

function handleRailResizeEnd() {
  if (!isDraggingRail.value)
    return

  persistRailWidth(railWidth.value)
  teardownRailResize()
}

function teardownRailResize() {
  isDraggingRail.value = false
  document.removeEventListener('mousemove', handleRailResizeMove)
  document.removeEventListener('mouseup', handleRailResizeEnd)
  document.body.style.cursor = ''
  document.body.style.userSelect = ''
}

function resetRailWidth() {
  persistRailWidth(DEFAULT_RAIL_WIDTH)
}

function parseRouteState(): RouteState {
  return {
    search: typeof route.query.search === 'string' ? route.query.search : '',
    valueKind: typeof route.query.kind === 'string' && MANUAL_KIND_OPTIONS.some(item => item.value === route.query.kind)
      ? route.query.kind as ManualTranslationAssetFilterValueKind
      : DEFAULT_MANUAL_KIND,
    editableOnly: route.query.editable === '1',
    overriddenOnly: route.query.overridden === '1',
    page: Math.max(1, Number(route.query.page ?? 1) || 1),
    pageSize: Math.max(1, Number(route.query.pageSize ?? DEFAULT_MANUAL_PAGE_SIZE) || DEFAULT_MANUAL_PAGE_SIZE),
    assetId: typeof route.query.asset === 'string' ? route.query.asset : '',
  }
}

function applyRouteState(state: RouteState) {
  search.value = state.search
  valueKind.value = state.valueKind
  editableOnly.value = state.editableOnly
  overriddenOnly.value = state.overriddenOnly
  currentPage.value = state.page
  pageSize.value = state.pageSize
}

function buildQuery(state: RouteState) {
  const query: Record<string, string> = {}
  if (state.search)
    query.search = state.search
  if (state.valueKind !== DEFAULT_MANUAL_KIND)
    query.kind = state.valueKind
  if (state.editableOnly)
    query.editable = '1'
  if (state.overriddenOnly)
    query.overridden = '1'
  if (state.page > 1)
    query.page = String(state.page)
  if (state.pageSize !== DEFAULT_MANUAL_PAGE_SIZE)
    query.pageSize = String(state.pageSize)
  if (state.assetId)
    query.asset = state.assetId
  return query
}

async function replaceRouteState(partial: Partial<RouteState>) {
  const current = parseRouteState()
  const next: RouteState = {
    search: partial.search ?? search.value.trim(),
    valueKind: partial.valueKind ?? valueKind.value,
    editableOnly: partial.editableOnly ?? editableOnly.value,
    overriddenOnly: partial.overriddenOnly ?? overriddenOnly.value,
    page: partial.page ?? currentPage.value,
    pageSize: partial.pageSize ?? pageSize.value,
    assetId: partial.assetId ?? selectedAsset.value?.assetId ?? '',
  }

  if (JSON.stringify(current) === JSON.stringify(next))
    return

  await router.replace({
    path: `/manual-translation/${gameId.value}/assets`,
    query: buildQuery(next),
  })
}

async function loadWorkspace() {
  loading.value = true

  try {
    const [nextGame, nextStatus] = await Promise.all([
      gamesApi.get(gameId.value),
      manualTranslationApi.getStatus(gameId.value),
    ])
    game.value = nextGame
    status.value = nextStatus
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '加载工作区失败')
  }
  finally {
    loading.value = false
    await nextTick()
    scheduleWorkspaceLayout()
  }
}

async function syncFromRoute() {
  const state = parseRouteState()
  applyRouteState(state)

  if (!status.value?.hasScan) {
    assets.value = []
    totalAssets.value = 0
    selectedAsset.value = null
    selectedContent.value = null
    draftValue.value = ''
    await nextTick()
    attachWorkspaceObservers()
    scheduleWorkspaceLayout()
    return
  }

  await loadAssets(state.assetId)
}

async function loadAssets(desiredAssetId = '') {
  listLoading.value = true
  failedThumbs.value = {}

  try {
    const response = await manualTranslationApi.getAssets(gameId.value, {
      search: search.value.trim() || undefined,
      editableOnly: editableOnly.value,
      overriddenOnly: overriddenOnly.value,
      page: currentPage.value,
      pageSize: pageSize.value,
      valueKind: valueKind.value,
    })

    assets.value = response.assets
    totalAssets.value = response.total
    currentPage.value = response.page
    pageSize.value = response.pageSize

    const nextSelected = desiredAssetId
      ? response.assets.find(asset => asset.assetId === desiredAssetId) ?? null
      : response.assets.find(asset => asset.assetId === selectedAsset.value?.assetId) ?? response.assets[0] ?? null

    if (nextSelected) {
      await selectAsset(nextSelected, false)
    }
    else {
      selectedAsset.value = null
      selectedContent.value = null
      draftValue.value = ''
      resetSelectedPreviewState()
    }

    if (response.page !== parseRouteState().page || desiredAssetId !== (nextSelected?.assetId ?? ''))
      await replaceRouteState({ page: response.page, assetId: nextSelected?.assetId ?? '' })
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '加载资源列表失败')
  }
  finally {
    listLoading.value = false
    await nextTick()
    attachWorkspaceObservers()
    scheduleWorkspaceLayout()
  }
}

async function selectAsset(asset: ManualTranslationAssetEntry, syncRoute = true) {
  selectedAsset.value = asset
  contentLoading.value = true
  resetSelectedPreviewState()

  try {
    selectedContent.value = await manualTranslationApi.getAssetContent(gameId.value, asset.assetId)
    draftValue.value = selectedContent.value.overrideText ?? selectedContent.value.originalText ?? ''
    if (syncRoute)
      await replaceRouteState({ assetId: asset.assetId })

    await nextTick()
    if (inspectorBody.value) {
      inspectorBody.value.scrollTop = 0
      scrollTopBySource.set(inspectorBody.value, 0)
    }
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '加载资源详情失败')
  }
  finally {
    contentLoading.value = false
  }
}

async function refreshStatus() {
  status.value = await manualTranslationApi.getStatus(gameId.value)
  game.value = await gamesApi.get(gameId.value)
}

async function handleScan() {
  scanning.value = true
  try {
    await manualTranslationApi.scan(gameId.value)
    message.success('资源索引已刷新')
    await loadWorkspace()
    await syncFromRoute()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '扫描失败')
  }
  finally {
    scanning.value = false
  }
}

async function handleSaveOverride() {
  if (!selectedAsset.value)
    return

  saving.value = true
  try {
    await manualTranslationApi.saveOverride(gameId.value, selectedAsset.value.assetId, draftValue.value, 'manual')
    message.success('文本覆盖已保存')
    await refreshStatus()
    await loadAssets(selectedAsset.value.assetId)
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '保存覆盖失败')
  }
  finally {
    saving.value = false
  }
}

async function handleDeleteOverride() {
  if (!selectedAsset.value)
    return

  saving.value = true
  try {
    if (selectedAsset.value.valueKind === 'Image')
      await manualTranslationApi.deleteImageOverride(gameId.value, selectedAsset.value.assetId)
    else
      await manualTranslationApi.deleteOverride(gameId.value, selectedAsset.value.assetId)

    message.success('覆盖已删除')
    await refreshStatus()
    await loadAssets(selectedAsset.value.assetId)
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '删除覆盖失败')
  }
  finally {
    saving.value = false
  }
}

async function handleChooseImage() {
  if (!selectedAsset.value)
    return

  const filePath = await selectFile({
    title: '选择替换贴图',
    filters: [{ label: '图片', extensions: ['.png', '.jpg', '.jpeg'] }],
  })
  if (!filePath)
    return

  saving.value = true
  try {
    await manualTranslationApi.uploadImageOverrideFromPath(gameId.value, selectedAsset.value.assetId, filePath, 'manual-image')
    message.success('贴图覆盖已保存')
    await refreshStatus()
    await loadAssets(selectedAsset.value.assetId)
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '上传贴图失败')
  }
  finally {
    saving.value = false
  }
}

async function handleApply() {
  applying.value = true
  try {
    const result: ManualTranslationApplyResult = await manualTranslationApi.apply(gameId.value)
    message.success(`已写回 ${result.appliedOverrides} 个覆盖`)
    await refreshStatus()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '应用失败')
  }
  finally {
    applying.value = false
  }
}

async function handleRestore() {
  restoring.value = true
  try {
    await manualTranslationApi.restore(gameId.value)
    message.success('已恢复最近一次备份')
    await refreshStatus()
    await loadAssets(selectedAsset.value?.assetId ?? '')
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '回滚失败')
  }
  finally {
    restoring.value = false
  }
}

function resetSelectedPreviewState() {
  originalPreviewFailed.value = false
  overridePreviewFailed.value = false
  previewRetryToken.value += 1
}

function retrySelectedPreview(kind?: 'original' | 'override') {
  if (!kind || kind === 'original')
    originalPreviewFailed.value = false
  if (!kind || kind === 'override')
    overridePreviewFailed.value = false
  previewRetryToken.value += 1
}

function buildAssetThumbUrl(asset: ManualTranslationAssetEntry) {
  const stamp = [
    status.value?.scannedAt ?? 'scan',
    asset.assetId,
    asset.overrideUpdatedAt ?? 'thumb',
  ].join(':')
  return `${manualTranslationApi.getImagePreviewUrl(gameId.value, asset.assetId, 'Original', 'Thumb')}&ts=${encodeURIComponent(stamp)}`
}

function handleThumbError(assetId: string) {
  if (failedThumbs.value[assetId])
    return

  failedThumbs.value = {
    ...failedThumbs.value,
    [assetId]: true,
  }
}

function isThumbFailed(assetId: string) {
  return Boolean(failedThumbs.value[assetId])
}

function exportSelected() {
  if (!selectedAsset.value)
    return

  window.open(manualTranslationApi.exportAssetUrl(gameId.value, selectedAsset.value.assetId), '_blank', 'noopener')
}

function getAssetListTitle(asset: ManualTranslationAssetEntry) {
  return asset.listTitle || asset.preview || asset.displayName || asset.objectType
}

function getAssetListLocation(asset: ManualTranslationAssetEntry) {
  return asset.listSubtitle || asset.fieldPath || asset.codeLocation || asset.relativePath || asset.bundleEntry || asset.objectType
}

function getAssetListMetaLine(asset: ManualTranslationAssetEntry) {
  const segments = [asset.listMeta || asset.assetFile]

  if (asset.pathId !== undefined)
    segments.push(`Path ID ${asset.pathId}`)
  if (asset.valueKind === 'Image' && asset.width && asset.height)
    segments.push(`${asset.width} × ${asset.height}`)

  return segments.join(' · ')
}

function getSelectedAssetTitle() {
  if (!selectedAsset.value)
    return ''
  return getAssetListTitle(selectedAsset.value)
}

function getSelectedAssetSubtitle() {
  if (!selectedAsset.value)
    return ''
  return getAssetListLocation(selectedAsset.value)
}

function getSelectedAssetIconKind(asset?: ManualTranslationAssetEntry | null): ManualTranslationAssetValueKind | null {
  return asset?.valueKind ?? null
}

function getAssetIconComponent(asset?: ManualTranslationAssetEntry | null) {
  switch (getSelectedAssetIconKind(asset)) {
    case 'Text':
      return ArticleOutlined
    case 'Code':
      return DataObjectOutlined
    case 'Image':
      return WallpaperOutlined
    case 'Font':
      return FontDownloadOutlined
    case 'Binary':
      return Inventory2Outlined
    default:
      return TranslateOutlined
  }
}

function getAssetToneClass(asset?: ManualTranslationAssetEntry | null) {
  switch (getSelectedAssetIconKind(asset)) {
    case 'Text':
      return 'tone-text'
    case 'Code':
      return 'tone-code'
    case 'Image':
      return 'tone-image'
    case 'Font':
      return 'tone-font'
    case 'Binary':
      return 'tone-binary'
    default:
      return 'tone-default'
  }
}

function getImagePreviewFailureTitle(kind: 'original' | 'override') {
  return kind === 'original' ? '原图预览暂不可用' : '替换图预览暂不可用'
}

function getImagePreviewFailureDescription(kind: 'original' | 'override') {
  if (kind === 'override')
    return '可以重新上传替换图，或点击重试重新请求当前预览。'

  return '可以重新扫描资源索引，或导出资源后检查当前纹理格式是否支持预览。'
}

function handleSearchCommit() {
  if (searchTimer)
    window.clearTimeout(searchTimer)

  const nextValue = search.value.trim()
  if (nextValue === parseRouteState().search)
    return

  void replaceRouteState({ search: nextValue, page: 1, assetId: '' })
}

function handleValueKindChange(nextValue: ManualTranslationAssetFilterValueKind) {
  if (nextValue === valueKind.value)
    return

  if (nextValue !== 'Image')
    persistImageListMode('list')

  void replaceRouteState({
    valueKind: nextValue,
    page: 1,
    assetId: '',
  })
}

function handleEditableOnlyChange(nextValue: boolean) {
  void replaceRouteState({
    editableOnly: nextValue,
    page: 1,
    assetId: '',
  })
}

function handleOverriddenOnlyChange(nextValue: boolean) {
  void replaceRouteState({
    overriddenOnly: nextValue,
    page: 1,
    assetId: '',
  })
}

function handlePageSizeChange(nextValue: number) {
  void replaceRouteState({
    pageSize: nextValue,
    page: 1,
    assetId: '',
  })
}

function handlePrevPage() {
  if (currentPage.value <= 1)
    return

  void replaceRouteState({
    page: currentPage.value - 1,
    assetId: '',
  })
}

function handleNextPage() {
  if (currentPage.value >= totalPages.value)
    return

  void replaceRouteState({
    page: currentPage.value + 1,
    assetId: '',
  })
}

function openProjectOverview() {
  void router.push(`/manual-translation/${gameId.value}`)
}

function openXUnityOverview() {
  void router.push(`/games/${gameId.value}`)
}
</script>
<template>
  <div v-if="game" ref="workspacePage" class="sub-page manual-workspace-page">
    <div class="manual-workspace-page__top-chrome" :class="{ 'is-hidden': isTopChromeHidden }">
      <div class="manual-workspace-page__top-chrome-inner">
    <GameWorkspaceHero
      :game="game"
      back-to="/manual-translation"
      back-label="返回项目目录"
      :status-label="workspaceHeroStatusLabel"
      :status-tone="workspaceHeroTone"
      :subtitle="workspaceHeroSubtitle"
      :x-unity-state="game.xUnityStatus.state"
      :manual-state="status?.state ?? game.manualTranslationStatus.state"
      :manual-overrides="status?.overrideCount ?? game.manualTranslationStatus.overrideCount"
    >
      <template #nav-actions>
        <NButton quaternary size="small" @click="openProjectOverview">
          手动翻译概览
        </NButton>
      </template>
    </GameWorkspaceHero>

    <section class="section-card manual-workspace-page__action-card" style="animation-delay: 0.08s">
      <div class="manual-workspace-page__action-copy">
        <h2 class="section-title">工作区操作</h2>
        <div class="manual-workspace-page__section-meta">
          这里保留扫描、写回和回滚。具体资源的编辑与上传，则固定放在右侧 inspector。
        </div>
      </div>

      <div class="manual-workspace-page__action-row">
        <NButton type="primary" :loading="scanning" @click="handleScan">
          <template #icon><RefreshOutlined /></template>
          {{ status?.hasScan ? '重新扫描' : '开始扫描' }}
        </NButton>
        <NButton secondary @click="openProjectOverview">
          返回概览页
        </NButton>
        <NButton secondary @click="openXUnityOverview">
          查看 XUnity
        </NButton>
        <NButton secondary :disabled="!status?.hasBackups" :loading="restoring" @click="handleRestore">
          回滚备份
        </NButton>
        <NButton type="primary" ghost :disabled="!status?.overrideCount" :loading="applying" @click="handleApply">
          应用到游戏
        </NButton>
      </div>
    </section>
      </div>
    </div>

    <div v-if="loading" class="manual-workspace-page__loading">
      <NSpin size="large" />
    </div>

    <section v-else-if="!status?.hasScan" class="section-card manual-workspace-page__empty-card" style="animation-delay: 0.12s">
      <NEmpty description="还没有建立手动翻译索引。先扫描一次项目资源。">
        <template #extra>
          <NButton type="primary" :loading="scanning" @click="handleScan">
            开始扫描资源
          </NButton>
        </template>
      </NEmpty>
    </section>

    <div
      v-else
      ref="workspaceCanvas"
      class="manual-workspace__canvas"
      :class="{ 'is-stacked': shouldStackWorkspace }"
      :style="workspaceCanvasStyle"
      style="animation-delay: 0.12s"
    >
      <div class="manual-workspace__rail-shell" :style="railShellStyle">
        <section class="section-card manual-workspace__rail">
          <div class="manual-workspace__rail-controls">
            <div class="section-header manual-workspace__rail-header">
              <div>
                <h2 class="section-title">资源列表</h2>
                <div class="manual-workspace-page__section-meta">{{ currentRangeText }}</div>
              </div>
            </div>

            <div class="manual-workspace__kind-strip">
              <button
                v-for="item in kindSummaryCards"
                :key="item.value"
                class="manual-kind-filter"
                :class="{ active: valueKind === item.value }"
                type="button"
                @click="handleValueKindChange(item.value)"
              >
                <span class="manual-kind-filter__label">{{ item.label }}</span>
                <span class="manual-kind-filter__count">{{ item.count }}</span>
              </button>
            </div>

            <p class="manual-workspace__kind-desc">
              {{ getManualKindDescription(valueKind) }}
            </p>

            <div class="manual-workspace__filter-stack">
              <div class="manual-workspace__search">
                <NInput
                  v-model:value="search"
                  clearable
                  placeholder="搜索文本值、文件名、字段或定位"
                  @keyup.enter="handleSearchCommit"
                >
                  <template #prefix>
                    <NIcon><SearchOutlined /></NIcon>
                  </template>
                </NInput>
              </div>

              <div class="manual-workspace__switch-grid">
                <label class="manual-workspace__switch-row">
                  <span>只看可编辑资源</span>
                  <NSwitch :value="editableOnly" @update:value="handleEditableOnlyChange" />
                </label>
                <label class="manual-workspace__switch-row">
                  <span>只看已覆盖资源</span>
                  <NSwitch :value="overriddenOnly" @update:value="handleOverriddenOnlyChange" />
                </label>
              </div>

              <div v-if="valueKind === 'Image'" class="manual-workspace__display-mode">
                <span>显示方式</span>
                <div class="manual-workspace__display-actions">
                  <button
                    class="manual-workspace__display-btn"
                    :class="{ active: assetListMode === 'list' }"
                    type="button"
                    @click="persistImageListMode('list')"
                  >
                    <NIcon :size="16"><ViewListRound /></NIcon>
                    <span>列表</span>
                  </button>
                  <button
                    class="manual-workspace__display-btn"
                    :class="{ active: assetListMode === 'gallery' }"
                    type="button"
                    @click="persistImageListMode('gallery')"
                  >
                    <NIcon :size="16"><GridViewRound /></NIcon>
                    <span>图库</span>
                  </button>
                </div>
              </div>
            </div>
          </div>

          <div v-if="listLoading" class="manual-workspace__list-state">
            <NSpin size="large" />
          </div>

          <div v-else-if="assets.length === 0" class="manual-workspace__list-state">
            <NEmpty description="当前筛选没有命中的资源。" />
          </div>

          <div v-else ref="assetScroller" class="manual-workspace__asset-scroller">
            <div v-if="isGalleryMode" class="manual-workspace__gallery-grid">
              <button
                v-for="asset in assets"
                :key="asset.assetId"
                class="manual-gallery-card"
                :class="{ active: selectedAsset?.assetId === asset.assetId }"
                type="button"
                @click="selectAsset(asset)"
              >
                <div class="manual-gallery-card__thumb" :class="getAssetToneClass(asset)">
                  <img
                    v-if="!isThumbFailed(asset.assetId)"
                    :src="buildAssetThumbUrl(asset)"
                    :alt="getAssetListTitle(asset)"
                    loading="lazy"
                    @error="handleThumbError(asset.assetId)"
                  >
                  <NIcon v-else :size="24">
                    <WallpaperOutlined />
                  </NIcon>
                </div>

                <div class="manual-gallery-card__copy">
                  <strong>{{ getAssetListTitle(asset) }}</strong>
                  <span>{{ getAssetListLocation(asset) }}</span>
                  <small>{{ getAssetListMetaLine(asset) }}</small>
                </div>

                <div class="manual-gallery-card__tags">
                  <NTag size="small" :bordered="false" :type="getManualValueKindTagType(asset.valueKind)">
                    {{ getManualKindLabel(asset.valueKind) }}
                  </NTag>
                  <NTag v-if="asset.editable" size="small" type="success" :bordered="false">
                    可编辑
                  </NTag>
                  <NTag v-if="asset.overridden" size="small" type="warning" :bordered="false">
                    已覆盖
                  </NTag>
                </div>
              </button>
            </div>

            <div v-else class="manual-workspace__asset-list">
              <button
                v-for="asset in assets"
                :key="asset.assetId"
                class="manual-asset-row"
                :class="{ active: selectedAsset?.assetId === asset.assetId }"
                type="button"
                @click="selectAsset(asset)"
              >
                <div class="manual-asset-row__media" :class="getAssetToneClass(asset)">
                  <img
                    v-if="asset.valueKind === 'Image' && !isThumbFailed(asset.assetId)"
                    :src="buildAssetThumbUrl(asset)"
                    :alt="getAssetListTitle(asset)"
                    loading="lazy"
                    @error="handleThumbError(asset.assetId)"
                  >
                  <NIcon v-else :size="22">
                    <component :is="getAssetIconComponent(asset)" />
                  </NIcon>
                </div>

                <div class="manual-asset-row__main">
                  <div class="manual-asset-row__headline">
                    <div class="manual-asset-row__title">
                      {{ getAssetListTitle(asset) }}
                    </div>

                    <div class="manual-asset-row__badges">
                      <NTag size="small" :bordered="false" :type="getManualValueKindTagType(asset.valueKind)">
                        {{ getManualKindLabel(asset.valueKind) }}
                      </NTag>
                      <NTag v-if="asset.editable" size="small" type="success" :bordered="false">
                        可编辑
                      </NTag>
                      <NTag v-if="asset.overridden" size="small" type="warning" :bordered="false">
                        已覆盖
                      </NTag>
                    </div>
                  </div>

                  <div class="manual-asset-row__subtitle">
                    {{ getAssetListLocation(asset) }}
                  </div>

                  <div class="manual-asset-row__meta">
                    {{ getAssetListMetaLine(asset) }}
                  </div>
                </div>
              </button>
            </div>
          </div>

          <div class="manual-workspace__rail-footer">
            <NSelect
              class="manual-workspace__page-size"
              :value="pageSize"
              :options="MANUAL_PAGE_SIZE_OPTIONS"
              @update:value="handlePageSizeChange"
            />
            <div class="manual-workspace__pagination">
              <NButton secondary size="small" :disabled="currentPage <= 1" @click="handlePrevPage">
                上一页
              </NButton>
              <span>{{ currentPage }} / {{ totalPages }}</span>
              <NButton secondary size="small" :disabled="currentPage >= totalPages" @click="handleNextPage">
                下一页
              </NButton>
            </div>
          </div>
        </section>
      </div>

      <div
        v-if="canResizeRail"
        class="manual-workspace__divider"
        :class="{ 'is-dragging': isDraggingRail }"
        role="separator"
        aria-orientation="vertical"
        @mousedown="handleRailResizeStart"
        @dblclick="resetRailWidth"
      >
        <span class="manual-workspace__divider-handle" />
      </div>

      <section class="section-card manual-workspace__inspector-shell">
        <div v-if="selectedAsset" class="manual-inspector">
          <div class="manual-inspector__header">
            <div class="manual-inspector__hero">
              <div class="manual-inspector__icon" :class="getAssetToneClass(selectedAsset)">
                <NIcon :size="26">
                  <component :is="getAssetIconComponent(selectedAsset)" />
                </NIcon>
              </div>

              <div class="manual-inspector__copy">
                <div class="manual-inspector__tags">
                  <NTag size="small" :bordered="false" :type="getManualValueKindTagType(selectedAsset.valueKind)">
                    {{ getManualKindLabel(selectedAsset.valueKind) }}
                  </NTag>
                  <NTag v-if="selectedAsset.editable" size="small" type="success" :bordered="false">
                    可编辑
                  </NTag>
                  <NTag v-if="selectedAsset.overridden" size="small" type="warning" :bordered="false">
                    已覆盖
                  </NTag>
                </div>
                <h2 class="manual-inspector__title">{{ getSelectedAssetTitle() }}</h2>
                <p class="manual-inspector__subtitle">{{ getSelectedAssetSubtitle() }}</p>
                <div class="manual-inspector__meta">{{ selectedInspectorMeta }}</div>
              </div>
            </div>

            <div class="manual-inspector__header-actions">
              <NButton size="small" secondary :disabled="!selectedAsset.exportable" @click="exportSelected">
                <template #icon><FileDownloadOutlined /></template>
                导出
              </NButton>
              <NButton
                v-if="selectedAsset.overridden"
                size="small"
                tertiary
                :loading="saving"
                @click="handleDeleteOverride"
              >
                删除覆盖
              </NButton>
            </div>
          </div>

          <div ref="inspectorBody" class="manual-inspector__body">
            <div class="manual-inspector__facts">
              <article
                v-for="fact in selectedAssetFacts"
                :key="`${fact.label}:${fact.value}`"
                class="manual-inspector__fact"
              >
                <span>{{ fact.label }}</span>
                <strong :class="{ mono: fact.mono }">{{ fact.value }}</strong>
              </article>
            </div>

            <NAlert
              v-if="selectedAsset.editHint"
              type="info"
              :show-icon="false"
              class="manual-inspector__hint"
            >
              {{ selectedAsset.editHint }}
            </NAlert>

            <div v-if="contentLoading" class="manual-inspector__loading">
              <NSpin size="large" />
            </div>

            <template v-else-if="selectedContent">
              <div v-if="isTextLike" class="manual-text-editor">
                <div class="manual-text-editor__panel">
                  <span class="manual-text-editor__label">原始内容</span>
                  <pre>{{ selectedContent.originalText || '该条目没有可直接渲染的原始文本。' }}</pre>
                </div>

                <div class="manual-text-editor__panel">
                  <span class="manual-text-editor__label">覆盖内容</span>
                  <NInput
                    v-model:value="draftValue"
                    type="textarea"
                    :autosize="{ minRows: 10, maxRows: 20 }"
                    :disabled="!selectedAsset.editable"
                    placeholder="在这里输入手动翻译覆盖文本"
                  />
                </div>
              </div>

              <div v-else-if="isImageAsset" class="manual-image-editor">
                <div class="manual-image-editor__grid">
                  <article class="manual-image-editor__panel">
                    <div class="manual-image-editor__panel-header">
                      <span>原图预览</span>
                      <NButton text size="small" @click="retrySelectedPreview('original')">
                        重试
                      </NButton>
                    </div>
                    <div class="manual-image-editor__panel-body">
                      <img
                        v-if="!originalPreviewFailed"
                        :src="originalImageUrl"
                        :alt="getSelectedAssetTitle()"
                        @error="originalPreviewFailed = true"
                      >
                      <div v-else class="manual-image-editor__fallback">
                        <NAlert type="warning" :show-icon="false">
                          <strong>{{ getImagePreviewFailureTitle('original') }}</strong>
                          <div>{{ getImagePreviewFailureDescription('original') }}</div>
                        </NAlert>
                      </div>
                    </div>
                  </article>

                  <article class="manual-image-editor__panel">
                    <div class="manual-image-editor__panel-header">
                      <span>替换图预览</span>
                      <NButton v-if="selectedAsset.overridden" text size="small" @click="retrySelectedPreview('override')">
                        重试
                      </NButton>
                    </div>
                    <div class="manual-image-editor__panel-body">
                      <img
                        v-if="selectedAsset.overridden && !overridePreviewFailed"
                        :src="overrideImageUrl"
                        :alt="`${getSelectedAssetTitle()} override`"
                        @error="overridePreviewFailed = true"
                      >
                      <div v-else-if="selectedAsset.overridden" class="manual-image-editor__fallback">
                        <NAlert type="warning" :show-icon="false">
                          <strong>{{ getImagePreviewFailureTitle('override') }}</strong>
                          <div>{{ getImagePreviewFailureDescription('override') }}</div>
                        </NAlert>
                      </div>
                      <div v-else class="manual-image-editor__empty">
                        还没有上传替换贴图。
                      </div>
                    </div>
                  </article>
                </div>
              </div>

              <div v-else-if="isFontAsset" class="manual-font-editor">
                <ManualTranslationFontPanel
                  :game-id="gameId"
                  :asset="selectedAsset"
                  @updated="refreshStatus(); loadAssets(selectedAsset.assetId)"
                />
              </div>

              <div v-else class="manual-binary-view">
                <NAlert type="default" :show-icon="false">
                  当前资源以元数据和导出为主，不提供直接文本编辑。
                </NAlert>
              </div>
            </template>
          </div>

          <div v-if="isTextLike || isImageAsset" class="manual-inspector__action-bar">
            <div class="manual-inspector__action-copy">
              <strong>{{ isTextLike ? '覆盖先保存为工作区差异。' : '上传图片后会保存为贴图覆盖。' }}</strong>
              <span>应用到游戏前会自动创建或复用备份，避免直接改坏原始资源。</span>
            </div>
            <div class="manual-inspector__action-buttons">
              <NButton
                v-if="isTextLike"
                type="primary"
                :disabled="!hasEditableSelection || !isSelectedDirty"
                :loading="saving"
                @click="handleSaveOverride"
              >
                <template #icon><SaveOutlined /></template>
                保存覆盖
              </NButton>
              <NButton
                v-else-if="isImageAsset"
                type="primary"
                :loading="saving"
                @click="handleChooseImage"
              >
                <template #icon><WallpaperOutlined /></template>
                上传替换贴图
              </NButton>
            </div>
          </div>
        </div>

        <div v-else class="manual-inspector__empty">
          <NEmpty description="从左侧选择一个资源后，这里会固定显示对应的详情和编辑器。" />
        </div>
      </section>
    </div>
  </div>
</template>
<style scoped>
.manual-workspace-page {
  gap: 14px;
}

.manual-workspace-page__top-chrome {
  max-height: 720px;
  opacity: 1;
  transform: translateY(0);
  overflow-x: visible;
  overflow-y: hidden;
  transform-origin: top;
  transition:
    max-height 0.32s cubic-bezier(0.22, 1, 0.36, 1),
    opacity 0.22s ease,
    transform 0.22s ease;
}

.manual-workspace-page__top-chrome.is-hidden {
  max-height: 0;
  opacity: 0;
  transform: translateY(-18px);
  pointer-events: none;
}

.manual-workspace-page__top-chrome-inner {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.manual-workspace-page__action-card,
.manual-workspace-page__action-row,
.manual-workspace__display-actions,
.manual-workspace__pagination,
.manual-inspector__header,
.manual-inspector__hero,
.manual-inspector__header-actions,
.manual-inspector__tags,
.manual-inspector__action-bar,
.manual-inspector__action-buttons,
.manual-image-editor__panel-header {
  display: flex;
}

.manual-workspace-page__action-card {
  align-items: flex-start;
  justify-content: space-between;
  gap: 14px;
}

.manual-workspace-page__action-copy {
  min-width: 0;
}

.manual-workspace-page__action-row {
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.manual-workspace-page__section-meta,
.manual-workspace__kind-desc,
.manual-inspector__subtitle,
.manual-inspector__meta,
.manual-inspector__action-copy span {
  margin: 0;
  color: var(--text-2);
  line-height: 1.55;
}

.manual-workspace-page__loading,
.manual-workspace-page__empty-card,
.manual-workspace__list-state,
.manual-inspector__empty,
.manual-inspector__loading {
  min-height: 260px;
  display: grid;
  place-items: center;
}

.manual-workspace__canvas {
  display: flex;
  align-items: stretch;
  gap: 0;
  min-height: 0;
  overflow: hidden;
}

.manual-workspace__rail-shell {
  width: 420px;
  min-width: 0;
  flex: 0 0 auto;
  display: flex;
}

.manual-workspace__rail,
.manual-workspace__inspector-shell {
  min-height: 0;
  height: 100%;
}

.manual-workspace__rail {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 14px;
  overflow: hidden;
}

.manual-workspace__rail-controls {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-bottom: 2px;
}

.manual-workspace__rail-header {
  margin-bottom: 0;
}

.manual-workspace__kind-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.manual-kind-filter {
  display: inline-flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  min-width: 84px;
  padding: 8px 10px;
  border-radius: 12px;
  border: 1px solid var(--border);
  background: var(--bg-subtle);
  color: var(--text-2);
  cursor: pointer;
  transition: border-color 0.18s ease, background 0.18s ease, color 0.18s ease, transform 0.18s ease;
}

.manual-kind-filter:hover,
.manual-kind-filter.active {
  border-color: var(--accent-border);
  background: color-mix(in srgb, var(--accent) 10%, var(--bg-subtle));
  color: var(--text-1);
  transform: translateY(-1px);
}

.manual-kind-filter__label {
  font-size: 12px;
  font-weight: 600;
  white-space: nowrap;
}

.manual-kind-filter__count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 28px;
  padding: 1px 7px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.05);
  color: inherit;
  font-family: var(--font-display);
  font-size: 11px;
  font-weight: 700;
  line-height: 1.2;
}

.manual-workspace__kind-desc {
  font-size: 11px;
}

.manual-workspace__filter-stack {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.manual-workspace__switch-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}

.manual-workspace__switch-row,
.manual-workspace__display-mode {
  color: var(--text-2);
}

.manual-workspace__switch-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 8px 10px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
}

.manual-workspace__display-mode {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.manual-workspace__display-mode > span {
  font-size: 12px;
  color: var(--text-3);
}

.manual-workspace__display-actions {
  align-items: center;
  gap: 6px;
}

.manual-workspace__display-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 7px 10px;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--bg-subtle);
  color: var(--text-2);
  font-size: 12px;
  cursor: pointer;
  transition: border-color 0.18s ease, background 0.18s ease, color 0.18s ease;
}

.manual-workspace__display-btn.active,
.manual-workspace__display-btn:hover {
  border-color: var(--accent-border);
  background: var(--accent-soft);
  color: var(--text-1);
}

.manual-workspace__asset-scroller {
  flex: 1;
  min-height: 0;
  overflow: auto;
  padding-right: 2px;
}

.manual-workspace__list-state {
  flex: 1;
}

.manual-workspace__asset-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.manual-asset-row {
  width: 100%;
  display: grid;
  grid-template-columns: 48px minmax(0, 1fr);
  gap: 10px;
  padding: 10px 11px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
  text-align: left;
  cursor: pointer;
  transition: border-color 0.18s ease, background 0.18s ease, transform 0.18s ease, box-shadow 0.18s ease;
}

.manual-asset-row:hover,
.manual-asset-row.active {
  border-color: var(--accent-border);
  background: color-mix(in srgb, var(--accent) 8%, var(--bg-subtle));
  transform: translateY(-1px);
  box-shadow: 0 10px 24px rgba(0, 0, 0, 0.16);
}

.manual-asset-row__media,
.manual-gallery-card__thumb,
.manual-inspector__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-muted);
  flex-shrink: 0;
}

.manual-asset-row__media {
  width: 48px;
  height: 48px;
}

.manual-asset-row__media img,
.manual-gallery-card__thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.manual-asset-row__main {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.manual-asset-row__headline {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
}

.manual-asset-row__title {
  flex: 1;
  min-width: 0;
  color: var(--text-1);
  font-size: 14px;
  font-weight: 700;
  line-height: 1.35;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.manual-asset-row__badges {
  display: flex;
  align-items: flex-start;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 4px;
  flex-shrink: 0;
}

.manual-asset-row__subtitle,
.manual-gallery-card__copy span {
  color: var(--text-2);
  font-size: 12px;
  line-height: 1.45;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.manual-asset-row__meta,
.manual-gallery-card__copy small {
  color: var(--text-3);
  font-size: 11px;
  line-height: 1.45;
  font-family: var(--font-mono);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.manual-workspace__gallery-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(142px, 1fr));
  gap: 10px;
}

.manual-gallery-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
  text-align: left;
  cursor: pointer;
  transition: border-color 0.18s ease, background 0.18s ease, transform 0.18s ease, box-shadow 0.18s ease;
}

.manual-gallery-card:hover,
.manual-gallery-card.active {
  border-color: var(--accent-border);
  background: color-mix(in srgb, var(--accent) 8%, var(--bg-subtle));
  transform: translateY(-1px);
  box-shadow: 0 10px 24px rgba(0, 0, 0, 0.16);
}

.manual-gallery-card__thumb {
  aspect-ratio: 1 / 1;
  width: 100%;
}

.manual-gallery-card__copy {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.manual-gallery-card__copy strong {
  color: var(--text-1);
  font-size: 13px;
  font-weight: 700;
  line-height: 1.35;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.manual-gallery-card__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.manual-workspace__rail-footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding-top: 10px;
  border-top: 1px solid var(--border);
}

.manual-workspace__page-size {
  width: 116px;
  flex-shrink: 0;
}

.manual-workspace__pagination {
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  color: var(--text-2);
  justify-content: flex-end;
  font-size: 12px;
}

.manual-workspace__divider {
  flex: 0 0 18px;
  display: flex;
  align-items: stretch;
  justify-content: center;
  cursor: col-resize;
  user-select: none;
  touch-action: none;
}

.manual-workspace__divider-handle {
  position: relative;
  width: 18px;
}

.manual-workspace__divider-handle::before,
.manual-workspace__divider-handle::after {
  content: '';
  position: absolute;
  top: 20px;
  bottom: 20px;
  left: 50%;
  transform: translateX(-50%);
  border-radius: 999px;
  transition: background 0.18s ease, opacity 0.18s ease, box-shadow 0.18s ease;
}

.manual-workspace__divider-handle::before {
  width: 2px;
  background: var(--border);
}

.manual-workspace__divider-handle::after {
  width: 8px;
  opacity: 0;
  background: color-mix(in srgb, var(--accent) 22%, transparent);
}

.manual-workspace__divider:hover .manual-workspace__divider-handle::before,
.manual-workspace__divider.is-dragging .manual-workspace__divider-handle::before {
  background: var(--accent);
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--accent) 18%, transparent);
}

.manual-workspace__divider:hover .manual-workspace__divider-handle::after,
.manual-workspace__divider.is-dragging .manual-workspace__divider-handle::after {
  opacity: 1;
}

.manual-workspace__inspector-shell {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.manual-inspector {
  display: flex;
  flex-direction: column;
  min-height: 0;
  height: 100%;
}

.manual-inspector__header {
  align-items: flex-start;
  justify-content: space-between;
  gap: 14px;
  padding-bottom: 12px;
  border-bottom: 1px solid var(--border);
}

.manual-inspector__hero {
  gap: 12px;
  min-width: 0;
  flex: 1;
}

.manual-inspector__icon {
  width: 48px;
  height: 48px;
}

.manual-inspector__copy {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.manual-inspector__tags,
.manual-inspector__header-actions {
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.manual-inspector__title {
  margin: 0;
  font-family: var(--font-display);
  font-size: 24px;
  line-height: 1.1;
  color: var(--text-1);
  overflow-wrap: anywhere;
}

.manual-inspector__subtitle {
  font-size: 13px;
}

.manual-inspector__meta {
  font-family: var(--font-mono);
  font-size: 11px;
  overflow-wrap: anywhere;
}

.manual-inspector__body {
  flex: 1;
  min-height: 0;
  overflow: auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 14px 2px 6px 0;
}

.manual-inspector__facts {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
  gap: 10px;
}

.manual-inspector__fact {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 12px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
}

.manual-inspector__fact span {
  font-size: 11px;
  color: var(--text-3);
}

.manual-inspector__fact strong {
  color: var(--text-1);
  line-height: 1.45;
  overflow-wrap: anywhere;
}

.manual-inspector__fact strong.mono {
  font-family: var(--font-mono);
  font-size: 11px;
}

.manual-inspector__hint {
  margin-top: -2px;
}

.manual-text-editor,
.manual-image-editor,
.manual-font-editor,
.manual-binary-view {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.manual-text-editor {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 12px;
}

.manual-text-editor__panel,
.manual-image-editor__panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
}

.manual-text-editor__label {
  font-size: 12px;
  color: var(--text-3);
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.manual-text-editor__panel pre {
  min-height: 248px;
  padding: 14px;
  margin: 0;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
  color: var(--text-1);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  font-family: var(--font-mono);
  font-size: 12px;
  line-height: 1.65;
}

.manual-image-editor__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.manual-image-editor__panel-header {
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.manual-image-editor__panel-header span {
  font-size: 12px;
  color: var(--text-3);
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.manual-image-editor__panel-body {
  min-height: 252px;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
}

.manual-image-editor__panel-body img {
  width: 100%;
  height: 100%;
  object-fit: contain;
  background: color-mix(in srgb, var(--bg-root) 84%, transparent);
}

.manual-image-editor__fallback,
.manual-image-editor__empty {
  width: 100%;
  height: 100%;
  padding: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.manual-image-editor__empty {
  color: var(--text-3);
  text-align: center;
}

.manual-inspector__action-bar {
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--border);
  background: var(--bg-card);
}

.manual-inspector__action-copy {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.manual-inspector__action-copy strong {
  color: var(--text-1);
}

.manual-inspector__action-buttons {
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.tone-text {
  color: #7dd3fc;
  background: rgba(125, 211, 252, 0.12);
}

.tone-code {
  color: #60a5fa;
  background: rgba(96, 165, 250, 0.12);
}

.tone-image {
  color: #fbbf24;
  background: rgba(251, 191, 36, 0.12);
}

.tone-font {
  color: #c084fc;
  background: rgba(192, 132, 252, 0.14);
}

.tone-binary {
  color: #cbd5e1;
  background: rgba(203, 213, 225, 0.12);
}

.tone-default {
  color: var(--accent);
  background: var(--accent-soft);
}

@media (max-width: 1280px) {
  .manual-workspace__canvas {
    flex-direction: column;
    gap: 16px;
    height: auto !important;
  }

  .manual-workspace__rail-shell {
    width: 100% !important;
  }

  .manual-workspace__divider {
    display: none;
  }

  .manual-workspace__rail,
  .manual-workspace__inspector-shell {
    height: auto;
  }

  .manual-workspace__asset-scroller,
  .manual-inspector__body {
    overflow: visible;
    padding-right: 0;
  }

  .manual-workspace__rail {
    overflow: visible;
  }

  .manual-workspace__inspector-shell {
    overflow: visible;
  }
}

@media (max-width: 980px) {
  .manual-workspace__switch-grid,
  .manual-text-editor,
  .manual-image-editor__grid {
    grid-template-columns: 1fr;
  }

  .manual-asset-row__headline {
    flex-direction: column;
  }

  .manual-asset-row__badges {
    justify-content: flex-start;
  }
}

@media (max-width: 720px) {
  .manual-workspace-page__action-card,
  .manual-inspector__header,
  .manual-inspector__action-bar,
  .manual-workspace__display-mode {
    flex-direction: column;
    align-items: stretch;
  }

  .manual-workspace__rail-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .manual-workspace__page-size {
    width: 100%;
  }

  .manual-workspace__pagination {
    justify-content: space-between;
  }

  .manual-inspector__facts {
    grid-template-columns: 1fr;
  }
}
</style>
