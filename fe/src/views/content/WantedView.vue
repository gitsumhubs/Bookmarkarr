<!--
  Bookmarkarr - Audiobook Management System
  Copyright (C) 2024-2026 Bookmarkarr Contributors

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU Affero General Public License as published
  by the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
  GNU Affero General Public License for more details.

  You should have received a copy of the GNU Affero General Public License
  along with this program. If not, see <https://www.gnu.org/licenses/>.
-->
<template>
  <div class="wanted-view">
    <div class="page-header">
      <h1>
        <PhHeart />
        Wanted
      </h1>
      <div class="wanted-actions">
        <div class="filter-input-wrapper">
          <PhMagnifyingGlass class="filter-icon" />
          <input
            v-model="filterText"
            type="text"
            class="filter-input"
            placeholder="Filter wanted..."
          />
          <button v-if="filterText" class="filter-clear" @click="filterText = ''">
            <PhX />
          </button>
        </div>
        <button
          class="btn btn-primary"
          @click="searchMissing"
          :disabled="categorizedWanted.missing.length === 0"
        >
          <PhRobot />
          Search All
        </button>
        <button class="btn btn-secondary" @click="openManualImport">
          <PhFolderPlus />
          Manual Import
        </button>
      </div>
    </div>

    <!-- Loading State -->
    <LoadingState v-if="loading" message="Loading wanted audiobooks..." />

    <!-- Wanted Table -->
    <div
      v-else-if="filteredWanted.length > 0"
      ref="scrollContainer"
      :class="['wanted-grid-container', { 'is-static': !useVirtualWantedList }]"
      @scroll="updateVisibleRange"
    >
      <div class="wanted-header" :style="gridTemplate ? { gridTemplateColumns: gridTemplate } : undefined">
        <div class="col-poster"></div>
        <div
          v-for="column in sortableColumns"
          :key="column.key"
          :class="['sortable-header', column.class]"
          :aria-sort="ariaSortFor(column.key)"
        >
          <button type="button" class="sort-button" @click="toggleSort(column.key)">
            <span class="header-content">
              {{ column.label }}
              <component :is="sortIconFor(column.key)" class="sort-icon" />
            </span>
          </button>
          <span
            class="resize-handle"
            :class="{ active: resizingColumn === column.key }"
            title="Drag to resize, double-click to reset"
            @pointerdown="startResize(column.key, $event)"
            @dblclick="resetWidths"
          />
        </div>
        <div class="col-actions"></div>
      </div>
      <div
        :class="['wanted-body-spacer', { 'is-static': !useVirtualWantedList }]"
        :style="useVirtualWantedList ? { height: `${totalHeight}px` } : undefined"
      >
        <div
          :class="['wanted-body', { 'is-static': !useVirtualWantedList }]"
          :style="useVirtualWantedList ? { transform: `translateY(${topPadding}px)` } : undefined"
        >
          <div
            v-for="item in visibleWanted"
            :key="item.wantedKey"
            class="wanted-row"
            :style="gridTemplate ? { gridTemplateColumns: gridTemplate } : undefined"
          >
            <div class="col-poster">
              <img
                class="row-poster"
                :src="getProtectedImageSrc(item.imageUrl, getPlaceholderUrl())"
                :alt="item.title"
                loading="lazy"
                decoding="async"
                @error="handleImageError"
              />
            </div>
            <div class="col-title">
              <div class="title-cell">
                <span v-if="hasActiveDownload(item)" class="download-indicator" title="Downloading">
                  <PhDownloadSimple :size="14" weight="fill" />
                </span>
                <RouterLink :to="`/audiobooks/${item.id}`" class="title-link">{{
                  safeText(item.title)
                }}</RouterLink>
                <span v-if="item.wantedEdition" class="media-badge">{{ item.wantedEdition.mediaType === 'Audiobook' ? 'Audio' : 'Ebook' }}</span>
              </div>
            </div>
            <div class="col-author">
              <template v-if="item.authors?.length">
                <template v-for="(a, i) in item.authors" :key="a">
                  <RouterLink
                    :to="`/collection/author/${encodeURIComponent(a)}`"
                    class="author-link"
                    >{{ safeText(a) }}</RouterLink
                  ><span v-if="i < item.authors.length - 1">, </span>
                </template>
              </template>
              <span v-else class="author-text">-</span>
            </div>
            <div class="col-series">
              <span v-if="item.series" class="series-text">
                <RouterLink
                  :to="`/collection/series/${encodeURIComponent(item.series)}`"
                  class="series-link"
                  >{{ safeText(item.series) }}</RouterLink
                ><span v-if="item.seriesNumber"> #{{ item.seriesNumber }}</span>
              </span>
              <span v-else class="muted">-</span>
            </div>
            <div class="col-quality">
              <span class="quality-tag">
                {{ getQualityProfileForAudiobook(item)?.name ?? item.quality ?? 'Unknown' }}
              </span>
            </div>
            <div class="col-status">
              <span :class="['status-badge', getStatusClass(item)]">
                {{ getStatusText(item) }}
              </span>
              <div v-if="searchResults[item.wantedKey]" class="search-info">
                <PhSpinner v-if="searching[item.wantedKey]" class="ph-spin" :size="12" />
                {{ searchResults[item.wantedKey] }}
              </div>
            </div>
            <div class="col-actions">
              <div class="actions-cell">
                <button
                  class="btn-icon"
                  @click="searchAudiobook(item)"
                  :disabled="searching[item.wantedKey]"
                  title="Automatic Search"
                >
                  <PhRobot />
                </button>
                <button class="btn-icon" @click="openManualSearch(item)" :title="item.wantedEdition?.mediaType === 'Ebook' ? 'Search ebook releases' : 'Manual Search'">
                  <PhMagnifyingGlass />
                </button>
                <button
                  class="btn-icon btn-danger-icon"
                  @click="markAsSkipped(item)"
                  :disabled="searching[item.wantedKey]"
                  title="Unmonitor edition"
                >
                  <PhX />
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Empty State -->
    <EmptyState
      v-else
      :title="filterText ? 'No Matching Audiobooks' : 'No Wanted Audiobooks'"
      :message="
        filterText
          ? 'No wanted audiobooks match your filter.'
          : 'All your monitored audiobooks have files!'
      "
    >
      <template #icon>
        <PhCheckCircle :size="48" />
      </template>
    </EmptyState>

    <!-- Manual Search Modal -->
    <ManualSearchModal
      :is-open="showManualSearchModal"
      :audiobook="selectedAudiobook"
      @close="closeManualSearch"
      @downloaded="handleDownloaded"
    />

    <!-- Manual Import Modal -->
    <ManualImportModal
      :is-open="showManualImportModal"
      @close="closeManualImport"
      @imported="handleImported"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, nextTick, watch } from 'vue'
