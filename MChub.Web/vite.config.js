import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  // 相对路径产物：放 Vercel 根域、GitHub Pages 子路径（/MChub/）、本地直开都不会 404
  base: './',
  plugins: [vue()],
  server: {
    host: '127.0.0.1',
    port: 5178,
    open: false,
  },
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    chunkSizeWarningLimit: 800,
  },
})