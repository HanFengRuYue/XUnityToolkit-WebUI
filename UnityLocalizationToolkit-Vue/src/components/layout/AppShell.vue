<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { RouterView, useRouter, useRoute } from 'vue-router'
import { NIcon, NTooltip } from 'naive-ui'
import { GamepadFilled, SettingsOutlined, SmartToyOutlined, ArticleOutlined, FontDownloadOutlined, KeyboardDoubleArrowLeftOutlined, KeyboardDoubleArrowRightOutlined, TranslateOutlined } from '@vicons/material'
import InstallProgressDrawer from '@/components/progress/InstallProgressDrawer.vue'
import { settingsApi } from '@/api/games'
import { useUpdateStore } from '@/stores/update'
import { useSidebarStore } from '@/stores/sidebar'
import { useWindowControls } from '@/composables/useWindowControls'

const { isWebView2, isMaximized, minimize, toggleMaximize, close: closeWindow } = useWindowControls()

const router = useRouter()
const route = useRoute()
const sidebarOpen = ref(false)
const appVersion = ref('')
const updateStore = useUpdateStore()
const sidebarStore = useSidebarStore()

const showUpdateBadge = computed(() =>
  updateStore.isUpdateAvailable || updateStore.isReady || updateStore.isDownloading
)

onMounted(async () => {
  try {
    const info = await settingsApi.getVersion()
    const match = info.version.match(/^(\d+\.\d+\.\d+)/)
    appVersion.value = match?.[1] ?? info.version
  } catch {
    appVersion.value = '1.0.0'
  }
  updateStore.init()
  window.addEventListener('resize', updateViewport)
})

onBeforeUnmount(() => {
  removeGuard()
  window.removeEventListener('resize', updateViewport)
  document.removeEventListener('mousemove', onResizeMove)
  document.removeEventListener('mouseup', onResizeEnd)
})

// Cache top-level pages to avoid full re-renders on navigation
const cachedPages = ['LibraryView', 'AiTranslationView', 'FontGeneratorView', 'LogView', 'SettingsView']

const mainNavItems = [
  { label: '游戏库', key: '/', icon: GamepadFilled },
  { label: '手动翻译', key: '/manual-translation', icon: TranslateOutlined },
  { label: 'XUnity AI 翻译', key: '/ai-translation', icon: SmartToyOutlined },
  { label: '字体生成', key: '/font-generator', icon: FontDownloadOutlined },
  { label: '运行日志', key: '/logs', icon: ArticleOutlined },
]

const settingsNavItem = { label: '设置', key: '/settings', icon: SettingsOutlined }

function navigateTo(key: string) {
  if (key === '/manual-translation') {
    const routeGameId = typeof route.params.id === 'string' ? route.params.id : ''
    router.push(routeGameId ? `/manual-translation/${routeGameId}` : key)
    sidebarOpen.value = false
    return
  }

  router.push(key)
  sidebarOpen.value = false
}

function isActive(key: string): boolean {
  if (key === '/') return route.path === '/' || route.path.startsWith('/games/')
  return route.path.startsWith(key)
}

// Render the initial route without Transition; enable route animations only after the first real navigation.
const transitionDirection = ref('page')
const routeTransitionsStarted = ref(false)

const removeGuard = router.beforeEach((to, from) => {
  if (from.matched.length === 0) {
    return
  }

  if (!routeTransitionsStarted.value) {
    routeTransitionsStarted.value = true
  }

  const toDepth = to.meta.depth ?? 1
  const fromDepth = from.meta.depth ?? 1

  if (toDepth > fromDepth) {
    transitionDirection.value = 'page-slide-left'
  } else if (toDepth < fromDepth) {
    transitionDirection.value = 'page-slide-right'
  } else {
    transitionDirection.value = 'page'
  }
})

watch(() => route.path, () => {
  sidebarOpen.value = false
})

// Viewport & responsive breakpoints
const viewportWidth = ref(window.innerWidth)
const isMobile = computed(() => viewportWidth.value <= 768)
const isNarrowDesktop = computed(() => !isMobile.value && viewportWidth.value <= 900)

function updateViewport() {
  viewportWidth.value = window.innerWidth
}