import { useLibraryStore } from '@/stores/library'
import { useConfigurationStore } from '@/stores/configuration'
import { apiService } from '@/services/api'
import { errorTracking } from '@/services/errorTracking'
import { handleImageError } from '@/utils/imageFallback'
import ManualSearchModal from '@/components/domain/search/ManualSearchModal.vue'
import ManualImportModal from '@/components/feedback/ManualImportModal.vue'
import { EmptyState, LoadingState } from '@/components/base'
import type { Audiobook, SearchResult, Download } from '@/types'
import { safeText } from '@/utils/textUtils'
import {
  PhHeart,
  PhRobot,
  PhFolderPlus,
  PhSpinner,
  PhMagnifyingGlass,
  PhX,
  PhCheckCircle,
  PhDownloadSimple,
  PhArrowUp,
  PhArrowDown,
  PhArrowsDownUp,
} from '@phosphor-icons/vue'
import { useTableSort } from '@/composables/useTableSort'
import { useColumnResize } from '@/composables/useColumnResize'
import { logger } from '@/utils/logger'
import { useDownloadsStore } from '@/stores/downloads'
import { useProtectedImages } from '@/composables/useProtectedImages'
import { getPlaceholderUrl } from '@/utils/placeholder'

const downloadsStore = useDownloadsStore()
const { getProtectedImageSrc } = useProtectedImages()
const libraryStore = useLibraryStore()
const configurationStore = useConfigurationStore()
type EditionSummary = NonNullable<Audiobook['editions']>[number]
type WantedItem = Audiobook & { wantedEdition?: EditionSummary; wantedKey: string }

// Filter
const filterText = ref('')

// Virtual scrolling setup
const scrollContainer = ref<HTMLElement | null>(null)
const ROW_HEIGHT = 48
const BUFFER_ROWS = 5
const MOBILE_WANTED_BREAKPOINT = 768

const visibleRange = ref({ start: 0, end: 30 })
const isMobileWantedLayout = ref(false)
const useVirtualWantedList = computed(() => !isMobileWantedLayout.value)

