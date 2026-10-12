<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { icons } from '../data/icons.js'
import { site } from '../data/site.js'
import { t, locale, toggleLocale } from '../composables/useLocale.js'

/* ── 交互 ─────────────────────────────────────────────────── */
const scrolled = ref(false)
const onScroll = () => {
  scrolled.value = window.scrollY > 0
}

const visibleSocials = computed(() => site.socials.filter((s) => s.url))

function scrollToDownload() {
  document.getElementById('download')?.scrollIntoView({ behavior: 'smooth' })
}

onMounted(() => {
  window.addEventListener('scroll', onScroll, { passive: true })
})

onUnmounted(() => {
  window.removeEventListener('scroll', onScroll)
})
</script>

<template>
  <div class="header-container">
    <h2 class="side-text" :class="{ hidden: scrolled }">
      WELCOME TO {{ site.watermark.toUpperCase() }}
    </h2>

    <div class="content">
      <!-- MChub logo -->
      <img class="logo" :src="site.avatar" alt="MChub logo" />

      <div class="content-text">
        <p class="eyebrow animated" style="animation-delay: 0.05s">
          {{ site.watermark.toUpperCase() }} / OPEN SOURCE
        </p>

        <p class="p-input animated" style="animation-delay: 0.1s">
          <span class="c-user">{{ site.user }}@{{ site.host }}</span
          ><span class="c-sep">:</span><span class="c-path">~</span
          ><span class="c-sep">$</span><span class="c-cmd"> {{ site.terminal.command }}</span>
        </p>

        <h1 class="animated" style="animation-delay: 0.2s">
          <span class="c-arrow">&gt;</span
          ><span class="c-name">{{ site.terminal.output }}</span
          ><span class="cursor">_</span>
        </h1>

        <p class="p-font animated" style="animation-delay: 0.3s">
          <span class="c-dim">[ </span>
          <template v-for="(role, i) in t(site.terminal.roles)" :key="role">
            <span v-if="i > 0" class="c-dim"> | </span>
            <span class="c-white">{{ role }}</span>
          </template>
          <span class="c-dim"> ]</span>
        </p>

        <p v-if="t(site.terminal.motto)" class="note animated" style="animation-delay: 0.4s">
          &quot;{{ t(site.terminal.motto) }}&quot;
        </p>

        <p v-if="t(site.terminal.status)" class="p-eye animated" style="animation-delay: 0.5s">
          <span class="c-sep slashes">//</span>{{ t(site.terminal.status) }}
        </p>

        <div class="apps">
          <a
            v-for="(s, i) in visibleSocials"
            :key="s.name"
            class="app animated-fade"
            :style="{ animationDelay: 0.55 + i * 0.1 + 's' }"
            :title="s.name"
            :href="s.url"
            target="_blank"
            rel="noopener noreferrer"
          >
            <div class="bg" :style="{ backgroundColor: s.color }"></div>
            <div class="icon" :style="{ fill: s.color }" v-html="s.icon"></div>
          </a>

          <!-- 语言切换：挨着社交图标放，外观统一，而且首屏就能看见 -->
          <button
            class="app locale-toggle animated-fade"
            :style="{ animationDelay: 0.55 + visibleSocials.length * 0.1 + 's' }"
            :title="locale === 'zh' ? 'Switch to English' : '切换到中文'"
            @click="toggleLocale"
          >
            {{ locale === 'zh' ? 'EN' : '中' }}
          </button>
        </div>

        <div class="btns animated" style="animation-delay: 0.85s">
          <button class="btn" @click="scrollToDownload">
            <span>{{ t(site.buttons.download) }}</span>
            <div class="btn-icon" v-html="icons.arrowRight"></div>
          </button>
          <a class="g-btn" :href="site.contact.url" target="_blank" rel="noopener noreferrer">
            <span>{{ t(site.buttons.contact) }}</span>
            <div class="g-btn-icon" v-html="icons.chat"></div>
          </a>
        </div>
      </div>
    </div>

    <div class="down" :class="{ hidden: scrolled }" @click="scrollToDownload">
      <div class="down-icon" v-html="icons.arrowDown"></div>
    </div>
  </div>
</template>

<style scoped>
.header-container {
  width: 100vw;
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  position: relative;
}

.content {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 70px;
  max-width: 1152px;
  padding: 0 24px;
}

/* ── MChub logo ── */
.logo {
  flex-shrink: 0;
  width: 300px;
  height: 300px;
  max-width: 34vw;
  max-height: 34vw;
  border-radius: 24px;
  object-fit: cover;
  box-shadow: 0 0 60px rgba(0, 0, 0, 0.4);
}

/* ── 左侧竖排文字 ── */
.side-text {
  writing-mode: vertical-rl;
  position: fixed;
  top: 50%;
  left: 10px;
  transform: translateY(-50%);
  font-size: 1.25rem;
  font-weight: 700;
  letter-spacing: 0.1em;
  line-height: 1.75rem;
  transition: opacity 0.3s ease;
  color: var(--text);
}

.hidden {
  opacity: 0 !important;
  pointer-events: none !important;
}

/* ── 文案 ── */
.content-text {
  max-width: 620px;
}

.c-user {
  color: var(--accent);
}
.c-sep {
  color: var(--text-dim);
}
.c-path {
  color: var(--accent-2);
}
.c-cmd {
  color: #f2f2f2;
}
.c-arrow {
  color: #8b949e;
}
.c-name {
  color: #fff;
}
.c-dim {
  color: #737a83;
}
.c-white {
  color: #fff;
}

