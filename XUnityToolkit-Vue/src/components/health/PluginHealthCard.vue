<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue'
import { NIcon, NButton, NAlert, NSpin, useMessage } from 'naive-ui'
import {
  CheckCircleOutlined,
  ErrorOutlineOutlined,
  WarningAmberOutlined,
  HelpOutlineOutlined,
  PlayArrowFilled,
  MonitorHeartOutlined,
} from '@vicons/material'
import { pluginHealthApi } from '@/api/games'
import type { PluginHealthReport, HealthStatus, HealthCheckItem } from '@/api/types'

const props = defineProps<{
  gameId: string
  initialReport?: PluginHealthReport | null
}>()

type ReportSource = 'passive' | 'verification' | 'install'
type ReportFreshness = 'current' | 'previous'

const report = ref<PluginHealthReport | null>(null)
const reportSource = ref<ReportSource>('passive')
const reportFreshness = ref<ReportFreshness>('current')
const loading = ref(false)
const verifying = ref(false)
const error = ref<string | null>(null)
const message = useMessage()

function problemItemOrder(item: HealthCheckItem) {
  switch (item.id) {
    case 'logErrors':
      return 2
    case 'toolboxAiState':
      return 3
    default:
      return 1
  }
}

// Only show problematic items (non-Healthy)
const problemItems = computed<HealthCheckItem[]>(() =>
  (report.value?.checks ?? [])
    .map((item, index) => ({ item, index }))
    .filter(({ item }) => item.status !== 'Healthy')
    .sort((a, b) => {
      const orderDiff = problemItemOrder(a.item) - problemItemOrder(b.item)
      return orderDiff !== 0 ? orderDiff : a.index - b.index
    })
    .map(({ item }) => item)
)

const allHealthy = computed(() =>
  report.value !== null && report.value.overall === 'Healthy'
)

const reportSourceLabel = computed(() => {
  switch (reportSource.value) {
    case 'verification':
      return '本次启动验证'
    case 'install':
      return '安装流程验证'
    default:
      return '静态检查 / 历史日志'
  }
})

const reportMetaText = computed(() => {
  if (!report.value) return ''
  const parts = [`来源：${reportSourceLabel.value}`, `检查时间：${formatDateTime(report.value.checkedAt)}`]
  parts.push(report.value.logLastModified
    ? `日志时间：${formatDateTime(report.value.logLastModified)}`
    : '日志时间：未发现运行日志')
  return parts.join(' · ')
})

async function loadPassiveCheck() {
  loading.value = true
  try {
    report.value = await pluginHealthApi.check(props.gameId)
    reportSource.value = 'passive'
    reportFreshness.value = 'current'
  } catch {
    // Silent fail for passive check — user can use verify button
  } finally {
    loading.value = false
  }
}

async function verifyInstallation() {
  verifying.value = true
  error.value = null
  if (report.value) {
    reportFreshness.value = 'previous'
  }
  try {
    report.value = await pluginHealthApi.verify(props.gameId)
    reportSource.value = 'verification'
    reportFreshness.value = 'current'
    if (report.value.overall === 'Healthy') {
      message.success('验证通过，所有检查项均正常')
    } else {
      message.warning('验证完成，发现需关注项，结果仅供参考')
    }
  } catch {
    error.value = '验证安装失败，请确认游戏可执行文件可以正常启动'
    message.error('验证安装失败')
  } finally {
    verifying.value = false
  }
}

function formatDateTime(value?: string | null) {
  if (!value) return '未知'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '未知'
  return date.toLocaleString('zh-CN', { hour12: false })
}

function displayItemLabel(item: HealthCheckItem) {
  if (item.id === 'toolboxAiState') {
    return `工具箱环境：${item.label}`
  }
  return item.label
}

function displayItemDetail(item: HealthCheckItem) {
  if (item.id === 'toolboxAiState' && item.detail) {
    return `${item.detail}（这不代表 BepInEx/XUnity 插件安装损坏。）`
  }
  return item.detail
}