const updateWantedLayoutMode = () => {
  if (typeof window === 'undefined') return

  if (typeof window.matchMedia === 'function') {
    isMobileWantedLayout.value = window.matchMedia(
      `(max-width: ${MOBILE_WANTED_BREAKPOINT}px)`,
    ).matches
    return
  }

  isMobileWantedLayout.value = window.innerWidth <= MOBILE_WANTED_BREAKPOINT
}

const updateVisibleRange = () => {
  if (!useVirtualWantedList.value) {
    visibleRange.value = { start: 0, end: filteredWanted.value.length }
    return
  }

  if (!scrollContainer.value) return

  const scrollTop = scrollContainer.value.scrollTop
  const viewportHeight = scrollContainer.value.clientHeight

  const firstVisibleIndex = Math.floor(scrollTop / ROW_HEIGHT)
  const visibleItemCount = Math.ceil(viewportHeight / ROW_HEIGHT)

  const startIndex = Math.max(0, firstVisibleIndex - BUFFER_ROWS)
  const endIndex = Math.min(
    firstVisibleIndex + visibleItemCount + BUFFER_ROWS,
    filteredWanted?.value?.length || 0,
  )

  visibleRange.value = { start: startIndex, end: endIndex }
}

const getQualityProfileForAudiobook = (audiobook: WantedItem) => {
  const profileId = audiobook.wantedEdition?.qualityProfileId ?? audiobook.qualityProfileId
  if (!audiobook || !profileId) {
    return null
  }
  const profile = configurationStore.qualityProfiles.find(
    (profile) => profile.id === profileId,
  )
  return profile || null
}

const loading = computed(() => libraryStore.loading)
const searching = ref<Record<string, boolean>>({})
const searchResults = ref<Record<string, string>>({})
const showManualSearchModal = ref(false)
const selectedAudiobook = ref<Audiobook | null>(null)
const showManualImportModal = ref(false)

const syncWantedLayout = async () => {
  await nextTick()
  updateVisibleRange()
}

const handleViewportResize = () => {
  updateWantedLayoutMode()
  void syncWantedLayout()
}

onMounted(async () => {
  updateWantedLayoutMode()
  if (typeof window !== 'undefined') {
    window.addEventListener('resize', handleViewportResize, { passive: true })
  }

  if (downloadsStore.downloads.length === 0) {
    await downloadsStore.loadDownloads()
  }

  if (libraryStore.audiobooks.length === 0) {
    await libraryStore.fetchLibrary()
  }
  await configurationStore.loadQualityProfiles()

  await syncWantedLayout()
})

onBeforeUnmount(() => {
  if (typeof window !== 'undefined') {
    window.removeEventListener('resize', handleViewportResize)
  }
})

// Expand unified books into independent wanted editions.
const wantedAudiobooks = computed<WantedItem[]>(() => {
  return libraryStore.audiobooks.flatMap((audiobook) => {
    if (audiobook.editions?.length) {
      return audiobook.editions
        .filter((edition) => edition.wanted)
        .map((edition) => ({ ...audiobook, wantedEdition: edition, wantedKey: `edition-${edition.id}` }))
    }
    const serverWanted = (audiobook as unknown as Record<string, unknown>)['wanted']

    if (serverWanted === true) return [{ ...audiobook, wantedKey: `book-${audiobook.id}` }]
    if (serverWanted === false) return []

    const hasFiles = Array.isArray(audiobook.files) ? audiobook.files.length > 0 : false
    const hasPrimaryFile = !!(audiobook.filePath && audiobook.filePath.toString().trim() !== '')

    return audiobook.monitored && !hasFiles && !hasPrimaryFile
      ? [{ ...audiobook, wantedKey: `book-${audiobook.id}` }]
      : []
  })
})

// Categorize wanted audiobooks by their current search state
const categorizedWanted = computed(() => {
  const all = wantedAudiobooks.value
  const missingItems = all.filter((a) => !searching.value[a.wantedKey] && !searchResults.value[a.wantedKey])

  return {
    all,
    missing: missingItems,
  }
})

type WantedSortKey = 'title' | 'author' | 'series' | 'quality' | 'status'

const sortableColumns: Array<{ key: WantedSortKey; label: string; class: string }> = [
  { key: 'title', label: 'Title', class: 'col-title' },
  { key: 'author', label: 'Author', class: 'col-author' },
  { key: 'series', label: 'Series', class: 'col-series' },
  { key: 'quality', label: 'Quality', class: 'col-quality' },
  { key: 'status', label: 'Status', class: 'col-status' },
]

