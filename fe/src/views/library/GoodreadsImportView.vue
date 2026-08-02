<!-- Bookmarkarr is licensed under the GNU AGPL v3 or later. -->
<template>
  <main class="goodreads-import">
    <header>
      <p class="eyebrow">Library · Import · Goodreads</p>
      <h1>Import your Goodreads library</h1>
      <p>Preview and choose editions before committing. Imports add missing editions only and never start searches.</p>
    </header>

    <section class="card upload-card">
      <label class="file-picker">
        <span>Goodreads CSV</span>
        <input type="file" accept=".csv,text/csv" :disabled="loading" @change="selectFile" />
      </label>
      <button class="primary" :disabled="!file || loading" @click="preview">
        {{ loading ? 'Reading…' : 'Preview import' }}
      </button>
      <span class="hint">Maximum 10 MB and 25,000 rows. The raw CSV is never retained.</span>
      <p v-if="error" class="error" role="alert">{{ error }}</p>
    </section>

    <section v-if="batch" class="card preview-card">
      <div class="toolbar">
        <div>
          <strong>{{ selectedCount }} selected</strong>
          <span>{{ batch.eligibleCount }} eligible · {{ batch.ambiguousCount }} ambiguous</span>
        </div>
        <div class="actions">
          <button @click="selectAll(true)">Select all</button>
          <button @click="selectAll(false)">Select none</button>
          <select aria-label="Bulk media format" @change="applyBulkFormat">
            <option value="">Bulk format…</option>
            <option value="both">Audiobook + ebook</option>
            <option value="audio">Audiobook only</option>
            <option value="ebook">Ebook only</option>
          </select>
        </div>
      </div>

      <div class="table-wrap">
        <table>
          <thead><tr><th></th><th>Goodreads book</th><th>Match</th><th>Requested editions</th></tr></thead>
          <tbody>
            <tr v-for="row in batch.rows" :key="row.rowId" :class="{ ineligible: !row.eligible }">
              <td><input v-model="row.selected" type="checkbox" :disabled="!row.eligible" :aria-label="`Select ${row.title}`" /></td>
              <td>
                <strong>{{ row.title || 'Untitled row' }}</strong>
                <span>{{ row.primaryAuthor || row.ineligibleReason }}</span>
                <small v-if="row.goodreadsId">Goodreads {{ row.goodreadsId }}</small>
              </td>
              <td>
                <span class="match" :class="row.matchStatus">{{ matchLabel(row) }}</span>
                <select v-if="row.matchStatus === 'ambiguous'" v-model.number="row.resolvedBookId" :aria-label="`Resolve ${row.title}`">
                  <option :value="undefined">Resolve match…</option>
                  <option v-for="candidate in row.matchCandidates" :key="candidate.bookId" :value="candidate.bookId">
                    {{ candidate.title }} — {{ candidate.primaryAuthor }}
                  </option>
                </select>
              </td>
              <td class="formats">
                <label><input type="checkbox" :checked="row.mediaFormats.includes('Audiobook')" :disabled="!row.eligible" @change="toggleFormat(row, 'Audiobook')" /> Audiobook</label>
                <label><input type="checkbox" :checked="row.mediaFormats.includes('Ebook')" :disabled="!row.eligible" @change="toggleFormat(row, 'Ebook')" /> Ebook</label>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <footer>
        <span>Batch expires {{ new Date(batch.expiresAt).toLocaleString() }}</span>
        <button class="primary" :disabled="committing || selectedCount === 0 || unresolvedCount > 0" @click="commit">
          {{ committing ? 'Importing…' : `Import ${selectedCount} books` }}
        </button>
      </footer>
      <p v-if="unresolvedCount" class="error">Resolve {{ unresolvedCount }} ambiguous selected row(s) before importing.</p>
    </section>

    <section v-if="summary" class="card success" role="status">
      <h2>Import complete</h2>
      <p>{{ summary.createdBooks }} books and {{ summary.createdEditions }} missing editions added. {{ summary.unchangedRows }} rows were already complete.</p>
      <p>No automatic searches were initiated.</p>
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { apiService } from '@/services/api'
import type { EditionMediaType, GoodreadsCommitSummary, GoodreadsPreviewResponse, GoodreadsPreviewRow } from '@/types'