// Sidebar width (inline style, not applied on mobile; auto-collapse on narrow desktop)
const sidebarStyle = computed(() => {
  if (isMobile.value) return undefined
  if (isNarrowDesktop.value) return { width: sidebarStore.COLLAPSED_WIDTH + 'px' }
  return { width: sidebarStore.effectiveWidth + 'px' }
})

// Drag resize
const isResizing = ref(false)
let startX = 0
let startWidth = 0

function onResizeStart(e: MouseEvent) {
  if (sidebarStore.collapsed || isNarrowDesktop.value || isMobile.value) return
  e.preventDefault()
  isResizing.value = true
  startX = e.clientX
  startWidth = sidebarStore.customWidth
  document.addEventListener('mousemove', onResizeMove)
  document.addEventListener('mouseup', onResizeEnd)
  document.body.style.cursor = 'col-resize'
  document.body.style.userSelect = 'none'
}

function onResizeMove(e: MouseEvent) {
  if (!isResizing.value) return
  const delta = e.clientX - startX
  sidebarStore.setWidth(startWidth + delta)
}

function onResizeEnd() {
  if (!isResizing.value) return
  isResizing.value = false
  document.removeEventListener('mousemove', onResizeMove)
  document.removeEventListener('mouseup', onResizeEnd)
  document.body.style.cursor = ''
  document.body.style.userSelect = ''
}

function onResizeDoubleClick() {
  sidebarStore.resetWidth()
}
</script>