// Sort on what the row actually displays, so the order matches what the user is reading —
// the first author rather than the raw array, the resolved profile name rather than the id.
const { toggleSort, directionFor, sortItems } = useTableSort<WantedItem, WantedSortKey>({
  title: (item) => item.title,
  author: (item) => item.authors?.[0],
  series: (item) => (item.series ? `${item.series} ${item.seriesNumber ?? ''}`.trim() : null),
  quality: (item) => getQualityProfileForAudiobook(item)?.name ?? item.quality,
  status: (item) => getStatusText(item),
})

function ariaSortFor(key: WantedSortKey): 'ascending' | 'descending' | 'none' {
  const direction = directionFor(key)
  if (!direction) return 'none'
  return direction === 'asc' ? 'ascending' : 'descending'
}

function sortIconFor(key: WantedSortKey) {
  const direction = directionFor(key)
  if (!direction) return PhArrowsDownUp
  return direction === 'asc' ? PhArrowUp : PhArrowDown
}

const { widths, resizingColumn, startResize, resetWidths } = useColumnResize<WantedSortKey>({
  storageKey: 'bookmarkarr.wanted.columnWidths.v1',
  defaults: { title: 340, author: 220, series: 200, quality: 120, status: 140 },
  minWidths: { title: 160, author: 120, series: 100, quality: 80, status: 100 },
  mobileBreakpoint: MOBILE_WANTED_BREAKPOINT,
})

// The leading 48px is the poster and the trailing 120px the action buttons; neither resizes.
const gridTemplate = computed(() =>
  isMobileWantedLayout.value
    ? undefined
    : `48px ${sortableColumns.map((column) => `${widths.value[column.key]}px`).join(' ')} 120px`,
)

const filteredWanted = computed(() => {
  const items = wantedAudiobooks.value
  if (!filterText.value) return sortItems(items)

  const query = filterText.value.toLowerCase()
  const matches = items.filter((item) => {
    const title = (item.title || '').toLowerCase()
    const authors = (item.authors || []).join(' ').toLowerCase()
    const series = (item.series || '').toLowerCase()
    return title.includes(query) || authors.includes(query) || series.includes(query)
  })

  return sortItems(matches)
})

const visibleWanted = computed(() => {
  if (!useVirtualWantedList.value) {
    return filteredWanted.value
  }

  return filteredWanted.value.slice(visibleRange.value.start, visibleRange.value.end)
})

const totalHeight = computed(() => {
  if (!useVirtualWantedList.value) return 0
  return filteredWanted.value.length * ROW_HEIGHT
})

const topPadding = computed(() => {
  if (!useVirtualWantedList.value) return 0
  return visibleRange.value.start * ROW_HEIGHT
})

watch(
  filteredWanted,
  () => {
    void syncWantedLayout()
  },
  { flush: 'post' },
)

// Work is finished for these; the row should fall through to its library state.
const TERMINAL_DOWNLOAD_STATES = ['Completed', 'Moved', 'SourceMissing']

// Still in flight from the user's point of view, including the post-transfer stages.
const ACTIVE_DOWNLOAD_STATES = [
  'Queued',
  'Downloading',
  'Paused',
  'Processing',
  'Ready',
  'ImportPending',
]

// Downloads are indexed by both keys because a queue-only or legacy record can arrive
// with just an audiobookId. Matching on edition alone is what previously left rows
// showing "Missing" while a download was plainly running.
const downloadsByEdition = computed(() => {
  const map = new Map<number, Download>()
  downloadsStore.downloads.forEach((download) => {
    if (download.editionId && !TERMINAL_DOWNLOAD_STATES.includes(download.status)) {
      map.set(download.editionId, download)
    }
  })
  return map
})

const downloadsByBook = computed(() => {
  const map = new Map<number, Download>()
  downloadsStore.downloads.forEach((download) => {
    if (download.audiobookId && !TERMINAL_DOWNLOAD_STATES.includes(download.status)) {
      // Prefer a record that still carries an edition id if several share a book.
      const existing = map.get(download.audiobookId)
      if (!existing || (!existing.editionId && download.editionId)) {
        map.set(download.audiobookId, download)
      }
    }
  })
  return map
})

function itemKey(item: Pick<WantedItem, 'id'> & Partial<Pick<WantedItem, 'wantedKey'>>): string {
  return item.wantedKey ?? `book-${item.id}`
}

