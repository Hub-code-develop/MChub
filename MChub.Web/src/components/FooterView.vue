<script setup>
import { ref } from 'vue'
import { icons } from '../data/icons.js'
import { site } from '../data/site.js'
import { locale, toggleLocale, t } from '../composables/useLocale.js'

const showQr = ref(false)

function act(e, link) {
  if (link.action === 'qq') {
    e.preventDefault()
    showQr.value = !showQr.value
  }
}
</script>

<template>
  <footer id="opensource" class="footer">
    <div class="container">
      <!-- 开源区 -->
      <div class="section open-source">
        <div class="section-head reveal">
          <h2 class="section-title font-display">{{ t(site.footer.openSourceTitle) }}</h2>
          <p class="section-sub">{{ t(site.footer.openSourceBody) }}</p>
        </div>

        <div class="os-links reveal">
          <a
            v-for="(l, i) in site.footer.links"
            :key="i"
            class="os-btn"
            :class="{ ghost: l.action !== 'repo' }"
            :href="l.url"
            @click="act($event, l)"
          >
            <span class="os-ico" v-if="l.action === 'repo'" v-html="icons.github"></span>
            <span class="os-ico qq" v-else-if="l.action === 'qq'">Q</span>
            {{ t(l.label) }}
          </a>
        </div>

        <div v-if="showQr" class="qr reveal in">
          <img src="/qq-group-qrcode.png" alt="QQ 群二维码" />
          <div class="qr-prompt">{{ t(site.footer.qqGroup.prompt) }} <strong>{{ site.footer.qqGroup.number }}</strong></div>
        </div>
      </div>

      <div class="meta">
        <p>{{ t(site.footer.copyright) }}</p>
        <p class="dim">{{ t(site.footer.disclaimer) }}</p>
        <div class="meta-row">
          <a class="dim-link" href="./policy/index.html">{{ t(site.footer.policyLink) }}</a>
          <button class="locale-toggle" @click="toggleLocale">
            当前语言：{{ locale === 'zh' ? '简体中文' : 'English' }} · 切换
          </button>
        </div>
      </div>
    </div>
  </footer>
</template>

<style scoped>
.footer .open-source {
  padding-bottom: 60px;
}
.os-links {
  display: flex;
  justify-content: center;
  flex-wrap: wrap;
  gap: 12px;
}
.os-btn {
  display: inline-flex;
  align-items: center;
  gap: 9px;
  padding: 12px 24px;
  border-radius: 999px;
  font-size: 15px;
  font-weight: 600;
  border: 1px solid var(--border);
  background: var(--panel);
  color: var(--text);
  transition: transform 0.2s var(--ease), border-color 0.2s var(--ease),
    background-color 0.2s var(--ease);
}
.os-btn:hover {
  transform: translateY(-1px);
  border-color: rgba(74, 162, 111, 0.5);
  background: var(--panel-strong);
}
.os-btn[class*='ghost'] {
  color: var(--text-muted);
}
.os-btn:not(.ghost) {
  background: var(--accent-soft);
  border-color: rgba(74, 162, 111, 0.4);
}
.os-ico {
  width: 18px;
  height: 18px;
  display: inline-flex;
  align-items: center;
}
.os-ico :deep(svg) {
  width: 100%;
  height: 100%;
}
.os-ico.qq {
  font-family: 'AlimamaFangYuanTi', sans-serif;
  font-size: 16px;
  justify-content: center;
  color: var(--accent-bright);
}

.qr {
  margin: 26px auto 0;
  width: fit-content;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  padding: 20px;
  border-radius: var(--radius);
  border: 1px solid var(--border);
  background: var(--panel);
  animation: fadeIn 0.25s var(--ease);
}
.qr img {
  border-radius: 10px;
  width: 180px;
  height: 180px;
}
.qr-prompt {
  color: var(--text-muted);
  font-size: 13px;
}
@keyframes fadeIn {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

.meta {
  margin-top: 20px;
  padding: 30px 0 40px;
  border-top: 1px solid var(--border);
  text-align: center;
  color: var(--text-muted);
  font-size: 13px;
  line-height: 1.8;
}
.meta .dim {
  color: var(--text-dim);
}
.meta-row {
  margin-top: 14px;
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  justify-content: center;
  align-items: center;
}
.dim-link {
  color: var(--text-muted);
}
.dim-link:hover {
  color: var(--text);
  text-decoration: underline;
}
.locale-toggle {
  color: var(--accent-bright);
  font-size: 13px;
}
.locale-toggle:hover {
  text-decoration: underline;
}
</style>