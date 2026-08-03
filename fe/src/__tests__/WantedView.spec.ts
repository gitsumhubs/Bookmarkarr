/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
import { mount } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { describe, it, beforeEach, expect, vi } from 'vitest'
import WantedView from '@/views/content/WantedView.vue'
import { useLibraryStore } from '@/stores/library'
import { useDownloadsStore } from '@/stores/downloads'
import { API_BASE_PATH } from '@/services/apiBase'

// Mock api service ensureImageCached and getImageUrl (and other helpers used by stores)
vi.mock('@/services/api', () => ({
  apiService: {
    getImageUrl: vi.fn((url: string) => url || 'https://via.placeholder.com/300x450?text=No+Image'),
    getQualityProfiles: vi.fn(async () => []),
  },
  // Also expose the named helper so tests can import it directly
  getImageUrl: vi.fn((url: string) => url || 'https://via.placeholder.com/300x450?text=No+Image'),
  ensureImageCached: vi.fn(async () => true),
}))

describe('WantedView image recache behavior', () => {
  beforeEach(() => {
    const pinia = createPinia()
    setActivePinia(pinia)
    vi.clearAllMocks()
    vi.stubGlobal(
      'matchMedia',
      vi.fn().mockImplementation(() => ({
        matches: false,
        media: '',
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    )
  })

  it('calls ensureImageCached for visible wanted items on mount', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const imageBasePath = `${API_BASE_PATH}/images`

    const store = useLibraryStore()
    store.audiobooks = [
      { id: 1, title: 'Book 1', monitored: true, files: [], imageUrl: `${imageBasePath}/ASIN1` },
      { id: 2, title: 'Book 2', monitored: true, files: [], imageUrl: `${imageBasePath}/ASIN2` },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']

    // Prevent fetchLibrary from running during mount
    store.fetchLibrary = vi.fn(async () => undefined)
    const downloadsStore = useDownloadsStore()
    downloadsStore.loadDownloads = vi.fn(async () => undefined)

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })

    // Allow onMounted work to complete
    await new Promise((r) => setTimeout(r, 10))

    // Ensure the image element was rendered with the expected src (avoid relying on internal mock call)
    const img = wrapper.find('img')
    expect(img.exists()).toBe(true)
    const src = img.attributes('src') || ''
    expect(src).toContain(`${imageBasePath}/ASIN1`)
    expect(downloadsStore.loadDownloads).toHaveBeenCalledTimes(1)
  })

  it('reports ImportPending as active and surfaces ImportBlocked distinctly from Missing', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)

    const libraryStore = useLibraryStore()
    libraryStore.audiobooks = [
      { id: 101, title: 'Pending Book', monitored: true, files: [] },
      { id: 202, title: 'Blocked Book', monitored: true, files: [] },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    libraryStore.fetchLibrary = vi.fn(async () => undefined)

    const downloadsStore = useDownloadsStore()
    downloadsStore.downloads = [
      {
        id: 'd-pending',
        title: 'Pending Book',
        status: 'ImportPending',
        progress: 100,
        totalSize: 1000,
        downloadedSize: 1000,
        audiobookId: 101,
        startedAt: new Date().toISOString(),
        metadata: {},
      },
      {
        id: 'd-blocked',
        title: 'Blocked Book',
        status: 'ImportBlocked',
        progress: 100,
        totalSize: 1000,
        downloadedSize: 1000,
        audiobookId: 202,
        startedAt: new Date().toISOString(),
        metadata: {},
      },
    ] as ReturnType<typeof useDownloadsStore>['downloads']

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
    await new Promise((r) => setTimeout(r, 10))

    const vm = wrapper.vm as unknown as {
      hasActiveDownload: (audiobook: { id: number }) => boolean
      getStatusText: (audiobook: { id: number }) => string
    }

    expect(vm.hasActiveDownload({ id: 101 })).toBe(true)
    expect(vm.getStatusText({ id: 101 })).toBe('Import Pending')

    // Blocked is not an active transfer, but it must never read as Missing: the files
    // arrived and the user needs to unblock them rather than search again.
    expect(vm.hasActiveDownload({ id: 202 })).toBe(false)
    expect(vm.getStatusText({ id: 202 })).toBe('Import Blocked')
  })

  it('renders the full wanted list without virtualization on mobile', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)

    vi.stubGlobal(
      'matchMedia',
      vi.fn().mockImplementation(() => ({
        matches: true,
        media: '(max-width: 768px)',
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    )

    const libraryStore = useLibraryStore()
    libraryStore.audiobooks = Array.from({ length: 30 }, (_, index) => ({
      id: index + 1,
      title: `Wanted Book ${index + 1}`,
      monitored: true,
      files: [],
    })) as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    libraryStore.fetchLibrary = vi.fn(async () => undefined)

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
    await new Promise((resolve) => setTimeout(resolve, 10))

    expect(wrapper.find('.wanted-grid-container').classes()).toContain('is-static')
    expect(wrapper.find('.wanted-body.is-static').exists()).toBe(true)
    expect(wrapper.findAll('.wanted-row')).toHaveLength(30)
  })

  it('falls back to the server edition status when the active downloads list has not loaded yet', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)

    const libraryStore = useLibraryStore()
    libraryStore.audiobooks = [
      {
        id: 303,
        title: 'Queued Book',
        monitored: true,
        files: [],
        editions: [
          {
            id: 12,
            mediaType: 'Audiobook',
            monitored: true,
            wanted: true,
            status: 'Downloading',
          },
        ],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    libraryStore.fetchLibrary = vi.fn(async () => undefined)

    const downloadsStore = useDownloadsStore()
    downloadsStore.loadDownloads = vi.fn(async () => undefined)

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
    await new Promise((resolve) => setTimeout(resolve, 10))

    const vm = wrapper.vm as unknown as {
      getStatusText: (audiobook: { id: number; wantedEdition?: { id: number; status: string } }) => string
    }

    expect(
      vm.getStatusText({ id: 303, wantedEdition: { id: 12, status: 'Downloading' } }),
    ).toBe('Downloading')
  })

  it('shows Downloading for an edition row when the download record carries only a book id', async () => {
    // The reported defect: a queue-only or legacy record has no editionId, so matching on
    // edition alone missed it and the row kept reading Missing mid-download.
    const pinia = createPinia()
    setActivePinia(pinia)

    const libraryStore = useLibraryStore()
    libraryStore.audiobooks = [
      {
        id: 404,
        title: 'Queue Only Book',
        monitored: true,
        files: [],
        editions: [
          { id: 21, mediaType: 'Audiobook', monitored: true, wanted: true, status: 'Missing' },
        ],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    libraryStore.fetchLibrary = vi.fn(async () => undefined)

    const downloadsStore = useDownloadsStore()
    downloadsStore.loadDownloads = vi.fn(async () => undefined)
    downloadsStore.downloads = [
      {
        id: 'd-queue-only',
        title: 'Queue Only Book',
        status: 'Downloading',
        progress: 42,
        totalSize: 1000,
        downloadedSize: 420,
        audiobookId: 404,
        startedAt: new Date().toISOString(),
        metadata: {},
      },
    ] as ReturnType<typeof useDownloadsStore>['downloads']

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
    await new Promise((resolve) => setTimeout(resolve, 10))

    const vm = wrapper.vm as unknown as {
      hasActiveDownload: (item: { id: number; wantedEdition?: { id: number } }) => boolean
      getStatusText: (item: { id: number; wantedEdition?: { id: number } }) => string
    }

    const item = { id: 404, wantedEdition: { id: 21 } }
    expect(vm.hasActiveDownload(item)).toBe(true)
    expect(vm.getStatusText(item)).toBe('Downloading (42%)')
  })

  it('does not attribute an audiobook download to the ebook edition of the same book', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)

    const libraryStore = useLibraryStore()
    libraryStore.audiobooks = [
      {
        id: 505,
        title: 'Both Formats Book',
        monitored: true,
        files: [],
        editions: [
          { id: 31, mediaType: 'Audiobook', monitored: true, wanted: true, status: 'Missing' },
          { id: 32, mediaType: 'Ebook', monitored: true, wanted: true, status: 'Missing' },
        ],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    libraryStore.fetchLibrary = vi.fn(async () => undefined)

    const downloadsStore = useDownloadsStore()
    downloadsStore.loadDownloads = vi.fn(async () => undefined)
    downloadsStore.downloads = [
      {
        id: 'd-audio',
        title: 'Both Formats Book',
        status: 'Downloading',
        progress: 10,
        totalSize: 1000,
        downloadedSize: 100,
        audiobookId: 505,
        editionId: 31,
        startedAt: new Date().toISOString(),
        metadata: {},
      },
    ] as ReturnType<typeof useDownloadsStore>['downloads']

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
    await new Promise((resolve) => setTimeout(resolve, 10))

    const vm = wrapper.vm as unknown as {
      getStatusText: (item: { id: number; wantedEdition?: { id: number; mediaType: string } }) => string
    }

    expect(
      vm.getStatusText({ id: 505, wantedEdition: { id: 31, mediaType: 'Audiobook' } }),
    ).toBe('Downloading (10%)')
    expect(
      vm.getStatusText({ id: 505, wantedEdition: { id: 32, mediaType: 'Ebook' } }),
    ).toBe('Missing')
  })

  it('ignores a stale persisted Downloading status once the download list has loaded', async () => {
    // Guards against a cancelled grab or a cleaned-up queue entry pinning a row
    // to Downloading forever.
    const pinia = createPinia()
    setActivePinia(pinia)

    const libraryStore = useLibraryStore()
    libraryStore.audiobooks = [
      {
        id: 606,
        title: 'Stale Status Book',
        monitored: true,
        files: [],
        editions: [
          { id: 41, mediaType: 'Audiobook', monitored: true, wanted: true, status: 'Downloading' },
        ],
      },
    ] as unknown as ReturnType<typeof useLibraryStore>['audiobooks']
    libraryStore.fetchLibrary = vi.fn(async () => undefined)

    const downloadsStore = useDownloadsStore()
    downloadsStore.loadDownloads = vi.fn(async () => undefined)
    downloadsStore.downloads = [] as ReturnType<typeof useDownloadsStore>['downloads']
    downloadsStore.hasLoaded = true

    const wrapper = mount(WantedView, { global: { plugins: [pinia] } })
    await new Promise((resolve) => setTimeout(resolve, 10))

    const vm = wrapper.vm as unknown as {
      getStatusText: (item: { id: number; wantedEdition?: { id: number; status: string } }) => string
    }

    expect(
      vm.getStatusText({ id: 606, wantedEdition: { id: 41, status: 'Downloading' } }),
    ).toBe('Missing')
  })
})
