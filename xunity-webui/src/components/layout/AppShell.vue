<script setup lang="ts">
import { ref, watch } from 'vue'
import { RouterView, useRouter, useRoute } from 'vue-router'
import { NIcon } from 'naive-ui'
import { GamepadFilled } from '@vicons/material'
import InstallProgressDrawer from '@/components/progress/InstallProgressDrawer.vue'

const router = useRouter()
const route = useRoute()
const sidebarOpen = ref(false)

const navItems = [
  { label: '游戏库', key: '/', icon: GamepadFilled },
]

function navigateTo(key: string) {
  router.push(key)
  sidebarOpen.value = false
}

function isActive(key: string): boolean {
  if (key === '/') return true
  return route.path.startsWith(key)
}

watch(() => route.path, () => {
  sidebarOpen.value = false
})
</script>

<template>
  <div class="app-layout">
    <!-- Mobile Top Bar -->
    <header class="mobile-topbar">
      <button class="hamburger" @click="sidebarOpen = !sidebarOpen" :class="{ active: sidebarOpen }">
        <span></span>
        <span></span>
        <span></span>
      </button>
      <div class="topbar-logo">
        <svg class="logo-hex" viewBox="0 0 28 28" width="24" height="24">
          <path d="M14 2L25 8.5V19.5L14 26L3 19.5V8.5L14 2Z" fill="none" stroke="currentColor" stroke-width="1.2" opacity="0.6"/>
          <path d="M14 7L20.5 10.5V17.5L14 21L7.5 17.5V10.5L14 7Z" fill="currentColor" opacity="0.15"/>
          <path d="M14 10L17.5 12V16L14 18L10.5 16V12L14 10Z" fill="currentColor" opacity="0.4"/>
        </svg>
        <span class="topbar-title">XUnity Toolkit</span>
      </div>
    </header>

    <!-- Sidebar Overlay (mobile) -->
    <Transition name="overlay">
      <div v-if="sidebarOpen" class="sidebar-overlay" @click="sidebarOpen = false"></div>
    </Transition>

    <aside class="sidebar" :class="{ open: sidebarOpen }">
      <div class="sidebar-header">
        <div class="sidebar-logo">
          <svg class="logo-hex" viewBox="0 0 28 28" width="32" height="32">
            <path
              d="M14 2L25 8.5V19.5L14 26L3 19.5V8.5L14 2Z"
              fill="none"
              stroke="currentColor"
              stroke-width="1.2"
              opacity="0.6"
            />
            <path
              d="M14 7L20.5 10.5V17.5L14 21L7.5 17.5V10.5L14 7Z"
              fill="currentColor"
              opacity="0.15"
            />
            <path
              d="M14 10L17.5 12V16L14 18L10.5 16V12L14 10Z"
              fill="currentColor"
              opacity="0.4"
            />
          </svg>
          <div class="logo-text">
            <span class="logo-name">XUnity</span>
            <span class="logo-sub">Toolkit</span>
          </div>
        </div>
      </div>

      <div class="sidebar-divider"></div>

      <nav class="sidebar-nav">
        <a
          v-for="item in navItems"
          :key="item.key"
          class="nav-item"
          :class="{ active: isActive(item.key) }"
          @click="navigateTo(item.key)"
        >
          <NIcon :size="20">
            <component :is="item.icon" />
          </NIcon>
          <span>{{ item.label }}</span>
        </a>
      </nav>

      <div class="sidebar-spacer"></div>

      <div class="sidebar-footer">
        <span class="footer-version">v1.0.0</span>
      </div>
    </aside>

    <main class="main-content">
      <RouterView v-slot="{ Component }">
        <Transition name="page" mode="out-in">
          <component :is="Component" />
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
    rgba(34, 211, 167, 0.08) 30%,
    rgba(34, 211, 167, 0.08) 70%,
    transparent
  );
  pointer-events: none;
}

.sidebar-header {
  padding: 24px 20px 16px;
}

.sidebar-logo {
  display: flex;
  align-items: center;
  gap: 12px;
  animation: breathe 4s ease-in-out infinite;
}

.logo-hex {
  color: var(--accent);
  filter: drop-shadow(0 0 8px rgba(34, 211, 167, 0.3));
}

.logo-text {
  display: flex;
  flex-direction: column;
  line-height: 1.15;
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
}

/* ===== Navigation ===== */
.sidebar-nav {
  padding: 0 12px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 11px 16px;
  color: var(--text-2);
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: all 0.2s ease;
  position: relative;
  text-decoration: none;
  font-size: 14px;
  font-weight: 500;
  user-select: none;
  border-left: 3px solid transparent;
  margin-left: -1px;
}

.nav-item:hover {
  background: rgba(255, 255, 255, 0.04);
  color: var(--text-1);
}

.nav-item.active {
  border-left-color: var(--accent);
  background: linear-gradient(90deg, rgba(34, 211, 167, 0.08), transparent 80%);
  color: var(--text-1);
}

.nav-item.active .n-icon {
  color: var(--accent);
  filter: drop-shadow(0 0 6px rgba(34, 211, 167, 0.4));
}

/* ===== Sidebar Footer ===== */
.sidebar-spacer {
  flex: 1;
}

.sidebar-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--border);
}

.footer-version {
  font-family: var(--font-mono);
  font-size: 11px;
  color: var(--text-3);
  letter-spacing: 0.02em;
}

/* ===== Main Content ===== */
.main-content {
  flex: 1;
  overflow-y: auto;
  padding: 36px 40px;
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
    background: rgba(255, 255, 255, 0.03);
  }

  .topbar-logo {
    display: flex;
    align-items: center;
    gap: 8px;
  }

  .topbar-logo .logo-hex {
    color: var(--accent);
    filter: drop-shadow(0 0 6px rgba(34, 211, 167, 0.3));
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
    background: rgba(0, 0, 0, 0.6);
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
    z-index: 200;
    transform: translateX(-100%);
    transition: transform 0.3s var(--ease-out);
    animation: none;
    box-shadow: 8px 0 32px rgba(0, 0, 0, 0.4);
  }

  .sidebar.open {
    transform: translateX(0);
  }

  /* Main Content padding */
  .main-content {
    padding: 20px 16px;
  }
}

@media (max-width: 480px) {
  .main-content {
    padding: 16px 12px;
  }
}
</style>
