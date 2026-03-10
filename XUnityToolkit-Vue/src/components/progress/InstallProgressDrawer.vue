<script setup lang="ts">
import {
  NDrawer,
  NDrawerContent,
  NProgress,
  NTimeline,
  NTimelineItem,
  NButton,
  NAlert,
} from 'naive-ui'
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { useInstallStore } from '@/stores/install'
import type { InstallStep } from '@/api/types'

const installStore = useInstallStore()

const installSteps: { key: InstallStep; label: string }[] = [
  { key: 'DetectingGame', label: '检测游戏' },
  { key: 'DownloadingBepInEx', label: '下载 BepInEx' },
  { key: 'InstallingBepInEx', label: '安装 BepInEx' },
  { key: 'DownloadingXUnity', label: '下载 XUnity.AutoTranslator' },
  { key: 'InstallingXUnity', label: '安装 XUnity.AutoTranslator' },
  { key: 'GeneratingConfig', label: '生成配置' },
  { key: 'ApplyingConfig', label: '应用最佳配置' },
]

const uninstallSteps: { key: InstallStep; label: string }[] = [
  { key: 'RemovingXUnity', label: '移除 XUnity.AutoTranslator' },
  { key: 'RemovingBepInEx', label: '移除 BepInEx' },
]

const isUninstalling = computed(() => installStore.operationType === 'uninstall')

const steps = computed(() => isUninstalling.value ? uninstallSteps : installSteps)
const stepOrder = computed(() => steps.value.map((s) => s.key))

function getStepType(stepKey: InstallStep): 'default' | 'success' | 'error' | 'info' {
  if (!installStore.status) return 'default'
  const current = installStore.status.step
  if (current === 'Failed') return 'error'
  if (current === 'Complete') return 'success'

  const currentIndex = stepOrder.value.indexOf(current)
  const stepIndex = stepOrder.value.indexOf(stepKey)

  if (stepIndex < currentIndex) return 'success'
  if (stepIndex === currentIndex) return 'info'
  return 'default'
}

const isComplete = computed(() => installStore.status?.step === 'Complete')
const isFailed = computed(() => installStore.status?.step === 'Failed')
const isRunning = computed(
  () => installStore.status && !isComplete.value && !isFailed.value && installStore.status.step !== 'Idle',
)

const isDownloadStep = computed(() =>
  installStore.status?.step === 'DownloadingBepInEx' ||
  installStore.status?.step === 'DownloadingXUnity'
)

const title = computed(() => {
  if (isComplete.value) return isUninstalling.value ? '卸载完成' : '安装完成'
  if (isFailed.value) return '操作失败'
  if (isUninstalling.value) return '卸载中...'
  return '安装中...'
})

const drawerWidth = ref(420)
function updateDrawerWidth() {
  drawerWidth.value = window.innerWidth <= 480 ? window.innerWidth : 420
}
onMounted(() => {
  updateDrawerWidth()
  window.addEventListener('resize', updateDrawerWidth)
})
onUnmounted(() => {
  window.removeEventListener('resize', updateDrawerWidth)
})
</script>

<template>
  <NDrawer v-model:show="installStore.isDrawerOpen" :width="drawerWidth" placement="right">
    <NDrawerContent :title="title" closable @close="installStore.closeDrawer()">
      <div class="progress-content">
        <!-- Step Timeline -->
        <div class="timeline-section">
          <NTimeline>
            <NTimelineItem
              v-for="(step, index) in steps"
              :key="step.key"
              :type="getStepType(step.key)"
              :title="step.label"
            />
          </NTimeline>
        </div>

        <!-- Progress Bar -->
        <div v-if="installStore.status" class="progress-section">
          <NProgress
            type="line"
            :percentage="installStore.status.progressPercent"
            :status="isFailed ? 'error' : isComplete ? 'success' : 'default'"
            :indicator-placement="'inside'"
            :height="20"
            :border-radius="10"
          />
        </div>

        <!-- Download Stats (speed + retry) -->
        <div v-if="isDownloadStep" class="download-stats">
          <span v-if="installStore.status?.downloadSpeed" class="speed-badge">
            {{ installStore.status.downloadSpeed }}
          </span>
          <span v-if="installStore.status?.retryMessage" class="retry-notice">
            {{ installStore.status.retryMessage }}
          </span>
        </div>

        <!-- Status Message -->
        <div v-if="installStore.status?.message" class="status-message">
          {{ installStore.status.message }}
        </div>

        <!-- Error Alert -->
        <NAlert v-if="isFailed && installStore.status?.error" type="error" style="margin-top: 16px">
          {{ installStore.status.error }}
        </NAlert>

        <!-- Success Alert -->
        <NAlert v-if="isComplete" type="success" style="margin-top: 16px">
          {{ isUninstalling ? '已成功卸载插件。' : '插件已成功安装！启动游戏即可使用。' }}
        </NAlert>
      </div>

      <template #footer>
        <div class="drawer-footer">
          <NButton v-if="isRunning" @click="installStore.cancel()" tertiary type="warning">
            取消
          </NButton>
          <NButton
            v-if="isComplete || isFailed"
            type="primary"
            @click="installStore.closeDrawer()"
          >
            关闭
          </NButton>
        </div>
      </template>
    </NDrawerContent>
  </NDrawer>
</template>

<style scoped>
.progress-content {
  animation: fadeIn 0.3s ease;
}

.timeline-section {
  margin-bottom: 24px;
}

.progress-section {
  margin-bottom: 8px;
}

.download-stats {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 8px;
  margin-bottom: 4px;
  min-height: 24px;
}

.speed-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 10px;
  background: var(--accent-soft);
  border: 1px solid var(--accent-border);
  border-radius: 100px;
  font-family: var(--font-mono);
  font-size: 12px;
  font-weight: 500;
  color: var(--accent);
  letter-spacing: 0.02em;
}

.retry-notice {
  font-family: var(--font-mono);
  font-size: 12px;
  color: var(--warning);
  opacity: 0.9;
}

.status-message {
  color: var(--text-3);
  line-height: 1.5;
  margin-top: 8px;
  font-family: var(--font-mono);
  font-size: 12px;
  padding: 8px 12px;
  background: var(--bg-subtle);
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
}

.drawer-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
