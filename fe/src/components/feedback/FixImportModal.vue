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
  <div class="modal-overlay" @click.self="close">
    <div class="modal-content">
      <div class="modal-header">
        <h3>Fix Import</h3>
        <button class="close-button" @click="close"><PhX :size="18" /></button>
      </div>

      <div class="modal-body">
        <p v-if="loading" class="hint">Checking file names…</p>

        <p v-else-if="error" class="error-text">{{ error }}</p>

        <template v-else-if="issues.length">
          <p class="hint">
            Your download client wrote {{ issues.length }} file{{ issues.length === 1 ? '' : 's' }}
            under a different name than it reports. Renaming
            {{ issues.length === 1 ? 'it' : 'them' }} back lets the import finish.
          </p>

          <div v-for="issue in issues" :key="issue.reportedPath" class="issue">
            <div class="issue-row">
              <span class="issue-label">Expected</span>
              <code class="expected">{{ issue.reportedName }}</code>
            </div>
            <div class="issue-row">
              <span class="issue-label">Found</span>
              <code class="found">{{ issue.actualName }}</code>
              <span v-if="!issue.actualExists" class="missing-flag">no longer on disk</span>
            </div>
            <div class="issue-row">
              <span class="issue-label">Use instead</span>
              <input
                v-model="overrides[issue.reportedPath]"
                type="text"
                class="override-input"
                :placeholder="issue.actualName"
              />
            </div>
            <div class="issue-dir">{{ issue.directory }}</div>
          </div>

          <p class="hint subtle">
            Leave “Use instead” blank to accept the detected match. Any file you name must sit in
            the same folder as the download.
          </p>
        </template>

        <p v-else class="hint">No filename problems were detected for this download.</p>
      </div>

      <div class="modal-footer">
        <button class="btn btn-secondary" @click="close">Cancel</button>
        <button
          class="btn btn-primary"
          :disabled="applying || loading || issues.length === 0"
          @click="apply"
        >
          {{ applying ? 'Fixing…' : 'Rename and retry import' }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { PhX } from '@phosphor-icons/vue'
import { apiService } from '@/services/api'
import { errorTracking } from '@/services/errorTracking'

interface ImportIssue {
  reportedName: string
  actualName: string
  directory: string
  reportedPath: string
  actualPath: string
  actualExists: boolean
  reportedExists: boolean
}

const props = defineProps<{ downloadId: string }>()
const emit = defineEmits<{ close: []; fixed: [] }>()

const loading = ref(true)
const applying = ref(false)
const error = ref('')
const issues = ref<ImportIssue[]>([])

// Keyed by reported path so a blank entry means "use what was detected" rather than "no file".
const overrides = ref<Record<string, string>>({})

onMounted(async () => {
  try {
    const result = await apiService.getImportIssues(props.downloadId)
    issues.value = result.issues
  } catch (err) {
    error.value = (err as Error).message || 'Could not load the detected filename issues'
    errorTracking.captureException(err as Error, {
      component: 'FixImportModal',
      operation: 'getImportIssues',
    })
  } finally {
    loading.value = false
  }
})

async function apply() {
  applying.value = true
  error.value = ''
  try {
    const filled = Object.fromEntries(
      Object.entries(overrides.value).filter(([, value]) => value && value.trim().length > 0),
    )
    await apiService.fixImport(props.downloadId, filled)
    emit('fixed')
    emit('close')
  } catch (err) {
    error.value = (err as Error).message || 'The rename failed'
    errorTracking.captureException(err as Error, {
      component: 'FixImportModal',
      operation: 'fixImport',
    })
  } finally {
    applying.value = false
  }
}

function close() {
  emit('close')
}
</script>

<style scoped>
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-content {
  background: var(--surface, #1e222b);
  color: var(--text-primary, #e9ecef);
  border-radius: 8px;
  width: min(640px, 92vw);
  max-height: 85vh;
  display: flex;
  flex-direction: column;
}

.modal-header,
.modal-footer {
  display: flex;
  align-items: center;
  padding: 0.9rem 1.1rem;
}

.modal-header {
  justify-content: space-between;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.modal-footer {
  justify-content: flex-end;
  gap: 0.5rem;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
}

.modal-body {
  padding: 1rem 1.1rem;
  overflow-y: auto;
}

.close-button {
  background: none;
  border: none;
  color: inherit;
  cursor: pointer;
}

.hint {
  font-size: 0.88rem;
  color: var(--text-secondary, #adb5bd);
  margin: 0 0 0.9rem;
}

.hint.subtle {
  margin-top: 0.9rem;
  margin-bottom: 0;
  font-size: 0.8rem;
}

.error-text {
  color: #ff6b6b;
  font-size: 0.88rem;
}

.issue {
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 6px;
  padding: 0.7rem 0.8rem;
  margin-bottom: 0.7rem;
}

.issue-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.35rem;
  flex-wrap: wrap;
}

.issue-label {
  min-width: 5.5rem;
  font-size: 0.78rem;
  color: var(--text-secondary, #adb5bd);
}

code {
  font-size: 0.82rem;
  word-break: break-all;
}

.expected {
  color: #51cf66;
}

.found {
  color: #ffc107;
}

.missing-flag {
  font-size: 0.75rem;
  color: #ff6b6b;
}

.issue-dir {
  font-size: 0.74rem;
  color: var(--text-secondary, #868e96);
  word-break: break-all;
  margin-top: 0.3rem;
}

.override-input {
  flex: 1;
  min-width: 12rem;
  padding: 0.3rem 0.45rem;
  font-size: 0.82rem;
  border-radius: 4px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  background: rgba(0, 0, 0, 0.25);
  color: inherit;
}
</style>
