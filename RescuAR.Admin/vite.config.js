import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api/semaphore': {
        target: 'https://api.semaphore.co/api/v4',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api\/semaphore/, '')
      }
    }
  }
})
