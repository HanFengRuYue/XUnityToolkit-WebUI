<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { NAlert, NButton, NIcon, NInput, NPopconfirm, NSelect, NSpin, NTag, useMessage } from 'naive-ui'
import {
  AddCommentOutlined,
  AttachFileOutlined,
  AutoAwesomeOutlined,
  CheckCircleOutlined,
  CloseOutlined,
  DeleteOutlineOutlined,
  DeleteSweepOutlined,
  ErrorOutlineOutlined,
  HistoryOutlined,
  RemoveOutlined,
  SendOutlined,
  WarningAmberOutlined,
} from '@vicons/material'
import { gamesApi, toolboxAgentApi } from '@/api/games'
import { useGamesStore } from '@/stores/games'
import type {
  Game,
  LlmProvider,
  ToolboxAgentAttachment,
  ToolboxAgentConversationSummary,
  ToolboxAgentStatus,
  ToolboxAgentToolExecution,
} from '@/api/types'

const AUTOMATIC_ENDPOINT = '__automatic__'

const props = withDefaults(defineProps<{
  show: boolean
  defaultLeft?: number
}>(), {
  defaultLeft: 24,
})
const emit = defineEmits<{ 'update:show': [value: boolean] }>()

interface ChatMessage {
  id: string
  role: 'user' | 'assistant'
  text: string
  attachments?: ToolboxAgentAttachment[]
  executions?: ToolboxAgentToolExecution[]
  createdAt?: string
}

const notification = useMessage()
const gamesStore = useGamesStore()
const status = ref<ToolboxAgentStatus | null>(null)
const games = ref<Game[]>([])
const historySessions = ref<ToolboxAgentConversationSummary[]>([])
const selectedGameId = ref<string | null>(null)
const selectedEndpointId = ref(AUTOMATIC_ENDPOINT)
const lastEndpointName = ref<string | null>(null)
const input = ref('')
const messages = ref<ChatMessage[]>([])
const pendingAttachments = ref<ToolboxAgentAttachment[]>([])
const loading = ref(false)
const uploading = ref(false)
const historyLoading = ref(false)
const sessionLoading = ref(false)
const minimized = ref(false)
const historyOpen = ref(false)
const needsConfirmation = ref(false)
const pendingActionDescription = ref<string | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)
const messageList = ref<HTMLElement | null>(null)
const agentWindow = ref<HTMLElement | null>(null)
const sessionId = ref(createSessionId())
const position = ref<{ x: number; y: number } | null>(null)
const dragging = ref(false)

let dragPointerId: number | null = null
let dragOffsetX = 0
let dragOffsetY = 0
let previousBodyCursor = ''
let previousBodyUserSelect = ''

const windowStyle = computed<Record<string, string>>(() => {
  const style: Record<string, string> = {}
  if (position.value) {
    style.left = `${position.value.x}px`
    style.top = `${position.value.y}px`
    style.bottom = 'auto'
  } else {
    style['--agent-default-left'] = `${props.defaultLeft}px`
  }
  return style
})

const gameOptions = computed(() => games.value.map(game => ({
  label: game.name,
  value: game.id,
})))

const automaticEndpoint = computed(() =>
  status.value?.endpoints?.find(endpoint => endpoint.isAutomaticDefault) ?? null,
)

const endpointOptions = computed(() => {
  const automaticName = automaticEndpoint.value?.name || status.value?.endpointName
  const options: Array<{ label: string; value: string; disabled?: boolean }> = [{
    label: automaticName ? `自动（${automaticName}）` : '自动（按优先级）',
    value: AUTOMATIC_ENDPOINT,
  }]
  for (const endpoint of status.value?.endpoints ?? []) {
    const model = endpoint.modelName ? ` · ${endpoint.modelName}` : ''
    options.push({
      label: `${providerLabel(endpoint.provider)} · ${endpoint.name}${model}`,
      value: endpoint.id,
    })
  }
  if (selectedEndpointId.value !== AUTOMATIC_ENDPOINT
    && !(status.value?.endpoints?.some(endpoint => endpoint.id === selectedEndpointId.value))) {
    options.push({
      label: `${lastEndpointName.value || '原端点'}（当前不可用）`,
      value: selectedEndpointId.value,
      disabled: true,
    })
  }
  return options
})