.p-input {
  margin-bottom: 16px;
  font-size: 1rem;
}

.cursor {
  color: var(--accent);
  animation: blink 1.2s step-end infinite;
}

@keyframes blink {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0;
  }
}

h1 {
  font-size: 60px;
  margin-bottom: 24px;
  line-height: 1.1;
}

.p-font {
  font-size: 1rem;
  margin-bottom: 24px;
}

.note {
  border-left: 3px solid var(--accent-2);
  padding: 8px 16px;
  margin-bottom: 16px;
  font-size: 18px;
  color: #fff;
}

.p-eye {
  color: #585f68;
  font-size: 14px;
  margin-bottom: 24px;
}

.slashes {
  margin-right: 8px;
}

/* ── 社交图标 ── */
.apps {
  display: flex;
  gap: 16px;
  margin-top: 24px;
  margin-bottom: 24px;
}

.app {
  position: relative;
  width: 44px;
  height: 44px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: transform 0.2s ease;
}

.app:hover {
  transform: scale(1.1);
}

.bg {
  position: absolute;
  width: 44px;
  height: 44px;
  border-radius: 999px;
  opacity: 0.2;
  transition: opacity 0.2s ease;
}

.app:hover .bg {
  opacity: 0.3;
}

/* 语言切换钮：外观跟旁边那几个社交圆钮保持一致 */
.locale-toggle {
  padding: 0;
  border: 1px solid #ffffff33;
  border-radius: 999px;
  background: #ffffff14;
  color: #fff;
  font-family: inherit;
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.locale-toggle:hover {
  background: #ffffff26;
  border-color: #ffffff59;
}

.icon {
  position: relative;
  width: 24px;
  height: 24px;
}

.icon :deep(svg) {
  width: 100%;
  height: 100%;
}

/* ── 按钮 ── */
.btns {
  display: flex;
  flex-wrap: wrap;
  gap: 15px;
}

.btn,
.g-btn {
  display: inline-flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 14px 22px;
  border: 1px solid var(--line-strong);
  font-size: 15px;
  font-weight: 600;
  transition: transform 0.3s var(--ease), background 0.3s var(--ease),
    border-color 0.3s var(--ease), color 0.3s var(--ease);
  /* 切角按钮（clip-path 缺一角） */
  clip-path: polygon(0 0, 100% 0, 100% calc(100% - 11px), calc(100% - 11px) 100%, 0 100%);
}

.btn {
  color: var(--text);
  background-color: transparent;
}

.btn:hover {
  border-color: var(--accent);
  color: var(--accent);
  transform: translateY(-3px);
}

.g-btn {
  color: #04121a;
  background-color: var(--accent);
  border-color: var(--accent);
}

.g-btn:hover {
  background-color: #5cf0ff;
  border-color: #5cf0ff;
  transform: translateY(-3px);
}

.btn-icon,
.g-btn-icon {
  width: 18px;
  height: 18px;
  margin-left: 0;
  position: relative;
  transition: all 0.3s ease;
}

.btn-icon {
  fill: currentColor;
}

.btn:hover .btn-icon {
  transform: translateX(5px);
}

.g-btn-icon {
  fill: #04121a;
}

.g-btn:hover .g-btn-icon {
  transform: scale(1.15);
}

.btn-icon :deep(svg),
.g-btn-icon :deep(svg) {
  width: 100%;
  height: 100%;
}

/* ── 向下箭头 ── */
.down {
  position: fixed;
  bottom: 24px;
  width: 34px;
  cursor: pointer;
  animation: bounce 1s linear infinite;
  transition: opacity 0.3s ease;
}

.down-icon {
  width: 34px;
  height: 34px;
}

.down-icon :deep(svg) {
  width: 100%;
  height: 100%;
  fill: #5f656c;
}

@keyframes bounce {
  0%,
  100% {
    animation-timing-function: cubic-bezier(0.8, 0, 1, 1);
    transform: translateY(-25%);
  }
  50% {
    animation-timing-function: cubic-bezier(0, 0, 0.2, 1);
    transform: translateY(0);
  }
}

/* ── 首屏错峰淡入 ── */
.animated {
  opacity: 0;
  animation: fadeInUp 0.5s ease-out forwards;
}

@keyframes fadeInUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.animated-fade {
  opacity: 0;
  animation: fadeIn 0.5s ease-out forwards;
}

@keyframes fadeIn {
  from {
    opacity: 0;
  }
  to {
    opacity: 1;
  }
}

/* ── 响应式 ── */
@media (max-width: 1250px) {
  .logo {
    display: block;
  }

  .content {
    flex-direction: column;
    gap: 24px;
    width: calc(100vw - 66px);
    text-align: center;
  }

  h1 {
    font-size: 38px;
    margin-bottom: 8px;
  }

  .note {
    font-size: 14px;
    margin-bottom: 12px;
  }

  .p-font,
  .p-eye {
    font-size: 0.875rem;
  }

  .apps {
    margin-top: 12px;
    margin-bottom: 12px;
    justify-content: center;
  }

  .btns {
    justify-content: center;
  }
}

@media (max-height: 920px) and (min-width: 1251px) {
  h1 {
    font-size: 44px;
    margin-bottom: 14px;
  }

  .content-text {
    max-width: 520px;
  }
}

@media (max-width: 648px) {
  .side-text {
    left: 0;
  }

  h1 {
    font-size: 34px;
  }
}
</style>