<template>
  <div class="app-layout" :class="{ 'has-titlebar': isWebView2 }">
    <!-- Custom Window Title Bar (WebView2 only) -->
    <div v-if="isWebView2" class="window-titlebar">
      <div class="window-controls">
        <button class="win-btn win-minimize" @click="minimize" title="最小化">
          <svg width="10" height="1" viewBox="0 0 10 1"><rect width="10" height="1" fill="currentColor" /></svg>
        </button>
        <button class="win-btn win-maximize" @click="toggleMaximize" :title="isMaximized ? '向下还原' : '最大化'">
          <svg v-if="!isMaximized" width="10" height="10" viewBox="0 0 10 10">
            <rect x="0.5" y="0.5" width="9" height="9" rx="1" fill="none" stroke="currentColor" stroke-width="1" />
          </svg>
          <svg v-else width="10" height="10" viewBox="0 0 10 10">
            <rect x="2.5" y="0.5" width="7" height="7" rx="1" fill="none" stroke="currentColor" stroke-width="1" />
            <rect x="0.5" y="2.5" width="7" height="7" rx="1" fill="var(--bg-surface)" stroke="currentColor" stroke-width="1" />
          </svg>
        </button>
        <button class="win-btn win-close" @click="closeWindow" title="关闭">
          <svg width="10" height="10" viewBox="0 0 10 10">
            <path d="M1 1L9 9M9 1L1 9" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" />
          </svg>
        </button>
      </div>
    </div>

    <!-- Mobile Top Bar -->
    <header class="mobile-topbar" :class="{ 'has-wv2-controls': isWebView2 }">
      <button class="hamburger" @click="sidebarOpen = !sidebarOpen" :class="{ active: sidebarOpen }">
        <span></span>
        <span></span>
        <span></span>
      </button>
      <div class="topbar-logo">
        <img class="logo-icon" src="/logo.png" width="24" height="24" alt="UnityLocalizationToolkit" />
        <span class="topbar-title">UnityLocalizationToolkit</span>
      </div>
      <!-- Window controls in mobile topbar (WebView2 only) -->
      <div v-if="isWebView2" class="topbar-window-controls">
        <button class="win-btn win-minimize" @click="minimize" title="最小化">
          <svg width="10" height="1" viewBox="0 0 10 1"><rect width="10" height="1" fill="currentColor" /></svg>
        </button>
        <button class="win-btn win-maximize" @click="toggleMaximize" :title="isMaximized ? '向下还原' : '最大化'">
          <svg v-if="!isMaximized" width="10" height="10" viewBox="0 0 10 10">
            <rect x="0.5" y="0.5" width="9" height="9" rx="1" fill="none" stroke="currentColor" stroke-width="1" />
          </svg>
          <svg v-else width="10" height="10" viewBox="0 0 10 10">
            <rect x="2.5" y="0.5" width="7" height="7" rx="1" fill="none" stroke="currentColor" stroke-width="1" />
            <rect x="0.5" y="2.5" width="7" height="7" rx="1" fill="var(--bg-surface)" stroke="currentColor" stroke-width="1" />
          </svg>
        </button>
        <button class="win-btn win-close" @click="closeWindow" title="关闭">
          <svg width="10" height="10" viewBox="0 0 10 10">
            <path d="M1 1L9 9M9 1L1 9" stroke="currentColor" stroke-width="1.2" stroke-linecap="round" />
          </svg>
        </button>
      </div>
    </header>

    <!-- Sidebar Overlay (mobile) -->
    <Transition name="overlay">
      <div v-if="sidebarOpen" class="sidebar-overlay" @click="sidebarOpen = false"></div>
    </Transition>

    <aside
      class="sidebar"
      :class="{ open: sidebarOpen, collapsed: (sidebarStore.collapsed || isNarrowDesktop) && !isMobile, resizing: isResizing }"
      :style="sidebarStyle"
    >
      <div class="sidebar-header">
        <div class="sidebar-logo">
          <img class="logo-icon" src="/logo.png" width="32" height="32" alt="UnityLocalizationToolkit" />
          <div class="logo-text">
            <span class="logo-name">UnityLocalization</span>
            <span class="logo-sub">WebUI</span>
          </div>
        </div>
      </div>

      <div class="sidebar-divider"></div>

      <nav class="sidebar-nav">
        <template v-for="item in mainNavItems" :key="item.key">
          <NTooltip v-if="(sidebarStore.collapsed || isNarrowDesktop) && !isMobile" placement="right" :show-arrow="false">
            <template #trigger>
              <a class="nav-item" :class="{ active: isActive(item.key) }" @click="navigateTo(item.key)">
                <NIcon :size="20"><component :is="item.icon" /></NIcon>
                <span class="nav-label">{{ item.label }}</span>
              </a>
            </template>
            {{ item.label }}
          </NTooltip>
          <a v-else class="nav-item" :class="{ active: isActive(item.key) }" @click="navigateTo(item.key)">
            <NIcon :size="20"><component :is="item.icon" /></NIcon>
            <span class="nav-label">{{ item.label }}</span>
          </a>
        </template>
      </nav>

      <div class="sidebar-spacer"></div>

      <div class="sidebar-collapse-toggle">
        <NTooltip v-if="(sidebarStore.collapsed || isNarrowDesktop) && !isMobile" placement="right" :show-arrow="false">
          <template #trigger>
            <a class="nav-item collapse-item" @click="sidebarStore.toggleCollapse">
              <NIcon :size="20"><KeyboardDoubleArrowRightOutlined /></NIcon>
              <span class="nav-label">展开</span>
            </a>
          </template>
          展开侧栏
        </NTooltip>
        <a v-else class="nav-item collapse-item" @click="sidebarStore.toggleCollapse">
          <NIcon :size="20"><KeyboardDoubleArrowLeftOutlined /></NIcon>
          <span class="nav-label">收起</span>
        </a>
      </div>

      <div class="sidebar-bottom-nav">
        <div class="bottom-divider"></div>
        <NTooltip v-if="(sidebarStore.collapsed || isNarrowDesktop) && !isMobile" placement="right" :show-arrow="false">
          <template #trigger>
            <a class="nav-item" :class="{ active: isActive(settingsNavItem.key) }" @click="navigateTo(settingsNavItem.key)">
              <NIcon :size="20"><component :is="settingsNavItem.icon" /></NIcon>
              <span class="nav-label">{{ settingsNavItem.label }}</span>
              <span v-if="showUpdateBadge" class="update-dot" />
            </a>
          </template>
          {{ settingsNavItem.label }}
        </NTooltip>
        <a v-else class="nav-item" :class="{ active: isActive(settingsNavItem.key) }" @click="navigateTo(settingsNavItem.key)">
          <NIcon :size="20"><component :is="settingsNavItem.icon" /></NIcon>
          <span class="nav-label">{{ settingsNavItem.label }}</span>
          <span v-if="showUpdateBadge" class="update-dot" />
        </a>
      </div>

      <div class="sidebar-footer">
        <span class="footer-version">v{{ appVersion }}</span>
      </div>

      <!-- Resize handle -->
      <div
        class="sidebar-resize-handle"
        @mousedown="onResizeStart"
        @dblclick="onResizeDoubleClick"
      ></div>
    </aside>

    <main class="main-content">
      <RouterView v-slot="{ Component }">
        <KeepAlive v-if="!routeTransitionsStarted" :include="cachedPages">
          <component :is="Component" :key="route.path" />
        </KeepAlive>
        <Transition v-else :name="transitionDirection" mode="out-in">
          <KeepAlive :include="cachedPages">
            <component :is="Component" :key="route.path" />
          </KeepAlive>
        </Transition>
      </RouterView>
    </main>
  </div>
  <InstallProgressDrawer />
