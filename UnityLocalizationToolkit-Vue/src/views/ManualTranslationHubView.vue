<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { NButton, NEmpty, NSpin, NTag, useMessage } from 'naive-ui'
import { TranslateOutlined } from '@vicons/material'
import type { Game } from '@/api/types'
import { gamesApi } from '@/api/games'
import { formatManualWorkspaceState } from '@/components/manual/manualTranslationMeta'

defineOptions({ name: 'ManualTranslationHubView' })

const router = useRouter()
const message = useMessage()

const loading = ref(true)
const games = ref<Game[]>([])

const unityGames = computed(() =>
  [...games.value]
    .filter(game => game.isUnityGame)
    .sort((left, right) => right.manualTranslationStatus.assetCount - left.manualTranslationStatus.assetCount),
)

onMounted(async () => {
  await loadGames()
})

async function loadGames() {
  loading.value = true
  try {
    games.value = await gamesApi.list()
  }
  catch (error) {
    message.error(error instanceof Error ? error.message : '加载项目列表失败')
  }
  finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="sub-page manual-hub">
    <div class="section-card manual-hub__hero" style="animation-delay: 0s">
      <div class="manual-hub__hero-copy">
        <span class="manual-hub__eyebrow">手动翻译</span>
        <h1 class="page-title">
          <span class="page-title-icon">
            <TranslateOutlined />
          </span>
          先选项目，再进入资源工作区
        </h1>
        <p class="manual-hub__desc">
          手动翻译不再把所有信息挤在一个三栏页里。先进入游戏项目，再按文本、贴图、字体或代码资源分别处理。
        </p>
      </div>

      <div class="manual-hub__hero-stats">
        <div class="manual-hub__stat">
          <span>Unity 项目</span>
          <strong>{{ unityGames.length }}</strong>
        </div>
        <div class="manual-hub__stat">
          <span>已扫描项目</span>
          <strong>{{ unityGames.filter(game => game.manualTranslationStatus.available).length }}</strong>
        </div>
      </div>
    </div>

    <div class="section-card" style="animation-delay: 0.06s">
      <div class="section-header">
        <div>
          <h2 class="section-title">项目目录</h2>
          <div class="section-meta">先进入项目概览页，再决定要处理哪一类资源。</div>
        </div>
        <NButton secondary @click="loadGames">
          刷新
        </NButton>
      </div>

      <NSpin :show="loading">
        <div v-if="unityGames.length" class="manual-hub__grid">
          <button
            v-for="game in unityGames"
            :key="game.id"
            class="manual-hub__card"
            type="button"
            @click="router.push(`/manual-translation/${game.id}`)"
          >
            <div class="manual-hub__card-top">
              <div>
                <strong class="manual-hub__card-title">{{ game.name }}</strong>
                <div class="manual-hub__card-meta">{{ game.manualTranslationStatus.assetCount }} 个资源 · {{ game.manualTranslationStatus.overrideCount }} 个覆盖</div>
              </div>
              <NTag size="small" :bordered="false" :type="game.manualTranslationStatus.overrideCount > 0 ? 'warning' : (game.manualTranslationStatus.available ? 'success' : 'default')">
                {{ formatManualWorkspaceState(game.manualTranslationStatus.state) }}
              </NTag>
            </div>

            <div class="manual-hub__counts">
              <span>文本 {{ game.manualTranslationStatus.editableAssetCount }}</span>
              <span>已写回 {{ game.manualTranslationStatus.updatedAt ? '有记录' : '无记录' }}</span>
            </div>

            <div class="manual-hub__cta">
              <span>进入项目</span>
            </div>
          </button>
        </div>
        <div v-else class="manual-hub__empty">
          <NEmpty description="游戏库里还没有可用于手动翻译的 Unity 项目。" />
        </div>
      </NSpin>
    </div>
  </div>
</template>

<style scoped>
.manual-hub {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.manual-hub__hero,
.manual-hub__hero-stats,
.manual-hub__card-top,
.manual-hub__counts,
.manual-hub__cta {
  display: flex;
}

.manual-hub__hero {
  justify-content: space-between;
  gap: 24px;
  align-items: stretch;
}

.manual-hub__hero-copy {
  display: flex;
  flex-direction: column;
  gap: 10px;
  max-width: 680px;
}

.manual-hub__eyebrow {
  font-size: 12px;
  letter-spacing: 0.08em;
  color: var(--text-3);
}

.manual-hub__desc {
  margin: 0;
  color: var(--text-2);
  line-height: 1.7;
}

.manual-hub__hero-stats {
  gap: 12px;
  align-self: flex-end;
}

.manual-hub__stat {
  min-width: 140px;
  padding: 14px 16px;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border);
  background: var(--bg-subtle);
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.manual-hub__stat span,
.manual-hub__card-meta {
  font-size: 12px;
  color: var(--text-3);
}

.manual-hub__stat strong {
  font-size: 28px;
  color: var(--text-1);
}

.manual-hub__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

.manual-hub__card {
  padding: 18px;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border);
  background: linear-gradient(180deg, color-mix(in srgb, var(--bg-subtle) 82%, transparent), var(--bg-card));
  display: flex;
  flex-direction: column;
  gap: 16px;
  text-align: left;
  cursor: pointer;
  transition: border-color 0.18s ease, transform 0.18s ease, background 0.18s ease;
}

.manual-hub__card:hover {
  border-color: var(--accent-border);
  transform: translateY(-2px);
}

.manual-hub__card-top {
  justify-content: space-between;
  gap: 14px;
}

.manual-hub__card-title {
  font-size: 20px;
  color: var(--text-1);
}

.manual-hub__counts {
  gap: 12px;
  flex-wrap: wrap;
  color: var(--text-2);
  font-size: 13px;
}

.manual-hub__cta {
  justify-content: flex-end;
  color: var(--accent);
  font-size: 13px;
  font-weight: 600;
}

.manual-hub__empty {
  min-height: 260px;
  display: grid;
  place-items: center;
}

@media (max-width: 960px) {
  .manual-hub__hero,
  .manual-hub__hero-stats {
    flex-direction: column;
  }

  .manual-hub__grid {
    grid-template-columns: 1fr;
  }
}
</style>
