<script setup>
import { ref, onMounted, onBeforeUnmount } from 'vue'
import FooterView from './FooterView.vue'
import { icons } from '../data/icons.js'
import { site } from '../data/site.js'
import { locale, toggleLocale, t } from '../composables/useLocale.js'

/* ---- 双层渐变背景随滚动互补淡入淡出 ---- */
const upBg = ref(null)
const downBg = ref(null)
const upOpacity = ref(0.7)
const downOpacity = ref(0.4)
const scrolled = ref(false)
let rafId = null

const lerp = (a, b, t) => a + (b - a) * t

function paint() {
  if (upBg.value) upBg.value.style.opacity = String(upOpacity.value)
  if (downBg.value) downBg.value.style.opacity = String(downOpacity.value)
}

function tick() {
  const p = Math.min(1, Math.max(0, window.scrollY / (window.innerHeight * 0.9)))
  upOpacity.value = lerp(0.7, 0.4, p)
  downOpacity.value = lerp(0.4, 0.15, p)
  scrolled.value = window.scrollY > 40
  paint()
  if (p < 1) rafId = requestAnimationFrame(tick)
}

function onScroll() {
  if (scrolled.value !== (window.scrollY > 40)) scrolled.value = window.scrollY > 40
  if (rafId) cancelAnimationFrame(rafId)
  tick()
}

function toTop() {
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

function scrollToHash(e) {
  const a = e.target.closest('a[href^="#"]')
  if (!a) return
  const el = document.querySelector(a.getAttribute('href'))
  if (el) {
    e.preventDefault()
    el.scrollIntoView({ behavior: 'smooth' })
  }
}

let observer = null
function observeReveals() {
  observer = new IntersectionObserver(
    (entries) =>
      entries.forEach((en) => {
        if (en.isIntersecting) {
          en.target.classList.add('in')
          observer.unobserve(en.target)
        }
      }),
    { threshold: 0.12 }
  )
  document.querySelectorAll('.reveal').forEach((n) => observer.observe(n))
}

onMounted(() => {
  paint()
  window.addEventListener('scroll', onScroll, { passive: true })
  observeReveals()
})

onBeforeUnmount(() => {
  window.removeEventListener('scroll', onScroll)
  if (rafId) cancelAnimationFrame(rafId)
  observer?.disconnect()
})
</script>

<template>
  <div>
    <div ref="upBg" class="up-background" aria-hidden="true"></div>
    <div ref="downBg" class="down-background" aria-hidden="true"></div>

    <header class="topbar" :class="{ scrolled }" @click="scrollToHash">
      <div class="container topbar-inner">
        <a class="brand" href="#top" aria-label="MChub">
          <img class="brand-logo" src="/mchub-logo.png" alt="MChub" />
          <span class="brand-name font-display">MChub</span>
        </a>

        <nav class="nav">
          <a v-for="l in site.nav.links" :key="l.label.zh" class="nav-link" :href="l.target || l.url">
            <span>{{ t(l.label) }}</span>
          </a>
        </nav>

        <div class="topbar-right">
          <a class="icon-btn" :href="'https://github.com/CodeHub-develop/MChub'" target="_blank" rel="noopener" aria-label="GitHub" v-html="icons.github"></a>
          <button class="locale-btn" @click="toggleLocale">{{ locale === 'zh' ? 'EN' : '中' }}</button>
          <a class="btn btn-primary topbar-cta" :href="site.nav.cta.target">{{ t(site.nav.cta.label) }}</a>
        </div>
      </div>
    </header>

    <main id="top">
      <slot />
    </main>

    <FooterView />

    <div class="to-top" :class="{ show: scrolled }" @click="toTop" aria-label="回顶">
      <div class="ico" v-html="icons.arrowUp"></div>
    </div>
  </div>
</template>

<style scoped>
.up-background,
.down-background {
  position: fixed;
  z-index: -1;
  inset: 0;
  min-width: 100vw;
  min-height: 100vh;
  will-change: opacity;
  transform: translateZ(0);
  transition: opacity 0.12s linear;
}
.up-background {
  background-color: var(--bg);
  background-image:
    radial-gradient(120% 90% at 50% 0%, rgba(74, 162, 111, 0.16), transparent 55%),
    radial-gradient(140% 120% at 80% 110%, rgba(22, 48, 33, 0.55), transparent 60%);
}
.down-background {
  background-image:
    linear-gradient(var(--grid-color) var(--grid-line), transparent var(--grid-line)),
    linear-gradient(90deg, var(--grid-color) var(--grid-line), transparent var(--grid-line));
  background-size: var(--grid-size) var(--grid-size);
  opacity: 0.35;
}

/* ---- 顶部导航 ---- */
.topbar {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 50;
  backdrop-filter: blur(14px);
  background: transparent;
  transition: background-color 0.25s var(--ease), border-color 0.25s var(--ease);
  border-bottom: 1px solid transparent;
}
.topbar.scrolled {
  background: rgba(13, 18, 15, 0.72);
  border-bottom-color: var(--border);
}
.topbar-inner {
  display: flex;
  align-items: center;
  gap: 24px;
  height: 68px;
}
.brand {
  display: inline-flex;
  align-items: center;
  gap: 10px;
}
.brand-logo {
  width: 32px;
  height: 32px;
  border-radius: 8px;
}
.brand-name {
  font-size: 20px;
  font-weight: 700;
  letter-spacing: 0.01em;
}
.nav {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-left: auto;
}
.nav-link {
  padding: 7px 13px;
  border-radius: 999px;
  color: var(--text-muted);
  font-size: 14px;
  transition: color 0.2s var(--ease), background-color 0.2s var(--ease);
}
.nav-link:hover {
  color: var(--text);
  background: var(--panel);
}
.topbar-right {
  display: flex;
  align-items: center;
  gap: 10px;
}
.icon-btn {
  width: 38px;
  height: 38px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 10px;
  color: var(--text-muted);
  transition: color 0.2s var(--ease), background-color 0.2s var(--ease);
}
.icon-btn:hover {
  color: var(--text);
  background: var(--panel);
}
.icon-btn :deep(svg) {
  width: 18px;
  height: 18px;
}
.locale-btn {
  height: 38px;
  padding: 0 12px;
  border-radius: 10px;
  border: 1px solid var(--border);
  background: var(--panel);
  color: var(--text-muted);
  font-size: 13px;
  font-weight: 600;
  transition: color 0.2s var(--ease), background-color 0.2s var(--ease);
}
.locale-btn:hover {
  color: var(--text);
  background: var(--panel-strong);
}
.topbar-cta {
  padding: 9px 20px;
  font-size: 14px;
}

/* ---- 回顶 ---- */
.to-top {
  position: fixed;
  z-index: 40;
  right: 22px;
  bottom: 26px;
  width: 46px;
  height: 46px;
  border-radius: 50%;
  background: rgba(234, 242, 236, 0.92);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  opacity: 0;
  pointer-events: none;
  transform: translateY(18px);
  transition: all 0.22s var(--ease);
}
.to-top.show {
  opacity: 1;
  pointer-events: auto;
  transform: translateY(0);
}
.ico {
  width: 22px;
  height: 22px;
}
.ico :deep(svg) {
  width: 100%;
  height: 100%;
  fill: #0d120f;
}

@media (max-width: 900px) {
  .nav {
    display: none;
  }
}
</style>