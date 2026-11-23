import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Backend ASP.NET Core rulează implicit pe http://localhost:5080 (vezi launchSettings.json).
const apiTarget = process.env.MDEL_API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
    },
  },
})