const selectedEndpointAvailable = computed(() => {
  if (selectedEndpointId.value === AUTOMATIC_ENDPOINT) return status.value?.supported === true
  return status.value?.endpoints?.some(endpoint => endpoint.id === selectedEndpointId.value) === true
})

const headerEndpointName = computed(() => {
  if (lastEndpointName.value) return lastEndpointName.value
  if (selectedEndpointId.value !== AUTOMATIC_ENDPOINT) {
    return status.value?.endpoints?.find(endpoint => endpoint.id === selectedEndpointId.value)?.name
      || '所选端点不可用'
  }
  return status.value?.endpointName || '云端 AI'
})

const canSend = computed(() =>
  status.value?.supported === true
  && selectedEndpointAvailable.value
  && !loading.value
  && !uploading.value
  && !sessionLoading.value
  && (input.value.trim().length > 0 || pendingAttachments.value.length > 0),
)

const historyBusy = computed(() =>
  loading.value || uploading.value || sessionLoading.value || needsConfirmation.value,
)

watch(() => props.show, async (show) => {
  if (!show) {
    if (dragging.value) endDrag()
    return
  }
  minimized.value = false
  await nextTick()
  clampCurrentPosition()
  await initialize()
  await scrollToBottom()
})

watch(historyOpen, async () => {
  await nextTick()
  clampCurrentPosition()
})

function startDrag(event: PointerEvent) {
  if (dragging.value) return
  if (event.pointerType === 'mouse' && event.button !== 0) return
  const element = agentWindow.value
  const handle = event.currentTarget as HTMLElement | null
  if (!element || !handle) return

  event.preventDefault()
  const rect = element.getBoundingClientRect()
  position.value = { x: rect.left, y: rect.top }
  dragPointerId = event.pointerId
  dragOffsetX = event.clientX - rect.left
  dragOffsetY = event.clientY - rect.top
  dragging.value = true
  previousBodyCursor = document.body.style.cursor
  previousBodyUserSelect = document.body.style.userSelect
  document.body.style.cursor = 'grabbing'
  document.body.style.userSelect = 'none'
  handle.setPointerCapture(event.pointerId)
}

function moveDrag(event: PointerEvent) {
  if (!dragging.value || event.pointerId !== dragPointerId) return
  position.value = clampPosition(event.clientX - dragOffsetX, event.clientY - dragOffsetY)
}

function endDrag(event?: PointerEvent) {
  if (!dragging.value || dragPointerId === null) return
  if (event && event.pointerId !== dragPointerId) return
  const handle = event?.currentTarget as HTMLElement | null
  if (handle && handle.hasPointerCapture(dragPointerId)) handle.releasePointerCapture(dragPointerId)
  dragging.value = false
  dragPointerId = null
  document.body.style.cursor = previousBodyCursor
  document.body.style.userSelect = previousBodyUserSelect
}

function clampPosition(x: number, y: number) {
  const element = agentWindow.value
  if (!element) return { x, y }
  const margin = window.innerWidth <= 768 ? 10 : 12
  const maxX = Math.max(margin, window.innerWidth - element.offsetWidth - margin)
  const maxY = Math.max(margin, window.innerHeight - element.offsetHeight - margin)
  return {
    x: Math.min(maxX, Math.max(margin, x)),
    y: Math.min(maxY, Math.max(margin, y)),
  }
}

function clampCurrentPosition() {
  if (!position.value) return
  position.value = clampPosition(position.value.x, position.value.y)
}

async function toggleMinimized() {
  minimized.value = !minimized.value
  if (minimized.value) historyOpen.value = false
  await nextTick()
  clampCurrentPosition()
}

async function toggleHistory() {
  if (minimized.value) minimized.value = false
  historyOpen.value = !historyOpen.value
  if (historyOpen.value) await refreshHistory()
}

function handleViewportResize() {
  clampCurrentPosition()
}

window.addEventListener('resize', handleViewportResize)

onBeforeUnmount(() => {
  window.removeEventListener('resize', handleViewportResize)
  if (dragging.value) endDrag()
})

