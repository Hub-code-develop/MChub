#!/usr/bin/env node
/**
 * 从 GitHub 拉 MChub 三个发布通道的版本号与日期，生成 src/data/releases.json。
 *
 * 通道：
 *   latest  -> /releases/latest（正式版）
 *   commit  -> tag publish-commit
 *   nightly -> tag publish-nightly
 *
 * 下载链接本身是固定模式（见 site.js 的 channelTag），这里只补「版本号 + 发布日期」，
 * 以及校验对应 tag 是否真的存在（不存在则该通道显示「—」）。
 *
 * 单独跑：node tools/fetch-release.mjs
 * 失败时保留旧 releases.json，不中断构建。
 */
import { writeFile, mkdir } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const OUT = resolve(ROOT, 'src/data/releases.json')
const REPO = 'hub-code-develop/MChub'

async function gh(path) {
  const headers = {
    Accept: 'application/vnd.github+json',
    'User-Agent': 'mchub-site-build',
  }
  if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`

  let lastErr
  for (let i = 0; i < 4; i++) {
    try {
      const res = await fetch(`https://api.github.com${path}`, { headers })
      if (res.status === 404) return null
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      return await res.json()
    } catch (err) {
      lastErr = err
      await new Promise((r) => setTimeout(r, 600 * (i + 1)))
    }
  }
  throw new Error(`${path} 抓取失败: ${lastErr?.message}`)
}

async function main() {
  const [latest, commit, nightly] = await Promise.all([
    gh(`/repos/${REPO}/releases/latest`),
    gh(`/repos/${REPO}/releases/tags/publish-commit`),
    gh(`/repos/${REPO}/releases/tags/publish-nightly`),
  ])

  const pick = (r) =>
    r && r.tag_name
      ? { tag: r.tag_name, publishedAt: r.published_at, prerelease: !!r.prerelease }
      : null

  const channels = {
    latest: pick(latest),
    commit: pick(commit),
    nightly: pick(nightly),
  }

  await mkdir(dirname(OUT), { recursive: true })
  await writeFile(
    OUT,
    JSON.stringify({ generatedAt: new Date().toISOString(), channels }, null, 2) + '\n',
    'utf-8',
  )

  for (const [k, v] of Object.entries(channels)) {
    console.log(
      `  ${k.padEnd(9)} ${v ? `${v.tag}  (${(v.publishedAt || '').slice(0, 10)})` : '无发布'}`,
    )
  }
  console.log(`已写入 ${OUT.replace(ROOT + '/', '')}`)
}

main().catch((err) => {
  console.error('\n拉取失败:', err.message)
  console.error('保留已有的 releases.json（如果存在）继续构建')
  process.exit(0)
})
