/**
 * SSR 冒烟测试：把整个应用渲染成 HTML 字符串，断言关键内容确实在输出里。
 * 运行：npm run smoke
 */
import { createServer } from 'vite'
import { createSSRApp } from 'vue'
import { renderToString } from '@vue/server-renderer'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')

const vite = await createServer({
  root,
  server: { middlewareMode: true },
  appType: 'custom',
  logLevel: 'warn',
})

try {
  const { default: App } = await vite.ssrLoadModule('/src/App.vue')
  const html = await renderToString(createSSRApp(App))

  const checks = [
    ['主标题', /你的 Minecraft，从这里出发/],
    ['slogan', /少一点配置，多一点游戏/],
    ['功能-游戏管理', /游戏管理/],
    ['功能-基岩版', /基岩版支持/],
    ['功能-命令行', /命令行调用/],
    ['下载-正式版通道', /正式版/],
    ['下载-commit 通道', /commit 版/],
    ['下载-Windows 资产', /MChub\.win\.x64\.installer\.zip/],
    ['下载-Linux AppImage', /AppImage/],
    ['下载-AUR', /mchub-commit-bin/],
    ['代理加速', /代理加速/],
    ['QQ 群号', /545716736/],
    ['开源协议', /AGPL-3\.0/],
    ['GitHub 链接', /hub-code-develop\/MChub/],
  ]

  let failed = 0
  for (const [label, re] of checks) {
    const ok = re.test(html)
    if (!ok) failed++
    console.log(`  ${ok ? '✓' : '✗'}  ${label}`)
  }

  console.log(`\n渲染输出 ${html.length} 字符`)
  console.log(failed === 0 ? '全部通过' : `${failed} 项未通过`)
  process.exitCode = failed === 0 ? 0 : 1
} catch (err) {
  console.error('\n渲染失败:', err)
  process.exitCode = 1
} finally {
  await vite.close()
}
