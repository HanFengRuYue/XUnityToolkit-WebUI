<script setup lang="ts">
import { ref, computed, onMounted, h } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  NButton, NDataTable, NIcon, NTag, NSwitch, NDrawer, NDrawerContent,
  NEmpty, NSpin,
  useMessage, useDialog,
  type DataTableColumns
} from 'naive-ui'
import {
  ArrowBackOutlined, WidgetsOutlined,
  DeleteOutlineOutlined, SettingsOutlined, FolderOpenOutlined,
  RefreshOutlined
} from '@vicons/material'
import type { Game, BepInExPlugin } from '@/api/types'
import { gamesApi, bepinexPluginApi } from '@/api/games'
import { useFileExplorer } from '@/composables/useFileExplorer'
import { formatBytes } from '@/utils/format'

const route = useRoute()
const router = useRouter()
const message = useMessage()
const dialog = useDialog()
const { selectFile } = useFileExplorer()

const gameId = computed(() => route.params.id as string)
const game = ref<Game | null>(null)
const plugins = ref<BepInExPlugin[]>([])
const loading = ref(true)
const installing = ref(false)

// Config drawer state
const showConfigDrawer = ref(false)
const configContent = ref('')
const configFileName = ref('')
const configLoading = ref(false)

const columns = computed<DataTableColumns<BepInExPlugin>>(() => [
  {
    title: '插件名称',
    key: 'name',
    minWidth: 180,
    render: (row) => {
      const name = row.pluginName ?? row.fileName
      const children = [h('span', { class: 'plugin-name-text' }, name)]
      if (row.isToolkitManaged) {
        children.push(
          h(NTag, { size: 'tiny', bordered: false, type: 'info', style: 'margin-left: 8px' },
            { default: () => '工具箱' })
        )
      }
      return h('div', { class: 'plugin-name-cell' }, children)
    }
  },
  {
    title: '大小',
    key: 'fileSize',
    width: 90,
    render: (row) => formatBytes(row.fileSize)
  },
  {
    title: '状态',
    key: 'enabled',
    width: 80,
    render: (row) => h(NSwitch, {
      value: row.enabled,
      disabled: row.isToolkitManaged,
      size: 'small',
      onUpdateValue: () => togglePlugin(row),
    })
  },
  {
    title: '操作',
    key: 'actions',
    width: 120,
    render: (row) => {
      const actions = []
      if (row.configFileName) {
        actions.push(
          h(NButton, {
            size: 'tiny',
            quaternary: true,
            onClick: () => viewConfig(row),
          }, { icon: () => h(NIcon, { size: 14 }, { default: () => h(SettingsOutlined) }) })
        )
      }
      if (!row.isToolkitManaged) {
        actions.push(
          h(NButton, {
            size: 'tiny',
            quaternary: true,
            type: 'error',
            onClick: () => confirmUninstall(row),
          }, { icon: () => h(NIcon, { size: 14 }, { default: () => h(DeleteOutlineOutlined) }) })
        )
      }
      return h('div', { class: 'action-btns' }, actions)
    }
  },
])

async function loadGame() {
  loading.value = true
  try {
    game.value = await gamesApi.get(gameId.value)
    await loadPlugins()
  } catch {
    message.error('加载游戏信息失败')
  } finally {
    loading.value = false
  }
}

async function loadPlugins() {
  try {
    plugins.value = await bepinexPluginApi.list(gameId.value)
  } catch {
    message.error('加载插件列表失败')
  }
}

async function selectAndInstall() {
  try {
    const filePath = await selectFile({
      title: '选择插件文件',
      filters: [
        { label: 'DLL 文件', extensions: ['.dll'] },
        { label: 'ZIP 压缩包', extensions: ['.zip'] },
        { label: '所有文件', extensions: [] },
      ],
    })
    if (!filePath) return

    installing.value = true
    await bepinexPluginApi.install(gameId.value, filePath)
    message.success('插件安装成功')
    await loadPlugins()
  } catch (e: any) {
    message.error(e?.message || '安装插件失败')
  } finally {
    installing.value = false
  }
}

function confirmUninstall(plugin: BepInExPlugin) {
  dialog.warning({
    title: '确认卸载',
    content: `确定要卸载插件 "${plugin.pluginName ?? plugin.fileName}" 吗？此操作不可撤销。`,
    positiveText: '卸载',
    negativeText: '取消',
    onPositiveClick: () => {
      uninstallPlugin(plugin)
    }
  })
}

