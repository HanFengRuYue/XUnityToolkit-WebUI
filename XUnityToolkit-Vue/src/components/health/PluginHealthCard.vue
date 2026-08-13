<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NIcon, NSpin, useMessage } from 'naive-ui'
import {
  AutoFixHighOutlined,
  ArticleOutlined,
  MonitorHeartOutlined,
  PlayArrowFilled,
} from '@vicons/material'
import { pluginHealthApi } from '@/api/games'
import type { PluginHealthReport } from '@/api/types'
import PluginDiagnosticReport from '@/components/health/PluginDiagnosticReport.vue'

const props = defineProps<{
  gameId: string
  initialReport?: PluginHealthReport | null
}>()

type ReportSource = 'passive' | 'analysis' | 'verification' | 'install'

const router = useRouter()
const message = useMessage()
const report = ref<PluginHealthReport | null>(null)
const reportSource = ref<ReportSource>('passive')
const loading = ref(false)
const analyzing = ref(false)
const verifying = ref(false)
const error = ref<string | null>(null)

const busy = computed(() => loading.value || analyzing.value || verifying.value)

const reportSourceLabel = computed(() => {
  switch (reportSource.value) {
    case 'analysis': return 'AI 智能诊断'
    case 'verification': return '本次启动并诊断'
    case 'install': return '安装流程本地验证'
    default: return '本地检查 / 缓存报告'
  }
})

const reportMeta = computed(() => {
  if (!report.value) return ''
  const checked = formatDateTime(report.value.checkedAt)
  const log = report.value.logLastModified ? formatDateTime(report.value.logLastModified) : '未发现'
  return `来源：${reportSourceLabel.value} · 检查时间：${checked} · 日志时间：${log}`
})

async function loadPassiveCheck() {
  loading.value = true
  error.value = null
  try {
    report.value = await pluginHealthApi.check(props.gameId)
    reportSource.value = 'passive'
  } catch (e: unknown) {
    if (!report.value) error.value = errorMessage(e, '读取插件状态失败')
  } finally {
    loading.value = false
  }
}

async function analyzeCurrentState() {
  analyzing.value = true
  error.value = null
  try {
    report.value = await pluginHealthApi.analyze(props.gameId)
    reportSource.value = 'analysis'
    notifyResult(report.value)
  } catch (e: unknown) {
    error.value = errorMessage(e, 'AI 智能诊断失败')
    message.error(error.value)
  } finally {
    analyzing.value = false
  }
}

async function verifyAndAnalyze() {
  verifying.value = true
  error.value = null
  try {
    report.value = await pluginHealthApi.verify(props.gameId)
    reportSource.value = 'verification'
    notifyResult(report.value)
  } catch (e: unknown) {
    error.value = errorMessage(e, '启动验证失败，请确认游戏可执行文件可以正常启动')
    message.error(error.value)
  } finally {
    verifying.value = false
  }
}

function notifyResult(value: PluginHealthReport) {
  if (value.analysisState === 'Completed') {
    if (value.overall === 'Healthy') message.success('智能诊断完成，已取得本次运行的健康证据')
    else message.warning('智能诊断完成，请查看报告中的关注项')
    return
  }
  message.warning(value.analysisMessage || '本地检查已完成，但 AI 诊断未完成')
}

function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback
}

function formatDateTime(value?: string | null) {
  if (!value) return '未知'
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? '未知'
    : date.toLocaleString('zh-CN', { hour12: false })
}

function openFullReport() {
  router.push(`/games/${props.gameId}/bepinex-log`)
}

onMounted(() => {
  if (props.initialReport) {
    report.value = props.initialReport
    reportSource.value = 'install'
  } else {
    loadPassiveCheck()
  }
})

watch(() => props.initialReport, (value) => {
  if (value) {
    report.value = value
    reportSource.value = 'install'
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
        插件智能诊断
      </h2>
      <div class="header-actions">
        <NButton size="small" :loading="analyzing" :disabled="busy && !analyzing" @click="analyzeCurrentState">
          <template #icon><NIcon :size="14"><AutoFixHighOutlined /></NIcon></template>
          {{ analyzing ? '正在诊断...' : 'AI 智能诊断' }}
        </NButton>
        <NButton size="small" :loading="verifying" :disabled="busy && !verifying" @click="verifyAndAnalyze">
          <template #icon><NIcon :size="14"><PlayArrowFilled /></NIcon></template>
          {{ verifying ? '正在启动并诊断...' : '启动并智能诊断' }}
        </NButton>
      </div>
    </div>

    <NAlert type="info" :bordered="false" class="cost-hint">
      打开页面只执行本地只读检查，不调用模型。点击诊断会进行两阶段模型调用，可能产生 API 费用。
    </NAlert>

    <div v-if="loading && !report" class="loading-state">
      <NSpin size="small" />
      <span>正在收集本地插件事实...</span>
    </div>

    <template v-else-if="report">
      <div class="report-meta">{{ reportMeta }}</div>
      <NAlert v-if="error" type="error" closable class="card-alert" @close="error = null">
        {{ error }}
      </NAlert>
      <NAlert v-if="verifying" type="info" :bordered="false" class="card-alert">
        正在启动游戏获取本次运行日志与连通性证据，完成后会继续执行 AI 智能诊断；游戏随后自动关闭。
      </NAlert>
      <NAlert v-else-if="analyzing" type="info" :bordered="false" class="card-alert">
        正在让 AI 先选择关键资料，再根据脱敏证据生成结构化报告。
      </NAlert>

      <PluginDiagnosticReport :report="report" compact />

      <div class="report-actions">
        <NButton text type="primary" @click="openFullReport">
          <template #icon><NIcon><ArticleOutlined /></NIcon></template>
          查看日志与完整诊断报告
        </NButton>
      </div>
    </template>

    <NAlert v-else-if="error" type="error">{{ error }}</NAlert>
  </div>
</template>

<style scoped>
.section-icon.health { color: var(--accent); }

.cost-hint,
.card-alert {
  margin-bottom: 12px;
  font-size: 12px;
}

.report-meta {
  margin: -2px 0 12px;
  color: var(--text-3);
  font-size: 11px;
  line-height: 1.5;
}

.loading-state {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 18px 0;
  color: var(--text-3);
  font-size: 13px;
}

.report-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
}

@media (max-width: 900px) {
  .section-header {
    align-items: flex-start;
    gap: 10px;
  }

  .header-actions {
    flex-wrap: wrap;
    justify-content: flex-end;
  }
}
</style>