function statusText(status: HealthStatus) {
  switch (status) {
    case 'Error': return '错误'
    case 'Warning': return '需关注'
    case 'Unknown': return '参考信息'
    default: return '正常'
  }
}

function statusIcon(status: HealthStatus) {
  switch (status) {
    case 'Healthy': return CheckCircleOutlined
    case 'Warning': return WarningAmberOutlined
    case 'Error': return ErrorOutlineOutlined
    default: return HelpOutlineOutlined
  }
}

function statusClass(status: HealthStatus) {
  switch (status) {
    case 'Healthy': return 'status-healthy'
    case 'Warning': return 'status-warning'
    case 'Error': return 'status-error'
    default: return 'status-unknown'
  }
}

onMounted(() => {
  if (props.initialReport) {
    report.value = props.initialReport
    reportSource.value = 'install'
    reportFreshness.value = 'current'
  } else {
    loadPassiveCheck()
  }
})

watch(() => props.initialReport, (newReport) => {
  if (newReport) {
    report.value = newReport
    reportSource.value = 'install'
    reportFreshness.value = 'current'
  } else {
    loadPassiveCheck()
  }
})
</script>

<template>
  <div class="section-card health-card">
    <div class="section-header">
      <h2 class="section-title">
        <span class="section-icon health">
          <NIcon :size="16"><MonitorHeartOutlined /></NIcon>
        </span>
        插件健康状态
      </h2>
      <div class="header-actions">
        <NButton
          size="small"
          :loading="verifying"
          :disabled="loading"
          @click="verifyInstallation"
        >
          <template #icon><NIcon :size="14"><PlayArrowFilled /></NIcon></template>
          {{ verifying ? '正在验证...' : '启动验证' }}
        </NButton>
      </div>
    </div>

    <!-- Initial loading -->
    <div v-if="loading && !report" class="loading-state">
      <NSpin size="small" />
      <span>正在检查插件状态...</span>
    </div>

    <!-- Report loaded -->
    <template v-else-if="report">
      <!-- Reference hint -->
      <NAlert type="info" :bordered="false" class="card-alert reference-hint">
        检查结果仅供参考，以实际运行情况为准。静态检查会结合本地文件和历史日志，不等同于本次运行验证。
      </NAlert>

      <div class="report-meta">
        {{ reportMetaText }}
      </div>

      <!-- Error from verify -->
      <NAlert v-if="error" type="error" closable class="card-alert" @close="error = null">
        {{ error }}
      </NAlert>

      <!-- Verifying hint -->
      <NAlert v-if="verifying" type="info" :bordered="false" class="card-alert">
        正在启动游戏验证插件状态，游戏将在检测完成后自动关闭...
      </NAlert>

      <NAlert
        v-if="reportFreshness === 'previous'"
        type="warning"
        :bordered="false"
        class="card-alert"
      >
        {{ verifying ? '正在生成新的验证结果，下方仍为上次结果。' : '下方为上次结果，当前验证未生成新的报告。' }}
      </NAlert>

      <!-- All healthy -->
      <div v-if="allHealthy && !verifying" class="overall-status status-healthy">
        <NIcon :size="18"><CheckCircleOutlined /></NIcon>
        <span>所有检查项均正常</span>
      </div>

      <!-- Game never run (no log) -->
      <div v-else-if="report.gameNeverRun && problemItems.length === 0 && !verifying" class="overall-status status-unknown">
        <NIcon :size="18"><HelpOutlineOutlined /></NIcon>
        <span>游戏尚未运行，请点击「启动验证」检查插件状态</span>
      </div>

      <!-- Problem items only -->
      <div v-if="problemItems.length > 0" class="check-group">
        <div
          v-for="item in problemItems"
          :key="item.id"
          class="check-item"
          :class="[statusClass(item.status), { 'has-details': item.details?.length }]"
        >
          <div class="check-item-main">
            <NIcon :size="16" class="check-icon"><component :is="statusIcon(item.status)" /></NIcon>
            <span class="check-label">{{ displayItemLabel(item) }}</span>
            <span class="check-status">{{ statusText(item.status) }}</span>
            <span v-if="displayItemDetail(item)" class="check-detail">{{ displayItemDetail(item) }}</span>
          </div>
          <ul v-if="item.details?.length" class="check-detail-list">
            <li v-for="(d, i) in item.details" :key="i" class="detail-entry">
              <span class="detail-category">{{ d.category }}</span>
              <span class="detail-excerpt">{{ d.excerpt }}</span>
              <span v-if="d.suggestion" class="detail-suggestion">{{ d.suggestion }}</span>
            </li>
          </ul>
        </div>
      </div>
    </template>

    <!-- Error without report -->
    <NAlert v-else-if="error" type="error">
      {{ error }}
    </NAlert>
  </div>
