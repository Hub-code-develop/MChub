<script setup>
import { ref, computed } from 'vue'
import { site } from '../data/site.js'
import { t } from '../composables/useLocale.js'

const platform = ref(site.downloadSection.platforms[0])
const source = ref(site.downloadSection.sources[0])

const setPlatform = (p) => {
  platform.value = p
}

const setSource = (s) => {
  source.value = s
}

const href = (url) => source.value.prefix + url
</script>

<template>
  <section id="download" class="section download">
    <div class="container">
      <div class="section-head reveal">
        <h2 class="section-title font-display">{{ t(site.downloadSection.title) }}</h2>
        <p class="section-sub">{{ t(site.downloadSection.subtitle) }}</p>
      </div>

      <div class="reveal panel">
        <!-- 平台选择 -->
        <div class="seg" role="tablist">
          <button
            v-for="p in site.downloadSection.platforms"
            :key="p.id"
            class="seg-item"
            :class="{ active: platform.id === p.id }"
            @click="setPlatform(p)"
          >{{ p.name }}</button>
        </div>

        <p class="platform-note">{{ platform.note }}</p>

        <!-- 下载源 -->
        <div class="source-row">
          <span class="source-label">{{ t(site.downloadSection.sourceHint) }}</span>
          <div class="source-tabs">
            <button
              v-for="s in site.downloadSection.sources"
              :key="s.id"
              class="source-tab"
              :class="{ active: source.id === s.id }"
              @click="setSource(s)"
            >{{ t(s.label) }}</button>
          </div>
        </div>

        <!-- 下载分组 -->
        <div class="groups">
          <div v-for="(g, i) in platform.groups" :key="i" class="group">
            <div class="group-label">{{ t(g.label) }}</div>
            <div class="group-items">
              <a
                v-for="(item, j) in g.items"
                :key="j"
                class="down-btn"
                :href="href(item.url)"
                target="_blank"
                rel="noopener"
              >
                <span class="down-name">{{ item.label }}</span>
                <span class="down-arrow">↓</span>
              </a>
            </div>
          </div>
        </div>
      </div>

      <p class="reveal footnote">{{ t(site.downloadSection.footnote) }}</p>
      <div class="reveal footnote-link">
        <a href="./install/index.html">{{ t(site.downloadSection.installGuide) }} →</a>
      </div>
    </div>
  </section>
</template>

<style scoped>
.panel {
  padding: 30px 32px 34px;
  border-radius: var(--radius);
  border: 1px solid var(--border);
  background: var(--panel);
}

.seg {
  display: inline-flex;
  padding: 4px;
  gap: 4px;
  border-radius: 12px;
  background: var(--bg-raised);
}
.seg-item {
  padding: 9px 22px;
  border-radius: 9px;
  color: var(--text-muted);
  font-size: 15px;
  font-weight: 600;
  transition: color 0.2s var(--ease), background-color 0.2s var(--ease);
}
.seg-item.active {
  color: #08120c;
  background: var(--accent);
}
.platform-note {
  margin-top: 14px;
  color: var(--text-dim);
  font-size: 14px;
}

.source-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 26px;
  padding-top: 22px;
  border-top: 1px dashed var(--border);
}
.source-label {
  color: var(--text-muted);
  font-size: 13px;
}
.source-tabs {
  display: inline-flex;
  gap: 6px;
  flex-wrap: wrap;
}
.source-tab {
  padding: 6px 16px;
  border-radius: 999px;
  border: 1px solid var(--border);
  color: var(--text-muted);
  font-size: 13px;
  transition: all 0.2s var(--ease);
}
.source-tab.active {
  color: var(--accent-bright);
  border-color: rgba(74, 162, 111, 0.5);
  background: var(--accent-soft);
}

.groups {
  margin-top: 26px;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 22px;
}
.group-label {
  font-size: 13px;
  color: var(--text-dim);
  letter-spacing: 0.04em;
  margin-bottom: 10px;
}
.group-items {
  display: flex;
  flex-direction: column;
  gap: 9px;
}
.down-btn {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 13px 16px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
  background: var(--panel-strong);
  transition: border-color 0.2s var(--ease), transform 0.2s var(--ease);
}
.down-btn:hover {
  border-color: rgba(74, 162, 111, 0.5);
  transform: translateY(-1px);
}
.down-name {
  font-size: 14px;
  font-weight: 600;
}
.down-arrow {
  color: var(--accent-bright);
  font-size: 15px;
}

.footnote {
  margin-top: 26px;
  text-align: center;
  color: var(--text-dim);
  font-size: 13px;
  max-width: 640px;
  margin-left: auto;
  margin-right: auto;
}
.footnote-link {
  text-align: center;
  margin-top: 14px;
}
.footnote-link a {
  color: var(--accent-bright);
  font-size: 14px;
  font-weight: 600;
}
.footnote-link a:hover {
  text-decoration: underline;
}
</style>