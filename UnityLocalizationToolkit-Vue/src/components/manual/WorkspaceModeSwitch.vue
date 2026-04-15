<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

const props = withDefaults(defineProps<{
  gameId: string
  xUnityState?: string
  manualState?: string
  manualOverrides?: number
}>(), {
  xUnityState: '未安装',
  manualState: '未扫描',
  manualOverrides: 0,
})

const route = useRoute()
const router = useRouter()

const activeMode = computed(() => route.path.startsWith('/manual-translation') ? 'manual' : 'xunity')
const manualMeta = computed(() => {
  if (props.manualOverrides > 0)
    return `${props.manualOverrides} 覆盖`

  return props.manualState
})

function navigateTo(mode: 'xunity' | 'manual') {
  if (mode === activeMode.value)
    return

  if (mode === 'manual') {
    void router.push(`/manual-translation/${props.gameId}`)
    return
  }

  void router.push(`/games/${props.gameId}`)
}
</script>

<template>
  <div class="workspace-switch" role="tablist" aria-label="翻译工作区切换">
    <button
      class="workspace-switch__chip"
      :class="{ active: activeMode === 'xunity' }"
      type="button"
      @click="navigateTo('xunity')"
    >
      <span>XUnity</span>
      <small>{{ xUnityState }}</small>
    </button>
    <button
      class="workspace-switch__chip"
      :class="{ active: activeMode === 'manual' }"
      type="button"
      @click="navigateTo('manual')"
    >
      <span>手动翻译</span>
      <small>{{ manualMeta }}</small>
    </button>
  </div>
</template>

<style scoped>
.workspace-switch {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.workspace-switch__chip {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-radius: 999px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  background: rgba(255, 255, 255, 0.04);
  color: var(--text-2);
  font-size: 13px;
  cursor: pointer;
  transition: border-color 0.18s ease, background 0.18s ease, color 0.18s ease;
}

.workspace-switch__chip:hover,
.workspace-switch__chip.active {
  border-color: rgba(255, 196, 110, 0.42);
  background: rgba(255, 196, 110, 0.12);
  color: #ffe7b2;
}

.workspace-switch__chip small {
  font-size: 11px;
  color: inherit;
  opacity: 0.72;
}
</style>
