import { createRouter, createWebHistory } from 'vue-router'
import LibraryView from '@/views/LibraryView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'library',
      component: LibraryView,
    },
    {
      path: '/games/:id',
      name: 'game-detail',
      component: () => import('@/views/GameDetailView.vue'),
    },
    {
      path: '/games/:id/config-editor',
      name: 'config-editor',
      component: () => import('@/views/ConfigEditorView.vue'),
    },
    {
      path: '/ai-translation',
      name: 'ai-translation',
      component: () => import('@/views/AiTranslationView.vue'),
    },
    {
      path: '/settings',
      name: 'settings',
      component: () => import('@/views/SettingsView.vue'),
    },
  ],
})

export default router