/**
 * Resolves the download record backing a Wanted row.
 *
 * Stable edition ids win; a book-level record is only borrowed when it cannot belong to
 * a different edition of the same book, so an audiobook grab never lights up the ebook row.
 */
function getDownloadForItem(item: WantedItem): Download | undefined {
  const editionId = item.wantedEdition?.id
  if (editionId) {
    const byEdition = downloadsByEdition.value.get(editionId)
    if (byEdition) return byEdition
  }

  const byBook = downloadsByBook.value.get(item.id)
  if (!byBook || byBook.editionId) {
    // A record that names a different edition is not ours to claim.
    return undefined
  }

  // Untargeted record: attribute it to the only wanted edition, else the audiobook one,
  // which is what pre-edition downloads always were.
  const siblings = wantedAudiobooks.value.filter((candidate) => candidate.id === item.id)
  if (siblings.length === 1) return byBook
  return item.wantedEdition?.mediaType === 'Audiobook' ? byBook : undefined
}

function hasActiveDownload(item: WantedItem): boolean {
  const download = getDownloadForItem(item)
  return !!download && ACTIVE_DOWNLOAD_STATES.includes(download.status)
}

function getStatusClass(item: WantedItem): string {
  const download = getDownloadForItem(item)
  if (download) {
    if (ACTIVE_DOWNLOAD_STATES.includes(download.status)) return 'downloading'
    if (download.status === 'ImportBlocked') return 'blocked'
    if (download.status === 'Failed') return 'failed'
  }
  if (usePersistedActiveStatus(item)) {
    return 'downloading'
  }
  if (searching.value[itemKey(item)]) {
    return 'searching'
  }
  if (searchResults.value[itemKey(item)] && searchResults.value[itemKey(item)] !== 'Searching...') {
    return 'failed'
  }
  return 'missing'
}

function getStatusText(item: WantedItem): string {
  const download = getDownloadForItem(item)
  if (download) {
    if (download.status === 'Downloading') {
      return `Downloading (${download.progress.toFixed(0)}%)`
    }
    if (download.status === 'ImportBlocked') return 'Import Blocked'
    if (download.status === 'ImportPending') return 'Import Pending'
    if (ACTIVE_DOWNLOAD_STATES.includes(download.status) || download.status === 'Failed') {
      return download.status
    }
  }
  const persisted = usePersistedActiveStatus(item)
  if (persisted) {
    return persisted
  }
  if (searching.value[itemKey(item)]) {
    return 'Searching'
  }
  if (searchResults.value[itemKey(item)] && searchResults.value[itemKey(item)] !== 'Searching...') {
    return 'Failed'
  }
  return 'Missing'
}

/**
 * Server-side fallback for the window before the download list has loaded.
 *
 * Once the authoritative list is in hand it always wins, so a cancelled grab or a
 * cleaned-up queue entry cannot leave a row stuck on Queued/Downloading forever.
 */
function usePersistedActiveStatus(item: WantedItem): string | undefined {
  if (downloadsStore.hasLoaded) return undefined
  const status = item.wantedEdition?.status
  return status === 'Queued' || status === 'Downloading' ? status : undefined
}

const searchMissing = async () => {
  logger.debug('Automatic search for all missing audiobooks')

  for (const audiobook of categorizedWanted.value.missing) {
    await searchAudiobook(audiobook)
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }
}

function openManualSearch(item: WantedItem) {
  if (item.wantedEdition?.mediaType === 'Ebook') {
    void searchAudiobook(item)
    return
  }
  selectedAudiobook.value = item
  showManualSearchModal.value = true
}

function openManualImport() {
  showManualImportModal.value = true
}

function closeManualImport() {
  showManualImportModal.value = false
}

async function handleImported(result: { imported: number }) {
  logger.debug('Manual import completed, imported:', result.imported)
  await libraryStore.fetchLibrary()
  closeManualImport()
}

function closeManualSearch() {
  showManualSearchModal.value = false
  selectedAudiobook.value = null
}

function handleDownloaded(result: SearchResult) {
  logger.debug('Downloaded:', result)
  setTimeout(async () => {
    try {
      await downloadsStore.loadDownloads()
    } catch (e) {
      logger.warn('Failed to refresh downloads after manual download:', e)
    }
    await libraryStore.fetchLibrary()
    closeManualSearch()
  }, 2000)
}

