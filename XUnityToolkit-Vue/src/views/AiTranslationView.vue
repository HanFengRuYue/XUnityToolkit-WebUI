<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import {
  NIcon,
  NButton,
  useMessage,
} from 'naive-ui'
import {
  SmartToyOutlined,
  TranslateOutlined,
  HourglassEmptyOutlined,
  LinkOutlined,
  LinkOffOutlined,
} from '@vicons/material'
import { useAiTranslationStore } from '@/stores/aiTranslation'
import { settingsApi } from '@/api/games'
import type { AppSettings, AiTranslationSettings } from '@/api/types'
import AiTranslationCard from '@/components/settings/AiTranslationCard.vue'

const aiStore = useAiTranslationStore()
const message = useMessage()

const DEFAULT_AI_TRANSLATION: AiTranslationSettings = {
  provider: 'OpenAI',
  apiBaseUrl: '',
  apiKey: '',
  modelName: '',
  systemPrompt:
    'You are a professional game text translator. ' +
    'Translate the following texts from {from} to {to}. ' +
    'Return ONLY a JSON array of translated strings in the same order as the input. ' +
    'Do not add any explanation or markdown formatting. ' +
    'Example input: ["Hello","World"] Example output: ["你好","世界"]',
  temperature: 0.3,
}

const settings = ref<AppSettings | null>(null)
const settingsLoading = ref(false)

const aiSettings = computed({
  get: () => settings.value?.aiTranslation ?? { ...DEFAULT_AI_TRANSLATION },
  set: (val: AiTranslationSettings) => {
    if (settings.value) {
      settings.value = { ...settings.value, aiTranslation: val }
    }
  },
})

const connectionStatus = computed(() => {
  if (!aiStore.stats?.lastRequestAt) return 'never'
  const last = new Date(aiStore.stats.lastRequestAt).getTime()
  const diff = Date.now() - last
  if (diff < 5 * 60 * 1000) return 'active'
  if (diff < 60 * 60 * 1000) return 'idle'
  return 'stale'
})

const connectionLabel = computed(() => {
  switch (connectionStatus.value) {
    case 'active': return '活跃'
    case 'idle': return '空闲'
    case 'stale': return '已断开'
    case 'never': return '无连接'
  }
})

const lastRequestFormatted = computed(() => {
  if (!aiStore.stats?.lastRequestAt) return '从未'
  const date = new Date(aiStore.stats.lastRequestAt)
  return date.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
})

async function loadSettings() {
  try {
    const loaded = await settingsApi.get()
    settings.value = {
      ...loaded,
      aiTranslation: loaded.aiTranslation ?? { ...DEFAULT_AI_TRANSLATION },
    }
  } catch {
    // Use defaults
  }
}

async function handleSave() {
  if (!settings.value) return
  settingsLoading.value = true
  try {
    await settingsApi.save(settings.value)
    message.success('设置已保存')
  } catch {
    message.error('保存设置失败')
  } finally {
    settingsLoading.value = false
  }
}

onMounted(async () => {
  await aiStore.connect()
  await aiStore.fetchStats()
  await loadSettings()
})

onUnmounted(() => {
  aiStore.disconnect()
})
</script>