async function initialize() {
  try {
    const [agentStatus, gameList, sessions] = await Promise.all([
      toolboxAgentApi.status(),
      gamesApi.list(),
      toolboxAgentApi.listSessions(),
    ])
    status.value = agentStatus
    games.value = gameList
    historySessions.value = sessions
    if (selectedGameId.value && !gameList.some(game => game.id === selectedGameId.value)) {
      selectedGameId.value = null
    }
  } catch (error) {
    notification.error(errorText(error, '读取智能体状态失败'))
  }
}

async function refreshHistory() {
  historyLoading.value = true
  try {
    historySessions.value = await toolboxAgentApi.listSessions()
  } catch (error) {
    notification.error(errorText(error, '读取历史对话失败'))
  } finally {
    historyLoading.value = false
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
      createdAt: new Date().toISOString(),
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
      endpointId: selectedEndpointId.value === AUTOMATIC_ENDPOINT ? null : selectedEndpointId.value,
    })
    messages.value.push({
      id: createMessageId(),
      role: 'assistant',
      text: response.message,
      executions: response.executions,
      createdAt: new Date().toISOString(),
    })
    needsConfirmation.value = response.requiresConfirmation
    pendingActionDescription.value = response.pendingActionDescription ?? null
    lastEndpointName.value = response.endpointName
    if (status.value) {
      status.value = { ...status.value, supported: true, reason: null }
    }
    await refreshChangedResources(response.executions)
  } catch (error) {
    const text = errorText(error, '智能体执行失败')
    messages.value.push({
      id: createMessageId(),
      role: 'assistant',
      text,
      createdAt: new Date().toISOString(),
    })
    notification.error(text)
    await initialize()
  } finally {
    loading.value = false
    await refreshHistory()
    await scrollToBottom()
  }
}

const mutatingTools = new Set([
  'auto_repair_plugins',
  'patch_game_file',
  'apply_custom_font',
  'update_toolbox_setting',
  'use_attachment',
  'call_toolbox_api',
])