const searchAudiobook = async (item: WantedItem) => {
  logger.debug('Searching wanted edition:', item.title, item.wantedEdition?.mediaType)

  searching.value[item.wantedKey] = true
  searchResults.value[item.wantedKey] = 'Searching...'

  try {
    if (item.wantedEdition?.mediaType === 'Ebook') {
      const response = await apiService.searchBookEdition(item.wantedEdition.id)
      searchResults.value[item.wantedKey] = response.results.length
        ? `${response.results.length} ebook release(s) found`
        : 'No ebook matches found'
      searching.value[item.wantedKey] = false
      return
    }
    const result = await apiService.searchAndDownload(item.id)

    if (result.success) {
      searchResults.value[item.wantedKey] = `Found on ${result.indexerUsed}, downloading...`

      setTimeout(async () => {
        try {
          await downloadsStore.loadDownloads()
        } catch (e) {
          logger.warn('Failed to refresh downloads after search:', e)
        }
        await libraryStore.fetchLibrary()
        delete searching.value[item.wantedKey]
        delete searchResults.value[item.wantedKey]
      }, 2000)
    } else {
      searchResults.value[item.wantedKey] = result.message || 'No matches found'
      setTimeout(() => {
        delete searching.value[item.wantedKey]
        delete searchResults.value[item.wantedKey]
      }, 5000)
    }
  } catch (err) {
    errorTracking.captureException(err as Error, {
      component: 'WantedView',
      operation: 'searchWanted',
      metadata: { itemId: item.id },
    })
    searchResults.value[item.wantedKey] = 'Search failed'
    setTimeout(() => {
      delete searching.value[item.wantedKey]
      delete searchResults.value[item.wantedKey]
    }, 5000)
  }
}

const markAsSkipped = async (item: WantedItem) => {
  logger.debug('Mark as skipped:', item.title)

  try {
    if (item.wantedEdition) await apiService.updateBookEdition(item.wantedEdition.id, { monitored: false })
    else await apiService.updateAudiobook(item.id, { monitored: false })
    await libraryStore.fetchLibrary()
  } catch (err) {
    logger.error('Failed to unmonitor audiobook:', err)
  }
}
</script>

<style scoped>
.wanted-view {
  padding: 1em;
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 2rem;
}

.page-header h1 {
  margin: 0;
  color: white;
  font-size: 2rem;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-weight: 500;
}

.page-header h1 svg {
  color: #fa5252;
  width: 32px;
  height: 32px;
}

.wanted-actions {
  display: flex;
  gap: 0.75rem;
  align-items: center;
}

/* Filter input */
.filter-input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.filter-icon {
  position: absolute;
  left: 0.75rem;
  color: #868e96;
  width: 16px;
  height: 16px;
  pointer-events: none;
}

.filter-input {
  background: var(--card-bg);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  color: var(--text-primary);
  padding: 0.5rem 2rem 0.5rem 2.25rem;
  font-size: 0.875rem;
  width: 220px;
  transition:
    border-color 0.2s,
    box-shadow 0.2s;
}

.filter-input::placeholder {
  color: #868e96;
}

.filter-input:focus {
  outline: none;
  border-color: #4dabf7;
  box-shadow: 0 0 0 2px rgba(77, 171, 247, 0.15);
}

.filter-clear {
  position: absolute;
  right: 0.5rem;
  background: none;
  border: none;
  color: #868e96;
  cursor: pointer;
  padding: 0.25rem;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: color 0.2s;
}

.filter-clear:hover {
  color: white;
}

.filter-clear svg {
  width: 14px;
  height: 14px;
}

/* Grid container with virtual scrolling */
.wanted-grid-container {
  height: calc(100vh - 220px);
  overflow-y: auto;
  position: relative;
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  background: var(--bg-primary);
}

.wanted-grid-container.is-static {
  height: auto;
  overflow-y: visible;
}

/* Desktop grid columns shared by header and rows */
.wanted-header,
.wanted-row {
  display: grid;
  grid-template-columns:
    48px minmax(0, 28fr) minmax(0, 20fr) minmax(0, 18fr) minmax(0, 10fr)
    minmax(0, 12fr) minmax(0, 12fr);
  align-items: center;
}

