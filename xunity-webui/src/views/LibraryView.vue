<script setup lang="ts">
import { onMounted } from 'vue'
import {
  NGrid,
  NGridItem,
  NCard,
  NButton,
  NEmpty,
  NTag,
  NSpin,
  NIcon,
  useMessage,
} from 'naive-ui'
import { Add } from '@vicons/ionicons5'
import { useRouter } from 'vue-router'
import { useGamesStore } from '@/stores/games'

const gamesStore = useGamesStore()
const router = useRouter()
const message = useMessage()

onMounted(() => {
  gamesStore.fetchGames()
})

async function handleAddGame() {
  const game = await gamesStore.addGameViaDialog()
  if (game) {
    message.success(`已添加: ${game.name}`)
  }
}

function navigateToGame(id: string) {
  router.push(`/games/${id}`)
}

function getStatusTag(state: string) {
  switch (state) {
    case 'FullyInstalled':
      return { label: '已安装', type: 'success' as const }
    case 'BepInExOnly':
      return { label: 'BepInEx', type: 'warning' as const }
    case 'PartiallyInstalled':
      return { label: '部分安装', type: 'warning' as const }
    default:
      return { label: '未安装', type: 'default' as const }
  }
}
</script>

<template>
  <div>
    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px">
      <h2 style="margin: 0; color: #fff">游戏库</h2>
      <NButton type="primary" @click="handleAddGame">
        <template #icon>
          <NIcon><Add /></NIcon>
        </template>
        添加游戏
      </NButton>
    </div>

    <NSpin :show="gamesStore.loading">
      <NEmpty v-if="!gamesStore.loading && gamesStore.games.length === 0" description="还没有添加任何游戏">
        <template #extra>
          <NButton type="primary" @click="handleAddGame">添加第一个游戏</NButton>
        </template>
      </NEmpty>

      <NGrid v-else :x-gap="16" :y-gap="16" :cols="'1 600:2 900:3 1200:4'">
        <NGridItem v-for="game in gamesStore.games" :key="game.id">
          <NCard
            hoverable
            style="cursor: pointer"
            @click="navigateToGame(game.id)"
          >
            <template #header>
              <div style="font-size: 15px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                {{ game.name }}
              </div>
            </template>
            <template #header-extra>
              <NTag :type="getStatusTag(game.installState).type" size="small">
                {{ getStatusTag(game.installState).label }}
              </NTag>
            </template>
            <div style="font-size: 12px; color: #888; overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
              {{ game.gamePath }}
            </div>
            <div v-if="game.detectedInfo" style="margin-top: 8px; display: flex; gap: 6px">
              <NTag size="tiny" round>Unity {{ game.detectedInfo.unityVersion }}</NTag>
              <NTag size="tiny" round>{{ game.detectedInfo.backend }}</NTag>
              <NTag size="tiny" round>{{ game.detectedInfo.architecture }}</NTag>
            </div>
          </NCard>
        </NGridItem>
      </NGrid>
    </NSpin>
  </div>
</template>