<template>
  <div class="ai-page">
    <h1 class="page-title" style="animation-delay: 0s">
      <span class="page-title-icon">
        <NIcon :size="24"><SmartToyOutlined /></NIcon>
      </span>
      AI 翻译
    </h1>

    <!-- Status Dashboard -->
    <div class="section-card" style="animation-delay: 0.05s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon status">
            <NIcon :size="16">
              <LinkOutlined v-if="connectionStatus === 'active'" />
              <LinkOffOutlined v-else />
            </NIcon>
          </span>
          翻译状态
        </h2>
        <NButton size="small" quaternary @click="aiStore.fetchStats()">
          刷新
        </NButton>
      </div>

      <div class="stats-grid">
        <div class="stat-card">
          <div class="stat-icon connection" :class="connectionStatus">
            <NIcon :size="20">
              <LinkOutlined v-if="connectionStatus === 'active'" />
              <LinkOffOutlined v-else />
            </NIcon>
          </div>
          <div class="stat-content">
            <span class="stat-label">连接状态</span>
            <span class="stat-value" :class="connectionStatus">{{ connectionLabel }}</span>
            <span class="stat-hint">最近请求: {{ lastRequestFormatted }}</span>
          </div>
        </div>

        <div class="stat-card">
          <div class="stat-icon translated">
            <NIcon :size="20"><TranslateOutlined /></NIcon>
          </div>
          <div class="stat-content">
            <span class="stat-label">已翻译</span>
            <span class="stat-value">{{ aiStore.stats?.totalTranslated ?? 0 }}</span>
            <span class="stat-hint">条文本（本次会话）</span>
          </div>
        </div>

        <div class="stat-card">
          <div class="stat-icon in-progress">
            <NIcon :size="20"><HourglassEmptyOutlined /></NIcon>
          </div>
          <div class="stat-content">
            <span class="stat-label">处理中</span>
            <span class="stat-value">{{ aiStore.stats?.inProgress ?? 0 }}</span>
            <span class="stat-hint">条文本正在翻译</span>
          </div>
        </div>
      </div>
    </div>

    <!-- AI Settings -->
    <AiTranslationCard
      v-if="settings"
      v-model="aiSettings"
      :saving="settingsLoading"
      @save="handleSave"
      style="animation-delay: 0.1s"
    />
  </div>
</template>

<style scoped>
.ai-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
  animation: fadeIn 0.3s ease;
}

.page-title {
  font-family: var(--font-display);
  font-size: 30px;
  font-weight: 600;
  color: var(--text-1);
  margin-bottom: 12px;
  letter-spacing: -0.03em;
  animation: slideUp 0.4s var(--ease-out) backwards;
  display: flex;
  align-items: center;
  gap: 14px;
}

.page-title-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 42px;
  height: 42px;
  border-radius: 12px;
  background: rgba(167, 139, 250, 0.10);
  color: #a78bfa;
  flex-shrink: 0;
}

/* ===== Section Card ===== */
.section-card {
  display: flex;
  flex-direction: column;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 24px;
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
  flex-shrink: 0;
}

.section-icon.status {
  background: rgba(52, 211, 153, 0.10);
  color: #34d399;
}

/* ===== Stats Grid ===== */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}

.stat-card {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  padding: 18px 20px;
  background: var(--bg-subtle);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  transition: border-color 0.2s ease, background 0.2s ease;
}

.stat-card:hover {
  border-color: var(--border-hover);
  background: var(--bg-subtle-hover);
}

.stat-icon {
  width: 42px;
  height: 42px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 10px;
  flex-shrink: 0;
}

.stat-icon.connection {
  background: rgba(107, 114, 128, 0.10);
  color: #6b7280;
}

.stat-icon.connection.active {
  background: rgba(52, 211, 153, 0.10);
  color: #34d399;
}

.stat-icon.connection.idle {
  background: rgba(251, 191, 36, 0.10);
  color: #fbbf24;
}

.stat-icon.translated {
  background: rgba(96, 165, 250, 0.10);
  color: #60a5fa;
}

.stat-icon.in-progress {
  background: rgba(167, 139, 250, 0.10);
  color: #a78bfa;
}

.stat-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.stat-label {
  font-size: 12px;
  font-weight: 500;
  color: var(--text-3);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.stat-value {
  font-family: var(--font-display);
  font-size: 22px;
  font-weight: 600;
  color: var(--text-1);
  letter-spacing: -0.02em;
  line-height: 1.2;
}

.stat-value.active {
  color: #34d399;
}

.stat-value.idle {
  color: #fbbf24;
}

.stat-value.stale,
.stat-value.never {
  color: var(--text-3);
}

.stat-hint {
  font-size: 12px;
  color: var(--text-3);
}

/* ===== Responsive ===== */
@media (max-width: 768px) {
  .stats-grid {
    grid-template-columns: 1fr;
  }

  .section-card {
    padding: 16px;
  }
}

@media (max-width: 480px) {
  .page-title {
    font-size: 22px;
    margin-bottom: 20px;
    gap: 10px;
  }

  .page-title-icon {
    width: 36px;
    height: 36px;
    border-radius: 10px;
  }

  .section-card {
    padding: 14px;
    border-radius: var(--radius-md);
  }

  .stat-card {
    padding: 14px 16px;
  }

  .stat-icon {
    width: 36px;
    height: 36px;
    border-radius: 8px;
  }

  .stat-value {
    font-size: 18px;
  }
}
</style>
