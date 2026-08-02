<template>
  <section class="appearance-section" aria-labelledby="appearance-heading">
    <div class="appearance-heading-row">
      <div>
        <h3 id="appearance-heading"><PhPalette /> Appearance</h3>
        <p>Choose a color theme and an independent light or dark mode.</p>
      </div>
      <img src="/bookmarkarr-logo.png" alt="" class="appearance-mark" aria-hidden="true" />
    </div>

    <div class="appearance-group">
      <div class="appearance-label">Theme</div>
      <div class="theme-grid" role="radiogroup" aria-label="Color theme">
        <button
          v-for="option in themeOptions"
          :key="option.id"
          type="button"
          class="theme-card"
          :class="{ selected: theme === option.id, branded: option.branded }"
          role="radio"
          :aria-checked="theme === option.id"
          @click="setTheme(option.id)"
        >
          <img v-if="option.branded" src="/bookmarkarr-logo.png" alt="" class="theme-logo" />
          <span v-else class="theme-swatches" aria-hidden="true">
            <span v-for="color in option.colors" :key="color" :style="{ background: color }" />
          </span>
          <span class="theme-copy">
            <strong>{{ option.name }}</strong>
            <small>{{ option.description }}</small>
          </span>
          <PhCheckCircle v-if="theme === option.id" class="selected-icon" weight="fill" />
        </button>
      </div>
    </div>

    <div class="appearance-group">
      <div class="appearance-label">Color mode</div>
      <div class="mode-picker" role="radiogroup" aria-label="Color mode">
        <button
          v-for="option in colorModeOptions"
          :key="option.id"
          type="button"
          :class="{ selected: colorMode === option.id }"
          role="radio"
          :aria-checked="colorMode === option.id"
          @click="setColorMode(option.id)"
        >
          <component :is="modeIcon(option.id)" />
          <span
            ><strong>{{ option.name }}</strong
            ><small>{{ option.description }}</small></span
          >
        </button>
      </div>
      <p class="appearance-note">
        Saved in this browser. Current display: <strong>{{ resolvedColorMode }}</strong
        >.
      </p>
    </div>
  </section>
</template>

<script setup lang="ts">
import { PhCheckCircle, PhDesktop, PhMoon, PhPalette, PhSun } from '@phosphor-icons/vue'
import { useAppearance, type ColorModePreference } from '@/services/appearance'

const {
  theme,
  colorMode,
  resolvedColorMode,
  themeOptions,
  colorModeOptions,
  setTheme,
  setColorMode,
} = useAppearance()

function modeIcon(mode: ColorModePreference) {
  if (mode === 'light') return PhSun
  if (mode === 'dark') return PhMoon
  return PhDesktop
}
</script>

<style scoped>
.appearance-section {
  padding: 1.25rem;
  border: 1px solid var(--border-color);
  border-radius: 12px;
  background: var(--card-bg);
  box-shadow: var(--app-card-shadow);
}

.appearance-heading-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--border-color);
}

.appearance-heading-row h3 {
  display: flex;
  align-items: center;
  gap: 0.55rem;
  margin: 0;
  color: var(--text-primary);
  font-size: 1.15rem;
  font-weight: 650;
}

.appearance-heading-row p,
.appearance-note {
  margin: 0.3rem 0 0;
  color: var(--text-muted);
  font-size: 0.86rem;
}

.appearance-mark {
  width: 64px;
  height: 64px;
  object-fit: contain;
}

.appearance-group {
  margin-top: 1.25rem;
}

.appearance-label {
  margin-bottom: 0.65rem;
  color: var(--text-primary);
  font-size: 0.9rem;
  font-weight: 650;
}

.theme-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.75rem;
}

.theme-card {
  position: relative;
  display: flex;
  align-items: center;
  gap: 0.85rem;
  min-height: 78px;
  padding: 0.85rem;
  border: 1px solid var(--border-color);
  border-radius: 10px;
  background: var(--input-bg);
  color: var(--text-color);
  text-align: left;
  cursor: pointer;
}

.theme-card:hover {
  border-color: var(--brand-400);
  background: var(--button-hover-bg);
}

.theme-card.selected {
  border-color: var(--brand-500);
  box-shadow: var(--focus-ring);
}

.theme-logo,
.theme-swatches {
  flex: 0 0 auto;
  width: 48px;
  height: 48px;
}

.theme-logo {
  object-fit: contain;
}

.theme-swatches {
  display: flex;
  overflow: hidden;
  border-radius: 50%;
  border: 2px solid rgba(255, 255, 255, 0.35);
}

.theme-swatches span {
  flex: 1;
}

.theme-copy {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.theme-copy strong,
.mode-picker strong {
  color: var(--text-primary);
  font-weight: 650;
}

.theme-copy small,
.mode-picker small {
  color: var(--text-muted);
  line-height: 1.3;
}

.selected-icon {
  position: absolute;
  top: 0.65rem;
  right: 0.65rem;
  color: var(--brand-500);
  font-size: 1.2rem;
}

.mode-picker {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.65rem;
}

.mode-picker button {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  padding: 0.75rem;
  border: 1px solid var(--border-color);
  border-radius: 9px;
  background: var(--input-bg);
  color: var(--text-color);
  cursor: pointer;
  text-align: left;
}

.mode-picker button > svg {
  flex: 0 0 auto;
  font-size: 1.25rem;
  color: var(--brand-500);
}

.mode-picker button > span {
  display: flex;
  flex-direction: column;
}

.mode-picker button:hover,
.mode-picker button.selected {
  border-color: var(--brand-500);
  background: var(--selected-bg);
}

.appearance-note strong {
  text-transform: capitalize;
  color: var(--text-secondary);
}

@media (max-width: 760px) {
  .theme-grid,
  .mode-picker {
    grid-template-columns: 1fr;
  }
}
</style>
