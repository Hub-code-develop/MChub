<script setup>
import { computed } from 'vue'
import { icons } from '../data/icons.js'
import { site } from '../data/site.js'
import { t } from '../composables/useLocale.js'

const props = defineProps({
  scrollRatio: { type: Number, default: 0 },
})

// 滚过一屏（scrollRatio 到 1）顶栏才浮出来
const shown = computed(() => props.scrollRatio >= 1)
</script>

<template>
  <header class="title-container" :class="{ show: shown }">
    <div class="content">
      <div class="left">
        <img class="avatar" :src="site.avatar" alt="MChub" />
        <span class="name">{{ site.name }}</span>
      </div>

      <a class="g-btn" :href="site.contact.url" target="_blank" rel="noopener noreferrer">
        <span class="btn-label">{{ t(site.buttons.topbarContact) }}</span>
        <div class="g-btn-icon" v-html="icons.chat"></div>
      </a>
    </div>
  </header>
</template>

<style scoped>
.title-container {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  padding: 20px 32px;
  box-sizing: border-box;
  z-index: 100;
  background: transparent;
  backdrop-filter: blur(2px) saturate(90%);
  -webkit-backdrop-filter: blur(2px) saturate(90%);
  /* 底边渐隐，滚上去的时候不会留一条硬边 */
  mask-image: linear-gradient(
    to bottom,
    black 0%,
    black 60%,
    rgba(0, 0, 0, 0.8) 80%,
    transparent 100%
  );
  -webkit-mask-image: linear-gradient(
    to bottom,
    black 0%,
    black 60%,
    rgba(0, 0, 0, 0.8) 80%,
    transparent 100%
  );
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.3s ease;
}

.title-container.show {
  opacity: 1;
  pointer-events: auto;
}

.content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  max-width: 1200px;
  margin: 0 auto;
}

.left {
  display: flex;
  align-items: center;
  gap: 12px;
}

.avatar {
  width: 42px;
  height: 42px;
  border-radius: 12px;
  object-fit: cover;
  transition: all 0.2s ease;
}

.name {
  font-size: 18px;
  font-weight: 700;
  letter-spacing: -0.5px;
  line-height: 1;
}

.g-btn {
  display: inline-flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  border: 1px solid var(--accent);
  border-radius: 0;
  padding: 12px 18px;
  font-size: 15px;
  font-weight: 600;
  color: #04121a;
  background: var(--accent);
  transition: transform 0.3s var(--ease), background 0.3s var(--ease);
  clip-path: polygon(0 0, 100% 0, 100% calc(100% - 10px), calc(100% - 10px) 100%, 0 100%);
}

.g-btn:hover {
  background: #5cf0ff;
  transform: translateY(-2px);
}

.btn-label {
  position: relative;
  top: 1px;
}

.g-btn-icon {
  width: 18px;
  height: 18px;
  margin-left: 0;
  fill: #04121a;
  transition: transform 0.3s ease;
}

.g-btn-icon :deep(svg) {
  width: 100%;
  height: 100%;
}

.g-btn:hover .g-btn-icon {
  transform: scale(1.1);
}

@media (max-width: 648px) {
  .title-container {
    padding: 14px 18px;
  }

  .name {
    font-size: 16px;
  }
}
</style>