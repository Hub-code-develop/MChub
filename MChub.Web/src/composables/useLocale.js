/**
 * 极简双语支持，不引 i18n 库 —— 站点体量小，一个 ref 加一个取值函数就够了。
 *
 * 约定：`site.js` 里所有跟语言有关的文案都写成 `{ zh: '…', en: '…' }`，
 * 模板里用 `t(...)` 取值。因为 `t()` 读的是 `locale.value`，
 * 所以语言一换，用到它的地方会自动重渲染。
 */
import { ref } from 'vue'

export const LOCALES = [
  { code: 'zh', label: '中', htmlLang: 'zh-CN' },
  { code: 'en', label: 'EN', htmlLang: 'en' },
]

const STORAGE_KEY = 'mchub-site-locale'

function detect() {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved && LOCALES.some((l) => l.code === saved)) return saved
  } catch {
    /* 无痕模式下 localStorage 会抛异常，忽略即可 */
  }
  // 首次访问跟随浏览器语言，中文环境默认给中文。
  // 这里用 typeof 兜底是因为 SSR 环境（vite 的 ssrLoadModule）里没有 navigator，
  // 直接读 navigator.language 会让冒烟测试整个炸掉
  const lang = typeof navigator === 'undefined' ? '' : navigator.language || ''
  return String(lang).toLowerCase().startsWith('zh') ? 'zh' : 'en'
}

export const locale = ref(detect())

export function setLocale(code) {
  if (!LOCALES.some((l) => l.code === code)) return
  locale.value = code
  try {
    localStorage.setItem(STORAGE_KEY, code)
  } catch {
    /* 同上 */
  }
  const target = LOCALES.find((l) => l.code === code)
  document.documentElement.setAttribute('lang', target.htmlLang)
}

/** 两个语言之间来回切 */
export function toggleLocale() {
  setLocale(locale.value === 'zh' ? 'en' : 'zh')
}

/**
 * 取当前语言的文案。
 * 传进来的不是双语对象（普通字符串/数组/undefined）就原样返回，
 * 所以项目名、链接这些语言无关的字段照样能直接丢进来。
 */
export function t(value) {
  if (
    value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    ('zh' in value || 'en' in value)
  ) {
    return value[locale.value] ?? value.zh ?? value.en ?? ''
  }
  return value
}

// 模块加载时同步一次 <html lang>，让浏览器和读屏软件知道当前是哪种语言
if (typeof document !== 'undefined') {
  const current = LOCALES.find((l) => l.code === locale.value)
  if (current) document.documentElement.setAttribute('lang', current.htmlLang)
}
