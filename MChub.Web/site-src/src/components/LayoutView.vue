<script setup>
import { ref, onMounted, onUnmounted } from 'vue'
import TitleBar from './TitleBar.vue'
import FooterView from './FooterView.vue'
import { icons } from '../data/icons.js'

/* ── 滚动驱动的双层背景 ──────────────────────────────────────
   两层 fixed 渐变背景，透明度随滚动进度互补：
   往上滚一点，上层从 0.1 淡到 0.4、下层从 0.6 淡到 0.2。
   更新直接写 DOM style，不经过响应式，省一次 patch。 */
const upBg = ref(null)
const downBg = ref(null)
const scrollRatio = ref(0)

const upOpacity = ref(0.1)
const downOpacity = ref(0.6)
let rafId = null

const lerp = (from, to, t) => from + (to - from) * t

function paint() {
  if (upBg.value) upBg.value.style.opacity = String(upOpacity.value)
  if (downBg.value) downBg.value.style.opacity = String(downOpacity.value)
}

function tick() {
  const range = window.innerHeight * 0.8
  const p = Math.min(1, Math.max(0, window.scrollY / range))

  upOpacity.value = lerp(0.1, 0.4, p)
  downOpacity.value = lerp(0.6, 0.2, p)
  // 顶栏和回顶按钮用的进度：滚过 55% 屏高就算满
  scrollRatio.value = Math.min(1, Math.max(0, window.scrollY / (window.innerHeight * 0.55)))

  paint()
  // 走完这一段就停掉 rAF，别空转；下次滚动再重启
  if (p < 1) rafId = requestAnimationFrame(tick)
}

function onScroll() {
  if (rafId) cancelAnimationFrame(rafId)
  tick()
}

function toTop() {
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

onMounted(() => {
  paint()
  window.addEventListener('scroll', onScroll, { passive: true })
})

onUnmounted(() => {
  window.removeEventListener('scroll', onScroll)
  if (rafId) cancelAnimationFrame(rafId)
})
</script>

<template>
  <div>
    <div ref="upBg" class="up-background" aria-hidden="true"></div>
    <div ref="downBg" class="down-background" aria-hidden="true"></div>

    <div class="layout-content">
      <TitleBar :scroll-ratio="scrollRatio" />
      <slot />
      <FooterView />
    </div>

    <!-- 回到顶部 -->
    <div class="to-top" :class="{ show: scrollRatio >= 1 }" @click="toTop">
      <div class="ico" v-html="icons.arrowUp"></div>
    </div>
  </div>
</template>

<style scoped>
.layout-content {
  position: relative;
  z-index: 1;
  min-height: 100vh;
}

/* ── 两层背景：全用 CSS 径向渐变堆出来，没有图片 ── */
.up-background,
.down-background {
  position: fixed;
  z-index: -1;
  min-width: 100vw;
  min-height: 100vh;
  will-change: opacity;
  transform: translateZ(0);
  transition: opacity 0.1s linear;
}

.up-background {
  background-color: #0c0d0b;
  /* 极淡网格线（ECP 风的 techy 底纹），外加两束克制的品牌微光 */
  background-image:
    linear-gradient(to right, var(--grid-line) 1px, transparent 1px),
    linear-gradient(to bottom, var(--grid-line) 1px, transparent 1px),
    radial-gradient(110% 90% at 18% 0%, rgba(0, 224, 255, 0.10), transparent 55%),
    radial-gradient(120% 110% at 100% 100%, rgba(183, 148, 255, 0.12), transparent 55%);
  background-size: 64px 64px, 64px 64px, 100% 100%, 100% 100%;
}

.down-background {
  background-color: transparent;
  background-image: radial-gradient(
    ellipse 80% 45% at 50% 105%,
    rgba(0, 224, 255, 0.07),
    transparent 70%
  );
}

/* ── 回到顶部 ── */
.to-top {
  position: fixed;
  z-index: 100;
  bottom: 25px;
  right: 20px;
  width: 48px;
  height: 48px;
  border-radius: 50%;
  background-color: #ffffffe6;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  opacity: 0;
  pointer-events: none;
  transform: translateY(20px);
  transition: all 0.2s ease;
}

.to-top.show {
  opacity: 1;
  pointer-events: auto;
  transform: translateY(0);
}

.ico {
  width: 25px;
  height: 25px;
  position: relative;
  top: -1px;
}

.ico :deep(svg) {
  width: 100%;
  height: 100%;
  fill: #000;
}
</style>