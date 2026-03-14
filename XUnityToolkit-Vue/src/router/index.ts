import { createRouter, createWebHistory } from 'vue-router'
import LibraryView from '@/views/LibraryView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'library',
      component: LibraryView,
      meta: { depth: 1 },
    },
    {
      path: '/games/:id',
      name: 'game-detail',
      component: () => import('@/views/GameDetailView.vue'),
      meta: { depth: 2 },
    },
    {
      path: '/games/:id/config-editor',
      name: 'config-editor',
      component: () => import('@/views/ConfigEditorView.vue'),
      meta: { depth: 3 },
    },
    {
      path: '/games/:id/asset-extraction',
      name: 'asset-extraction',
      component: () => import('@/views/AssetExtractionView.vue'),
      meta: { depth: 3 },
    },
    {
      path: '/games/:id/translation-editor',
      name: 'translation-editor',
      component: () => import('@/views/TranslationEditorView.vue'),
      meta: { depth: 3 },
    },
    {
      path: '/games/:id/glossary-editor',
      name: 'glossary-editor',
      component: () => import('@/views/GlossaryEditorView.vue'),
      meta: { depth: 3 },
    },
    {
      path: '/games/:id/font-replacement',
      name: 'font-replacement',
      component: () => import('@/views/FontReplacementView.vue'),
      meta: { depth: 3 },
    },
    {
      path: '/ai-translation',
      name: 'ai-translation',
      component: () => import('@/views/AiTranslationView.vue'),
      meta: { depth: 1 },
    },
    {
      path: '/logs',
      name: 'logs',
      component: () => import('@/views/LogView.vue'),
      meta: { depth: 1 },
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('@/views/SettingsView.vue'),
      meta: { depth: 1 },
    },
  ],
})

export default router
