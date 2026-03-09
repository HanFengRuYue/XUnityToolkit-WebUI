<script setup lang="ts">
import { NLayout, NLayoutSider, NLayoutContent, NMenu } from 'naive-ui'
import { h } from 'vue'
import { RouterView, useRouter, useRoute } from 'vue-router'
import { GamepadFilled } from '@vicons/material'
import { NIcon } from 'naive-ui'
import InstallProgressDrawer from '@/components/progress/InstallProgressDrawer.vue'

const router = useRouter()
const route = useRoute()

function renderIcon(icon: any) {
  return () => h(NIcon, null, { default: () => h(icon) })
}

const menuOptions = [
  {
    label: '游戏库',
    key: '/',
    icon: renderIcon(GamepadFilled),
  },
]

function handleMenuSelect(key: string) {
  router.push(key)
}
</script>

<template>
  <NLayout has-sider style="height: 100vh">
    <NLayoutSider
      bordered
      :width="200"
      :collapsed-width="64"
      show-trigger
      collapse-mode="width"
    >
      <div style="padding: 16px 0; text-align: center; font-weight: bold; font-size: 14px; color: #fff">
        XUnity Toolkit
      </div>
      <NMenu
        :value="route.path"
        :options="menuOptions"
        @update:value="handleMenuSelect"
      />
    </NLayoutSider>
    <NLayoutContent content-style="padding: 24px;" style="background: #1e1e22">
      <RouterView />
    </NLayoutContent>
  </NLayout>
  <InstallProgressDrawer />
</template>
