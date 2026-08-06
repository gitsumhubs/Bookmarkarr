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
  <div class="blocklist-view">
    <div class="page-header">
      <h1>
        <PhProhibit />
        Blocklist
      </h1>
      <div class="header-actions">
        <div class="filter-input-wrapper">
          <PhMagnifyingGlass class="filter-icon" />
          <input v-model="filterText" type="text" class="filter-input" placeholder="Filter releases..." />
          <button v-if="filterText" class="filter-clear" @click="filterText = ''">
            <PhX />
          </button>
        </div>
        <button class="btn btn-secondary" @click="load" :disabled="loading">
          <component :is="loading ? PhSpinner : PhArrowClockwise" />
          Refresh
        </button>
      </div>
    </div>

    <p class="page-intro">
      Releases that failed to download repeatedly. Automatic search skips these and picks the next
      best result instead. Removing an entry makes the release eligible again on the next search.
    </p>

    <LoadingState v-if="loading && entries.length === 0" message="Loading blocklist..." />

    <div v-else-if="filtered.length > 0" class="table-wrapper">
      <table class="blocklist-table">
        <thead>
          <tr>
            <th
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
            </th>
            <th class="col-actions"></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="entry in filtered" :key="entry.id">
            <td class="col-title">
              <div class="title-text">{{ safeText(entry.title) }}</div>
              <div v-if="entry.reason" class="reason">{{ safeText(entry.reason) }}</div>
            </td>
            <td class="col-book">
              <RouterLink v-if="entry.audiobookId" :to="`/audiobooks/${entry.audiobookId}`" class="book-link">
                {{ bookTitle(entry.audiobookId) }}
              </RouterLink>
              <span v-else class="muted">—</span>
            </td>
            <td class="col-source">{{ entry.source || '—' }}</td>
            <td class="col-failures">
              <span class="failure-count">{{ entry.failureCount }}</span>
            </td>
            <td class="col-date">{{ formatDate(entry.createdAt) }}</td>
            <td class="col-actions">
              <button
                class="btn-icon btn-danger-icon"
                :disabled="removing.has(entry.id)"
                title="Remove from blocklist"
                @click="remove(entry)"
              >
                <component :is="removing.has(entry.id) ? PhSpinner : PhTrash" />
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <EmptyState
      v-else
      :title="filterText ? 'No Matching Entries' : 'Nothing Blocklisted'"
      :message="
        filterText
          ? 'No blocklisted releases match your filter.'
          : 'Releases appear here after they fail to download twice for the same book.'
      "
    >
      <template #icon>
        <PhProhibit :size="48" />
      </template>
    </EmptyState>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import {
  PhProhibit,
  PhMagnifyingGlass,
  PhX,
  PhArrowClockwise,
  PhSpinner,
  PhTrash,
  PhArrowUp,
  PhArrowDown,
  PhArrowsDownUp,
} from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { errorTracking } from '@/services/errorTracking'
import { useToast } from '@/services/toastService'
import { safeText } from '@/utils/textUtils'
import { useTableSort } from '@/composables/useTableSort'
import { EmptyState, LoadingState } from '@/components/base'
import type { BlocklistEntry } from '@/types'

const entries = ref<BlocklistEntry[]>([])
const bookTitles = ref<Map<number, string>>(new Map())
const loading = ref(false)
const filterText = ref('')
const removing = ref<Set<number>>(new Set())

type BlocklistSortKey = 'title' | 'book' | 'source' | 'failures' | 'date'

const sortableColumns: Array<{ key: BlocklistSortKey; label: string; class: string }> = [
  { key: 'title', label: 'Release', class: 'col-title' },
  { key: 'book', label: 'Book', class: 'col-book' },
  { key: 'source', label: 'Indexer', class: 'col-source' },
  { key: 'failures', label: 'Failures', class: 'col-failures' },
  { key: 'date', label: 'Blocklisted', class: 'col-date' },
]

const { toggleSort, directionFor, sortItems } = useTableSort<BlocklistEntry, BlocklistSortKey>({
  title: (entry) => entry.title,
  book: (entry) => (entry.audiobookId ? bookTitle(entry.audiobookId) : null),
  source: (entry) => entry.source,
  failures: (entry) => entry.failureCount,
  date: (entry) => entry.createdAt,
})

function ariaSortFor(key: BlocklistSortKey): 'ascending' | 'descending' | 'none' {
  const direction = directionFor(key)
  if (!direction) return 'none'
  return direction === 'asc' ? 'ascending' : 'descending'
}

function sortIconFor(key: BlocklistSortKey) {
  const direction = directionFor(key)
  if (!direction) return PhArrowsDownUp
  return direction === 'asc' ? PhArrowUp : PhArrowDown
}

const filtered = computed(() => {
  const query = filterText.value.trim().toLowerCase()
  const matches = query
    ? entries.value.filter(
        (entry) =>
          entry.title.toLowerCase().includes(query) ||
          (entry.source || '').toLowerCase().includes(query) ||
          (entry.reason || '').toLowerCase().includes(query),
      )
    : entries.value

  return sortItems(matches)
})

