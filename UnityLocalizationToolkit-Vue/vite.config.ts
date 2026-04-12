import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { NaiveUiResolver } from 'unplugin-vue-components/resolvers'

export default defineConfig({
  plugins: [
    vue(),
    AutoImport({
      imports: ['vue', 'vue-router', 'pinia'],
      dts: 'src/auto-imports.d.ts',
    }),
    Components({
      resolvers: [NaiveUiResolver()],
      dts: 'src/components.d.ts',
    }),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    outDir: '../UnityLocalizationToolkit-WebUI/wwwroot',
    emptyOutDir: true,
    chunkSizeWarningLimit: 750,
    rolldownOptions: {
      onwarn(warning, defaultHandler) {
        // Suppress @microsoft/signalr ESM /*#__PURE__*/ annotation warnings
        if (warning.code === 'SOURCEMAP_ERROR' || warning.message?.includes('#__PURE__')) return
        defaultHandler(warning)
      },
      output: {
        codeSplitting: {
          groups: [
            { name: 'vendor-vue', test: /node_modules[\\/](vue|vue-router|pinia|@vue[\\/]devtools-api)/ },
            { name: 'vendor-naive-ui', test: /node_modules[\\/]naive-ui/ },
            { name: 'vendor-signalr', test: /node_modules[\\/]@microsoft[\\/]signalr/ },
          ],
        },
      },
    },
  },
  server: {
    proxy: {
      '/api': 'http://127.0.0.1:51821',
      '/hubs': {
        target: 'http://127.0.0.1:51821',
        ws: true,
      },
    },
  },
})
