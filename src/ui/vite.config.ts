import { createReadStream, readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import path from 'node:path'
import { fileURLToPath, URL } from 'node:url'
import { defineConfig, type Plugin } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

/**
 * Serves ReplayWeb.page's own two runtime files under /replay/, which is what SnapshotView passes
 * as `replayBase`.
 *
 * The npm import registers the <replay-web-page> element and nothing else. Replay itself happens
 * inside an iframe backed by a service worker, and both of those are fetched from the server at
 * runtime rather than bundled:
 *
 *   sw.js  registered as /replay/sw.js with scope /replay/. It is what intercepts the archived
 *          page's requests and answers them out of the WACZ. Without it the element renders a
 *          registration error instead of the archive.
 *   ui.js  the replay application. The service worker serves the iframe's index page itself
 *          (serveIndex=1) and that page loads ./ui.js, so it has to exist as a real file. Loading
 *          the package as an ES module leaves document.currentScript null, so the element's
 *          fallback of injecting its own script tag into the iframe cannot fire either.
 *
 * They are copied from the installed package rather than committed, so they cannot drift from the
 * version in package.json.
 */
function replayAssets(): Plugin {
  const base = '/replay/'
  const files = ['sw.js', 'ui.js']

  // The package exports map does not expose package.json, so the entry point is what can be
  // resolved; the two files sit next to dist/ at the package root.
  const packageRoot = path.resolve(
    path.dirname(createRequire(import.meta.url).resolve('replaywebpage')),
    '..',
  )
  const sourceOf = (file: string) => path.join(packageRoot, file)

  return {
    name: 'priorstate:replay-assets',

    // In development the API proxy only covers /api and /health, so nothing else would answer for
    // these; the service worker registration carries a query string, hence the split.
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const file = req.url?.startsWith(base) ? req.url.slice(base.length).split('?')[0] : undefined

        if (file === undefined || !files.includes(file)) {
          next()
          return
        }

        res.setHeader('Content-Type', 'text/javascript')
        createReadStream(sourceOf(file)).pipe(res)
      })
    },

    generateBundle() {
      for (const file of files) {
        this.emitFile({ type: 'asset', fileName: `replay/${file}`, source: readFileSync(sourceOf(file)) })
      }
    },
  }
}

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
    replayAssets(),
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
