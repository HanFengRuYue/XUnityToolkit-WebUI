<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { NAlert, NButton, NIcon, NInput, NSelect, NSpin, NTag, useMessage } from 'naive-ui'
import {
  AttachFileOutlined,
  AutoAwesomeOutlined,
  CheckCircleOutlined,
  CloseOutlined,
  DeleteSweepOutlined,
  ErrorOutlineOutlined,
  RemoveOutlined,
  SendOutlined,
  WarningAmberOutlined,
} from '@vicons/material'
import { gamesApi, toolboxAgentApi } from '@/api/games'
import type {
  Game,
  ToolboxAgentAttachment,
  ToolboxAgentStatus,
  ToolboxAgentToolExecution,
} from '@/api/types'

const props = defineProps<{ show: boolean }>()
const emit = defineEmits<{ 'update:show': [value: boolean] }>()

interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  text: string
  attachments?: ToolboxAgentAttachment[]
  executions?: ToolboxAgentToolExecution[]
}

const notification = useMessage()
const status = ref<ToolboxAgentStatus | null>(null)
const games = ref<Game[]>([])
const selectedGameId = ref<string | null>(null)
const input = ref('')
const messages = ref<ChatMessage[]>([])
const pendingAttachments = ref<ToolboxAgentAttachment[]>([])
const loading = ref(false)
const uploading = ref(false)
const minimized = ref(false)
const needsConfirmation = ref(false)
const pendingActionDescription = ref<string | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const messageList = ref<HTMLElement | null>(null)
const sessionId = ref(createSessionId())

const gameOptions = computed(() => games.value.map(game => ({
  label: game.name,
  value: game.id,
})))

const canSend = computed(() =>
  status.value?.supported === true
  && !loading.value
  && !uploading.value
  && (input.value.trim().length > 0 || pendingAttachments.value.length > 0),
)

watch(() => props.show, async (show) => {
  if (!show) return
  minimized.value = false
  await initialize()
  await scrollToBottom()
})

async function initialize() {
  try {
    const [agentStatus, gameList] = await Promise.all([
      toolboxAgentApi.status(),
      gamesApi.list(),
    ])
    status.value = agentStatus
    games.value = gameList
    if (selectedGameId.value && !gameList.some(game => game.id === selectedGameId.value)) {
      selectedGameId.value = null
    }
  } catch (error) {
    notification.error(errorText(error, '读取智能体状态失败'))
  }
}

async function send(confirmPendingAction = false) {
  if (loading.value) return
  if (!confirmPendingAction && !canSend.value) return

  const text = input.value.trim()
  const attachments = [...pendingAttachments.value]
  if (!confirmPendingAction) {
    messages.value.push({
      id: createMessageId(),
      role: 'user',
      text: text || '请处理我上传的附件。',
      attachments,
    })
    input.value = ''
    pendingAttachments.value = []
  }

  loading.value = true
  needsConfirmation.value = false
  pendingActionDescription.value = null
  await scrollToBottom()
  try {
    const response = await toolboxAgentApi.chat({
      sessionId: sessionId.value,
      message: confirmPendingAction ? '' : text,
      gameId: selectedGameId.value,
      attachmentIds: confirmPendingAction ? [] : attachments.map(item => item.id),
      confirmPendingAction,
    })
    messages.value.push({
      id: createMessageId(),
      role: 'assistant',
      text: response.message,
      executions: response.executions,
    })
    needsConfirmation.value = response.requiresConfirmation
    pendingActionDescription.value = response.pendingActionDescription ?? null
    status.value = { supported: true, endpointName: response.endpointName }
  } catch (error) {
    const text = errorText(error, '智能体执行失败')
    messages.value.push({ id: createMessageId(), role: 'assistant', text })
    notification.error(text)
    await initialize()
  } finally {
    loading.value = false
    await scrollToBottom()
  }
}

async function handleFileSelection(event: Event) {
  const target = event.target as HTMLInputElement
  const files = Array.from(target.files ?? [])
  target.value = ''
  if (files.length === 0) return
  uploading.value = true
  try {
    const uploaded = await toolboxAgentApi.upload(sessionId.value, files)
    pendingAttachments.value.push(...uploaded)
    notification.success(`已添加 ${uploaded.length} 个附件`)
  } catch (error) {
    notification.error(errorText(error, '附件上传失败'))
  } finally {
    uploading.value = false
  }
}

