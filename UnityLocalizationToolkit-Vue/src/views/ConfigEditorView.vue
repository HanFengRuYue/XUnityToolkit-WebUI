<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NIcon, useMessage } from 'naive-ui'
import { SaveOutlined, ArrowBackOutlined } from '@vicons/material'
import { gamesApi } from '@/api/games'
import type { Game } from '@/api/types'

const route = useRoute()
const router = useRouter()
const message = useMessage()

const gameId = route.params['id'] as string
const game = ref<Game | null>(null)
const content = ref('')
const originalContent = ref('')
const loading = ref(true)
const saving = ref(false)

const hasChanges = ref(false)

function updateContent(e: Event) {
  content.value = (e.target as HTMLTextAreaElement).value
  hasChanges.value = content.value !== originalContent.value
}

function handleBeforeUnload(e: BeforeUnloadEvent) {
  if (hasChanges.value) {
    e.preventDefault()
    e.returnValue = ''
  }
}

onMounted(async () => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  try {
    const [gameData, rawConfig] = await Promise.all([
      gamesApi.get(gameId),
      gamesApi.getRawConfig(gameId),
    ])
    game.value = gameData
    content.value = rawConfig
    originalContent.value = rawConfig
    document.title = `配置编辑 - ${gameData.name}`
  } catch {
    message.error('加载配置文件失败')
  } finally {
    loading.value = false
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
})

async function handleSave() {
  saving.value = true
  try {
    await gamesApi.saveRawConfig(gameId, content.value)
    originalContent.value = content.value
    hasChanges.value = false
    message.success('配置已保存')
  } catch {
    message.error('保存失败')
  } finally {
    saving.value = false
  }
}

function handleBack() {
  router.back()
}
</script>

<template>
  <div class="editor-page">
    <div class="editor-toolbar">
      <div class="toolbar-left">
        <NButton quaternary size="small" @click="handleBack">
          <template #icon>
            <NIcon><ArrowBackOutlined /></NIcon>
          </template>
          关闭
        </NButton>
        <span class="toolbar-title">
          {{ game?.name ?? '...' }} — AutoTranslatorConfig.ini
        </span>
        <span v-if="hasChanges" class="unsaved-badge">未保存</span>
      </div>
      <div class="toolbar-right">
        <NButton
          type="primary"
          size="small"
          :disabled="!hasChanges"
          :loading="saving"
          @click="handleSave"
        >
          <template #icon>
            <NIcon><SaveOutlined /></NIcon>
          </template>
          保存
        </NButton>
      </div>
    </div>

    <div v-if="loading" class="editor-loading">
      <div class="loading-spinner"></div>
    </div>
    <div v-else class="editor-container">
      <textarea
        class="config-textarea"
        :value="content"
        @input="updateContent"
        spellcheck="false"
        autocomplete="off"
        autocorrect="off"
        autocapitalize="off"
      ></textarea>
    </div>
  </div>
</template>

<style scoped>
.editor-page {
  display: flex;
  flex-direction: column;
  height: calc(100vh - 72px);
  animation: fadeIn 0.3s ease;
}

.editor-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 16px;
  background: var(--bg-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  margin-bottom: 12px;
  flex-shrink: 0;
}

.toolbar-left {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.toolbar-title {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-2);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.unsaved-badge {
  font-size: 11px;
  font-weight: 500;
  color: var(--warning);
  background: rgba(251, 191, 36, 0.1);
  padding: 2px 8px;
  border-radius: 4px;
  flex-shrink: 0;
}

.toolbar-right {
  flex-shrink: 0;
}

.editor-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  flex: 1;
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

.editor-container {
  flex: 1;
  min-height: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  overflow: hidden;
}

.config-textarea {
  width: 100%;
  height: 100%;
  padding: 16px;
  margin: 0;
  border: none;
  outline: none;
  resize: none;
  background: var(--bg-card);
  color: var(--text-1);
  font-family: 'JetBrains Mono', monospace;
  font-size: 13px;
  line-height: 1.6;
  tab-size: 4;
  white-space: pre;
  overflow: auto;
  box-sizing: border-box;
}

.config-textarea:focus {
  background: var(--bg-elevated, var(--bg-card));
}

.config-textarea::selection {
  background: var(--accent-soft);
}

/* Scrollbar styling */
.config-textarea::-webkit-scrollbar {
  width: 8px;
  height: 8px;
}

.config-textarea::-webkit-scrollbar-track {
  background: transparent;
}

.config-textarea::-webkit-scrollbar-thumb {
  background: var(--scrollbar-thumb);
  border-radius: 4px;
}

.config-textarea::-webkit-scrollbar-thumb:hover {
  background: var(--scrollbar-thumb-hover);
}

@media (max-width: 768px) {
  .editor-page {
    height: calc(100vh - 94px);
  }

  .editor-toolbar {
    flex-wrap: wrap;
    gap: 8px;
  }

  .toolbar-left {
    flex: 1;
    min-width: 0;
  }

  .toolbar-title {
    font-size: 12px;
  }

  .config-textarea {
    font-size: 12px;
    padding: 12px;
  }
}

@media (max-width: 480px) {
  .toolbar-title {
    display: none;
  }
}
</style>
