<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { NButton, NEmpty, NSpin, useMessage } from 'naive-ui'
import {
  ArticleOutlined,
  DataObjectOutlined,
  FileDownloadOutlined,
  FileUploadOutlined,
  FontDownloadOutlined,
  Inventory2Outlined,
  RefreshOutlined,
  WallpaperOutlined,
  WidgetsOutlined,
} from '@vicons/material'
import type { Game, ManualTranslationAssetFilterValueKind, ManualTranslationStatus } from '@/api/types'
import { gamesApi, manualTranslationApi } from '@/api/games'
import GameWorkspaceHero from '@/components/manual/GameWorkspaceHero.vue'
import { DEFAULT_MANUAL_KIND, formatManualWorkspaceState, getManualKindCount } from '@/components/manual/manualTranslationMeta'
import { useFileExplorer } from '@/composables/useFileExplorer'

defineOptions({ name: 'ManualTranslationGameView' })

const route = useRoute()
const router = useRouter()
const message = useMessage()
const { selectFile } = useFileExplorer()

const gameId = computed(() => route.params.id as string)
const game = ref<Game | null>(null)
const status = ref<ManualTranslationStatus | null>(null)
const loading = ref(true)
const scanning = ref(false)
const applying = ref(false)
const restoring = ref(false)
const importingPackage = ref(false)
const buildingPackage = ref(false)

const workspaceHeroTone = computed<'success' | 'warning' | 'default'>(() => {
  if (!status.value?.hasScan)
    return 'default'
  if (status.value.overrideCount > 0)
    return 'warning'
  return 'success'
})

const workspaceHeroStatusLabel = computed(() =>
  status.value?.hasScan ? formatManualWorkspaceState(status.value.state) : '未扫描',
)

const overviewCards = computed(() => [
  {
    key: 'assets',
    label: '资源总数',
    value: String(status.value?.assetCount ?? 0),
    hint: '索引里的全部手动翻译资产',
  },
  {
    key: 'editable',
    label: '可编辑文本',
    value: String(status.value?.editableAssetCount ?? 0),
    hint: '可以直接写入覆盖值的文本或代码',
  },
  {
    key: 'overrides',
    label: '覆盖数量',
    value: String(status.value?.overrideCount ?? 0),
    hint: '当前已经保存但尚未写回或已写回的覆盖',
  },
  {
    key: 'last-apply',
    label: '最近写回',
    value: status.value?.appliedAt ? '已有记录' : '尚未写回',
    hint: status.value?.appliedAt ?? '还没有执行过写回游戏',
  },
])

const kindCards = computed(() => [
  {
    value: 'All' as ManualTranslationAssetFilterValueKind,
    label: '全部',
    description: '查看整个项目的手动翻译资产。',
    count: getManualKindCount('All', status.value?.kindCounts ?? null),
    icon: WidgetsOutlined,
    tone: 'all',
  },
  {
    value: 'Text' as ManualTranslationAssetFilterValueKind,
    label: '文本',
    description: '编辑 TextAsset 和 MonoBehaviour 里的文本字段。',
    count: getManualKindCount('Text', status.value?.kindCounts ?? null),
    icon: ArticleOutlined,
    tone: 'text',
  },
  {
    value: 'Code' as ManualTranslationAssetFilterValueKind,
    label: '代码',
    description: '处理程序集和二进制内的字符串字面量。',
    count: getManualKindCount('Code', status.value?.kindCounts ?? null),
    icon: DataObjectOutlined,
    tone: 'code',
  },
  {
    value: 'Image' as ManualTranslationAssetFilterValueKind,
    label: '贴图',
    description: '预览原图并上传替换贴图。',
    count: getManualKindCount('Image', status.value?.kindCounts ?? null),
    icon: WallpaperOutlined,
    tone: 'image',
  },
  {
    value: 'Font' as ManualTranslationAssetFilterValueKind,
    label: '字体',
    description: '进入字体专用面板处理替换与恢复。',
    count: getManualKindCount('Font', status.value?.kindCounts ?? null),
    icon: FontDownloadOutlined,
    tone: 'font',
  },
  {
    value: 'Binary' as ManualTranslationAssetFilterValueKind,
    label: '二进制',
    description: '查看元数据并导出原始资产信息。',
    count: getManualKindCount('Binary', status.value?.kindCounts ?? null),
    icon: Inventory2Outlined,
    tone: 'binary',
  },
])

onMounted(async () => {
  await load()
})

async function load() {
  loading.value = true

  try {
    const [nextGame, nextStatus] = await Promise.all([
      gamesApi.get(gameId.value),
      manualTranslationApi.getStatus(gameId.value),
    ])
    game.value = nextGame
    status.value = nextStatus
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '加载手动翻译项目失败')
  }
  finally {
    loading.value = false
  }
}

