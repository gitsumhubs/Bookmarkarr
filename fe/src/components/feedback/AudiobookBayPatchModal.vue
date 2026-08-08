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
  <Modal :visible="visible" title="AudioBook Bay is capped at 9 results" @close="dismiss">
    <div class="abb-patch">
      <!-- Applied. Report the concrete before/after rather than a bare success. -->
      <template v-if="result?.kind === 'success'">
        <p class="abb-lede">
          <strong>{{ result.indexerName }}</strong> now reads
          {{ result.pages }} page{{ result.pages === 1 ? '' : 's' }} of results.
        </p>
        <div class="abb-delta">
          <span class="abb-before">{{ result.previousResultCap }}</span>
          <span class="abb-arrow">→</span>
          <span class="abb-after">{{ result.projectedResults }}</span>
          <span class="abb-delta-label">results per search</span>
        </div>
        <p class="abb-note">
          Prowlarr loaded the definition without a restart. Re-run a search to see the difference.
        </p>
      </template>

      <template v-else>
        <p class="abb-lede">
          <strong>{{ status?.indexerName }}</strong> uses Prowlarr's built-in AudioBook Bay
          indexer, which reads only the first page of the site's results — about a third of what
          AudioBook Bay actually holds.
        </p>

        <div class="abb-delta">
          <span class="abb-before">{{ status?.currentResultCap ?? 9 }}</span>
          <span class="abb-arrow">→</span>
          <span class="abb-after">{{ projectedForPages }}</span>
          <span class="abb-delta-label">results per search</span>
        </div>

        <label class="abb-field">
          <span class="abb-field-label">Pages to read</span>
          <select v-model.number="pages" class="form-input" :disabled="busy">
            <option v-for="option in pageOptions" :key="option" :value="option">
              {{ option }} page{{ option === 1 ? '' : 's' }} — about
              {{ option * (status?.resultsPerPage ?? 9) }} results
            </option>
          </select>
        </label>
        <p class="abb-hint">
          Each page is one more request to AudioBook Bay per search, so a higher number means
          slower searches. Three is a good balance.
        </p>

        <!-- The mount is the one prerequisite the app cannot arrange for itself. -->
        <div v-if="showPathField" class="abb-manual">
          <p class="abb-warn">
            {{ mountMessage }}
          </p>
          <p class="abb-hint">
            To let Bookmarkarr apply this itself, mount Prowlarr's config directory and restart
            Bookmarkarr:
          </p>
          <pre class="abb-code">volumes:
  - /path/to/prowlarr:/prowlarr-config</pre>
          <label class="abb-field">
            <span class="abb-field-label">Or enter the path as mounted in Bookmarkarr</span>
            <input
              v-model="definitionsDirectory"
              class="form-input"
              placeholder="/prowlarr-config"
              :disabled="busy"
            />
          </label>
          <p class="abb-hint">
            Prefer to do it by hand? Download the file and drop it in Prowlarr's
            <code>Definitions/Custom/</code> folder — it is picked up without a restart.
          </p>
          <a class="btn btn-secondary btn-sm" :href="definitionUrl" download>
            Download audiobookbay.yml
          </a>
        </div>

        <p v-else-if="errorMessage" class="abb-warn">{{ errorMessage }}</p>
      </template>
    </div>

    <template #footer>
      <template v-if="result?.kind === 'success'">
        <button class="btn btn-primary" @click="dismiss">Done</button>
      </template>
      <template v-else>
        <button class="btn btn-secondary" :disabled="busy" @click="dismiss">Not now</button>
        <button class="btn btn-primary" :disabled="busy" @click="apply">
          {{ busy ? 'Applying…' : 'Apply patch' }}
        </button>
      </template>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import Modal from './Modal.vue'
import { apiService } from '@/services/api'
import { logger } from '@/utils/logger'
import type { AudiobookBayPatchStatus, AudiobookBayPatchResult } from '@/types'

const props = defineProps<{
  visible: boolean
  status: AudiobookBayPatchStatus | null
}>()

const emit = defineEmits<{
  (e: 'close'): void
  (e: 'applied', result: AudiobookBayPatchResult): void
}>()

const pages = ref(props.status?.defaultPages ?? 3)
const definitionsDirectory = ref('')
const busy = ref(false)
const result = ref<AudiobookBayPatchResult | null>(null)
const errorMessage = ref('')
const mountMessage = ref('')

const pageOptions = computed(() => {
  const max = props.status?.maximumPages ?? 10
  return Array.from({ length: max }, (_, index) => index + 1)
})

const projectedForPages = computed(() => pages.value * (props.status?.resultsPerPage ?? 9))

// Shown once the server reports the mount is missing, and up front when it already knows.
const showPathField = computed(() => Boolean(mountMessage.value) || props.status?.canAutoApply === false)

const definitionUrl = computed(() => apiService.getAudiobookBayDefinitionUrl(pages.value))

async function apply() {
  busy.value = true
  errorMessage.value = ''
  mountMessage.value = ''

  try {
    const applied = await apiService.applyAudiobookBayPatch(
      pages.value,
      definitionsDirectory.value.trim() || undefined,
    )
    result.value = applied
    emit('applied', applied)
  } catch (error) {
    const status = (error as { status?: number }).status
    const body = (error as { body?: string }).body
    let message = ''
    try {
      message = body ? (JSON.parse(body).message ?? '') : ''
    } catch {
      message = body ?? ''
    }

    // 409 means the config directory isn't reachable — recoverable by supplying a path or
    // installing the file by hand, so it gets the fallback UI rather than a bare error.
    if (status === 409) {
      mountMessage.value = message || 'Prowlarr’s config directory is not mounted into Bookmarkarr.'
    } else {
      errorMessage.value = message || 'Could not apply the patch. Check the logs for details.'
      logger.warn('AudioBook Bay patch failed', error)
    }
  } finally {
    busy.value = false
  }
}

function dismiss() {
  emit('close')
}
</script>

<style scoped>
.abb-patch {
  display: flex;
  flex-direction: column;
  gap: 0.85rem;
}

.abb-lede {
  margin: 0;
  line-height: 1.5;
}

.abb-delta {
  display: flex;
  align-items: baseline;
  gap: 0.6rem;
  padding: 0.75rem 1rem;
  border-radius: 8px;
  background: var(--color-background-soft, rgba(127, 127, 127, 0.12));
}

.abb-before {
  font-size: 1.5rem;
  font-weight: 600;
  text-decoration: line-through;
  opacity: 0.6;
}

.abb-arrow {
  opacity: 0.6;
}

.abb-after {
  font-size: 1.9rem;
  font-weight: 700;
  color: var(--color-success, #3fb950);
}

.abb-delta-label {
  opacity: 0.75;
  font-size: 0.9rem;
}

.abb-field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.abb-field-label {
  font-size: 0.9rem;
  font-weight: 600;
}

.abb-hint,
.abb-note {
  margin: 0;
  font-size: 0.85rem;
  opacity: 0.75;
  line-height: 1.45;
}

.abb-warn {
  margin: 0;
  font-size: 0.9rem;
  color: var(--color-warning, #d29922);
  line-height: 1.45;
}

.abb-manual {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  padding-top: 0.6rem;
  border-top: 1px solid var(--color-border, rgba(127, 127, 127, 0.25));
}

.abb-code {
  margin: 0;
  padding: 0.6rem 0.75rem;
  border-radius: 6px;
  background: var(--color-background-mute, rgba(127, 127, 127, 0.16));
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.82rem;
  overflow-x: auto;
}
</style>
