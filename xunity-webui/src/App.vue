<script setup lang="ts">
import { NConfigProvider, NMessageProvider, NDialogProvider, darkTheme, zhCN } from 'naive-ui'
import type { GlobalThemeOverrides } from 'naive-ui'
import AppShell from '@/components/layout/AppShell.vue'

const themeOverrides: GlobalThemeOverrides = {
  common: {
    primaryColor: '#22d3a7',
    primaryColorHover: '#34e0b6',
    primaryColorPressed: '#1aba94',
    primaryColorSuppl: '#22d3a7',

    infoColor: '#60a5fa',
    infoColorHover: '#93bbfd',
    infoColorPressed: '#3b82f6',
    infoColorSuppl: '#60a5fa',

    successColor: '#34d399',
    successColorHover: '#6ee7b7',
    successColorPressed: '#10b981',
    successColorSuppl: '#34d399',

    warningColor: '#fbbf24',
    warningColorHover: '#fcd34d',
    warningColorPressed: '#f59e0b',
    warningColorSuppl: '#fbbf24',

    errorColor: '#f87171',
    errorColorHover: '#fca5a5',
    errorColorPressed: '#ef4444',
    errorColorSuppl: '#f87171',

    textColor1: '#ebebf0',
    textColor2: '#a0a0b8',
    textColor3: '#6b6b82',

    bodyColor: '#0b0b11',
    cardColor: '#16161f',
    modalColor: '#1a1a26',
    popoverColor: '#1a1a26',
    tableColor: '#16161f',
    inputColor: '#12121a',
    actionColor: '#12121a',

    borderColor: 'rgba(255, 255, 255, 0.08)',
    dividerColor: 'rgba(255, 255, 255, 0.06)',

    hoverColor: 'rgba(255, 255, 255, 0.05)',
    pressedColor: 'rgba(255, 255, 255, 0.03)',

    borderRadius: '10px',
    borderRadiusSmall: '8px',

    fontFamily: "'DM Sans', sans-serif",
    fontFamilyMono: "'JetBrains Mono', monospace",
  },
  Card: {
    borderRadius: '16px',
    borderColor: 'rgba(255, 255, 255, 0.06)',
    color: '#16161f',
    colorEmbedded: '#12121a',
    titleFontWeight: '600',
    titleFontSizeMedium: '16px',
  },
  Button: {
    borderRadiusMedium: '10px',
    borderRadiusSmall: '8px',
    borderRadiusLarge: '12px',
    fontWeight: '500',
  },
  Tag: {
    borderRadius: '6px',
  },
  Drawer: {
    color: '#13131d',
    borderColor: 'rgba(255, 255, 255, 0.06)',
  },
  Alert: {
    borderRadius: '12px',
  },
  Descriptions: {
    borderColor: 'rgba(255, 255, 255, 0.06)',
    thColor: '#12121a',
    tdColor: '#16161f',
    borderRadius: '12px',
  },
  Form: {
    labelFontWeight: '500',
  },
  Progress: {
    railColor: 'rgba(255, 255, 255, 0.06)',
  },
  Timeline: {
    circleBorder: '2px solid #22d3a7',
  },
  Collapse: {
    titleFontWeight: '500',
  },
  Popconfirm: {
    borderRadius: '12px',
  },
}
</script>

<template>
  <NConfigProvider :theme="darkTheme" :theme-overrides="themeOverrides" :locale="zhCN">
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
  background-color: #0b0b11;
}
</style>