async function refreshChangedResources(executions: ToolboxAgentToolExecution[]) {
  const changed = executions.some(execution =>
    execution.state === 'Completed' && mutatingTools.has(execution.tool),
  )
  if (!changed) return

  try {
    await Promise.all([
      gamesStore.fetchGames(),
      gamesStore.loadPreferences(),
    ])
    games.value = [...gamesStore.games]
    if (selectedGameId.value && !games.value.some(game => game.id === selectedGameId.value)) {
      selectedGameId.value = null
    }
  } catch {
    notification.warning('操作已执行，但界面状态刷新失败；重新进入页面后会再次同步。')
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

function resetConversation() {
  sessionId.value = createSessionId()
  messages.value = []
  pendingAttachments.value = []
  needsConfirmation.value = false
  pendingActionDescription.value = null
  selectedGameId.value = null
  selectedEndpointId.value = AUTOMATIC_ENDPOINT
  lastEndpointName.value = null
  input.value = ''
}

function newConversation() {
  if (historyBusy.value) return
  resetConversation()
  historyOpen.value = false
}

async function loadConversation(summary: ToolboxAgentConversationSummary) {
  if (historyBusy.value || summary.sessionId === sessionId.value) {
    historyOpen.value = false
    return
  }
  if (input.value.trim() || pendingAttachments.value.length > 0) {
    notification.warning('请先发送或清除当前草稿和待上传附件。')
    return
  }

  sessionLoading.value = true
  try {
    const conversation = await toolboxAgentApi.getSession(summary.sessionId)
    sessionId.value = conversation.summary.sessionId
    messages.value = conversation.messages.map(message => ({
      id: message.id,
      role: message.role,
      text: message.text,
      attachments: message.attachments,
      executions: message.executions,
      createdAt: message.createdAt,
    }))
    selectedGameId.value = conversation.summary.gameId
      && games.value.some(game => game.id === conversation.summary.gameId)
      ? conversation.summary.gameId
      : null
    selectedEndpointId.value = conversation.summary.endpointId || AUTOMATIC_ENDPOINT
    lastEndpointName.value = conversation.summary.endpointName ?? null
    needsConfirmation.value = false
    pendingActionDescription.value = null
    pendingAttachments.value = []
    input.value = ''
    historyOpen.value = false
    await scrollToBottom()
  } catch (error) {
    notification.error(errorText(error, '加载历史对话失败'))
    await refreshHistory()
  } finally {
    sessionLoading.value = false
  }
}

async function deleteConversation(targetSessionId: string) {
  if (historyBusy.value) return
  try {
    await toolboxAgentApi.deleteSession(targetSessionId)
    if (targetSessionId === sessionId.value) resetConversation()
    await refreshHistory()
    notification.success('已删除历史对话')
  } catch (error) {
    notification.error(errorText(error, '删除历史对话失败'))
  }
}

async function clearAllConversations() {
  if (historyBusy.value || historySessions.value.length === 0) return
  try {
    await toolboxAgentApi.clearSessions()
    resetConversation()
    historySessions.value = []
    historyOpen.value = false
    notification.success('已清空全部历史对话')
  } catch (error) {
    notification.error(errorText(error, '清空历史对话失败'))
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

function providerLabel(provider: LlmProvider) {
  const labels: Record<LlmProvider, string> = {
    OpenAI: 'OpenAI',
    Claude: 'Claude',
    Gemini: 'Gemini',
    DeepSeek: 'DeepSeek',
    Qwen: '通义千问',
    GLM: '智谱 GLM',
    Kimi: 'Kimi',
    Custom: '自定义',
  }
  return labels[provider]
}

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function formatHistoryTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  }).format(date)
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
      ref="agentWindow"
      class="agent-window"
      :class="{ minimized, dragging, 'history-open': historyOpen }"
      :style="windowStyle"
      aria-label="工具箱智能体"
    >
      <header
        class="agent-header"
        title="拖动窗口"
        @pointerdown="startDrag"
        @pointermove="moveDrag"
        @pointerup="endDrag"
        @pointercancel="endDrag"
        @lostpointercapture="endDrag"
      >
        <div class="agent-title">
          <span class="agent-avatar"><NIcon :size="20"><AutoAwesomeOutlined /></NIcon></span>
          <div>
            <strong>工具箱智能体</strong>
            <span>{{ headerEndpointName }}</span>
          </div>
        </div>
        <div class="window-actions" @pointerdown.stop>
          <NButton quaternary circle size="small" title="历史对话" :class="{ active: historyOpen }" @click="toggleHistory">
            <template #icon><NIcon><HistoryOutlined /></NIcon></template>
          </NButton>
          <NButton quaternary circle size="small" title="新对话" :disabled="historyBusy" @click="newConversation">
            <template #icon><NIcon><AddCommentOutlined /></NIcon></template>
          </NButton>
          <NButton quaternary circle size="small" :title="minimized ? '还原' : '最小化'" @click="toggleMinimized">
            <template #icon><NIcon><RemoveOutlined /></NIcon></template>
          </NButton>
          <NButton quaternary circle size="small" title="关闭" @click="close">
            <template #icon><NIcon><CloseOutlined /></NIcon></template>
          </NButton>
        </div>
      </header>

      <div v-if="!minimized" class="agent-content">
        <button v-if="historyOpen" class="history-backdrop" type="button" aria-label="关闭历史对话" @click="historyOpen = false" />
        <aside v-if="historyOpen" class="history-drawer">
          <div class="history-heading">
            <div>
              <strong>历史对话</strong>
              <span>本地最近 100 个</span>
            </div>
            <div class="history-actions">
              <NButton quaternary circle size="tiny" title="新对话" :disabled="historyBusy" @click="newConversation">
                <template #icon><NIcon><AddCommentOutlined /></NIcon></template>
              </NButton>
              <NPopconfirm
                positive-text="清空"
                negative-text="取消"
                @positive-click="clearAllConversations"
              >
                <template #trigger>
                  <NButton quaternary circle size="tiny" title="清空历史" :disabled="historyBusy || historySessions.length === 0">
                    <template #icon><NIcon><DeleteSweepOutlined /></NIcon></template>
                  </NButton>
                </template>
                确定清空全部智能体历史对话吗？
              </NPopconfirm>
            </div>
          </div>

          <div class="history-list">
            <div v-if="historyLoading" class="history-empty"><NSpin size="small" />正在读取历史...</div>
            <div v-else-if="historySessions.length === 0" class="history-empty">还没有历史对话</div>
            <div
              v-for="item in historySessions"
              v-else
              :key="item.sessionId"
              class="history-item"
              :class="{ active: item.sessionId === sessionId, disabled: historyBusy }"
            >
              <button
                type="button"
                class="history-item-open"
                :disabled="historyBusy"
                @click="loadConversation(item)"
              >
                <span class="history-item-main">
                  <strong>{{ item.title }}</strong>
                  <small>{{ item.endpointName || '自动选择云端' }} · {{ item.messageCount }} 条消息</small>
                </span>
              </button>
              <span class="history-item-meta">
                <time>{{ formatHistoryTime(item.updatedAt) }}</time>
                <span @click.stop>
                  <NPopconfirm
                    positive-text="删除"
                    negative-text="取消"
                    @positive-click="deleteConversation(item.sessionId)"
                  >
                    <template #trigger>
                      <NButton quaternary circle size="tiny" title="删除对话" :disabled="historyBusy">
                        <template #icon><NIcon><DeleteOutlineOutlined /></NIcon></template>
                      </NButton>
                    </template>
                    删除“{{ item.title }}”？
                  </NPopconfirm>
                </span>
              </span>
            </div>
          </div>
        </aside>

        <main class="agent-main">
          <div class="agent-context">
            <NSelect
              v-model:value="selectedGameId"
              :options="gameOptions"
              :disabled="historyBusy"
              clearable
              filterable
              size="small"
              placeholder="选择要操作的游戏（可选）"
            />
            <NSelect
              v-model:value="selectedEndpointId"
              :options="endpointOptions"
              :disabled="historyBusy"
              filterable
              size="small"
              placeholder="选择云端提供商"
              @update:value="lastEndpointName = null"
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
          <NAlert
            v-else-if="!selectedEndpointAvailable"
            type="warning"
            :bordered="false"
            class="agent-alert"
          >
            该历史对话原先使用的云端端点当前不可用，请选择“自动”或其他端点后继续。
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
                  <NTag
                    v-for="attachment in item.attachments"
                    :key="attachment.id"
                    size="small"
                    :bordered="false"
                    title="历史仅保留附件信息；临时文件过期后需要重新上传"
                  >
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

            <div v-if="loading || sessionLoading" class="agent-thinking">
              <NSpin size="small" />
              <span>{{ sessionLoading ? '正在恢复历史对话...' : '智能体正在规划并执行工具...' }}</span>
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
              :disabled="loading || sessionLoading || !selectedEndpointAvailable"
              title="上传附件"
              @click="fileInput?.click()"
            >
              <template #icon><NIcon><AttachFileOutlined /></NIcon></template>
            </NButton>
            <NInput
              v-model:value="input"
              type="textarea"
              :autosize="{ minRows: 1, maxRows: 5 }"
              :disabled="loading || sessionLoading || !selectedEndpointAvailable"
              placeholder="描述你想让智能体完成的操作..."
              @keydown="handleKeydown"
            />
            <NButton circle type="primary" :disabled="!canSend" :loading="loading" @click="send(false)">
              <template #icon><NIcon><SendOutlined /></NIcon></template>
            </NButton>
          </footer>
          <div class="cloud-note">智能体独立使用上方所选云端端点并可能产生费用；附件二进制不发送给模型，必要的脱敏文本会发送到云端。</div>
        </main>
      </div>
    </section>
  </Transition>
</template>

<style scoped>
.agent-window {
  position: fixed;
  left: var(--agent-default-left, 24px);
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
  transition: width 0.24s var(--ease-out);
}

.agent-window.history-open { width: min(780px, calc(100vw - 48px)); }
.agent-window.minimized { width: min(390px, calc(100vw - 32px)); height: 58px; }

.agent-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex: 0 0 auto;
  min-height: 58px;
  padding: 0 12px 0 16px;
  border-bottom: 1px solid var(--border);
  background: linear-gradient(120deg, var(--accent-soft), transparent 60%);
  cursor: grab;
  touch-action: none;
  user-select: none;
}

.agent-window.dragging .agent-header { cursor: grabbing; }

.agent-title,
.window-actions,
.agent-title > div,
.execution-heading,
.composer,
.history-heading,
.history-actions,
.history-item-meta {
  display: flex;
  align-items: center;
}

.agent-title { gap: 10px; }
.window-actions { gap: 2px; }
.window-actions .active { color: var(--accent); background: var(--accent-soft); }
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

.agent-content {
  position: relative;
  display: flex;
  flex: 1;
  min-height: 0;
}

.agent-main {
  display: flex;
  flex: 1;
  min-width: 0;
  flex-direction: column;
}

.history-drawer {
  display: flex;
  flex: 0 0 240px;
  width: 240px;
  min-height: 0;
  flex-direction: column;
  border-right: 1px solid var(--border);
  background: color-mix(in srgb, var(--bg-surface) 98%, var(--accent-soft));
}

.history-heading {
  justify-content: space-between;
  flex: 0 0 auto;
  min-height: 52px;
  padding: 8px 8px 8px 12px;
  border-bottom: 1px solid var(--border);
}

.history-heading > div:first-child { display: flex; flex-direction: column; min-width: 0; }
.history-heading strong { color: var(--text-1); font-size: 12px; }
.history-heading span { color: var(--text-3); font-size: 9px; }
.history-actions { gap: 2px; }

.history-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 7px;
}