const file = ref<File | null>(null)
const batch = ref<GoodreadsPreviewResponse | null>(null)
const summary = ref<GoodreadsCommitSummary | null>(null)
const loading = ref(false)
const committing = ref(false)
const error = ref('')
const selectedCount = computed(() => batch.value?.rows.filter((row) => row.selected && row.eligible).length ?? 0)
const unresolvedCount = computed(() => batch.value?.rows.filter((row) => row.selected && row.matchStatus === 'ambiguous' && !row.resolvedBookId).length ?? 0)

function selectFile(event: Event) {
  file.value = (event.target as HTMLInputElement).files?.[0] ?? null
  batch.value = null
  summary.value = null
}
async function preview() {
  if (!file.value) return
  loading.value = true
  error.value = ''
  try { batch.value = await apiService.previewGoodreadsImport(file.value) }
  catch (cause) { error.value = cause instanceof Error ? cause.message : 'Preview failed.' }
  finally { loading.value = false }
}
function selectAll(selected: boolean) { batch.value?.rows.forEach((row) => { if (row.eligible) row.selected = selected }) }
function formats(value: string): EditionMediaType[] { return value === 'audio' ? ['Audiobook'] : value === 'ebook' ? ['Ebook'] : ['Audiobook', 'Ebook'] }
function applyBulkFormat(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  if (!value) return
  batch.value?.rows.forEach((row) => { if (row.eligible && row.selected) row.mediaFormats = formats(value) })
}
function toggleFormat(row: GoodreadsPreviewRow, format: EditionMediaType) {
  row.mediaFormats = row.mediaFormats.includes(format) ? row.mediaFormats.filter((item) => item !== format) : [...row.mediaFormats, format]
}
function matchLabel(row: GoodreadsPreviewRow) {
  if (!row.eligible) return 'Ineligible'
  if (row.matchStatus === 'new') return 'New book'
  if (row.matchStatus === 'matched') return `Matched by ${row.matchMethod === 'titleAuthor' ? 'title + author' : row.matchMethod}`
  return 'Needs resolution'
}
async function commit() {
  if (!batch.value || unresolvedCount.value) return
  if (batch.value.rows.some((row) => row.selected && row.mediaFormats.length === 0)) { error.value = 'Every selected row needs at least one edition.'; return }
  committing.value = true
  error.value = ''
  try { summary.value = await apiService.commitGoodreadsImport(batch.value.batchId, batch.value.rows) }
  catch (cause) { error.value = cause instanceof Error ? cause.message : 'Import failed.' }
  finally { committing.value = false }
}
</script>

<style scoped>
.goodreads-import{max-width:1180px;margin:0 auto;padding:2rem}.eyebrow{color:#11b8aa;font-weight:700;text-transform:uppercase;letter-spacing:.08em}.goodreads-import h1{margin:.2rem 0}.card{margin-top:1.5rem;padding:1.25rem;border:1px solid var(--border-color,#30405b);border-radius:14px;background:var(--card-bg,#102442)}.upload-card,.toolbar,.actions,footer{display:flex;align-items:center;gap:1rem;flex-wrap:wrap}.file-picker{display:flex;flex-direction:column;gap:.35rem}.hint,.toolbar span,td span,td small,footer span{display:block;color:var(--text-secondary,#9fb0c8)}button,select,input[type=file]{font:inherit}.primary{background:#11b8aa;color:#031a2e;border:0;border-radius:8px;padding:.65rem 1rem;font-weight:700}.primary:disabled{opacity:.5}.actions{margin-left:auto}.actions button,.actions select,td select{background:transparent;color:inherit;border:1px solid #53647c;border-radius:7px;padding:.45rem}.table-wrap{overflow:auto;margin-top:1rem}table{width:100%;border-collapse:collapse;min-width:780px}th,td{text-align:left;padding:.8rem;border-bottom:1px solid #30405b;vertical-align:top}.formats label{display:block;margin-bottom:.4rem}.match{display:inline-block;border-radius:999px;padding:.2rem .55rem;font-size:.8rem;background:#314158}.match.matched{background:#174d45;color:#7ef0dc}.match.ambiguous{background:#674b16;color:#ffd47a}.ineligible{opacity:.55}.error{color:#ff8977}.success{border-color:#11b8aa}footer{justify-content:space-between;margin-top:1rem}@media(max-width:700px){.goodreads-import{padding:1rem}.actions{margin-left:0}}
</style>
