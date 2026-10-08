import { icons } from './icons.js'

/**
 * ============================================================
 *  站点内容配置 —— 日常只改这个文件就够了
 * ============================================================
 *
 *  关于双语：凡是 `{ zh: '…', en: '…' }` 这样的字段都会跟着语言切换走
 *  （取值逻辑在 src/composables/useLocale.js 的 t()）。
 *  项目名、链接、颜色这些语言无关的照旧直接写字符串。
 *  结构完全复刻自参考站 x-coder-ocs/website，仅把内容换成 MChub。
 */

// MChub GitHub 仓库
export const GITHUB_REPO = 'https://github.com/hub-code-develop/MChub'
const RELEASES = `${GITHUB_REPO}/releases/latest`
// 正式版资产直链
const DL = `${RELEASES}/download`
// QQ 群加群短链。Contact / 加入群聊按钮和社交图标共用这一个
const QQ_URL = 'https://qm.qq.com/q/iU5DvgYclq'

export const site = {
  /* ---------- 1. 身份（语言无关） ---------- */

  name: 'MChub',
  // 页脚超大水印用的短名，会用 24vw 字号撑满屏幕宽度，别超过 6 个字符
  watermark: 'MChub',
  // 终端提示符 mchub@mc
  user: 'mchub',
  host: 'mc',

  /* ---------- 2. 首屏终端文案 ---------- */

  terminal: {
    // 命令本身不翻译，保持 whoami 才有终端味
    command: 'mchub',
    output: 'MChub-commit-2026-5-27',
    roles: {
      zh: ['开源启动器', '跨平台游戏管理'],
      en: ['Open-source launcher', 'Cross-platform game manager'],
    },
    // 左边竖线那段引言 —— 沿用 MChub 口号
    motto: {
      zh: '少一点配置，多一点游戏。',
      en: 'Less setup, more play.',
    },
    // 最后一行 // 开头的碎碎念，留空则不显示
    status: {
      zh: '启动、资源与记录，收进一个工作区。',
      en: 'Launching, resources and records in one workspace.',
    },
  },

  /* ---------- 3. 头像 / Logo ---------- */
  // 首屏左侧展示的图，用 MChub 自己的图标（public/icon.png）
  avatar: './icon.png',

  /* ---------- 4. 联系方式 ---------- */
  // 点 Contact / 加入群聊 直接打开这个链接
  contact: {
    url: QQ_URL,
  },

  /* ---------- 5. 社交链接 ---------- */
  // url 留空字符串 = 该项不显示，填上就自动出现
  socials: [
    { name: 'GitHub', icon: icons.github, url: GITHUB_REPO, color: '#ffffff' },
    { name: 'QQ 群', icon: icons.qq, url: QQ_URL, color: '#db9807' },
  ],

  /* ---------- 6. 关于 + 功能 + 下载 ---------- */

  about: {
    title: { zh: '关于', en: 'About' },
    featuresTitle: { zh: '功能', en: 'Features' },
    downloadTitle: { zh: '下载', en: 'Download' },
    // 不用 GitHub 拉仓库，所以这几个展示文案保留原样
    noDescription: { zh: '这个项目还没写简介', en: 'No description yet' },
    paragraphs: {
      zh: [
        '开源、跨平台的 Minecraft 启动器与实例管理器，同时支持 Java 版和基岩版。',
        '从游戏安装、账户登录，到资源查找与文件整理——一个工作区装下你的整个 Minecraft。少一点配置，多一点游戏。',
      ],
      en: [
        'An open-source, cross-platform Minecraft launcher and instance manager for Java and Bedrock.',
        'From game install and account login to resource discovery and file organization — one workspace for your whole Minecraft. Less setup, more play.',
      ],
    },
  },

  // 功能亮点（沿用原官网 7 项）
  features: [
    { title: { zh: '游戏管理', en: 'Game management' }, desc: { zh: '查看、搜索、排序、收藏并启动游戏，列表里直接看最近游玩记录与时长；安装原版 Minecraft 与常用 Java 版加载器。', en: 'View, search, sort, favorite and launch your games, with recent play time right in the list; install vanilla Minecraft and common Java loaders.' } },
    { title: { zh: '账户登录', en: 'Account sign-in' }, desc: { zh: '支持离线账户、微软账户和第三方账户登录，一个入口管理你的所有身份。', en: 'Offline, Microsoft and third-party accounts, all managed from a single entry.' } },
    { title: { zh: '资源安装', en: 'Resource installation' }, desc: { zh: '直接浏览 Modrinth 和 CurseForge，模组、整合包、资源包、光影、数据包、地图一键安装，文件自动归位。', en: 'Browse Modrinth and CurseForge to install mods, modpacks, resource packs, shaders, datapacks and maps — files auto-filed.' } },
    { title: { zh: '文件整理', en: 'File organization' }, desc: { zh: '集中查看游戏日志、存档、截图、设置与资源文件，模组、光影、存档一目了然。', en: 'All your logs, worlds, screenshots, settings and resources in one place — mods, shaders and worlds at a glance.' } },
    { title: { zh: '投影材料', en: 'Litematica materials' }, desc: { zh: '打开 .litematic 与 .nbt 文件预览结构，统计并导出所需的材料清单。', en: 'Preview .litematic and .nbt structures, tally and export the required material list.' } },
    { title: { zh: '基岩版支持', en: 'Bedrock support' }, desc: { zh: 'Windows 支持 GDK、UWP 本体的下载安装启动，以及 DLL 模组、预加载和可配置鼠标锁；Linux 走 Proton 启动。', en: 'On Windows: install & launch GDK/UWP packages, plus DLL mods, preloading and configurable mouse lock; on Linux it runs via Proton.' } },
    { title: { zh: '命令行调用', en: 'CLI' }, desc: { zh: '支持命令行参数与浏览器 mchub:// 链接调用安装与启动，脚本化一切。', en: 'Install and launch via command-line args or browser mchub:// links — script everything.' } },
  ],

  // 下载平台（沿用原官网数据，资产直链走 GitHub Releases）
  platforms: [
    { name: 'Windows', sub: 'x64 · 10 / 11', assets: [
      { file: 'MChub.win.x64.installer.zip', label: { zh: '安装程序', en: 'Installer' } },
      { file: 'MChub.win.x64.portable.zip', label: { zh: '便携版', en: 'Portable' } },
    ] },
    { name: 'macOS', sub: 'Apple Silicon', assets: [
      { file: 'MChub.osx.mac.arm64.dmg', label: { zh: '磁盘映像', en: 'DMG' } },
      { file: 'MChub.osx.mac.arm64.app.zip', label: { zh: '应用包', en: 'App archive' } },
    ] },
    { name: 'macOS', sub: 'Intel', assets: [
      { file: 'MChub.osx.mac.x64.dmg', label: { zh: '磁盘映像', en: 'DMG' } },
      { file: 'MChub.osx.mac.x64.app.zip', label: { zh: '应用包', en: 'App archive' } },
    ] },
    { name: 'Linux', sub: 'x64', assets: [
      { file: 'MChub.linux.x64.AppImage', label: 'AppImage' },
      { file: 'MChub.linux.x64.deb', label: { zh: 'deb 包', en: 'deb package' } },
      { file: 'MChub.linux.x64.rpm', label: { zh: 'rpm 包', en: 'rpm package' } },
    ] },
  ],
  // 下载直链前缀
  downloadBase: DL,
  releasesUrl: RELEASES,

  /* ---------- 7. 页脚 ---------- */

  footer: {
    copyright: {
      zh: '用热爱和代码打造。',
      en: 'Crafted with passion and code.',
    },
    // 版权归属跳转链接
    ownerUrl: GITHUB_REPO,
  },

  /* ---------- 8. 按钮文案 ---------- */

  buttons: {
    download: { zh: '下载 MChub', en: 'Download MChub' },
    contact: { zh: '加入 QQ 群', en: 'Join the QQ group' },
    topbarContact: { zh: '联系', en: 'Contact' },
  },
}

export default site