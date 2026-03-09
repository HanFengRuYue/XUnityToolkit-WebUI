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
import { computed } from 'vue'
import { useInstallStore } from '@/stores/install'
import type { InstallStep } from '@/api/types'

const installStore = useInstallStore()

const installSteps: { key: InstallStep; label: string }[] = [
  { key: 'DetectingGame', label: '检测游戏' },
  { key: 'DownloadingBepInEx', label: '下载 BepInEx' },
  { key: 'InstallingBepInEx', label: '安装 BepInEx' },
  { key: 'DownloadingXUnity', label: '下载 XUnity.AutoTranslator' },
  { key: 'InstallingXUnity', label: '安装 XUnity.AutoTranslator' },
  { key: 'WritingConfig', label: '写入配置' },
]

const uninstallSteps: { key: InstallStep; label: string }[] = [
  { key: 'RemovingXUnity', label: '移除 XUnity.AutoTranslator' },
  { key: 'RemovingBepInEx', label: '移除 BepInEx' },
  { key: 'RestoringBackup', label: '还原备份文件' },
]

const isUninstalling = computed(() => {
  const step = installStore.status?.step
  return step === 'RemovingXUnity' || step === 'RemovingBepInEx' || step === 'RestoringBackup'
})

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

const title = computed(() => {
  if (isComplete.value) return isUninstalling.value ? '卸载完成' : '操作完成'
  if (isFailed.value) return '操作失败'
  if (isUninstalling.value) return '卸载中...'
  return '安装中...'
})
</script>

<template>
  <NDrawer v-model:show="installStore.isDrawerOpen" :width="400" placement="right">
    <NDrawerContent :title="title" closable @close="installStore.closeDrawer()">
      <NTimeline>
        <NTimelineItem
          v-for="step in steps"
          :key="step.key"
          :type="getStepType(step.key)"
          :title="step.label"
        />
      </NTimeline>

      <NProgress
        v-if="installStore.status"
        type="line"
        :percentage="installStore.status.progressPercent"
        :status="isFailed ? 'error' : isComplete ? 'success' : 'default'"
        :indicator-placement="'inside'"
        style="margin-top: 16px"
      />

      <div v-if="installStore.status?.message" style="margin-top: 12px; color: #aaa; font-size: 13px">
        {{ installStore.status.message }}
      </div>

      <NAlert v-if="isFailed && installStore.status?.error" type="error" style="margin-top: 16px">
        {{ installStore.status.error }}
      </NAlert>

      <NAlert v-if="isComplete" type="success" style="margin-top: 16px">
        插件已成功安装！启动游戏即可使用。
      </NAlert>

      <template #footer>
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
      </template>
    </NDrawerContent>
  </NDrawer>
</template>
