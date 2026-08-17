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
import { describe, it, beforeEach, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'

describe('DownloadsView mobile virtualization', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
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
  })

  it('renders the full downloads list without virtualization on mobile', async () => {
    const activeDownloads = Array.from({ length: 30 }, (_, index) => ({
      id: `download-${index + 1}`,
      title: `Download ${index + 1}`,
      artist: `Artist ${index + 1}`,
      album: `Album ${index + 1}`,
      status: 'Downloading',
      progress: 25,
      totalSize: 1024,
      downloadedSize: 256,
      downloadClientId: 'qbittorrent',
      startedAt: new Date().toISOString(),
      finalPath: index % 2 === 0 ? `D:\\Downloads\\Download ${index + 1}` : '',
      errorMessage: '',
    }))

    vi.doMock('@/stores/downloads', () => ({
      useDownloadsStore: () => ({
        isLoading: false,
        activeDownloads,
        completedDownloads: [],
        failedDownloads: [],
        loadDownloads: vi.fn(async () => undefined),
        cancelDownload: vi.fn(async () => undefined),
      }),
    }))

    vi.doMock('@/services/toastService', () => ({
      useToast: () => ({
        success: vi.fn(),
        error: vi.fn(),
        info: vi.fn(),
      }),
    }))

    vi.doMock('@/services/errorTracking', () => ({
      errorTracking: {
        captureException: vi.fn(),
      },
    }))

    vi.doMock('@/utils/logger', () => ({
      logger: {
        warn: vi.fn(),
      },
    }))

    vi.doMock('@/services/api', () => ({
      apiService: {
        getCachedAnnounces: vi.fn(async () => ({ announces: [] })),
      },
    }))

    const { default: DownloadsView } = await import('@/views/activity/DownloadsView.vue')
    const wrapper = mount(DownloadsView, {
      global: {
        stubs: {
          CustomSelect: true,
          EmptyState: true,
          ProgressBar: true,
          InspectTorrentModal: true,
        },
      },
    })

    await new Promise((resolve) => setTimeout(resolve, 10))

    expect(wrapper.find('.downloads-list-container').classes()).toContain('is-static')
    expect(wrapper.find('.downloads-list.is-static').exists()).toBe(true)
    expect(wrapper.findAll('.download-card')).toHaveLength(30)

    wrapper.unmount()
  })
})

describe('DownloadsView stalled badge', () => {
  beforeEach(() => {
    vi.resetModules()
    vi.clearAllMocks()
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
  })

  const buildDownload = (overrides: Record<string, unknown>) => ({
    id: 'download-1',
    title: 'A Book',
    artist: 'An Author',
    album: '',
    status: 'Downloading',
    progress: 3,
    totalSize: 1024,
    downloadedSize: 32,
    downloadClientId: 'qbittorrent',
    startedAt: new Date().toISOString(),
    finalPath: '',
    errorMessage: '',
    metadata: {},
    ...overrides,
  })

  const mountWith = async (activeDownloads: unknown[]) => {
    vi.doMock('@/stores/downloads', () => ({
      useDownloadsStore: () => ({
        isLoading: false,
        activeDownloads,
        completedDownloads: [],
        failedDownloads: [],
        loadDownloads: vi.fn(async () => undefined),
        cancelDownload: vi.fn(async () => undefined),
      }),
    }))

    vi.doMock('@/services/toastService', () => ({
      useToast: () => ({ success: vi.fn(), error: vi.fn(), info: vi.fn() }),
    }))
    vi.doMock('@/services/errorTracking', () => ({
      errorTracking: { captureException: vi.fn() },
    }))
    vi.doMock('@/utils/logger', () => ({ logger: { warn: vi.fn() } }))
    vi.doMock('@/services/api', () => ({
      apiService: { getCachedAnnounces: vi.fn(async () => ({ announces: [] })) },
    }))

    const { default: DownloadsView } = await import('@/views/activity/DownloadsView.vue')
    const wrapper = mount(DownloadsView, {
      global: {
        stubs: {
          CustomSelect: true,
          EmptyState: true,
          ProgressBar: true,
          InspectTorrentModal: true,
        },
      },
    })

    await new Promise((resolve) => setTimeout(resolve, 10))
    return wrapper
  }

  it('marks a stalled download without replacing its status', async () => {
    const wrapper = await mountWith([
      buildDownload({
        isStalled: true,
        metadata: { StateChangedAt: new Date(Date.now() - 3 * 3_600_000).toISOString() },
      }),
    ])

    // The status is still Downloading — the client has not given up, it is simply not moving.
    expect(wrapper.find('.download-status').text()).toBe('Downloading')
    expect(wrapper.find('.download-stalled').exists()).toBe(true)
    expect(wrapper.find('.download-stalled').attributes('title')).toContain('3.0 hours')

    wrapper.unmount()
  })

  it('leaves a healthy download unmarked', async () => {
    const wrapper = await mountWith([buildDownload({ isStalled: false })])

    expect(wrapper.find('.download-stalled').exists()).toBe(false)

    wrapper.unmount()
  })

  it('leaves a row with no stall clock unmarked', async () => {
    // Queue-only rows arrive from a client snapshot and carry no isStalled at all.
    const wrapper = await mountWith([buildDownload({})])

    expect(wrapper.find('.download-stalled').exists()).toBe(false)

    wrapper.unmount()
  })
})
