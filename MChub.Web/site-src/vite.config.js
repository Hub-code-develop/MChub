import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// 站点使用独立子域 mchub.code.hub-develop.top 挂在根路径 / 下，base 用 '/'
export default defineConfig({
  base: '/',
  plugins: [vue()],
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    chunkSizeWarningLimit: 800,
  },
})