</template>

<style scoped>
.app-layout {
  display: flex;
  height: 100vh;
  position: relative;
  z-index: 1;
}

/* ===== Custom Window Title Bar (WebView2 only) ===== */
.window-titlebar {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  height: 32px;
  z-index: 9000;
  app-region: drag;
  display: flex;
  justify-content: flex-end;
  pointer-events: none;
}

.window-controls {
  display: flex;
  height: 32px;
  pointer-events: auto;
  app-region: no-drag;
  background: color-mix(in srgb, var(--bg-surface) 75%, transparent);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border-radius: 0 0 0 var(--radius-sm);
  border-bottom: 1px solid var(--border);
  border-left: 1px solid var(--border);
  overflow: hidden;
}

.win-btn {
  width: 46px;
  height: 32px;
  border: none;
  background: transparent;
  color: var(--text-2);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: background 0.15s ease, color 0.15s ease;
  appearance: none;
  padding: 0;
}

.win-btn:hover {
  background: var(--bg-muted-hover);
  color: var(--text-1);
}

.win-close:hover {
  background: #e81123;
  color: #fff;
}

.app-layout.has-titlebar .sidebar-header {
  padding-top: 40px;
}

.app-layout.has-titlebar .main-content {
  padding-top: 40px;
}

/* ===== Sidebar ===== */
.sidebar {
  width: 230px;
  background: var(--bg-surface);
  border-right: 1px solid var(--border);
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  position: relative;
  animation: slideInLeft 0.4s var(--ease-out) backwards;
  transition: width 0.3s var(--ease-out), background 0.3s ease, border-color 0.3s ease;
}

.sidebar.resizing {
  transition: background 0.3s ease, border-color 0.3s ease;
}

.sidebar::after {
  content: '';
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  width: 1px;
  background: linear-gradient(
    180deg,
    transparent,
    var(--accent-border) 30%,
    var(--accent-border) 70%,
    transparent
  );
  pointer-events: none;
}

.sidebar-header {
  padding: 24px 20px 16px;
  transition: padding 0.3s var(--ease-out);
}

.sidebar-logo {
  display: flex;
  align-items: center;
  gap: 12px;
  animation: breathe 4s ease-in-out infinite;
  transition: justify-content 0.3s var(--ease-out);
}

.logo-icon {
  filter: var(--logo-glow);
  border-radius: 4px;
  flex-shrink: 0;
}

.logo-text {
  display: flex;
  flex-direction: column;
  line-height: 1.15;
  overflow: hidden;
  white-space: nowrap;
  opacity: 1;
  transition: opacity 0.2s ease, max-width 0.3s var(--ease-out);
  max-width: 150px;
}

.logo-name {
  font-family: var(--font-display);
  font-weight: 600;
  font-size: 18px;
  color: var(--text-1);
  letter-spacing: -0.02em;
}

.logo-sub {
  font-family: var(--font-display);
  font-weight: 300;
  font-size: 13px;
  color: var(--text-3);
  letter-spacing: 0.04em;
}

.sidebar-divider {
  height: 1px;
  margin: 4px 20px 12px;
  background: linear-gradient(90deg, var(--accent), var(--secondary), transparent);
  background-size: 200% 100%;
  animation: shimmer 4s ease-in-out infinite;
  opacity: 0.4;
  transition: margin 0.3s var(--ease-out);
}

/* ===== Navigation ===== */
.sidebar-nav {
  padding: 0 12px;
  display: flex;
  flex-direction: column;
  gap: 2px;
  transition: padding 0.3s var(--ease-out);
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 11px 16px;
  color: var(--text-2);
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: all 0.25s var(--ease-out);
  position: relative;
  text-decoration: none;
  font-size: 14px;
  font-weight: 500;
  user-select: none;
  border-left: 3px solid transparent;
  margin-left: -1px;
  animation: slideInLeft 0.4s var(--ease-out) backwards;
  overflow: hidden;
}

