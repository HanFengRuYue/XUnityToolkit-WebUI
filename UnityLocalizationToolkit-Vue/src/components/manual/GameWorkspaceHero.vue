<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NIcon } from 'naive-ui'
import { GamepadFilled } from '@vicons/material'
import type { Game } from '@/api/types'
import { gamesApi } from '@/api/games'
import WorkspaceModeSwitch from '@/components/manual/WorkspaceModeSwitch.vue'

const props = withDefaults(defineProps<{
  game: Game
  backTo: string
  backLabel: string
  statusLabel: string
  statusTone?: 'success' | 'warning' | 'default'
  subtitle?: string
  xUnityState?: string
  manualState?: string
  manualOverrides?: number
  showWorkspaceSwitch?: boolean
}>(), {
  statusTone: 'default',
  subtitle: '',
  xUnityState: '未安装',
  manualState: '未扫描',
  manualOverrides: 0,
  showWorkspaceSwitch: true,
})

const router = useRouter()
const iconFailed = ref(false)
const heroBgLoaded = ref(false)

const iconUrl = computed(() => `${gamesApi.getIconUrl(props.game.id)}?t=${props.game.updatedAt}`)
const hasBackground = computed(() => props.game.hasBackground !== false)
const backgroundUrl = computed(() =>
  hasBackground.value ? `${gamesApi.getBackgroundUrl(props.game.id)}?t=${props.game.updatedAt}` : '',
)
const statusClass = computed(() => `workspace-hero-status--${props.statusTone}`)

watch(iconUrl, () => {
  iconFailed.value = false
})

watch(backgroundUrl, () => {
  heroBgLoaded.value = false
})

function handleBack() {
  void router.push(props.backTo)
}
</script>

<template>
  <section class="workspace-hero-shell">
    <div v-if="hasBackground" class="workspace-hero-backdrop" :class="{ 'is-visible': heroBgLoaded }">
      <img
        :src="backgroundUrl"
        class="workspace-hero-backdrop__image"
        alt=""
        @load="heroBgLoaded = true"
        @error="heroBgLoaded = false"
      >
      <div class="workspace-hero-backdrop__gradient"></div>
      <div class="workspace-hero-backdrop__vignette"></div>
    </div>

    <div class="workspace-hero-nav" style="animation-delay: 0s">
      <button class="back-button" :class="{ 'on-hero': heroBgLoaded }" @click="handleBack">
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
          <path d="M12 5L7 10L12 15" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        <span>{{ backLabel }}</span>
      </button>

      <div v-if="$slots['nav-actions']" class="workspace-hero-nav__actions">
        <slot name="nav-actions"></slot>
      </div>
    </div>

    <div class="workspace-hero-title-banner" :class="{ 'has-hero': heroBgLoaded }" style="animation-delay: 0.05s">
      <div class="workspace-hero-title-banner__icon-shell">
        <img
          v-if="!iconFailed"
          :src="iconUrl"
          :alt="game.name"
          class="workspace-hero-title-banner__icon"
          @error="iconFailed = true"
        >
        <div v-else class="workspace-hero-title-banner__icon-fallback">
          <NIcon :size="36" color="var(--text-3)">
            <GamepadFilled />
          </NIcon>
        </div>
      </div>

      <div class="workspace-hero-title-banner__copy">
        <h1 class="workspace-hero-title-banner__title">{{ game.name }}</h1>

        <div class="workspace-hero-title-banner__meta">
          <div class="workspace-hero-status" :class="[statusClass, { 'on-hero': heroBgLoaded }]">
            <span class="workspace-hero-status__dot"></span>
            <span class="workspace-hero-status__text">{{ statusLabel }}</span>
          </div>

          <WorkspaceModeSwitch
            v-if="showWorkspaceSwitch && game.isUnityGame"
            :game-id="game.id"
            :x-unity-state="xUnityState"
            :manual-state="manualState"
            :manual-overrides="manualOverrides"
          />
        </div>

        <p v-if="subtitle" class="workspace-hero-title-banner__subtitle">
          {{ subtitle }}
        </p>
      </div>
    </div>
  </section>
</template>

<style scoped>
.workspace-hero-shell {
  position: relative;
}

.workspace-hero-backdrop,
.workspace-hero-nav,
.workspace-hero-title-banner {
  position: relative;
  z-index: 1;
}

