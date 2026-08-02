import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  initAppearance,
  setColorMode,
  setTheme,
  THEME_OPTIONS,
  useAppearance,
} from '@/services/appearance'

describe('appearance service', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.removeAttribute('data-theme')
    document.documentElement.removeAttribute('data-color-mode')
    document.documentElement.removeAttribute('data-color-mode-preference')

    vi.stubGlobal(
      'matchMedia',
      vi.fn().mockReturnValue({
        matches: false,
        media: '(prefers-color-scheme: dark)',
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }),
    )
  })

  it('applies the polished Bookmarkarr theme by default', () => {
    initAppearance()

    expect(THEME_OPTIONS.find((option) => option.id === 'bookmarkarr')?.branded).toBe(true)
    expect(document.documentElement.dataset.theme).toBe('bookmarkarr')
    expect(document.documentElement.dataset.colorMode).toBe('light')
    expect(document.documentElement.dataset.colorModePreference).toBe('system')
  })

  it('persists theme and explicit dark mode choices', () => {
    initAppearance()
    setTheme('plum')
    setColorMode('dark')

    expect(localStorage.getItem('bookmarkarr.appearance.theme')).toBe('plum')
    expect(localStorage.getItem('bookmarkarr.appearance.mode')).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('plum')
    expect(document.documentElement.dataset.colorMode).toBe('dark')
    expect(useAppearance().resolvedColorMode.value).toBe('dark')
  })
})
