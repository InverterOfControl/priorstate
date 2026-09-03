import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [
    vue({
      template: {
        compilerOptions: {
          // ReplayWeb.page ships as a custom element. Vue must hand <replay-web-page> to the
          // browser untouched rather than try to resolve it as a Vue component.
          isCustomElement: (tag) => tag.startsWith('replay-'),
        },
      },
    }),
    tailwindcss(),
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  build: {
    // Built into the API's wwwroot so the published container serves the UI directly and the
    // production compose file needs no Node runtime.
    outDir: '../PriorState.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:8080', changeOrigin: true },
      '/health': { target: 'http://localhost:8080', changeOrigin: true },
    },
  },
  test: {
    environment: 'happy-dom',
    globals: true,
  },
})