</template>

<style scoped>
.section-icon.health {
  color: var(--accent);
}

.card-alert {
  margin-bottom: 12px;
}

.reference-hint {
  font-size: 12px;
  opacity: 0.75;
}

.report-meta {
  margin: -4px 0 12px;
  color: var(--text-3);
  font-size: 12px;
  line-height: 1.5;
}

.overall-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 14px;
  border-radius: 8px;
  font-size: 14px;
  font-weight: 500;
}

.overall-status.status-healthy {
  background: color-mix(in srgb, var(--success) 10%, transparent);
  color: var(--success);
}

.overall-status.status-unknown {
  background: color-mix(in srgb, var(--text-3) 10%, transparent);
  color: var(--text-3);
}

.check-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.check-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 6px 10px;
  border-radius: 6px;
  font-size: 13px;
}

.check-item.has-details {
  flex-direction: column;
  gap: 0;
}

.check-item-main {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  flex-wrap: wrap;
}

.check-icon {
  flex-shrink: 0;
  margin-top: 1px;
}

.check-detail-list {
  list-style: none;
  padding: 4px 0 2px 24px;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.detail-entry {
  display: flex;
  flex-direction: column;
  gap: 1px;
  font-size: 12px;
  line-height: 1.5;
  padding: 3px 0;
  border-bottom: 1px solid color-mix(in srgb, var(--border) 50%, transparent);
}

.detail-entry:last-child {
  border-bottom: none;
}

.detail-category {
  font-weight: 600;
  color: var(--text-2);
  white-space: nowrap;
}

.detail-excerpt {
  color: var(--text-3);
  font-family: var(--font-mono);
  font-size: 11px;
  word-break: break-all;
}

.detail-suggestion {
  color: color-mix(in srgb, var(--accent) 80%, var(--text-3));
  font-size: 11px;
}

.check-item.status-warning .check-icon {
  color: var(--warning);
}

.check-item.status-error .check-icon {
  color: var(--danger);
}

.check-item.status-unknown .check-icon {
  color: var(--text-3);
}

.check-label {
  color: var(--text-1);
  white-space: nowrap;
}

.check-status {
  flex-shrink: 0;
  padding: 1px 6px;
  border-radius: 999px;
  background: color-mix(in srgb, var(--text-3) 12%, transparent);
  color: var(--text-3);
  font-size: 11px;
  line-height: 1.5;
  white-space: nowrap;
}

.check-item.status-warning .check-status {
  background: color-mix(in srgb, var(--warning) 12%, transparent);
  color: color-mix(in srgb, var(--warning) 80%, var(--text-2));
}

.check-item.status-error .check-status {
  background: color-mix(in srgb, var(--danger) 12%, transparent);
  color: color-mix(in srgb, var(--danger) 80%, var(--text-2));
}

.check-detail {
  flex: 1 1 240px;
  color: var(--text-3);
  font-size: 12px;
  margin-left: 4px;
}

.check-item.status-error .check-detail {
  color: color-mix(in srgb, var(--danger) 80%, var(--text-3));
}

.check-item.status-warning .check-detail {
  color: color-mix(in srgb, var(--warning) 70%, var(--text-3));
}

.loading-state {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 16px 0;
  color: var(--text-3);
  font-size: 13px;
}
</style>
