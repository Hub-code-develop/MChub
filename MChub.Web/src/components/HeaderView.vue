<script setup>
import { site } from '../data/site.js'
import { t } from '../composables/useLocale.js'

function isAnchor(c) {
  return c.target && !c.url
}

function onClick(e, c) {
  if (isAnchor(c)) {
    const el = document.querySelector(c.target)
    if (el) {
      e.preventDefault()
      el.scrollIntoView({ behavior: 'smooth' })
    }
  }
}
</script>

<template>
  <section class="hero">
    <div class="container hero-inner">
      <div class="reveal hero-badge">{{ t(site.badgetext) }}</div>

      <img class="reveal hero-logo" src="/mchub-logo.png" alt="MChub" />

      <h1 class="reveal hero-title font-display">{{ site.name }}</h1>
      <p class="reveal hero-tagline">{{ t(site.tagline) }}</p>
      <p class="reveal hero-intro">{{ t(site.intro) }}</p>

      <div class="reveal hero-cta">
        <a
          v-for="c in site.heroCtas"
          :key="c.label.zh"
          class="btn"
          :class="c.type === 'primary' ? 'btn-primary' : 'btn-ghost'"
          :href="c.url || c.target"
          @click="onClick($event, c)"
        >{{ t(c.label) }}</a>
      </div>
    </div>
  </section>
</template>

<style scoped>
.hero {
  min-height: 100vh;
  display: flex;
  align-items: center;
  padding: 120px 0 80px;
}
.hero-inner {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
}
.hero-badge {
  display: inline-flex;
  padding: 6px 16px;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--accent-soft);
  color: var(--accent-bright);
  font-size: 13px;
  font-weight: 600;
  letter-spacing: 0.04em;
  margin-bottom: 30px;
}
.hero-logo {
  width: 108px;
  height: 108px;
  border-radius: 26px;
  box-shadow: 0 24px 60px -20px rgba(74, 162, 111, 0.55);
  margin-bottom: 28px;
}
.hero-title {
  font-size: clamp(52px, 9vw, 96px);
  font-weight: 900;
  line-height: 1.02;
  letter-spacing: -0.02em;
  background: linear-gradient(180deg, #fff, var(--accent-bright));
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
}
.hero-tagline {
  margin-top: 18px;
  font-size: clamp(20px, 3vw, 30px);
  font-weight: 700;
  color: var(--text);
}
.hero-intro {
  margin-top: 16px;
  max-width: 560px;
  font-size: 16px;
  color: var(--text-muted);
}
.hero-cta {
  display: flex;
  gap: 14px;
  margin-top: 40px;
  flex-wrap: wrap;
  justify-content: center;
}

/* 分隔提示：让首次触发的 reveal 稍微错开 */
.hero .reveal:nth-child(2),
.hero .reveal:nth-child(3),
.hero .reveal:nth-child(4) {
  transition-delay: 0.06s;
}
</style>