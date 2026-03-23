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

const report = ref<PluginHealthReport | null>(null)
const loading = ref(false)
const verifying = ref(false)
const error = ref<string | null>(null)
const message = useMessage()

// Only show problematic items (non-Healthy)
const problemItems = computed<HealthCheckItem[]>(() =>
  report.value?.checks.filter(c => c.status !== 'Healthy') ?? []
)

const allHealthy = computed(() =>
  report.value !== null && report.value.overall === 'Healthy'
)

async function loadPassiveCheck() {
  loading.value = true
  try {
    report.value = await pluginHealthApi.check(props.gameId)
  } catch {
    // Silent fail for passive check — user can use verify button
  } finally {
    loading.value = false
  }
}

async function verifyInstallation() {
  verifying.value = true
  error.value = null
  try {
    report.value = await pluginHealthApi.verify(props.gameId)
    if (report.value.overall === 'Healthy') {
      message.success('验证通过，所有检查项均正常')
    } else {
      message.warning('验证完成，发现问题')
    }
  } catch {
    error.value = '验证安装失败，请确认游戏可执行文件可以正常启动'
    message.error('验证安装失败')
  } finally {
    verifying.value = false
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
  } else {
    loadPassiveCheck()
  }
})

watch(() => props.initialReport, (newReport) => {
  if (newReport) {
    report.value = newReport
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
      <!-- Error from verify -->
      <NAlert v-if="error" type="error" closable class="card-alert" @close="error = null">
        {{ error }}
      </NAlert>

      <!-- Verifying hint -->
      <NAlert v-if="verifying" type="info" :bordered="false" class="card-alert">
        正在启动游戏验证插件状态，游戏将在检测完成后自动关闭...
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
          :class="statusClass(item.status)"
        >
          <NIcon :size="16" class="check-icon"><component :is="statusIcon(item.status)" /></NIcon>
          <span class="check-label">{{ item.label }}</span>
          <span v-if="item.detail" class="check-detail">{{ item.detail }}</span>
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

.check-icon {
  flex-shrink: 0;
  margin-top: 1px;
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

.check-detail {
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