.nav-item .n-icon {
  flex-shrink: 0;
}

.nav-label {
  overflow: hidden;
  white-space: nowrap;
  opacity: 1;
  transition: opacity 0.2s ease, max-width 0.3s var(--ease-out);
  max-width: 200px;
}

.nav-item:nth-child(1) { animation-delay: 0.12s; }
.nav-item:nth-child(2) { animation-delay: 0.18s; }
.nav-item:nth-child(3) { animation-delay: 0.24s; }
.nav-item:nth-child(4) { animation-delay: 0.30s; }

.nav-item:hover {
  background: var(--bg-subtle-hover);
  color: var(--text-1);
}

.nav-item.active {
  border-left-color: var(--accent);
  background: var(--accent-soft);
  color: var(--text-1);
  box-shadow: inset 0 0 16px -8px var(--accent-soft);
}

.nav-item.active .n-icon {
  color: var(--accent);
  filter: drop-shadow(0 0 6px var(--accent-glow));
  transition: filter 0.3s ease, color 0.3s ease;
}

.update-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--accent);
  box-shadow: 0 0 6px var(--accent-glow);
  margin-left: auto;
  animation: pulse-dot 2s ease-in-out infinite;
  flex-shrink: 0;
}

@keyframes pulse-dot {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}

/* ===== Collapse Toggle ===== */
.sidebar-collapse-toggle {
  padding: 0 12px 4px;
  transition: padding 0.3s var(--ease-out);
}

.collapse-item {
  animation: none !important;
}

/* ===== Bottom Nav (Settings) ===== */
.sidebar-bottom-nav {
  padding: 0 12px 4px;
  transition: padding 0.3s var(--ease-out);
}

.bottom-divider {
  height: 1px;
  margin: 0 8px 8px;
  background: var(--border);
  opacity: 0.6;
}

.sidebar-bottom-nav .nav-item {
  animation-delay: 0.36s;
}

/* ===== Sidebar Footer ===== */
.sidebar-spacer {
  flex: 1;
}

.sidebar-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--border);
  transition: padding 0.3s var(--ease-out), opacity 0.2s ease;
}

.footer-version {
  font-family: var(--font-mono);
  font-size: 11px;
  color: var(--text-3);
  letter-spacing: 0.02em;
  animation: fadeIn 0.6s var(--ease-out) 0.5s backwards;
  white-space: nowrap;
  overflow: hidden;
}

/* ===== Resize Handle ===== */
.sidebar-resize-handle {
  position: absolute;
  top: 0;
  right: -3px;
  bottom: 0;
  width: 6px;
  cursor: col-resize;
  z-index: 10;
  transition: background 0.2s ease;
}

.sidebar-resize-handle:hover {
  background: var(--accent-border);
}

.sidebar-resize-handle:active {
  background: var(--accent);
  opacity: 0.5;
}

/* ===== Collapsed State ===== */
.sidebar.collapsed .sidebar-header {
  padding: 24px 0 16px;
}

.sidebar.collapsed .sidebar-logo {
  justify-content: center;
  gap: 0;
}

.sidebar.collapsed .logo-text {
  opacity: 0;
  max-width: 0;
}

.sidebar.collapsed .sidebar-divider {
  margin: 4px 12px 12px;
}

.sidebar.collapsed .sidebar-nav {
  padding: 0 8px;
}

.sidebar.collapsed .nav-item {
  justify-content: center;
  border-left-width: 0;
  margin-left: 0;
  gap: 0;
  padding: 0;
  width: 44px;
  height: 44px;
  margin: 0 auto;
}

.sidebar.collapsed .nav-label {
  opacity: 0;
  max-width: 0;
}

.sidebar.collapsed .update-dot {
  position: absolute;
  top: 4px;
  right: 4px;
  margin-left: 0;
  width: 7px;
  height: 7px;
}

.sidebar.collapsed .sidebar-collapse-toggle {
  padding: 0 8px 4px;
}

.sidebar.collapsed .sidebar-bottom-nav {
  padding: 0 8px 4px;
}

.sidebar.collapsed .bottom-divider {
  margin: 0 4px 8px;
}

