<script setup lang="ts">
import { NConfigProvider, NMessageProvider, NDialogProvider, darkTheme, zhCN } from 'naive-ui'
import AppShell from '@/components/layout/AppShell.vue'
</script>

<template>
  <NConfigProvider :theme="darkTheme" :locale="zhCN">
    <NMessageProvider>
      <NDialogProvider>
        <AppShell />
      </NDialogProvider>
    </NMessageProvider>
  </NConfigProvider>
</template>

<style>
body {
  margin: 0;
  padding: 0;
  background-color: #18181c;
}
</style>
