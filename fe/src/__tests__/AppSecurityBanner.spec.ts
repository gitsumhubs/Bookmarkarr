/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { computed } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { createRouter, createMemoryHistory } from 'vue-router'
import { SECURITY_WARNING_BANNER_PREF_KEY } from '@/utils/securityWarningBannerPreference'

vi.mock('@/stores/downloads', () => ({
  useDownloadsStore: () => ({
    activeDownloads: computed(() => []),
    loadDownloads: vi.fn(async () => undefined),
  }),
}))

// Authentication disabled is what makes the banner render at all.
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    user: { authenticated: false },
    loadCurrentUser: vi.fn(async () => undefined),
    logout: vi.fn(async () => undefined),
  }),
}))

vi.mock('@/services/signalr', () => ({
  signalRService: {
    connect: vi.fn(async () => undefined),
    onConnected: vi.fn(() => () => undefined),
    onQueueUpdate: vi.fn(() => () => undefined),
    onFilesRemoved: vi.fn(() => () => undefined),
    onToast: vi.fn(() => () => undefined),
    onDownloadUpdate: vi.fn(() => () => undefined),
    onDownloadsList: vi.fn(() => () => undefined),
    onNotification: vi.fn(() => () => undefined),
  },
}))

vi.mock('@/services/api', () => ({
  apiService: {
    getQueue: vi.fn(async () => []),
    getServiceHealth: vi.fn(async () => ({ version: '0.0.0' })),
    getBootstrapConfig: vi.fn(async () => ({ authenticationRequired: false })),
    getStartupConfig: vi.fn(async () => ({ authenticationRequired: false })),
    getLibrary: vi.fn(async () => []),
  },
}))

vi.mock('@/router', () => ({
  preloadRoute: vi.fn(),
}))

async function mountApp() {
  const { default: AppComponent } = await import('@/App.vue')
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', name: 'home', component: { template: '<div />' } }],
  })
  await router.push('/')
  await router.isReady().catch(() => {})

  const wrapper = mount(AppComponent, {
    global: { stubs: ['RouterLink', 'RouterView'], plugins: [createPinia(), router] },
  })
  await new Promise((r) => setTimeout(r, 20))
  return wrapper
}

describe('App.vue security warning banner', () => {
  let wrapper: VueWrapper | undefined

  beforeEach(() => {
    setActivePinia(createPinia())
    window.localStorage.removeItem(SECURITY_WARNING_BANNER_PREF_KEY)
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = undefined
    window.localStorage.removeItem(SECURITY_WARNING_BANNER_PREF_KEY)
    vi.clearAllMocks()
  })

  it('shows the banner while authentication is disabled and nothing is stored', async () => {
    wrapper = await mountApp()

    expect(wrapper.find('.security-warning-banner').exists()).toBe(true)
    expect(window.localStorage.getItem(SECURITY_WARNING_BANNER_PREF_KEY)).toBeNull()
  }, 20000)

  it('persists the dismissal so a single close survives a reload', async () => {
    wrapper = await mountApp()

    await wrapper.find('.security-warning-dismiss').trigger('click')

    expect(wrapper.find('.security-warning-banner').exists()).toBe(false)
    // Persisting is the whole point: an in-memory flag alone would bring the banner
    // straight back on the next page load.
    expect(window.localStorage.getItem(SECURITY_WARNING_BANNER_PREF_KEY)).toBe('true')

    wrapper.unmount()
    wrapper = await mountApp()
    expect(wrapper.find('.security-warning-banner').exists()).toBe(false)
  }, 20000)

  it('stays hidden on a fresh mount when the preference is already stored', async () => {
    window.localStorage.setItem(SECURITY_WARNING_BANNER_PREF_KEY, 'true')

    wrapper = await mountApp()

    expect(wrapper.find('.security-warning-banner').exists()).toBe(false)
  }, 20000)
})
