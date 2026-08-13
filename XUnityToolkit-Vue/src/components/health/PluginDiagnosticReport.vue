<script setup lang="ts">
import { computed } from 'vue'
import { NAlert, NIcon, NTag } from 'naive-ui'
import {
  CheckCircleOutlined,
  ErrorOutlineOutlined,
  HelpOutlineOutlined,
  InsertDriveFileOutlined,
  WarningAmberOutlined,
} from '@vicons/material'
import type {
  DiagnosticConfidence,
  DiagnosticSeverity,
  HealthStatus,
  PluginDiagnosticFinding,
  PluginHealthReport,
} from '@/api/types'

const props = withDefaults(defineProps<{
  report: PluginHealthReport
  compact?: boolean
}>(), {
  compact: false,
})

const visibleFindings = computed(() => {
  const findings = [...(props.report.analysis?.findings ?? [])]
  findings.sort((left, right) => severityWeight(right.severity) - severityWeight(left.severity))
  return props.compact ? findings.slice(0, 3) : findings
})

const objectiveItems = computed(() => {
  const items = [...props.report.checks]
  items.sort((left, right) => statusWeight(right.status) - statusWeight(left.status))
  return props.compact ? items.slice(0, 4) : items
})

const hiddenFindingCount = computed(() =>
  Math.max(0, (props.report.analysis?.findings.length ?? 0) - visibleFindings.value.length),
)

const hiddenObjectiveCount = computed(() =>
  Math.max(0, props.report.checks.length - objectiveItems.value.length),
)

function severityWeight(severity: DiagnosticSeverity) {
  if (severity === 'Error') return 3
  if (severity === 'Warning') return 2
  return 1
}

function statusWeight(status: HealthStatus) {
  if (status === 'Error') return 4
  if (status === 'Warning') return 3
  if (status === 'Unknown') return 2
  return 1
}