function removeAttachment(id: string) {
  pendingAttachments.value = pendingAttachments.value.filter(item => item.id !== id)
}

async function newConversation() {
  const previous = sessionId.value
  sessionId.value = createSessionId()
  messages.value = []
  pendingAttachments.value = []
  needsConfirmation.value = false
  pendingActionDescription.value = null
  input.value = ''
  try {
    await toolboxAgentApi.clear(previous)
  } catch {
    // The local session also expires automatically; a failed cleanup must not block a new chat.
  }
}

function handleKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault()
    void send(false)
  }
}

function executionType(state: ToolboxAgentToolExecution['state']) {
  if (state === 'Completed') return 'success'
  if (state === 'Failed') return 'error'
  if (state === 'RequiresConfirmation') return 'warning'
  return 'default'
}

function executionIcon(state: ToolboxAgentToolExecution['state']) {
  if (state === 'Completed') return CheckCircleOutlined
  if (state === 'Failed') return ErrorOutlineOutlined
  return WarningAmberOutlined
}

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

async function scrollToBottom() {
  await nextTick()
  if (messageList.value) messageList.value.scrollTop = messageList.value.scrollHeight
}

function close() {
  emit('update:show', false)
}

function errorText(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback
}

function createSessionId() {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, character => {
    const random = Math.floor(Math.random() * 16)
    const value = character === 'x' ? random : (random & 0x3) | 0x8
    return value.toString(16)
  })
}

function createMessageId() {
  return `${Date.now()}-${Math.random().toString(16).slice(2)}`
}
</script>