.sidebar.collapsed .sidebar-footer {
  padding: 12px 8px;
  text-align: center;
  opacity: 0;
  height: 0;
  padding: 0;
  border-top: none;
  overflow: hidden;
}

.sidebar.collapsed .sidebar-resize-handle {
  display: none;
}

/* ===== Main Content ===== */
.main-content {
  flex: 1;
  overflow-y: auto;
  padding: 24px 28px;
  position: relative;
}

/* ===== Mobile Top Bar (hidden on desktop) ===== */
.mobile-topbar {
  display: none;
}

.sidebar-overlay {
  display: none;
}

/* ===== Responsive: Tablet & Mobile ===== */
@media (max-width: 768px) {
  .app-layout {
    flex-direction: column;
    height: auto;
    min-height: 100vh;
  }

  /* Mobile Top Bar */
  .mobile-topbar {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 12px 16px;
    background: var(--bg-surface);
    border-bottom: 1px solid var(--border);
    position: sticky;
    top: 0;
    z-index: 100;
  }

  .hamburger {
    display: flex;
    flex-direction: column;
    justify-content: center;
    gap: 5px;
    width: 36px;
    height: 36px;
    padding: 8px 6px;
    background: none;
    border: 1px solid var(--border);
    border-radius: var(--radius-sm);
    cursor: pointer;
    transition: all 0.2s ease;
  }

  .hamburger span {
    display: block;
    width: 100%;
    height: 2px;
    background: var(--text-2);
    border-radius: 1px;
    transition: all 0.3s var(--ease-out);
    transform-origin: center;
  }

  .hamburger.active span:nth-child(1) {
    transform: translateY(7px) rotate(45deg);
  }
  .hamburger.active span:nth-child(2) {
    opacity: 0;
    transform: scaleX(0);
  }
  .hamburger.active span:nth-child(3) {
    transform: translateY(-7px) rotate(-45deg);
  }

  .hamburger:hover {
    border-color: var(--accent-border);
    background: var(--bg-subtle);
  }

  .topbar-logo {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .topbar-logo .logo-icon {
    filter: var(--logo-glow);
  }

  .topbar-title {
    font-family: var(--font-display);
    font-size: 15px;
    font-weight: 600;
    color: var(--text-1);
    letter-spacing: -0.02em;
  }

  /* Sidebar Overlay */
  .sidebar-overlay {
    display: block;
    position: fixed;
    inset: 0;
    background: var(--overlay-backdrop);
    backdrop-filter: blur(4px);
    z-index: 199;
  }

  .overlay-enter-active,
  .overlay-leave-active {
    transition: opacity 0.25s ease;
  }
  .overlay-enter-from,
  .overlay-leave-to {
    opacity: 0;
  }

  /* Sidebar as slide-over drawer */
  .sidebar {
    position: fixed;
    top: 0;
    left: 0;
    bottom: 0;
    width: 230px !important;
    z-index: 200;
    transform: translateX(-100%);
    transition: transform 0.3s var(--ease-out);
    animation: none;
    box-shadow: 8px 0 32px rgba(0, 0, 0, 0.4);
  }

  .sidebar.open {
    transform: translateX(0);
  }

  /* Hide desktop-only features on mobile */
  .sidebar-collapse-toggle {
    display: none;
  }

  .sidebar-resize-handle {
    display: none;
  }

  /* Main Content padding */
  .main-content {
    padding: 20px 20px;
  }

  /* Hide custom title bar on mobile -- controls are in the mobile topbar */
  .window-titlebar {
    display: none;
  }
  .app-layout.has-titlebar .sidebar-header {
    padding-top: 24px;
  }
  .app-layout.has-titlebar .main-content {
    padding-top: 20px;
  }

  /* WebView2 mode: make mobile topbar draggable for window movement */
  .mobile-topbar.has-wv2-controls {
    app-region: drag;
  }
  .mobile-topbar.has-wv2-controls .hamburger,
  .mobile-topbar.has-wv2-controls .topbar-window-controls {
    app-region: no-drag;
  }

  /* Window controls in mobile topbar */
  .topbar-window-controls {
    display: flex;
    margin-left: auto;
    align-items: center;
  }
  .topbar-window-controls .win-btn {
    width: 40px;
    height: 32px;
  }
}

@media (max-width: 480px) {
  .main-content {
    padding: 16px 12px;
  }
}
</style>