async function handleScan() {
  scanning.value = true
  try {
    await manualTranslationApi.scan(gameId.value)
    message.success('资源索引已刷新')
    await load()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '扫描失败')
  }
  finally {
    scanning.value = false
  }
}

async function handleApply() {
  applying.value = true
  try {
    const result = await manualTranslationApi.apply(gameId.value)
    message.success(`已写回 ${result.appliedOverrides} 个覆盖，涉及 ${result.modifiedFiles} 个文件`)
    await load()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '应用失败')
  }
  finally {
    applying.value = false
  }
}

async function handleRestore() {
  restoring.value = true
  try {
    await manualTranslationApi.restore(gameId.value)
    message.success('已恢复最近一次手动翻译备份')
    await load()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '回滚失败')
  }
  finally {
    restoring.value = false
  }
}

async function handleImportPackage() {
  const zipPath = await selectFile({
    title: '选择手动翻译补丁包',
    filters: [{ label: 'ZIP Files', extensions: ['.zip'] }],
  })
  if (!zipPath)
    return

  importingPackage.value = true
  try {
    await manualTranslationApi.importPackage(gameId.value, zipPath)
    message.success('补丁包已导入')
    await load()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '导入补丁包失败')
  }
  finally {
    importingPackage.value = false
  }
}

async function handleBuildPackage() {
  buildingPackage.value = true
  try {
    await manualTranslationApi.buildPackage(gameId.value)
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '导出补丁包失败')
  }
  finally {
    buildingPackage.value = false
  }
}

function openExplorer(kind: ManualTranslationAssetFilterValueKind) {
  void router.push({
    path: `/manual-translation/${gameId.value}/assets`,
    query: kind === DEFAULT_MANUAL_KIND ? undefined : { kind },
  })
}
</script>

<template>
  <div v-if="game" class="manual-game-page">
    <GameWorkspaceHero
      :game="game"
      back-to="/manual-translation"
      back-label="返回项目目录"
      :status-label="workspaceHeroStatusLabel"
      :status-tone="workspaceHeroTone"
      subtitle="手动翻译页沿用与 XUnity 一致的游戏头部和工作区切换，先确认项目状态，再进入文本、贴图、字体等独立流程。"
      :x-unity-state="game.xUnityStatus.state"
      :manual-state="status?.state ?? game.manualTranslationStatus.state"
      :manual-overrides="status?.overrideCount ?? game.manualTranslationStatus.overrideCount"
    />

    <section class="section-card manual-game-page__action-card" style="animation-delay: 0.08s">
      <div class="manual-game-page__action-copy">
        <h2 class="section-title">工作区操作</h2>
        <div class="manual-game-page__section-meta">刷新资源索引、导入导出补丁、回滚或应用覆盖都集中在这里。</div>
      </div>

      <div class="manual-game-page__action-row">
        <NButton type="primary" :loading="scanning" @click="handleScan">
          <template #icon><RefreshOutlined /></template>
          {{ status?.hasScan ? '重新扫描' : '开始扫描' }}
        </NButton>
        <NButton secondary :loading="importingPackage" @click="handleImportPackage">
          <template #icon><FileUploadOutlined /></template>
          导入补丁包
        </NButton>
        <NButton secondary :loading="buildingPackage" :disabled="!status?.hasScan" @click="handleBuildPackage">
          <template #icon><FileDownloadOutlined /></template>
          构建补丁包
        </NButton>
        <NButton secondary :disabled="!status?.overrideCount" :loading="restoring" @click="handleRestore">
          回滚备份
        </NButton>
        <NButton type="primary" ghost :disabled="!status?.overrideCount" :loading="applying" @click="handleApply">
          应用到游戏
        </NButton>
      </div>
    </section>

    <section class="section-card manual-game-page__overview" style="animation-delay: 0.12s">
      <div class="section-header">
        <div>
          <h2 class="section-title">项目概览</h2>
          <div class="manual-game-page__section-meta">扫描、覆盖和写回都围绕当前项目单独进行。</div>
        </div>
      </div>

      <NSpin :show="loading">
        <div v-if="status?.hasScan" class="manual-game-page__overview-grid">
          <article
            v-for="card in overviewCards"
            :key="card.key"
            class="manual-game-page__overview-card"
          >
            <span>{{ card.label }}</span>
            <strong>{{ card.value }}</strong>
            <small>{{ card.hint }}</small>
          </article>
        </div>

        <div v-else class="manual-game-page__empty">
          <NEmpty description="还没有建立手动翻译索引。先扫描一次项目资源。">
            <template #extra>
              <NButton type="primary" :loading="scanning" @click="handleScan">
                开始扫描
              </NButton>
            </template>
          </NEmpty>
        </div>
      </NSpin>
    </section>

    <section class="section-card manual-game-page__entries" style="animation-delay: 0.16s">
      <div class="section-header">
        <div>
          <h2 class="section-title">资源入口</h2>
          <div class="manual-game-page__section-meta">每种资源类型进入独立工作流，避免把项目、列表和检查器挤在同一页。</div>
        </div>
      </div>

      <div class="manual-game-page__kind-grid">
        <button
          v-for="item in kindCards"
          :key="item.value"
          class="manual-kind-card"
          :class="`manual-kind-card--${item.tone}`"
          type="button"
          :disabled="!status?.hasScan"
          @click="openExplorer(item.value)"
        >
          <div class="manual-kind-card__icon">
            <component :is="item.icon" />
          </div>
          <div class="manual-kind-card__copy">
            <div class="manual-kind-card__topline">
              <strong>{{ item.label }}</strong>
              <span>{{ item.count }}</span>
            </div>
            <p>{{ item.description }}</p>
          </div>
        </button>
      </div>
    </section>
  </div>
