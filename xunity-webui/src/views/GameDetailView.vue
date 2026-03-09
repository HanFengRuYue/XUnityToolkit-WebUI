<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NPageHeader,
  NCard,
  NButton,
  NTag,
  NSpace,
  NAlert,
  NDivider,
  NDescriptions,
  NDescriptionsItem,
  NPopconfirm,
  NSpin,
  useMessage,
  useDialog,
} from 'naive-ui'
import { useGamesStore } from '@/stores/games'
import { useInstallStore } from '@/stores/install'
import type { Game, XUnityConfig } from '@/api/types'
import { gamesApi } from '@/api/games'
import ConfigPanel from '@/components/config/ConfigPanel.vue'

const route = useRoute()
const router = useRouter()
const gamesStore = useGamesStore()
const installStore = useInstallStore()
const message = useMessage()
const dialog = useDialog()

const gameId = route.params['id'] as string
const game = ref<Game | null>(null)
const config = ref<XUnityConfig | null>(null)
const loading = ref(true)
const detecting = ref(false)

const isInstalled = computed(
  () => game.value?.installState === 'FullyInstalled',
)
const hasBepInEx = computed(
  () =>
    game.value?.installState === 'BepInExOnly' ||
    game.value?.installState === 'FullyInstalled',
)

onMounted(async () => {
  await loadGame()
})

async function loadGame() {
  loading.value = true
  try {
    game.value = await gamesApi.get(gameId)
    if (isInstalled.value) {
      config.value = await gamesApi.getConfig(gameId)
    }
  } catch {
    message.error('加载游戏信息失败')
  } finally {
    loading.value = false
  }
}

async function handleDetect() {
  detecting.value = true
  try {
    await gamesStore.detectGame(gameId)
    await loadGame()
    message.success('检测完成')
  } catch {
    message.error('检测失败')
  } finally {
    detecting.value = false
  }
}

async function handleInstall() {
  try {
    await installStore.startInstall(gameId, config.value ?? undefined)
  } catch (e) {
    message.error(e instanceof Error ? e.message : '安装失败')
  }
}

function handleUninstall() {
  dialog.warning({
    title: '确认卸载',
    content: '将移除 BepInEx 和 XUnity.AutoTranslator，并还原备份的原始文件。确定要继续吗？',
    positiveText: '确认卸载',
    negativeText: '取消',
    onPositiveClick: async () => {
      try {
        await installStore.startUninstall(gameId)
      } catch (e) {
        message.error(e instanceof Error ? e.message : '卸载失败')
      }
    },
  })
}

async function handleSaveConfig(cfg: XUnityConfig) {
  try {
    await gamesApi.saveConfig(gameId, cfg)
    config.value = cfg
    message.success('配置已保存')
  } catch {
    message.error('保存配置失败')
  }
}

function handleRemoveGame() {
  dialog.error({
    title: '移除游戏',
    content: '将从游戏库中移除此游戏（不会删除游戏文件）。确定吗？',
    positiveText: '确认移除',
    negativeText: '取消',
    onPositiveClick: async () => {
      const ok = await gamesStore.removeGame(gameId)
      if (ok) {
        message.success('已移除')
        router.push('/')
      }
    },
  })
}

// Refresh game data when install completes
const stopWatch = watch(
  () => installStore.status?.step,
  async (step) => {
    if (step === 'Complete') {
      await loadGame()
      await gamesStore.refreshGame(gameId)
    }
  },
)
onUnmounted(() => stopWatch())
</script>

<template>
  <NSpin :show="loading">
    <div v-if="game">
      <NPageHeader @back="router.push('/')">
        <template #title>{{ game.name }}</template>
        <template #extra>
          <NSpace>
            <NButton tertiary type="error" size="small" @click="handleRemoveGame">
              从库中移除
            </NButton>
          </NSpace>
        </template>
      </NPageHeader>

      <!-- Game Info -->
      <NCard style="margin-top: 16px" title="游戏信息">
        <NDescriptions :column="2" label-placement="left" bordered>
          <NDescriptionsItem label="路径">{{ game.gamePath }}</NDescriptionsItem>
          <NDescriptionsItem label="可执行文件">{{ game.executableName || '未检测' }}</NDescriptionsItem>
          <NDescriptionsItem label="Unity 版本">
            {{ game.detectedInfo?.unityVersion || '未检测' }}
          </NDescriptionsItem>
          <NDescriptionsItem label="脚本后端">
            <NTag v-if="game.detectedInfo" size="small" :type="game.detectedInfo.backend === 'IL2CPP' ? 'warning' : 'success'">
              {{ game.detectedInfo.backend }}
            </NTag>
            <span v-else>未检测</span>
          </NDescriptionsItem>
          <NDescriptionsItem label="架构">
            {{ game.detectedInfo?.architecture || '未检测' }}
          </NDescriptionsItem>
          <NDescriptionsItem label="安装状态">
            <NTag :type="isInstalled ? 'success' : hasBepInEx ? 'warning' : 'default'" size="small">
              {{ isInstalled ? '已安装' : hasBepInEx ? '仅 BepInEx' : '未安装' }}
            </NTag>
          </NDescriptionsItem>
        </NDescriptions>

        <NButton
          v-if="!game.detectedInfo"
          style="margin-top: 12px"
          :loading="detecting"
          @click="handleDetect"
        >
          检测游戏
        </NButton>
      </NCard>

      <!-- Install / Uninstall -->
      <NCard style="margin-top: 16px" title="安装管理">
        <NAlert v-if="game.detectedInfo?.backend === 'IL2CPP'" type="warning" style="margin-bottom: 16px">
          检测到 IL2CPP 游戏，将使用 BepInEx 6 (预发布版)。首次启动游戏可能需要 30-90 秒生成互操作程序集。
        </NAlert>

        <NSpace v-if="!isInstalled">
          <NButton
            type="primary"
            size="large"
            :disabled="!game.detectedInfo"
            @click="handleInstall"
          >
            一键安装 BepInEx + XUnity.AutoTranslator
          </NButton>
          <NButton v-if="!game.detectedInfo" @click="handleDetect" :loading="detecting">
            先检测游戏
          </NButton>
        </NSpace>

        <NSpace v-else>
          <NButton type="error" @click="handleUninstall">
            卸载并还原
          </NButton>
          <div style="color: #888; font-size: 13px; line-height: 34px">
            BepInEx {{ game.installedBepInExVersion }} · XUnity {{ game.installedXUnityVersion }}
          </div>
        </NSpace>
      </NCard>

      <!-- Config Panel -->
      <NCard style="margin-top: 16px" title="翻译配置">
        <ConfigPanel
          :config="config"
          :disabled="!isInstalled"
          @save="handleSaveConfig"
        />
      </NCard>
    </div>
  </NSpin>
</template>
