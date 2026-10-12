<script setup>
import { onMounted, onUnmounted } from 'vue'
import { site } from '../data/site.js'
import { t } from '../composables/useLocale.js'

/* 卡片滚进视口才淡入上移。直接在 mounted 里查 DOM ——
   列表是静态数据、挂载时已经渲染完，不需要用 ref 回调一个个收集 */
let io = null

onMounted(() => {
  const cards = document.querySelectorAll('.reveal-card')
  if (!cards.length) return

  io = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add('animate-in')
          io.unobserve(entry.target)
        }
      })
    },
    { threshold: 0.1 },
  )

  cards.forEach((el) => io.observe(el))
})

onUnmounted(() => {
  io?.disconnect()
})
</script>

<template>
  <!-- 功能：直接占满首屏往下滚动就能看到 -->
  <section id="feature" class="about">
    <div class="section-head">
      <div>
        <p class="eyebrow">01 / FEATURES</p>
        <h2 class="section-title">{{ t(site.about.featuresTitle) }}</h2>
      </div>
    </div>
    <div class="card-grid">
      <div v-for="(f, i) in site.features" :key="f.title.zh" class="reveal-card">
        <span class="card-index">{{ String(i + 1).padStart(2, '0') }}</span>
        <h3 class="item-title">{{ t(f.title) }}</h3>
        <p class="item-desc">{{ t(f.desc) }}</p>
      </div>
    </div>
  </section>

  <!-- 关于 -->
  <section id="about" class="about">
    <div class="section-head">
      <div>
        <p class="eyebrow">02 / ABOUT</p>
        <h2 class="section-title">{{ t(site.about.title) }}</h2>
      </div>
    </div>
    <div class="intro">
      <p v-for="p in t(site.about.paragraphs)" :key="p">{{ p }}</p>
    </div>
  </section>

  <!-- 下载 -->
  <section id="download" class="about">
    <div class="section-head">
      <div>
        <p class="eyebrow">03 / DOWNLOAD</p>
        <h2 class="section-title">{{ t(site.about.downloadTitle) }}</h2>
      </div>
    </div>

    <div class="card-grid">
      <div v-for="(plat, i) in site.platforms" :key="plat.name + plat.sub" class="reveal-card">
        <span class="card-index">{{ String(i + 1).padStart(2, '0') }}</span>
        <h3 class="item-title">{{ plat.name }}</h3>
        <p class="item-sub">{{ plat.sub }}</p>
        <div class="download-links">
          <a
            v-for="a in plat.assets"
            :key="a.file"
            class="dl-link"
            :href="`${site.downloadBase}/${a.file}`"
            target="_blank"
            rel="noopener noreferrer"
          >
            {{ t(a.label) }}
          </a>
        </div>
      </div>
    </div>

    <p class="more">
      <a :href="site.releasesUrl" target="_blank" rel="noopener noreferrer">
        {{ t({ zh: '在 GitHub 上看全部版本', en: 'See all releases on GitHub' }) }} →
      </a>
    </p>
  </section>
</template>

<style scoped>
.about {
  max-width: 1200px;
  margin: 0 auto;
  padding: 80px 24px 140px;
}

.section-title {
  font-size: clamp(34px, 4.6vw, 60px);
  line-height: 1.05;
  letter-spacing: -0.04em;
  font-weight: 700;
  margin: 0;
  color: var(--text);
}

.intro {
  max-width: 760px;
  line-height: 1.85;
  color: var(--text-muted);
  font-size: 1.05rem;
}

.intro p + p {
  margin-top: 1rem;
}

/* ── 卡片网格（功能 / 下载平台共用） ── */
.card-grid {
  display: grid;
  grid-template-columns: repeat(1, minmax(0, 1fr));
  gap: 16px;
}

.reveal-card {
  position: relative;
  display: block;
  padding: 26px 24px 24px;
  border: 1px solid var(--line);
  border-radius: 14px;
  background-color: var(--surface);
  color: var(--text);
  /* 收起来的状态，等 IntersectionObserver 加 .animate-in */
  opacity: 0;
  transform: translateY(16px);
  transition:
    opacity 0.7s ease-out,
    transform 0.7s ease-out,
    border-color 0.3s ease,
    background-color 0.3s ease;
}

.reveal-card.animate-in {
  opacity: 1;
  transform: translateY(0);
}

.reveal-card:hover {
  border-color: var(--accent);
  background-color: var(--surface-2);
  transform: translateY(-4px);
}

/* 卡片左上角等宽序号（ECP 风） */
.card-index {
  font-family: var(--mono);
  font-size: 12px;
  letter-spacing: 1px;
  color: var(--text-dim);
  display: block;
  margin-bottom: 14px;
}

.reveal-card:hover .card-index {
  color: var(--accent);
}

.item-title {
  font-size: 1.2rem;
  font-weight: 700;
  margin-bottom: 0.5rem;
  word-break: break-word;
  letter-spacing: -0.01em;
}

.item-sub {
  font-size: 0.85rem;
  color: var(--text-muted);
  margin-bottom: 0.75rem;
}

.item-desc {
  font-size: 0.9rem;
  line-height: 1.65;
  color: var(--text-muted);
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

/* ── 下载链接 ── */
.download-links {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 10px;
}

.dl-link {
  display: inline-block;
  padding: 6px 13px;
  border-radius: 999px;
  font-size: 0.8rem;
  color: var(--text);
  background: transparent;
  border: 1px solid var(--line-strong);
  transition: all 0.25s ease;
}

.dl-link:hover {
  background: var(--accent-soft);
  border-color: var(--accent);
  color: var(--accent);
}

.more {
  margin-top: 36px;
  text-align: right;
}

.more a {
  font-size: 0.9rem;
  color: var(--text-muted);
  border-bottom: 1px solid var(--line-strong);
  padding-bottom: 2px;
  transition: color 0.25s ease, border-color 0.25s ease;
}

.more a:hover {
  color: var(--accent);
  border-color: var(--accent);
}

@media (min-width: 768px) {
  .card-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (min-width: 1200px) {
  .card-grid {
    grid-template-columns: repeat(3, minmax(0, 1fr));
  }
}

@media (max-width: 648px) {
  .about {
    padding: 56px 18px 90px;
  }

  .section-head {
    margin-bottom: 30px;
  }

  .more {
    text-align: left;
  }
}
</style>