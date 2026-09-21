<script setup>
import { site } from '../data/site.js'
import { t } from '../composables/useLocale.js'
</script>

<template>
  <section id="features" class="section features">
    <div class="container">
      <div class="section-head reveal">
        <h2 class="section-title font-display">{{ t(site.featuresSection.title) }}</h2>
        <p class="section-sub">{{ t(site.featuresSection.subtitle) }}</p>
      </div>

      <div class="cards">
        <article v-for="(item, i) in site.featuresSection.items" :key="i" class="card reveal">
          <div class="card-tag">{{ String(i + 1).padStart(2, '0') }}</div>
          <h3 class="card-title font-display">{{ t(item.title) }}</h3>
          <p class="card-body">{{ t(item.body) }}</p>
        </article>
      </div>

      <div class="extras reveal">
        <span class="extras-label">{{ t(site.featuresSection.extraTitle) }}</span>
        <div class="chips">
          <span v-for="(x, i) in site.featuresSection.extras" :key="i" class="chip">{{ t(x) }}</span>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.cards {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 22px;
}
.card {
  position: relative;
  padding: 30px 28px;
  border-radius: var(--radius);
  border: 1px solid var(--border);
  background: var(--panel);
  overflow: hidden;
  transition: transform 0.25s var(--ease), border-color 0.25s var(--ease),
    background-color 0.25s var(--ease);
}
.card::before {
  content: '';
  position: absolute;
  inset: 0;
  background: radial-gradient(120% 90% at 50% 0%, rgba(74, 162, 111, 0.12), transparent 60%);
  opacity: 0;
  transition: opacity 0.25s var(--ease);
}
.card:hover {
  transform: translateY(-4px);
  border-color: rgba(74, 162, 111, 0.35);
  background: var(--panel-strong);
}
.card:hover::before {
  opacity: 1;
}
.card-tag {
  font-size: 13px;
  font-weight: 700;
  color: var(--accent-bright);
  letter-spacing: 0.1em;
}
.card-title {
  position: relative;
  margin-top: 14px;
  font-size: 21px;
  font-weight: 700;
}
.card-body {
  position: relative;
  margin-top: 12px;
  color: var(--text-muted);
  font-size: 15px;
}

.extras {
  margin-top: 52px;
  text-align: center;
}
.extras-label {
  font-size: 14px;
  color: var(--text-dim);
  letter-spacing: 0.04em;
  margin-right: 14px;
}
.chips {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 10px;
  justify-content: center;
}
.chip {
  padding: 7px 16px;
  border-radius: 999px;
  border: 1px solid var(--border);
  background: var(--panel);
  color: var(--text-muted);
  font-size: 14px;
}

@media (max-width: 900px) {
  .cards {
    grid-template-columns: 1fr;
  }
}
</style>