function bookTitle(audiobookId: number): string {
  return bookTitles.value.get(audiobookId) ?? `Book ${audiobookId}`
}

function formatDate(value: string): string {
  if (!value) return '—'
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString()
}

async function load() {
  loading.value = true
  try {
    entries.value = await apiService.getBlocklist()

    // Titles are resolved separately so the blocklist endpoint stays a thin read of its own
    // table; only the books actually referenced are fetched.
    const ids = [...new Set(entries.value.map((entry) => entry.audiobookId).filter(Boolean))]
    await Promise.all(
      (ids as number[]).map(async (id) => {
        if (bookTitles.value.has(id)) return
        try {
          const book = await apiService.getAudiobook(id)
          if (book?.title) bookTitles.value.set(id, book.title)
        } catch {
          // A deleted book should not stop the rest of the list rendering.
        }
      }),
    )
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'BlocklistView',
      operation: 'load',
    })
    useToast().error('Could not load blocklist', (error as Error).message)
  } finally {
    loading.value = false
  }
}

async function remove(entry: BlocklistEntry) {
  removing.value = new Set(removing.value).add(entry.id)
  try {
    await apiService.removeBlocklistEntry(entry.id)
    entries.value = entries.value.filter((candidate) => candidate.id !== entry.id)
    useToast().success('Removed from blocklist', 'This release can be grabbed again.')
  } catch (error) {
    errorTracking.captureException(error as Error, {
      component: 'BlocklistView',
      operation: 'remove',
    })
    useToast().error('Could not remove entry', (error as Error).message)
  } finally {
    const next = new Set(removing.value)
    next.delete(entry.id)
    removing.value = next
  }
}

onMounted(load)
</script>

<style scoped>
.blocklist-view {
  padding: 1em;
}

.page-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.75rem;
}

.page-header h1 {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0;
  color: var(--text-primary);
  font-size: 2rem;
  font-weight: 500;
}

.page-header h1 svg {
  width: 32px;
  height: 32px;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.page-intro {
  color: #868e96;
  margin: 0 0 1.25rem;
  max-width: 70ch;
}

.filter-input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.filter-icon {
  position: absolute;
  left: 0.75rem;
  width: 16px;
  height: 16px;
  color: #868e96;
  pointer-events: none;
}

.filter-input {
  width: 220px;
  height: var(--control-height, 40px);
  padding: 0.5rem 2rem 0.5rem 2.25rem;
  background: var(--card-bg);
  color: var(--text-primary);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  font-size: 0.875rem;
}

.filter-clear {
  position: absolute;
  right: 0.5rem;
  display: flex;
  padding: 0.25rem;
  background: none;
  border: none;
  border-radius: 4px;
  color: #868e96;
  cursor: pointer;
}

.filter-clear svg {
  width: 14px;
  height: 14px;
}

.table-wrapper {
  background: var(--bg-primary);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  overflow-x: auto;
}

.blocklist-table {
  width: 100%;
  border-collapse: collapse;
}

.blocklist-table th {
  padding: 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: var(--bg-secondary);
  text-align: left;
}

.sortable-header {
  position: relative;
}

.sortable-header:hover {
  background: var(--bg-secondary, rgba(0, 0, 0, 0.04));
}

.sortable-header[aria-sort='ascending'],
.sortable-header[aria-sort='descending'] {
  background: linear-gradient(180deg, rgba(99, 102, 241, 0.08), rgba(99, 102, 241, 0.02));
}

.sort-button {
  width: 100%;
  padding: 0.6rem 0.75rem;
  background: none;
  border: none;
  cursor: pointer;
  color: #868e96;
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  text-align: left;
}

.header-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.sort-icon {
  opacity: 0.72;
  flex-shrink: 0;
}

.blocklist-table td {
  padding: 0.6rem 0.75rem;
  border-bottom: 1px solid rgba(255, 255, 255, 0.04);
  color: #adb5bd;
  font-size: 0.875rem;
  vertical-align: top;
}

.blocklist-table tr:last-child td {
  border-bottom: none;
}

.title-text {
  color: var(--text-primary);
  word-break: break-word;
}

.reason {
  margin-top: 0.2rem;
  color: #868e96;
  font-size: 0.8rem;
}

.book-link {
  color: #4dabf7;
  text-decoration: none;
}

.book-link:hover {
  text-decoration: underline;
}

.failure-count {
  display: inline-block;
  padding: 0.1rem 0.45rem;
  border-radius: 4px;
  background: rgba(250, 82, 82, 0.15);
  color: #fa5252;
  font-weight: 600;
}

.col-actions {
  width: 48px;
  text-align: center;
}

.muted {
  color: #495057;
}

.btn-danger-icon {
  display: flex;
  padding: 0.35rem;
  background: none;
  border: none;
  border-radius: 4px;
  color: #868e96;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-danger-icon:hover:not(:disabled) {
  color: #fa5252;
  background: rgba(250, 82, 82, 0.15);
}

.btn-danger-icon:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-danger-icon svg {
  width: 16px;
  height: 16px;
}

@media (max-width: 768px) {
  .page-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .header-actions {
    width: 100%;
    flex-wrap: wrap;
  }

  .filter-input {
    width: 100%;
  }
}
</style>