</template>

<style scoped>
.manual-game-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.manual-game-page__action-card,
.manual-game-page__action-row,
.manual-game-page__overview-card,
.manual-kind-card,
.manual-kind-card__topline {
  display: flex;
}

.manual-game-page__action-card {
  align-items: flex-start;
  justify-content: space-between;
  gap: 18px;
}

.manual-game-page__action-copy {
  min-width: 0;
}

.manual-game-page__action-row {
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.manual-game-page__section-meta,
.manual-kind-card p {
  margin: 0;
  color: var(--text-2);
  line-height: 1.65;
}

.manual-game-page__overview-grid,
.manual-game-page__kind-grid {
  display: grid;
  gap: 12px;
}

.manual-game-page__overview-grid {
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
}

.manual-game-page__overview-card {
  flex-direction: column;
  gap: 8px;
  padding: 16px 18px;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
}

.manual-game-page__overview-card span,
.manual-game-page__overview-card small {
  color: var(--text-3);
  font-size: 12px;
}

.manual-game-page__overview-card strong {
  font-family: var(--font-display);
  font-size: 30px;
  font-weight: 700;
  line-height: 1;
  color: var(--text-1);
}

.manual-game-page__kind-grid {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.manual-kind-card {
  align-items: flex-start;
  gap: 14px;
  width: 100%;
  padding: 18px;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border);
  background:
    linear-gradient(180deg, rgba(255, 255, 255, 0.04), transparent 70%),
    var(--bg-subtle);
  text-align: left;
  cursor: pointer;
  transition: transform 0.18s ease, border-color 0.18s ease, background 0.18s ease;
}

.manual-kind-card:hover:not(:disabled) {
  transform: translateY(-2px);
  border-color: var(--accent-border);
}

.manual-kind-card:disabled {
  cursor: not-allowed;
  opacity: 0.56;
}

.manual-kind-card__icon {
  width: 44px;
  height: 44px;
  border-radius: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 22px;
  flex-shrink: 0;
  color: var(--accent);
  background: var(--accent-soft);
}

.manual-kind-card__copy {
  min-width: 0;
}

.manual-kind-card__topline {
  align-items: baseline;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 8px;
}

.manual-kind-card__topline strong {
  font-size: 18px;
  color: var(--text-1);
}

.manual-kind-card__topline span {
  font-family: var(--font-display);
  font-size: 28px;
  line-height: 1;
  color: var(--text-1);
}

.manual-kind-card--text .manual-kind-card__icon {
  color: #7dd3fc;
  background: rgba(125, 211, 252, 0.14);
}

.manual-kind-card--code .manual-kind-card__icon {
  color: #60a5fa;
  background: rgba(96, 165, 250, 0.14);
}

.manual-kind-card--image .manual-kind-card__icon {
  color: #fbbf24;
  background: rgba(251, 191, 36, 0.14);
}

.manual-kind-card--font .manual-kind-card__icon {
  color: #c084fc;
  background: rgba(192, 132, 252, 0.14);
}

.manual-kind-card--binary .manual-kind-card__icon {
  color: #cbd5e1;
  background: rgba(203, 213, 225, 0.12);
}

.manual-kind-card--all .manual-kind-card__icon {
  color: #38bdf8;
  background: rgba(56, 189, 248, 0.14);
}

.manual-game-page__empty {
  min-height: 220px;
  display: grid;
  place-items: center;
}

@media (max-width: 980px) {
  .manual-game-page__kind-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 720px) {
  .manual-game-page__action-card {
    flex-direction: column;
  }

  .manual-game-page__kind-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 480px) {
  .manual-kind-card {
    padding: 16px;
  }
}
</style>
