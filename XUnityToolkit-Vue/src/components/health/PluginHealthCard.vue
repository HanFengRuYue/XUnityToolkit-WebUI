<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { NAlert, NButton, NIcon, NSpin, useMessage } from 'naive-ui'
import {
  AutoFixHighOutlined,
  ArticleOutlined,
  BuildOutlined,
  MonitorHeartOutlined,
  PlayArrowFilled,
} from '@vicons/material'
import { pluginHealthApi } from '@/api/games'
import type { PluginAutoRepairResult, PluginHealthReport } from '@/api/types'
import PluginDiagnosticReport from '@/components/health/PluginDiagnosticReport.vue'

const props = defineProps<{
  gameId: string
  initialReport?: PluginHealthReport | null
}>()

type ReportSource = 'passive' | 'analysis' | 'repair' | 'verification' | 'install'

const router = useRouter()
const message = useMessage()
const report = ref<PluginHealthReport | null>(null)
const reportSource = ref<ReportSource>('passive')
const loading = ref(false)
const analyzing = ref(false)
const repairing = ref(false)
const verifying = ref(false)
const repairResult = ref<PluginAutoRepairResult | null>(null)
const error = ref<string | null>(null)

const busy = computed(() => loading.value || analyzing.value || repairing.value || verifying.value)

const reportSourceLabel = computed(() => {
  switch (reportSource.value) {
    case 'analysis': return 'AI 智能诊断'
    case 'repair': return 'AI 全自动修复与复检'
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

async function repairAutomatically() {
  repairing.value = true
  repairResult.value = null
  error.value = null
  try {
    repairResult.value = await pluginHealthApi.repair(props.gameId)
    report.value = repairResult.value.after
    reportSource.value = 'repair'
    const completed = repairResult.value.actions.filter(action => action.state === 'Completed').length
    const failed = repairResult.value.actions.filter(action => action.state === 'Failed').length
    if (failed > 0) message.warning(`自动修复完成：${completed} 项成功，${failed} 项失败`)
    else if (completed > 0) message.success(`自动修复并复检完成，共修复 ${completed} 项`)
    else message.info('诊断完成，没有发现可安全自动执行的修复项')
  } catch (e: unknown) {
    error.value = errorMessage(e, '插件全自动修复失败')
    message.error(error.value)
  } finally {
    repairing.value = false
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
        工具箱智能体
      </h2>
      <div class="header-actions">
        <NButton size="small" :loading="analyzing" :disabled="busy && !analyzing" @click="analyzeCurrentState">
          <template #icon><NIcon :size="14"><AutoFixHighOutlined /></NIcon></template>
          {{ analyzing ? '正在诊断...' : '智能诊断' }}
        </NButton>
        <NButton v-if="report?.repairAvailable" type="primary" size="small" :loading="repairing" :disabled="busy && !repairing" @click="repairAutomatically">
          <template #icon><NIcon :size="14"><BuildOutlined /></NIcon></template>
          {{ repairing ? '正在自动修复...' : 'AI 全自动修复' }}
        </NButton>
        <NButton size="small" :loading="verifying" :disabled="busy && !verifying" @click="verifyAndAnalyze">
          <template #icon><NIcon :size="14"><PlayArrowFilled /></NIcon></template>
          {{ verifying ? '正在启动并诊断...' : '启动并智能诊断' }}
        </NButton>
      </div>
    </div>

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
      <NAlert v-else-if="repairing" type="warning" :bordered="false" class="card-alert">
        正在使用云端 AI 诊断、规划受限修复，工具箱会先备份目标文件，执行后再重新诊断。请保持游戏关闭。
      </NAlert>
      <NAlert v-else-if="analyzing" type="info" :bordered="false" class="card-alert">
        正在让工具箱智能体先选择关键资料，再根据所选证据生成结构化报告。
      </NAlert>

      <div v-if="repairResult" class="repair-result">
        <strong>{{ repairResult.summary }}</strong>
        <div v-if="repairResult.actions.length" class="repair-actions">
          <div v-for="action in repairResult.actions" :key="action.id" :class="`repair-${action.state.toLowerCase()}`">
            <span>{{ action.description }}</span>
            <small>{{ action.message }}</small>
          </div>
        </div>
      </div>

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

.repair-result {
  margin-bottom: 12px;
  padding: 10px 12px;
  border: 1px solid var(--accent-border);
  border-radius: var(--radius-md);
  background: var(--accent-soft);
}

.repair-result > strong { color: var(--text-1); font-size: 12px; }
.repair-actions { display: flex; flex-direction: column; gap: 6px; margin-top: 8px; }
.repair-actions > div { padding-left: 8px; border-left: 2px solid var(--text-3); }
.repair-actions span,
.repair-actions small { display: block; }
.repair-actions span { color: var(--text-2); font-size: 11px; }
.repair-actions small { margin-top: 2px; color: var(--text-3); font-size: 10px; line-height: 1.45; }
.repair-actions .repair-completed { border-left-color: var(--success); }
.repair-actions .repair-failed { border-left-color: var(--danger); }
.repair-actions .repair-skipped { border-left-color: var(--warning); }

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
