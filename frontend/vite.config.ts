import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Dev: same-origin /api proxied to the Edge API, matching how nginx serves
    // the built app in Compose. Avoids cross-origin (CORS) calls in dev.
    proxy: {
      '/api': { target: 'http://127.0.0.1:5099', changeOrigin: true },
    },
  },
})