.wanted-header {
  position: sticky;
  top: 0;
  z-index: 2;
  background: var(--bg-secondary);
  padding: 0.65rem 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.wanted-header > div {
  padding: 0 0.75rem;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: #868e96;
  white-space: nowrap;
}

.wanted-header > .sortable-header {
  padding: 0;
  min-width: 0;
}

.sortable-header:hover {
  background: var(--bg-secondary, rgba(0, 0, 0, 0.04));
}

.sortable-header[aria-sort='ascending'],
.sortable-header[aria-sort='descending'] {
  background: linear-gradient(180deg, rgba(99, 102, 241, 0.08), rgba(99, 102, 241, 0.02));
}

.sort-button {
  /* Fills the now-flex header cell so the whole label area stays clickable. */
  flex: 1;
  min-width: 0;
  width: 100%;
  height: 100%;
  padding: 0 0.75rem;
  background: none;
  border: none;
  cursor: pointer;
  font: inherit;
  color: inherit;
  text-align: left;
  text-transform: inherit;
  letter-spacing: inherit;
}

.sort-button:focus-visible {
  outline: 2px solid var(--brand-500, #6366f1);
  outline-offset: -2px;
}

.header-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  min-width: 0;
}

.sort-icon {
  opacity: 0.72;
  flex-shrink: 0;
}

.sortable-header {
  position: relative;
  /* The header grid centres its items, so without this the cell is only as tall as its
     text and the resize handle collapses to an unhittable sliver. */
  align-self: stretch;
  display: flex;
  align-items: center;
}

.resize-handle {
  position: absolute;
  top: 0;
  right: -5px;
  width: 10px;
  height: 100%;
  cursor: col-resize;
  touch-action: none;
  /* Above the sort button so a drag near the edge resizes instead of sorting. */
  z-index: 3;
}

.resize-handle::after {
  content: '';
  position: absolute;
  top: 15%;
  left: 4px;
  width: 2px;
  height: 70%;
  background: var(--border-color, rgba(128, 128, 128, 0.45));
  /* Faintly visible at rest: an invisible handle cannot be discovered. */
  opacity: 0.5;
  transition: opacity 0.12s ease, background 0.12s ease;
}

.resize-handle:hover::after,
.resize-handle.active::after {
  opacity: 1;
  background: var(--brand-500, #6366f1);
}

.clear-count {
  margin-left: 0.35rem;
  padding: 0.05rem 0.4rem;
  border-radius: 999px;
  font-size: 0.7rem;
  font-weight: 600;
  background: rgba(128, 128, 128, 0.18);
}


.wanted-body-spacer {
  position: relative;
  width: 100%;
}

.wanted-body-spacer.is-static {
  position: static;
}

.wanted-body {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
}

.wanted-body.is-static {
  position: static;
}

/* Grid rows */
.wanted-row {
  transition: background-color 0.15s;
}

.wanted-row:hover {
  background-color: rgba(255, 255, 255, 0.03);
}

.wanted-row > div {
  padding: 0.4rem 0.75rem;
  font-size: 0.875rem;
  color: #adb5bd;
  border-bottom: 1px solid rgba(255, 255, 255, 0.04);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: flex;
  align-items: center;
  align-self: stretch;
}

/* Poster cell */
.row-poster {
  width: 32px;
  height: 32px;
  object-fit: cover;
  border-radius: 4px;
  display: block;
}

/* Title cell */
.title-cell {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  min-width: 0;
}

.title-text {
  color: white;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.title-link {
  color: white;
  font-weight: 500;
  text-decoration: none;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.title-link:hover {
  color: #4dabf7;
}

.media-badge {
  flex: 0 0 auto;
  border-radius: 999px;
  padding: 0.1rem 0.4rem;
  background: rgba(17, 184, 170, 0.2);
  color: #7ef0dc;
  font-size: 0.68rem;
  font-weight: 700;
  text-transform: uppercase;
}

.download-indicator {
  color: #51cf66;
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
  animation: bounce 2s ease-in-out infinite;
}

@keyframes bounce {
  0%,
  100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-2px);
  }
}

/* Author cell */
.author-text {
  color: #4dabf7;
  font-size: 0.8rem;
}

.author-link {
  color: #4dabf7;
  font-size: 0.8rem;
  text-decoration: none;
}

.author-link:hover {
  text-decoration: underline;
}

/* Series cell */
.series-text {
  color: #868e96;
  font-size: 0.8rem;
}

.series-link {
  color: #868e96;
  text-decoration: none;
}

.series-link:hover {
  color: #adb5bd;
  text-decoration: underline;
}

.muted {
  color: #495057;
  font-size: 0.8rem;
}

/* Quality tag */
.quality-tag {
  font-size: 0.7rem;
  padding: 0.15rem 0.45rem;
  border-radius: 4px;
  background: rgba(255, 212, 59, 0.12);
  color: #ffd43b;
  font-weight: 500;
}

/* Status badges */
.status-badge {
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.3px;
  display: inline-block;
}

.status-badge.missing {
  background-color: rgba(250, 82, 82, 0.15);
  color: #fa5252;
}

.status-badge.searching {
  background-color: rgba(77, 171, 247, 0.15);
  color: #4dabf7;
}

.status-badge.downloading {
  background-color: rgba(81, 207, 102, 0.15);
  color: #51cf66;
  animation: pulse 2s ease-in-out infinite;
}

@keyframes pulse {
  0%,
  100% {
    opacity: 1;
  }
  50% {
    opacity: 0.6;
  }
}

.status-badge.failed {
  background-color: rgba(134, 142, 150, 0.15);
  color: #868e96;
}

/* Import blocked needs to read as "needs attention", distinct from both an active
   transfer and a plain miss, so the user knows the file arrived but did not import. */
.status-badge.blocked {
  background-color: rgba(250, 176, 5, 0.15);
  color: #fab005;
}

.search-info {
  font-size: 0.7rem;
  color: #4dabf7;
  display: flex;
  align-items: center;
  gap: 0.3rem;
  margin-top: 0.15rem;
}

.ph-spin {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

/* Actions cell */
.actions-cell {
  display: flex;
  gap: 0.25rem;
}

.btn-icon {
  background: none;
  border: 1px solid rgba(255, 255, 255, 0.08);
  color: #adb5bd;
  cursor: pointer;
  padding: 0.3rem;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
}

.btn-icon:hover {
  background-color: rgba(255, 255, 255, 0.08);
  color: white;
}

.btn-icon:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

.btn-icon svg {
  width: 16px;
  height: 16px;
}

.btn-danger-icon {
  color: #868e96;
}

.btn-danger-icon:hover {
  background-color: rgba(250, 82, 82, 0.15);
  color: #fa5252;
  border-color: rgba(250, 82, 82, 0.3);
}

@media (max-width: 768px) {
  .page-header {
    flex-direction: column;
    align-items: flex-start;
    gap: 0.75rem;
    margin-bottom: 1rem;
  }

  .wanted-actions {
    flex-direction: column;
    width: 100%;
    gap: 0.5rem;
  }

  .wanted-actions .btn {
    width: 100%;
    justify-content: center;
  }

  .filter-input-wrapper {
    width: 100%;
  }

  .filter-input {
    width: 100%;
  }

  .wanted-grid-container {
    height: auto;
    overflow-y: visible;
    border: none;
    background: transparent;
  }

  .wanted-header {
    display: none;
  }

  .wanted-body-spacer {
    height: auto !important;
    position: static !important;
  }

  .wanted-body {
    position: static !important;
    transform: none !important;
  }

  /* Each row becomes a card */
  .wanted-row {
    grid-template-columns: 40px 1fr auto;
    grid-template-rows: auto auto;
    gap: 0.2rem 0.6rem;
    padding: 0.75rem;
    margin-bottom: 0.5rem;
    background: var(--card-bg);
    border-radius: 6px;
    border: 1px solid rgba(255, 255, 255, 0.06);
  }

  .wanted-row:hover {
    background-color: var(--bg-tertiary);
  }

  .wanted-row > div {
    padding: 0;
    border: none;
    overflow: visible;
    white-space: normal;
  }

  /* Hide series and quality on mobile */
  .wanted-row .col-series,
  .wanted-row .col-quality {
    display: none;
  }

  /* Row 1: Poster (spans 2 rows) | Title | Status */
  .wanted-row .col-poster {
    grid-column: 1;
    grid-row: 1 / 3;
    align-self: center;
  }

  .wanted-row .col-title {
    grid-column: 2;
    grid-row: 1;
    min-width: 0;
  }

  .wanted-row .col-status {
    grid-column: 3;
    grid-row: 1;
    white-space: nowrap;
  }

  /* Row 2: Author | Actions */
  .wanted-row .col-author {
    grid-column: 2;
    grid-row: 2;
    min-width: 0;
  }

  .wanted-row .col-author .author-text {
    display: block;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    font-size: 0.75rem;
  }

  .wanted-row .col-actions {
    grid-column: 3;
    grid-row: 2;
    display: flex;
    justify-content: flex-end;
    overflow: visible;
  }

  .row-poster {
    width: 36px;
    height: 36px;
  }

  .actions-cell {
    gap: 0.15rem;
  }

  .btn-icon {
    padding: 0.35rem;
    min-width: 32px;
    min-height: 32px;
  }

  .btn-icon svg {
    width: 16px;
    height: 16px;
  }
}
</style>