<template>
  <Transition name="agent-window">
    <section
      v-if="show"
      class="agent-window"
      :class="{ minimized }"
      aria-label="工具箱智能体"
    >
      <header class="agent-header">
        <div class="agent-title">
          <span class="agent-avatar"><NIcon :size="20"><AutoAwesomeOutlined /></NIcon></span>
          <div>
            <strong>工具箱智能体</strong>
            <span>{{ status?.endpointName || '云端 AI' }}</span>
          </div>
        </div>
        <div class="window-actions">
          <NButton quaternary circle size="small" title="新对话" :disabled="loading" @click="newConversation">
            <template #icon><NIcon><DeleteSweepOutlined /></NIcon></template>
          </NButton>
          <NButton quaternary circle size="small" title="最小化" @click="minimized = !minimized">
            <template #icon><NIcon><RemoveOutlined /></NIcon></template>
          </NButton>
          <NButton quaternary circle size="small" title="关闭" @click="close">
            <template #icon><NIcon><CloseOutlined /></NIcon></template>
          </NButton>
        </div>
      </header>

      <template v-if="!minimized">
        <div class="agent-context">
          <NSelect
            v-model:value="selectedGameId"
            :options="gameOptions"
            clearable
            filterable
            size="small"
            placeholder="选择要操作的游戏（可选）"
          />
        </div>

        <NAlert
          v-if="status && !status.supported"
          type="warning"
          :bordered="false"
          class="agent-alert"
        >
          {{ status.reason }}
        </NAlert>

        <div ref="messageList" class="message-list">
          <div v-if="messages.length === 0" class="welcome">
            <NIcon :size="30"><AutoAwesomeOutlined /></NIcon>
            <strong>直接告诉我你想完成什么</strong>
            <p>我能调用工具箱现有功能、检查和修改游戏配置、自动修复插件问题，也能接收字体或插件附件。</p>
            <p class="welcome-example">例如：上传 TTF 后说“给当前游戏生成并应用这个字体”。</p>
          </div>

          <article
            v-for="item in messages"
            :key="item.id"
            class="chat-message"
            :class="item.role"
          >
            <div class="bubble">
              <p>{{ item.text }}</p>
              <div v-if="item.attachments?.length" class="message-attachments">
                <NTag v-for="attachment in item.attachments" :key="attachment.id" size="small" :bordered="false">
                  {{ attachment.fileName }} · {{ formatSize(attachment.fileSize) }}
                </NTag>
              </div>
              <div v-if="item.executions?.length" class="execution-list">
                <div v-for="execution in item.executions" :key="execution.id" class="execution-item">
                  <NIcon :class="`execution-${execution.state.toLowerCase()}`">
                    <component :is="executionIcon(execution.state)" />
                  </NIcon>
                  <div>
                    <div class="execution-heading">
                      <strong>{{ execution.description }}</strong>
                      <NTag size="tiny" :type="executionType(execution.state)" :bordered="false">
                        {{ execution.state }}
                      </NTag>
                    </div>
                    <span>{{ execution.message }}</span>
                  </div>
                </div>
              </div>
            </div>
          </article>

          <div v-if="loading" class="agent-thinking">
            <NSpin size="small" />
            <span>智能体正在规划并执行工具...</span>
          </div>
        </div>

        <div v-if="needsConfirmation" class="confirmation-bar">
          <div>
            <strong>需要确认高影响操作</strong>
            <span>{{ pendingActionDescription }}</span>
          </div>
          <NButton type="warning" size="small" :loading="loading" @click="send(true)">确认执行</NButton>
        </div>

        <div v-if="pendingAttachments.length" class="pending-attachments">
          <div v-for="attachment in pendingAttachments" :key="attachment.id" class="attachment-chip">
            <NIcon><AttachFileOutlined /></NIcon>
            <span>{{ attachment.fileName }}</span>
            <small>{{ formatSize(attachment.fileSize) }}</small>
            <button type="button" @click="removeAttachment(attachment.id)">×</button>
          </div>
        </div>

        <footer class="composer">
          <input
            ref="fileInput"
            class="hidden-file-input"
            type="file"
            multiple
            accept=".ttf,.otf,.bundle,.dll,.zip,.txt,.ini,.cfg,.json,.xml,.yaml,.yml,.png,.jpg,.jpeg,.webp"
            @change="handleFileSelection"
          />
          <NButton
            circle
            secondary
            :loading="uploading"
            :disabled="loading || status?.supported === false"
            title="上传附件"
            @click="fileInput?.click()"
          >
            <template #icon><NIcon><AttachFileOutlined /></NIcon></template>
          </NButton>
          <NInput
            v-model:value="input"
            type="textarea"
            :autosize="{ minRows: 1, maxRows: 5 }"
            :disabled="loading || status?.supported === false"
            placeholder="描述你想让智能体完成的操作..."
            @keydown="handleKeydown"
          />
          <NButton circle type="primary" :disabled="!canSend" :loading="loading" @click="send(false)">
            <template #icon><NIcon><SendOutlined /></NIcon></template>
          </NButton>
        </footer>
        <div class="cloud-note">仅使用当前云端 AI；附件二进制不发送给模型，必要的脱敏文本会发送到云端，工具调用与备份均在本机执行。</div>
      </template>
    </section>
  </Transition>
</template>

<style scoped>
.agent-window {
  position: fixed;
  right: 24px;
  bottom: 20px;
  z-index: 1200;
  display: flex;
  flex-direction: column;
  width: min(540px, calc(100vw - 48px));
  height: min(720px, calc(100vh - 72px));
  overflow: hidden;
  border: 1px solid var(--accent-border);
  border-radius: 16px;
  background: color-mix(in srgb, var(--bg-surface) 96%, transparent);
  box-shadow: 0 24px 70px rgba(0, 0, 0, 0.34), 0 0 28px var(--accent-soft);
  backdrop-filter: blur(18px);
}

.agent-window.minimized {
  width: min(390px, calc(100vw - 32px));
  height: 58px;
}

.agent-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 58px;
  padding: 0 12px 0 16px;
  border-bottom: 1px solid var(--border);
  background: linear-gradient(120deg, var(--accent-soft), transparent 60%);
}

.agent-title,
.window-actions,
.agent-title > div,
.execution-heading,
.composer {
  display: flex;
  align-items: center;
}

.agent-title { gap: 10px; }
.window-actions { gap: 2px; }
.agent-title > div { align-items: flex-start; flex-direction: column; }
.agent-title strong { color: var(--text-1); font-size: 14px; }
.agent-title span { color: var(--text-3); font-size: 10px; }

.agent-avatar {
  display: grid;
  width: 34px;
  height: 34px;
  place-items: center;
  border-radius: 10px;
  color: var(--accent);
  background: var(--accent-soft);
  box-shadow: inset 0 0 14px var(--accent-soft);
}