function statusLabel(status: HealthStatus) {
  switch (status) {
    case 'Healthy': return '健康'
    case 'Warning': return '需关注'
    case 'Error': return '错误'
    default: return '尚未确认'
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

function stateAlertType() {
  switch (props.report.analysisState) {
    case 'Failed': return 'error'
    case 'Stale':
    case 'Unavailable': return 'warning'
    default: return 'info'
  }
}

function stateMessage() {
  if (props.report.analysisMessage) return props.report.analysisMessage
  switch (props.report.analysisState) {
    case 'NotRun': return '尚未运行 AI 智能诊断。当前仅展示本地可验证事实。'
    case 'Running': return '正在执行两阶段 AI 智能诊断。'
    case 'Completed': return 'AI 智能诊断已完成。'
    case 'Stale': return '诊断输入已经变化，下方 AI 报告仅供参考。'
    case 'Unavailable': return '当前没有可用于智能诊断的模型端点。'
    case 'Failed': return 'AI 智能诊断失败，请检查模型端点后重试。'
  }
}

function severityType(severity: DiagnosticSeverity) {
  if (severity === 'Error') return 'error'
  if (severity === 'Warning') return 'warning'
  return 'info'
}

function severityLabel(severity: DiagnosticSeverity) {
  if (severity === 'Error') return '错误'
  if (severity === 'Warning') return '需关注'
  return '信息'
}

function confidenceLabel(confidence: DiagnosticConfidence) {
  if (confidence === 'High') return '高置信度'
  if (confidence === 'Medium') return '中置信度'
  return '低置信度'
}

function formatDateTime(value?: string | null) {
  if (!value) return '未知'
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? '未知'
    : date.toLocaleString('zh-CN', { hour12: false })
}
</script>

<template>
  <div class="diagnostic-report" :class="{ compact }">
    <div class="overall-row" :class="`status-${report.overall.toLowerCase()}`">
      <NIcon :size="19"><component :is="statusIcon(report.overall)" /></NIcon>
      <div class="overall-copy">
        <span class="overall-title">{{ statusLabel(report.overall) }}</span>
        <span class="overall-detail">
          本地检查：{{ statusLabel(report.objectiveOverall) }}
          <template v-if="report.freshRunVerified"> · 已取得本次启动与连通性证据</template>
        </span>
      </div>
    </div>

    <NAlert
      v-if="report.analysisState !== 'Completed'"
      :type="stateAlertType()"
      :bordered="false"
      class="state-alert"
    >
      {{ stateMessage() }}
    </NAlert>

    <section v-if="report.analysis" class="analysis-section">
      <div class="analysis-heading">
        <div>
          <h3>AI 诊断结论</h3>
          <p>{{ report.analysis.summary }}</p>
        </div>
        <div class="analysis-meta">
          {{ report.analysis.endpointName }} · {{ formatDateTime(report.analysis.analyzedAt) }}
        </div>
      </div>

      <div v-if="visibleFindings.length" class="finding-list">
        <article
          v-for="finding in visibleFindings"
          :key="finding.id"
          class="finding-card"
          :class="`finding-${finding.severity.toLowerCase()}`"
        >
          <div class="finding-header">
            <div class="finding-tags">
              <NTag size="small" :type="severityType(finding.severity)" :bordered="false">
                {{ severityLabel(finding.severity) }}
              </NTag>
              <NTag size="small" :bordered="false">{{ finding.category }}</NTag>
              <span class="confidence">{{ confidenceLabel(finding.confidence) }}</span>
            </div>
            <h4>{{ finding.title }}</h4>
          </div>
          <p class="finding-explanation">{{ finding.explanation }}</p>

          <div v-if="finding.evidence.length" class="evidence-list">
            <div v-for="(evidence, index) in finding.evidence" :key="`${evidence.artifactId}-${index}`" class="evidence-item">
              <div class="evidence-source">
                <NIcon :size="14"><InsertDriveFileOutlined /></NIcon>
                <span>{{ evidence.relativePath || evidence.label }}</span>
                <span>资料第 {{ evidence.startLine }}–{{ evidence.endLine }} 行</span>
              </div>
              <pre>{{ evidence.excerpt }}</pre>
            </div>
          </div>

          <ul v-if="finding.suggestedActions.length" class="action-list">
            <li v-for="(action, index) in finding.suggestedActions" :key="index">{{ action }}</li>
          </ul>
        </article>
      </div>

      <div v-else class="no-findings">
        <NIcon :size="18"><CheckCircleOutlined /></NIcon>
        <span>所审阅资料中未发现有明确证据支持的插件异常。</span>
      </div>

      <p v-if="hiddenFindingCount" class="hidden-hint">
        另有 {{ hiddenFindingCount }} 项，请在 BepInEx 日志页查看完整报告。
      </p>

      <details v-if="!compact && report.analysis.reviewedArtifacts.length" class="reviewed-artifacts">
        <summary>AI 实际审阅的关键资料（{{ report.analysis.reviewedArtifacts.length }} 项）</summary>
        <ul>
          <li v-for="artifact in report.analysis.reviewedArtifacts" :key="artifact.id">
            <div class="artifact-title">
              <span>{{ artifact.relativePath || artifact.label }}</span>
              <NTag v-if="artifact.truncated" size="tiny" type="warning" :bordered="false">已截断</NTag>
            </div>
            <span v-if="artifact.selectionReason" class="artifact-reason">{{ artifact.selectionReason }}</span>
          </li>
        </ul>
      </details>
    </section>

    <section v-if="objectiveItems.length" class="objective-section">
      <h3>本地确定性检查</h3>
      <div class="objective-list">
        <div v-for="item in objectiveItems" :key="item.id" class="objective-item">
          <NIcon :size="15" :class="`status-${item.status.toLowerCase()}`">
            <component :is="statusIcon(item.status)" />
          </NIcon>
          <div>
            <strong>{{ item.label }}</strong>
            <span v-if="item.detail">{{ item.detail }}</span>
          </div>
        </div>
      </div>
      <p v-if="hiddenObjectiveCount" class="hidden-hint">
        另有 {{ hiddenObjectiveCount }} 项本地事实，请在 BepInEx 日志页查看完整报告。
      </p>
    </section>
  </div>
</template>

<style scoped>
.diagnostic-report {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
}

.overall-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
}

.overall-row.status-healthy { color: var(--success); }
.overall-row.status-warning { color: var(--warning); }
.overall-row.status-error { color: var(--danger); }
.overall-row.status-unknown { color: var(--text-3); }

.overall-copy {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.overall-title {
  color: currentColor;
  font-size: 14px;
  font-weight: 650;
}

.overall-detail,
.analysis-meta,
.confidence,
.hidden-hint,
.artifact-reason {
  color: var(--text-3);
  font-size: 11px;
}

.state-alert { font-size: 12px; }

.analysis-section,
.objective-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
}

.analysis-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.analysis-heading > div:first-child { min-width: 0; }

.analysis-heading h3,
.objective-section h3 {
  margin: 0;
  color: var(--text-1);
  font-size: 13px;
}

.analysis-heading p {
  margin: 4px 0 0;
  color: var(--text-2);
  font-size: 13px;
  line-height: 1.65;
}

.analysis-meta {
  flex-shrink: 0;
  text-align: right;
}

.finding-list,
.objective-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.finding-card {
  padding: 12px;
  border: 1px solid var(--border);
  border-left-width: 3px;
  border-radius: var(--radius-md);
  background: var(--bg-subtle);
  min-width: 0;
}

.finding-card.finding-error { border-left-color: var(--danger); }
.finding-card.finding-warning { border-left-color: var(--warning); }
.finding-card.finding-info { border-left-color: var(--accent); }

.finding-header {
  display: flex;
  flex-direction: column;
  gap: 7px;
}

.finding-tags {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.finding-header h4 {
  margin: 0;
  color: var(--text-1);
  font-size: 13px;
}

.finding-explanation {
  margin: 7px 0 0;
  color: var(--text-2);
  font-size: 12px;
  line-height: 1.65;
}

.evidence-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin-top: 9px;
}

.evidence-item {
  min-width: 0;
  padding: 8px 9px;
  border-radius: 6px;
  background: color-mix(in srgb, var(--bg) 70%, transparent);
}

.evidence-source {
  display: flex;
  align-items: center;
  gap: 5px;
  color: var(--text-3);
  font-size: 10px;
  overflow-wrap: anywhere;
}

.evidence-source span:last-child {
  margin-left: auto;
  white-space: nowrap;
}

.evidence-item pre {
  margin: 5px 0 0;
  color: var(--text-2);
  font-family: var(--font-mono);
  font-size: 11px;
  line-height: 1.55;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.action-list {
  margin: 9px 0 0;
  padding-left: 18px;
  color: var(--text-2);
  font-size: 12px;
  line-height: 1.6;
}

.no-findings {
  display: flex;
  align-items: center;
  gap: 7px;
  padding: 10px 12px;
  border-radius: var(--radius-md);
  color: var(--success);
  background: color-mix(in srgb, var(--success) 9%, transparent);
  font-size: 12px;
}

.reviewed-artifacts {
  border-top: 1px solid var(--border);
  padding-top: 10px;
  color: var(--text-2);
  font-size: 12px;
}

.reviewed-artifacts summary {
  cursor: pointer;
  color: var(--text-2);
}

.reviewed-artifacts ul {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 7px 14px;
  margin: 10px 0 0;
  padding: 0;
  list-style: none;
}

.artifact-title {
  display: flex;
  align-items: center;
  gap: 6px;
  overflow-wrap: anywhere;
}

.artifact-reason {
  display: block;
  margin-top: 2px;
}

.objective-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 7px;
  background: var(--bg-subtle);
}

.objective-item > div {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.objective-item strong {
  color: var(--text-1);
  font-size: 12px;
}

.objective-item span {
  color: var(--text-3);
  font-size: 11px;
  line-height: 1.5;
  overflow-wrap: anywhere;
}

.status-healthy { color: var(--success); }
.status-warning { color: var(--warning); }
.status-error { color: var(--danger); }
.status-unknown { color: var(--text-3); }

.compact .analysis-heading {
  display: block;
}

.compact .analysis-meta {
  margin-top: 5px;
  text-align: left;
}

@media (max-width: 900px) {
  .analysis-heading {
    display: block;
  }

  .analysis-meta {
    margin-top: 5px;
    text-align: left;
  }
}
</style>