.workspace-hero-backdrop {
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

.workspace-hero-backdrop.is-visible {
  opacity: 1;
}

.workspace-hero-backdrop__image {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  object-position: center 25%;
  filter: brightness(0.65) saturate(1.08);
}

.workspace-hero-backdrop__gradient {
  position: absolute;
  inset: 0;
  background:
    linear-gradient(
      to bottom,
      transparent 0%,
      transparent 32%,
      color-mix(in srgb, var(--bg-root) 48%, transparent) 68%,
      var(--bg-root) 100%
    );
}

.workspace-hero-backdrop__vignette {
  position: absolute;
  inset: 0;
  box-shadow: inset 0 0 100px 30px color-mix(in srgb, var(--bg-root) 35%, transparent);
}

.workspace-hero-nav {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.workspace-hero-nav__actions {
  display: flex;
  align-items: center;
  gap: 6px;
}

.workspace-hero-title-banner {
  display: flex;
  align-items: flex-end;
  gap: 20px;
  padding-top: 60px;
  padding-bottom: 24px;
  margin-bottom: 20px;
  animation: slideUp 0.4s var(--ease-out) backwards;
}

.workspace-hero-title-banner.has-hero {
  padding-top: 100px;
  padding-bottom: 28px;
  margin-bottom: 24px;
}

.workspace-hero-title-banner__icon-shell {
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
}

.workspace-hero-title-banner.has-hero .workspace-hero-title-banner__icon-shell {
  box-shadow: 0 6px 28px rgba(0, 0, 0, 0.5), 0 0 0 1px rgba(255, 255, 255, 0.1);
  border-color: rgba(255, 255, 255, 0.1);
}

.workspace-hero-title-banner__icon {
  width: 100%;
  height: 100%;
  object-fit: cover;
  image-rendering: -webkit-optimize-contrast;
}

.workspace-hero-title-banner__icon-fallback {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}

.workspace-hero-title-banner__copy {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 0;
  flex: 1;
}

.workspace-hero-title-banner__title {
  font-family: var(--font-display);
  font-size: 28px;
  font-weight: 700;
  color: var(--text-1);
  margin: 0;
  letter-spacing: -0.03em;
  line-height: 1.2;
}

.workspace-hero-title-banner.has-hero .workspace-hero-title-banner__title {
  color: #fff;
  text-shadow: 0 2px 8px rgba(0, 0, 0, 0.5), 0 1px 2px rgba(0, 0, 0, 0.3);
}

.workspace-hero-title-banner__meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 12px;
}

.workspace-hero-title-banner__subtitle {
  margin: 0;
  max-width: 78ch;
  color: var(--text-2);
  line-height: 1.65;
}

.workspace-hero-title-banner.has-hero .workspace-hero-title-banner__subtitle {
  color: rgba(235, 235, 240, 0.8);
  text-shadow: 0 1px 6px rgba(0, 0, 0, 0.32);
}

.workspace-hero-status {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 4px 12px;
  border-radius: 20px;
  background: var(--bg-subtle-hover);
  border: 1px solid var(--border);
  flex-shrink: 0;
}

.workspace-hero-status.on-hero {
  background: rgba(0, 0, 0, 0.3);
  border-color: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
}

.workspace-hero-status__dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--text-3);
  flex-shrink: 0;
}

.workspace-hero-status--success .workspace-hero-status__dot {
  background: var(--success);
  box-shadow: 0 0 10px rgba(52, 211, 153, 0.5);
  animation: pulse 2s ease-in-out infinite;
}

.workspace-hero-status--warning .workspace-hero-status__dot {
  background: var(--warning);
  box-shadow: 0 0 8px rgba(251, 191, 36, 0.4);
}

.workspace-hero-status__text {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-2);
}

.workspace-hero-title-banner.has-hero .workspace-hero-status__text {
  color: rgba(255, 255, 255, 0.78);
}

.back-button.on-hero {
  color: rgba(255, 255, 255, 0.75);
  text-shadow: 0 1px 4px rgba(0, 0, 0, 0.4);
}

.back-button.on-hero:hover {
  color: #fff;
}

@media (max-width: 768px) {
  .workspace-hero-backdrop {
    top: -20px;
    left: -20px;
    right: -20px;
    height: 320px;
  }

  .workspace-hero-title-banner {
    padding-top: 40px;
    gap: 14px;
  }

  .workspace-hero-title-banner.has-hero {
    padding-top: 70px;
  }

  .workspace-hero-title-banner__icon-shell {
    width: 64px;
    height: 64px;
    border-radius: 14px;
  }

  .workspace-hero-title-banner__title {
    font-size: 22px;
    word-break: break-word;
  }
}

@media (max-width: 480px) {
  .workspace-hero-backdrop {
    top: -16px;
    left: -12px;
    right: -12px;
    height: 250px;
  }

  .workspace-hero-title-banner {
    padding-top: 30px;
    gap: 12px;
  }

  .workspace-hero-title-banner.has-hero {
    padding-top: 50px;
  }

  .workspace-hero-title-banner__icon-shell {
    width: 56px;
    height: 56px;
    border-radius: 12px;
  }

  .workspace-hero-title-banner__title {
    font-size: 20px;
  }
}
</style>