.agent-context { padding: 10px 12px 0; }
.agent-alert { margin: 10px 12px 0; font-size: 12px; }

.message-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 14px 14px 8px;
  scroll-behavior: smooth;
}

.welcome {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 260px;
  padding: 30px;
  color: var(--text-3);
  text-align: center;
}

.welcome .n-icon { color: var(--accent); filter: drop-shadow(0 0 9px var(--accent-glow)); }
.welcome strong { margin-top: 12px; color: var(--text-1); font-size: 15px; }
.welcome p { max-width: 390px; margin: 8px 0 0; font-size: 12px; line-height: 1.65; }
.welcome-example { color: var(--text-2); }

.chat-message { display: flex; margin-bottom: 12px; }
.chat-message.user { justify-content: flex-end; }
.bubble { max-width: 88%; min-width: 0; padding: 10px 12px; border-radius: 12px; }
.assistant .bubble { border: 1px solid var(--border); background: var(--bg-subtle); }
.user .bubble { color: white; background: var(--accent); }
.bubble > p { margin: 0; white-space: pre-wrap; overflow-wrap: anywhere; font-size: 13px; line-height: 1.62; }

.message-attachments { display: flex; flex-wrap: wrap; gap: 5px; margin-top: 8px; }

.execution-list {
  display: flex;
  flex-direction: column;
  gap: 7px;
  margin-top: 10px;
  padding-top: 9px;
  border-top: 1px solid var(--border);
}

.execution-item { display: flex; align-items: flex-start; gap: 7px; }
.execution-item > div { flex: 1; min-width: 0; }
.execution-heading { justify-content: space-between; gap: 7px; }
.execution-heading strong { color: var(--text-2); font-size: 11px; }
.execution-item span { display: block; margin-top: 2px; color: var(--text-3); font-size: 10px; line-height: 1.45; }
.execution-completed { color: var(--success); }
.execution-failed { color: var(--danger); }
.execution-requiresconfirmation { color: var(--warning); }
.execution-skipped { color: var(--text-3); }

.agent-thinking {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 9px 10px;
  color: var(--text-3);
  font-size: 12px;
}

.confirmation-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin: 0 12px 8px;
  padding: 10px 12px;
  border: 1px solid color-mix(in srgb, var(--warning) 45%, transparent);
  border-radius: 10px;
  background: color-mix(in srgb, var(--warning) 9%, transparent);
}

.confirmation-bar > div { min-width: 0; }
.confirmation-bar strong,
.confirmation-bar span { display: block; }
.confirmation-bar strong { color: var(--text-1); font-size: 12px; }
.confirmation-bar span { margin-top: 2px; color: var(--text-3); font-size: 10px; overflow-wrap: anywhere; }

.pending-attachments {
  display: flex;
  gap: 6px;
  overflow-x: auto;
  padding: 7px 12px 0;
}

.attachment-chip {
  display: flex;
  align-items: center;
  flex: 0 0 auto;
  gap: 5px;
  max-width: 270px;
  padding: 5px 7px;
  border: 1px solid var(--border);
  border-radius: 7px;
  color: var(--text-2);
  background: var(--bg-subtle);
  font-size: 11px;
}

.attachment-chip span { max-width: 150px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.attachment-chip small { color: var(--text-3); white-space: nowrap; }
.attachment-chip button { padding: 0; border: 0; color: var(--text-3); background: none; cursor: pointer; font-size: 16px; }

.composer { gap: 8px; padding: 10px 12px 7px; }
.composer .n-input { flex: 1; }
.hidden-file-input { display: none; }
.cloud-note { padding: 0 12px 9px; color: var(--text-3); text-align: center; font-size: 9px; }

.agent-window-enter-active,
.agent-window-leave-active { transition: opacity 0.2s ease, transform 0.25s var(--ease-out); }
.agent-window-enter-from,
.agent-window-leave-to { opacity: 0; transform: translateY(18px) scale(0.97); }

@media (max-width: 768px) {
  .agent-window {
    right: 10px;
    bottom: 10px;
    width: calc(100vw - 20px);
    height: calc(100vh - 80px);
  }
  .agent-window.minimized { width: calc(100vw - 20px); }
  .bubble { max-width: 94%; }
}
</style>