.history-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  min-height: 120px;
  color: var(--text-3);
  font-size: 11px;
}

.history-item {
  display: flex;
  width: 100%;
  align-items: stretch;
  gap: 5px;
  margin-bottom: 5px;
  padding: 8px 5px 8px 9px;
  border: 1px solid transparent;
  border-radius: 9px;
  background: transparent;
}

.history-item:hover { background: var(--bg-subtle); }
.history-item.active { border-color: var(--accent-border); background: var(--accent-soft); }
.history-item.disabled { cursor: default; opacity: 0.65; }

.history-item-open {
  display: flex;
  flex: 1;
  min-width: 0;
  padding: 0;
  border: 0;
  color: var(--text-2);
  background: transparent;
  text-align: left;
  cursor: pointer;
}

.history-item-open:disabled { cursor: default; }

.history-item-main {
  display: flex;
  flex: 1;
  min-width: 0;
  flex-direction: column;
}

.history-item-main strong,
.history-item-main small { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.history-item-main strong { color: var(--text-1); font-size: 11px; font-weight: 600; }
.history-item-main small { margin-top: 4px; color: var(--text-3); font-size: 9px; }
.history-item-meta { flex-direction: column; justify-content: space-between; flex: 0 0 auto; }
.history-item-meta time { color: var(--text-3); font-size: 8px; white-space: nowrap; }
.history-backdrop { display: none; }

.agent-context {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 8px;
  padding: 10px 12px 0;
}

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
  min-height: 260px;
  flex-direction: column;
  align-items: center;
  justify-content: center;
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
.bubble > p { margin: 0; overflow-wrap: anywhere; white-space: pre-wrap; font-size: 13px; line-height: 1.62; }
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
.confirmation-bar span { margin-top: 2px; color: var(--text-3); overflow-wrap: anywhere; font-size: 10px; }

.pending-attachments { display: flex; gap: 6px; overflow-x: auto; padding: 7px 12px 0; }
.attachment-chip {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
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

@media (max-width: 900px) {
  .agent-window.history-open { width: min(540px, calc(100vw - 48px)); }
  .history-drawer {
    position: absolute;
    inset: 0 auto 0 0;
    z-index: 3;
    width: min(280px, calc(100% - 48px));
    box-shadow: 14px 0 32px rgba(0, 0, 0, 0.3);
  }
  .history-backdrop {
    position: absolute;
    inset: 0;
    z-index: 2;
    display: block;
    padding: 0;
    border: 0;
    background: rgba(0, 0, 0, 0.38);
  }
}

@media (max-width: 600px) {
  .agent-context { grid-template-columns: minmax(0, 1fr); }
}

@media (max-width: 768px) {
  .agent-window,
  .agent-window.history-open {
    left: var(--agent-default-left, 10px);
    bottom: 10px;
    width: calc(100vw - 20px);
    height: calc(100vh - 80px);
  }
  .agent-window.minimized { width: calc(100vw - 20px); }
  .bubble { max-width: 94%; }
}
</style>
