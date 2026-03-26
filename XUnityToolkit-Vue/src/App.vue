<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { NConfigProvider, NMessageProvider, NDialogProvider, darkTheme, zhCN } from 'naive-ui'
import type { GlobalThemeOverrides } from 'naive-ui'
import AppShell from '@/components/layout/AppShell.vue'
import { useThemeStore, getAccentVariants } from '@/stores/theme'

const FileExplorerModal = defineAsyncComponent(
  () => import('@/components/common/FileExplorerModal.vue'),
)

const themeStore = useThemeStore()

const isDark = computed(() => themeStore.resolvedTheme === 'dark')

const naiveTheme = computed(() => isDark.value ? darkTheme : null)

const themeOverrides = computed<GlobalThemeOverrides>(() => {
  const accent = getAccentVariants(themeStore.accentColor, themeStore.resolvedTheme)

  if (isDark.value) {
    return {
      common: {
        primaryColor: accent.primary,
        primaryColorHover: accent.hover,
        primaryColorPressed: accent.pressed,
        primaryColorSuppl: accent.primary,

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
      Tag: { borderRadius: '6px' },
      Drawer: {
        color: '#13131d',
        borderColor: 'rgba(255, 255, 255, 0.06)',
      },
      Alert: { borderRadius: '12px' },
      Descriptions: {
        borderColor: 'rgba(255, 255, 255, 0.06)',
        thColor: '#12121a',
        tdColor: '#16161f',
        borderRadius: '12px',
      },
      Form: { labelFontWeight: '500' },
      Progress: { railColor: 'rgba(255, 255, 255, 0.06)' },
      Timeline: { circleBorder: `2px solid ${accent.primary}` },
      Collapse: { titleFontWeight: '500' },
      Popconfirm: { borderRadius: '12px' },
    }
  } else {
    return {
      common: {
        primaryColor: accent.primary,
        primaryColorHover: accent.hover,
        primaryColorPressed: accent.pressed,
        primaryColorSuppl: accent.primary,

        infoColor: '#3b82f6',
        infoColorHover: '#2563eb',
        infoColorPressed: '#1d4ed8',
        infoColorSuppl: '#3b82f6',

        successColor: '#059669',
        successColorHover: '#047857',
        successColorPressed: '#065f46',
        successColorSuppl: '#059669',

        warningColor: '#d97706',
        warningColorHover: '#b45309',
        warningColorPressed: '#92400e',
        warningColorSuppl: '#d97706',

        errorColor: '#dc2626',
        errorColorHover: '#b91c1c',
        errorColorPressed: '#991b1b',
        errorColorSuppl: '#dc2626',

        textColor1: '#1a1a2e',
        textColor2: '#5a5a72',
        textColor3: '#8b8ba0',

        bodyColor: '#f4f4f7',
        cardColor: '#ffffff',
        modalColor: '#ffffff',
        popoverColor: '#ffffff',
        tableColor: '#ffffff',
        inputColor: '#f8f8fb',
        actionColor: '#f4f4f7',

        borderColor: 'rgba(0, 0, 0, 0.09)',
        dividerColor: 'rgba(0, 0, 0, 0.06)',

        hoverColor: 'rgba(0, 0, 0, 0.04)',
        pressedColor: 'rgba(0, 0, 0, 0.06)',

        borderRadius: '10px',
        borderRadiusSmall: '8px',

        fontFamily: "'DM Sans', sans-serif",
        fontFamilyMono: "'JetBrains Mono', monospace",
      },
      Card: {
        borderRadius: '16px',
        borderColor: 'rgba(0, 0, 0, 0.07)',
        color: '#ffffff',
        colorEmbedded: '#f8f8fb',
        titleFontWeight: '600',
        titleFontSizeMedium: '16px',
      },
      Button: {
        borderRadiusMedium: '10px',
        borderRadiusSmall: '8px',
        borderRadiusLarge: '12px',
        fontWeight: '500',
      },
      Tag: { borderRadius: '6px' },
      Drawer: {
        color: '#ffffff',
        borderColor: 'rgba(0, 0, 0, 0.07)',
      },
      Alert: { borderRadius: '12px' },
      Descriptions: {
        borderColor: 'rgba(0, 0, 0, 0.07)',
        thColor: '#f8f8fb',
        tdColor: '#ffffff',
        borderRadius: '12px',
      },
      Form: { labelFontWeight: '500' },
      Progress: { railColor: 'rgba(0, 0, 0, 0.06)' },
      Timeline: { circleBorder: `2px solid ${accent.primary}` },
      Collapse: { titleFontWeight: '500' },
      Popconfirm: { borderRadius: '12px' },
    }
  }
})
</script>

<template>
  <NConfigProvider :theme="naiveTheme" :theme-overrides="themeOverrides" :locale="zhCN">
    <NMessageProvider>
      <NDialogProvider>
        <AppShell />
        <FileExplorerModal />
      </NDialogProvider>
    </NMessageProvider>
  </NConfigProvider>
</template>

<style>
body {
  margin: 0;
  padding: 0;
  background-color: var(--bg-root);
  transition: background-color 0.3s ease;
}
</style>
