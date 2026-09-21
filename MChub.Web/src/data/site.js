/**
 * ============================================================
 *  站点内容配置 —— 日常只改这个文件就够了
 * ============================================================
 *
 *  关于双语：凡是 `{ zh: '…', en: '…' }` 这样的字段都会跟着语言切换走
 *  （取值逻辑在 src/composables/useLocale.js 的 t()）。
 *  项目名、链接、颜色这些语言无关的照旧直接写字符串。
 */

const DL = 'https://github.com/CodeHub-develop/MChub/releases/latest/download'
export const GITHUB_REPO = 'https://github.com/CodeHub-develop/MChub'

export const site = {
  /* ---------- 1. 身份 ---------- */

  name: 'MChub',
  tagline: { zh: '你的 Minecraft，从这里出发', en: 'Your Minecraft, starts here' },
  badgetext: { zh: '开源 · 跨平台 · 为 Minecraft 而生', en: 'Open-source · Cross-platform · Made for Minecraft' },
  intro: {
    zh: 'MChub 把启动、实例、资源与记录收进一个工作区。少一点配置，多一点游戏。',
    en: 'MChub brings launching, instances, resources and records into one workspace. Less setup, more play.',
  },
  // 首屏主 CTA 滚动目标
  heroCtas: [
    { type: 'primary', label: { zh: '下载 MChub', en: 'Download MChub' }, target: '#download' },
    { type: 'ghost', label: { zh: '查看源代码', en: 'View source' }, url: GITHUB_REPO },
  ],

  /* ---------- 2. 功能亮点 ---------- */

  featuresSection: {
    title: { zh: '更好的游戏入口，把复杂留在幕后', en: 'A better gateway to your game' },
    subtitle: {
      zh: '每一项功能都围绕同一件事：让你更快抵达想去的世界。',
      en: 'Every feature serves one goal: getting you to the world you want, faster.',
    },
    items: [
      {
        title: { zh: '一处管理全部实例', en: 'Manage all instances in one place' },
        body: {
          zh: '搜索、排序、收藏与快速启动。Java 跨平台运行，基岩版支持 Windows x64 与 Linux x64。',
          en: 'Search, sort, favorite and quick-launch. Java runs cross-platform; Bedrock on Windows x64 and Linux x64.',
        },
      },
      {
        title: { zh: '资源下载一站式搞定', en: 'All your resources in one stop' },
        body: {
          zh: '连接 Modrinth 与 CurseForge，在启动器内查找并安装模组、整合包、资源包、光影、数据包与地图。',
          en: 'Connected to Modrinth and CurseForge — find and install mods, modpacks, resource packs, shaders, datapacks and maps inside the launcher.',
        },
      },
      {
        title: { zh: '不止是启动游戏', en: 'Not just launching the game' },
        body: {
          zh: '查看存档信息、筛选实时日志、分析投影材料。游戏之外的琐事，也交给 MChub 处理。',
          en: 'Inspect world saves, filter live logs, analyze litematica material. MChub also handles the stuff beyond launching.',
        },
      },
    ],
    extraTitle: { zh: '还支持', en: 'Also supports' },
    extras: [
      { zh: '多标签工作区', en: 'Multi-tab workspace' },
      { zh: '基岩版启动（Windows / Linux x64）', en: 'Bedrock launcher (Windows / Linux x64)' },
      { zh: '实时日志', en: 'Live logs' },
      { zh: '存档信息', en: 'World save info' },
      { zh: 'Litematica 分析', en: 'Litematica analysis' },
    ],
  },

  /* ---------- 3. 下载区 ---------- */

  downloadSection: {
    title: { zh: '现在开始', en: 'Get started' },
    subtitle: { zh: '选择你的平台', en: 'Choose your platform' },
    // 下载源：github 直连；proxy 走 gh-proxy 加速，国内可优先选用
    sourceHint: {
      zh: '国内网络可优先选用代理加速源。',
      en: 'Users in mainland China may prefer the proxy mirror.',
    },
    sources: [
      { id: 'github', label: { zh: 'GitHub', en: 'GitHub' }, prefix: '' },
      { id: 'proxy', label: { zh: '代理加速', en: 'Proxy mirror' }, prefix: 'https://gh-proxy.com/' },
    ],
    platforms: [
      {
        id: 'windows',
        name: 'Windows',
        note: 'Windows 10 / 11 x64',
        groups: [
          {
            label: { zh: '安装程序', en: 'Installer' },
            items: [
              { label: 'x64 · zip', url: `${DL}/MChub.win.x64.installer.zip` },
              { label: 'x64 · zip', url: `${DL}/MChub.win.x64.installer.zip` },
            ],
          },
          { label: { zh: '便携版', en: 'Portable' }, items: [{ label: 'x64 · zip', url: `${DL}/MChub.win.x64.portable.zip` }] },
        ],
      },
      {
        id: 'macos',
        name: 'macOS',
        note: 'Intel 与 Apple 芯片',
        groups: [
          {
            label: { zh: 'Apple 芯片版', en: 'Apple Silicon' },
            items: [
              { label: 'Apple 芯片 arm64 · dmg', url: `${DL}/MChub.osx.mac.arm64.dmg` },
              { label: 'Apple 芯片应用包 arm64 · app.zip', url: `${DL}/MChub.osx.mac.arm64.app.zip` },
            ],
          },
          {
            label: { zh: 'Intel 芯片版', en: 'Intel' },
            items: [
              { label: 'Intel 芯片 x64 · dmg', url: `${DL}/MChub.osx.mac.x64.dmg` },
              { label: 'Intel 芯片应用包 x64 · app.zip', url: `${DL}/MChub.osx.mac.x64.app.zip` },
            ],
          },
        ],
      },
      {
        id: 'linux',
        name: 'Linux',
        note: 'Linux x64',
        groups: [
          { label: { zh: 'x64', en: 'x64' }, items: [
            { label: 'x64 AppImage', url: `${DL}/MChub.linux.x64.AppImage` },
            { label: 'x64 · appimage', url: `${DL}/MChub.linux.x64.AppImage` },
          ] },
          { label: { zh: 'x64', en: 'x64' }, items: [
            { label: 'x64 deb 包 · deb', url: `${DL}/MChub.linux.x64.deb` },
            { label: 'x64 rpm 包 · rpm', url: `${DL}/MChub.linux.x64.rpm` },
          ] },
        ],
      },
    ],
    footnote: {
      zh: '基岩版 UWP（版本 ≤ 1.12.11401）与版本隔离仅支持 Windows x64。若无法打开、出现安全提示或文件损坏，请参考安装说明。',
      en: 'Bedrock UWP (≤ 1.12.11401) and version isolation are only available on Windows x64. If the app can’t open, triggers a security warning or appears corrupted, see the install guide.',
    },
    installGuide: { zh: '安装说明', en: 'Install guide' },
  },

  /* ---------- 4. 开源 / 页脚 ---------- */

  footer: {
    openSourceTitle: { zh: '自由且透明', en: 'Free and transparent' },
    openSourceBody: {
      zh: '由社区共同塑造。MChub 以 GPL-3.0-or-later 许可开源——你可以检查每一行代码、提交问题，或让自己的想法成为下一次更新的一部分。',
      en: 'Shaped by the community. MChub is open source under GPL-3.0-or-later — read every line, file an issue, or make your idea part of the next release.',
    },
    links: [
      { label: { zh: 'GitHub 仓库', en: 'GitHub repo' }, url: GITHUB_REPO, action: 'repo' },
      { label: { zh: '反馈问题', en: 'Feedback' }, url: `${GITHUB_REPO}/issues`, action: 'issue' },
      { label: { zh: '官方 QQ 群 545716736', en: 'QQ group 545716736' }, action: 'qq' },
    ],
    qqGroup: {
      number: '545716736',
      prompt: { zh: '扫码或搜索群号加入', en: 'Scan the QR code or search the group number' },
      qrcode: './qq-group-qrcode.png',
    },
    cols: {
      chinese: {
        label: { zh: '简体中文', en: 'Simplified Chinese' },
        value: 'zh-CN',
        prompt: { zh: '使用简体中文', en: 'Use simplified Chinese' },
      },
      english: {
        label: { zh: 'English', en: 'English' },
        value: 'en-US',
        prompt: { zh: '使用英文', en: 'Use English' },
      },
    },
    copyright: { zh: 'Copyright © MChub 2026. 基于 GPL-3.0-or-later 开源许可发布。', en: 'Copyright © MChub 2026. Released under the GPL-3.0-or-later license.' },
    disclaimer: {
      zh: 'MChub 是独立开源项目，与 Mojang Studios 或 Microsoft 没有从属关系。Minecraft 是 Microsoft 旗下商标。',
      en: 'MChub is an independent open-source project and is not affiliated with Mojang Studios or Microsoft. Minecraft is a trademark of Microsoft.',
    },
    policyLink: { zh: '隐私协议与使用条款', en: 'Privacy policy & terms of use' },
    policyUrl: './policy/index.html',
  },

  /* ---------- 5. 顶部导航 ---------- */

  nav: {
    links: [
      { label: { zh: '下载', en: 'Download' }, target: '#download' },
      { label: { zh: '功能', en: 'Features' }, target: '#features' },
      { label: { zh: '安装说明', en: 'Install' }, url: './install/index.html' },
      { label: { zh: '开源', en: 'Open source' }, target: '#opensource' },
    ],
    cta: { label: { zh: '下载', en: 'Download' }, target: '#download' },
  },
}

export default site