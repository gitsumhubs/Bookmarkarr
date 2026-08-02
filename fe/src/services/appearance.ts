import { computed, readonly, ref } from 'vue'

export type ThemeId = 'bookmarkarr' | 'ocean' | 'forest' | 'plum'
export type ColorModePreference = 'system' | 'light' | 'dark'
export type ResolvedColorMode = 'light' | 'dark'

export interface ThemeOption {
  id: ThemeId
  name: string
  description: string
  colors: [string, string, string]
  branded?: boolean
}

export const THEME_OPTIONS: ThemeOption[] = [
  {
    id: 'bookmarkarr',
    name: 'Bookmarkarr',
    description: 'Navy, teal, and coral drawn from the Bookmarkarr mark.',
    colors: ['#071f49', '#11b8aa', '#ff5b42'],
    branded: true,
  },
  {
    id: 'ocean',
    name: 'Ocean',
    description: 'A straightforward blue palette.',
    colors: ['#0b3b66', '#2196f3', '#7dd3fc'],
  },
  {
    id: 'forest',
    name: 'Forest',
    description: 'A calm evergreen palette.',
    colors: ['#173f35', '#2f9e44', '#8ce99a'],
  },
  {
    id: 'plum',
    name: 'Plum',
    description: 'A simple purple palette.',
    colors: ['#3b235c', '#8b5cf6', '#d8b4fe'],
  },
]

export const COLOR_MODE_OPTIONS: Array<{
  id: ColorModePreference
  name: string
  description: string
}> = [
  { id: 'system', name: 'System', description: 'Follow this device.' },
  { id: 'light', name: 'Light', description: 'Always use light mode.' },
  { id: 'dark', name: 'Dark', description: 'Always use dark mode.' },
]

const THEME_STORAGE_KEY = 'bookmarkarr.appearance.theme'
const MODE_STORAGE_KEY = 'bookmarkarr.appearance.mode'
const DEFAULT_THEME: ThemeId = 'bookmarkarr'
const DEFAULT_MODE: ColorModePreference = 'system'

const theme = ref<ThemeId>(readTheme())
const colorMode = ref<ColorModePreference>(readMode())
const systemPrefersDark = ref(false)
let initialized = false
let mediaQuery: MediaQueryList | null = null

const resolvedColorMode = computed<ResolvedColorMode>(() =>
  colorMode.value === 'system' ? (systemPrefersDark.value ? 'dark' : 'light') : colorMode.value,
)

function isTheme(value: string | null): value is ThemeId {
  return THEME_OPTIONS.some((option) => option.id === value)
}

function isColorMode(value: string | null): value is ColorModePreference {
  return value === 'system' || value === 'light' || value === 'dark'
}

function readStorage(key: string): string | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage.getItem(key)
  } catch {
    return null
  }
}

function readTheme(): ThemeId {
  const stored = readStorage(THEME_STORAGE_KEY)
  return isTheme(stored) ? stored : DEFAULT_THEME
}

function readMode(): ColorModePreference {
  const stored = readStorage(MODE_STORAGE_KEY)
  return isColorMode(stored) ? stored : DEFAULT_MODE
}

function persist(key: string, value: string) {
  try {
    window.localStorage.setItem(key, value)
  } catch {
    // Appearance still applies for the current page when storage is unavailable.
  }
}

function applyToDocument() {
  if (typeof document === 'undefined') return
  const root = document.documentElement
  root.dataset.theme = theme.value
  root.dataset.colorMode = resolvedColorMode.value
  root.dataset.colorModePreference = colorMode.value
  root.style.colorScheme = resolvedColorMode.value
}

function refreshSystemPreference(event?: MediaQueryListEvent) {
  systemPrefersDark.value = event?.matches ?? mediaQuery?.matches ?? false
  applyToDocument()
}

function handleStorage(event: StorageEvent) {
  if (event.key === THEME_STORAGE_KEY && isTheme(event.newValue)) theme.value = event.newValue
  if (event.key === MODE_STORAGE_KEY && isColorMode(event.newValue))
    colorMode.value = event.newValue
  applyToDocument()
}

export function initAppearance() {
  if (typeof window === 'undefined') return
  theme.value = readTheme()
  colorMode.value = readMode()
  mediaQuery = window.matchMedia?.('(prefers-color-scheme: dark)') ?? null
  systemPrefersDark.value = mediaQuery?.matches ?? false
  applyToDocument()

  if (initialized) return
  initialized = true
  mediaQuery?.addEventListener?.('change', refreshSystemPreference)
  window.addEventListener('storage', handleStorage)
}

export function setTheme(value: ThemeId) {
  theme.value = value
  persist(THEME_STORAGE_KEY, value)
  applyToDocument()
}

export function setColorMode(value: ColorModePreference) {
  colorMode.value = value
  persist(MODE_STORAGE_KEY, value)
  applyToDocument()
}

export function useAppearance() {
  return {
    theme: readonly(theme),
    colorMode: readonly(colorMode),
    resolvedColorMode: readonly(resolvedColorMode),
    themeOptions: THEME_OPTIONS,
    colorModeOptions: COLOR_MODE_OPTIONS,
    setTheme,
    setColorMode,
  }
}