async function uninstallPlugin(plugin: BepInExPlugin) {
  try {
    await bepinexPluginApi.uninstall(gameId.value, plugin.relativePath)
    message.success('插件已卸载')
    await loadPlugins()
  } catch (e: any) {
    message.error(e?.message || '卸载插件失败')
  }
}

async function togglePlugin(plugin: BepInExPlugin) {
  try {
    const updated = await bepinexPluginApi.toggle(gameId.value, plugin.relativePath)
    // Update the local list
    const idx = plugins.value.findIndex(p => p.relativePath === plugin.relativePath)
    if (idx >= 0) plugins.value[idx] = updated
    message.success(updated.enabled ? '插件已启用' : '插件已禁用')
  } catch (e: any) {
    message.error(e?.message || '切换插件状态失败')
  }
}

async function viewConfig(plugin: BepInExPlugin) {
  if (!plugin.configFileName) return
  configFileName.value = plugin.configFileName
  configContent.value = ''
  showConfigDrawer.value = true
  configLoading.value = true
  try {
    const content = await bepinexPluginApi.getConfig(gameId.value, plugin.configFileName)
    configContent.value = content ?? '（配置文件为空）'
  } catch {
    configContent.value = '（无法读取配置文件）'
  } finally {
    configLoading.value = false
  }
}

onMounted(loadGame)
</script>

<template>
  <div v-if="game" class="sub-page">
    <!-- Back Button -->
    <div class="sub-page-header" style="animation-delay: 0s">
      <button class="back-button" @click="router.push(`/games/${gameId}`)">
        <NIcon :size="20"><ArrowBackOutlined /></NIcon>
        <span>{{ game.name }}</span>
      </button>
    </div>

    <!-- Page Title -->
    <h1 class="page-title" style="animation-delay: 0.05s">
      <span class="page-title-icon">
        <NIcon :size="24"><WidgetsOutlined /></NIcon>
      </span>
      插件管理
    </h1>

    <!-- Action Bar -->
    <div class="section-card" style="animation-delay: 0.1s">
      <div class="section-header">
        <h2 class="section-title">
          <span class="section-icon">
            <NIcon :size="16"><WidgetsOutlined /></NIcon>
          </span>
          已安装插件
          <NTag v-if="plugins.length > 0" size="small" :bordered="false" round style="margin-left: 8px">
            {{ plugins.length }}
          </NTag>
        </h2>
        <div class="header-actions">
          <NButton size="small" :loading="loading" quaternary @click="loadPlugins">
            <template #icon><NIcon :size="16"><RefreshOutlined /></NIcon></template>
            刷新
          </NButton>
          <NButton size="small" :loading="installing" @click="selectAndInstall">
            <template #icon><NIcon :size="16"><FolderOpenOutlined /></NIcon></template>
            选择文件安装
          </NButton>
        </div>
      </div>

      <div v-if="loading" class="loading-state">
        <NSpin size="medium" />
      </div>

      <template v-else>
        <div v-if="plugins.length > 0" class="table-container">
          <NDataTable
            :columns="columns"
            :data="plugins"
            :row-key="(row: BepInExPlugin) => row.relativePath"
            size="small"
            :bordered="false"
          />
        </div>
        <NEmpty v-else description="暂无已安装的插件" style="padding: 40px 0" />
      </template>
    </div>

    <!-- Config Drawer -->
    <NDrawer v-model:show="showConfigDrawer" :width="520" placement="right">
      <NDrawerContent :title="configFileName">
        <NSpin v-if="configLoading" size="medium" style="display: flex; justify-content: center; padding: 40px 0" />
        <pre v-else class="config-content">{{ configContent }}</pre>
      </NDrawerContent>
    </NDrawer>
  </div>

  <div v-else-if="loading" class="loading-state" style="padding: 80px 0">
    <NSpin size="large" />
  </div>
</template>

<style scoped>
.plugin-name-cell {
  display: flex;
  align-items: center;
}

.plugin-name-text {
  font-weight: 500;
}

.action-btns {
  display: flex;
  gap: 4px;
}

.config-content {
  font-family: 'Cascadia Code', 'Fira Code', 'JetBrains Mono', monospace;
  font-size: 12px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-all;
  color: var(--text-1);
  background: var(--bg-subtle);
  border-radius: 8px;
  padding: 16px;
  margin: 0;
  overflow-x: auto;
}
</style>
