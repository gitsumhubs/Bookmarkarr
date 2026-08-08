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
  <div class="tab-content">
    <div class="abb-patch-tab">
      <div class="section-header">
        <h3>
          AudioBook Bay Patch
          <PhSpinner v-if="loading" class="ph-spin small-inline-spinner" />
        </h3>
        <Pill v-if="diagnostics" :class="statePill.className">{{ statePill.label }}</Pill>
      </div>

      <p class="section-intro">
        Prowlarr's compiled AudioBook Bay indexer reads only the first page of the site's results,
        so every search stops at {{ diagnostics?.resultsPerPage ?? 9 }}. Bookmarkarr can replace it
        with a paginated definition that reads several pages instead.
      </p>

      <LoadingState v-if="loading && !diagnostics" message="Checking..." />

      <template v-else-if="diagnostics">
        <!-- Each precondition reports its own outcome, so a failure names itself instead of
             collapsing into one unhelpful "not available". -->
        <div class="check-list">
          <div
            v-for="check in diagnostics.checks"
            :key="check.id"
            class="check"
            :class="check.state.toLowerCase()"
          >
            <component :is="checkIcon(check.state)" class="check-icon" />
            <div class="check-body">
              <div class="check-label">{{ check.label }}</div>
              <div v-if="check.detail" class="check-detail">{{ check.detail }}</div>
              <div v-if="check.remedy && check.state !== 'Pass'" class="check-remedy">
                {{ check.remedy }}
              </div>
            </div>
          </div>
        </div>

        <div class="actions">
          <button class="btn btn-secondary" :disabled="busy" @click="load">
            <PhArrowsClockwise /> Re-check
          </button>
          <button
            class="btn btn-secondary"
            :disabled="busy || !diagnostics.prowlarrIndexerId"
            @click="runSearchTest"
          >
            <PhMagnifyingGlass /> {{ searching ? 'Searching…' : 'Run search test' }}
          </button>
        </div>

        <div v-if="searchProbe" class="probe-result" :class="searchProbeClass">
          <strong>{{
            searchProbe.ran
              ? `${searchProbe.results} result(s) for "${searchProbe.query}"`
              : 'Search did not run'
          }}</strong>
          <span v-if="searchProbe.message">{{ searchProbe.message }}</span>
        </div>

        <!-- Apply -->
        <section v-if="diagnostics.patchState === 'NotPatched'" class="panel">
          <h4>Apply the patch</h4>
          <p>
            Reads {{ pages }} page(s) instead of one — up to
            {{ pages * diagnostics.resultsPerPage }} results per search, from
            {{ diagnostics.resultsPerPage }} today.
          </p>
          <div class="control-row">
            <label for="abb-pages">Pages</label>
            <select id="abb-pages" v-model.number="pages" class="form-input" :disabled="busy">
              <option v-for="n in diagnostics.maximumPages" :key="n" :value="n">{{ n }}</option>
            </select>
            <button
              class="btn btn-primary"
              :disabled="busy || !diagnostics.canAutoApply"
              @click="apply(false)"
            >
              {{ applying ? 'Applying…' : 'Apply patch' }}
            </button>
          </div>
          <p v-if="!diagnostics.canAutoApply" class="hint">
            Bookmarkarr cannot write to Prowlarr's definitions directory, so it cannot apply this
            itself. Set the path below, or install the definition by hand.
          </p>
        </section>

        <!-- Revert -->
        <section v-if="diagnostics.patchState === 'Patched'" class="panel">
          <h4>Remove the patch</h4>
          <p>
            Points the indexer back at Prowlarr's compiled AudioBook Bay and deletes the indexer
            this patch created.
          </p>
          <label class="checkbox-row">
            <input v-model="deleteDefinition" type="checkbox" :disabled="busy" />
            <span>Also delete the definition file. Leaving it makes re-applying immediate.</span>
          </label>
          <div class="control-row">
            <button
              class="btn btn-danger"
              :disabled="busy || !diagnostics.canRevert"
              @click="revert"
            >
              {{ reverting ? 'Reverting…' : 'Remove patch' }}
            </button>
          </div>
          <p v-if="!diagnostics.canRevert" class="hint">
            This patch was not applied by Bookmarkarr — or was applied before it kept a record — so
            there is nothing it can safely undo. Repoint the indexer by hand in Settings > Indexers.
          </p>
        </section>

        <!-- Prowlarr config directory -->
        <section class="panel">
          <h4>Prowlarr's config directory</h4>
          <p>
            The container-side path where Prowlarr's config is mounted into Bookmarkarr. Checked
            before anything is written, and remembered once it works.
          </p>
          <div class="control-row">
            <input
              v-model="directoryInput"
              class="form-input grow"
              placeholder="/prowlarr-config"
              :disabled="busy"
              @keyup.enter="probeDirectory"
            />
            <button
              class="btn btn-secondary"
              :disabled="busy || !directoryInput.trim()"
              @click="probeDirectory"
            >
              {{ probing ? 'Checking…' : 'Check path' }}
            </button>
          </div>
          <div
            v-if="directoryProbe"
            class="probe-result"
            :class="directoryProbe.writable ? 'ok' : 'bad'"
          >
            <strong>{{ directoryProbe.writable ? 'Usable and writable' : 'Not usable' }}</strong>
            <span v-if="directoryProbe.message">{{ directoryProbe.message }}</span>
          </div>
          <p class="hint">
            Not mounted yet? Add
            <code>- /path/to/prowlarr/config:/prowlarr-config</code> to Bookmarkarr's compose file
            and recreate the container — a new mount cannot appear in a running one. Prowlarr on
            another host cannot be mounted at all; install the definition by hand instead.
          </p>
        </section>

        <!-- Manual install -->
        <section class="panel">
          <h4>Install by hand</h4>
          <p>
            Copy this into
            <code
              >&lt;prowlarr-config&gt;/Definitions/{{ diagnostics.customSubdirectory }}/{{
                diagnostics.definitionFileName
              }}</code
            >. Bookmarkarr does the rest over Prowlarr's API — waiting for the reload, creating the
            indexer, and repointing itself.
          </p>
          <div class="control-row">
            <button class="btn btn-secondary" :disabled="busy" @click="loadDefinition">
              {{ definitionText ? 'Refresh' : 'Show definition' }}
            </button>
            <a class="btn btn-secondary" :href="definitionUrl" download>
              <PhDownloadSimple /> Download
            </a>
            <button v-if="definitionText" class="btn btn-secondary" @click="copyDefinition">
              {{ copied ? 'Copied' : 'Copy' }}
            </button>
          </div>
          <pre v-if="definitionText" class="definition">{{ definitionText }}</pre>
          <div class="control-row">
            <button
              class="btn btn-primary"
              :disabled="busy || diagnostics.patchState !== 'NotPatched'"
              @click="apply(true)"
            >
              {{ wiring ? 'Wiring up…' : "I've installed it — wire it up" }}
            </button>
          </div>
        </section>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  PhArrowsClockwise,
  PhCheckCircle,
  PhDownloadSimple,
  PhMagnifyingGlass,
  PhQuestion,
  PhSpinner,
  PhWarning,
  PhXCircle,
} from '@phosphor-icons/vue'
import { Pill, LoadingState } from '@/components/base'
import { apiService } from '@/services/api'
import { useToast } from '@/services/toastService'
import { logger } from '@/utils/logger'
import type {
  AudiobookBayCheckState,
  AudiobookBayDiagnostics,
  AudiobookBayDirectoryProbe,
  AudiobookBaySearchProbe,
} from '@/types'

const toast = useToast()

const diagnostics = ref<AudiobookBayDiagnostics | null>(null)
const loading = ref(false)
const applying = ref(false)
const wiring = ref(false)
const reverting = ref(false)
const probing = ref(false)
const searching = ref(false)

const pages = ref(3)
const deleteDefinition = ref(false)
const directoryInput = ref('')
const directoryProbe = ref<AudiobookBayDirectoryProbe | null>(null)
const searchProbe = ref<AudiobookBaySearchProbe | null>(null)
const definitionText = ref('')
const copied = ref(false)

const busy = computed(
  () =>
    loading.value ||
    applying.value ||
    wiring.value ||
    reverting.value ||
    probing.value ||
    searching.value,
)

const definitionUrl = computed(() => apiService.getAudiobookBayDefinitionUrl(pages.value))

const statePill = computed(() => {
  switch (diagnostics.value?.patchState) {
    case 'Patched':
      return { label: 'Patched', className: 'pill-ok' }
    case 'NotPatched':
      return { label: 'Not patched', className: 'pill-warn' }
    case 'NotApplicable':
      return { label: 'Not in use', className: 'pill-muted' }
    default:
      return { label: 'Unknown', className: 'pill-muted' }
  }
})

const searchProbeClass = computed(() =>
  searchProbe.value?.ran && searchProbe.value.results > 0 ? 'ok' : 'bad',
)

const checkIcon = (state: AudiobookBayCheckState) => {
  if (state === 'Pass') return PhCheckCircle
  if (state === 'Fail') return PhXCircle
  if (state === 'Warn') return PhWarning
  return PhQuestion
}

const load = async () => {
  loading.value = true
  try {
    diagnostics.value = await apiService.getAudiobookBayDiagnostics()
    pages.value = diagnostics.value.installedPages ?? diagnostics.value.defaultPages
    if (!directoryInput.value && diagnostics.value.definitionsDirectory) {
      directoryInput.value = diagnostics.value.definitionsDirectory
    }
  } catch (error) {
    logger.error('Failed to load AudioBook Bay diagnostics', String(error))
    toast.error('AudioBook Bay', 'Could not read the patch status')
  } finally {
    loading.value = false
  }
}

const apply = async (alreadyInstalled: boolean) => {
  const flag = alreadyInstalled ? wiring : applying
  flag.value = true
  try {
    const result = await apiService.applyAudiobookBayPatch(
      pages.value,
      directoryInput.value.trim() || undefined,
      alreadyInstalled,
    )
    if (result.kind === 'success') {
      toast.success('AudioBook Bay', `Patched: up to ${result.projectedResults} results per search`)
    } else {
      toast.error('AudioBook Bay', result.message || 'The patch could not be applied')
    }
  } catch (error) {
    // A missing mount answers 409 and a genuine failure answers 400; both arrive here as a
    // rejected request, and the server's message is the useful part either way.
    logger.error('Failed to apply the AudioBook Bay patch', String(error))
    toast.error('AudioBook Bay', extractMessage(error, 'The patch could not be applied'))
  } finally {
    flag.value = false
    await load()
  }
}

const revert = async () => {
  reverting.value = true
  try {
    const result = await apiService.revertAudiobookBayPatch(deleteDefinition.value)
    if (result.kind === 'success') {
      toast.success('AudioBook Bay', 'Patch removed')
    } else {
      toast.error('AudioBook Bay', result.message || 'The patch could not be removed')
    }
  } catch (error) {
    logger.error('Failed to revert the AudioBook Bay patch', String(error))
    toast.error('AudioBook Bay', extractMessage(error, 'The patch could not be removed'))
  } finally {
    reverting.value = false
    await load()
  }
}

const probeDirectory = async () => {
  probing.value = true
  try {
    directoryProbe.value = await apiService.probeAudiobookBayDirectory(directoryInput.value.trim())
    if (directoryProbe.value.writable) {
      await load()
    }
  } catch (error) {
    logger.error('Failed to probe the Prowlarr directory', String(error))
    toast.error('AudioBook Bay', 'Could not check that path')
  } finally {
    probing.value = false
  }
}

const runSearchTest = async () => {
  searching.value = true
  try {
    searchProbe.value = await apiService.probeAudiobookBaySearch()
  } catch (error) {
    logger.error('AudioBook Bay search test failed', String(error))
    toast.error('AudioBook Bay', 'The search test could not run')
  } finally {
    searching.value = false
  }
}

const loadDefinition = async () => {
  try {
    const definition = await apiService.getAudiobookBayDefinitionText(pages.value)
    definitionText.value = definition.yaml
    copied.value = false
  } catch (error) {
    logger.error('Failed to load the AudioBook Bay definition', String(error))
    toast.error('AudioBook Bay', 'Could not load the definition')
  }
}

const copyDefinition = async () => {
  try {
    await navigator.clipboard.writeText(definitionText.value)
    copied.value = true
  } catch {
    // Clipboard access is blocked in some browsers over plain HTTP; the text is on screen and
    // the download link still works, so this is not worth an error toast.
    copied.value = false
  }
}

const extractMessage = (error: unknown, fallback: string): string => {
  if (error && typeof error === 'object' && 'message' in error) {
    const message = (error as { message?: unknown }).message
    if (typeof message === 'string' && message.trim()) return message
  }
  return fallback
}

onMounted(load)

defineExpose({ load })
</script>

<style scoped>
.abb-patch-tab {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.section-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.section-intro {
  color: var(--text-secondary);
  margin: 0;
  max-width: 70ch;
}

.check-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.check {
  display: flex;
  gap: 0.65rem;
  padding: 0.65rem 0.75rem;
  border-radius: 6px;
  background: var(--bg-secondary);
  border-left: 3px solid var(--text-muted);
}

.check.pass {
  border-left-color: var(--success, #3fb950);
}

.check.fail {
  border-left-color: var(--danger, #f85149);
}

.check.warn {
  border-left-color: var(--warning, #d29922);
}

.check-icon {
  flex-shrink: 0;
  margin-top: 0.15rem;
  font-size: 1.1rem;
}

.check.pass .check-icon {
  color: var(--success, #3fb950);
}

.check.fail .check-icon {
  color: var(--danger, #f85149);
}

.check.warn .check-icon {
  color: var(--warning, #d29922);
}

.check-label {
  font-weight: 600;
}

.check-detail,
.check-remedy {
  color: var(--text-secondary);
  font-size: 0.9rem;
  margin-top: 0.15rem;
}

.check-remedy {
  color: var(--text-primary);
}

.panel {
  background: var(--bg-secondary);
  border-radius: 8px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}

.panel h4 {
  margin: 0;
}

.panel p {
  margin: 0;
  color: var(--text-secondary);
  max-width: 70ch;
}

.actions,
.control-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.control-row .grow {
  flex: 1 1 18rem;
}

.checkbox-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--text-secondary);
}

.hint {
  font-size: 0.9rem;
}

.probe-result {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  padding: 0.65rem 0.75rem;
  border-radius: 6px;
  background: var(--bg-tertiary, var(--bg-secondary));
  border-left: 3px solid var(--text-muted);
  font-size: 0.9rem;
}

.probe-result.ok {
  border-left-color: var(--success, #3fb950);
}

.probe-result.bad {
  border-left-color: var(--danger, #f85149);
}

.definition {
  max-height: 20rem;
  overflow: auto;
  background: var(--bg-primary);
  padding: 0.75rem;
  border-radius: 6px;
  font-size: 0.8rem;
  margin: 0;
}

code {
  background: var(--bg-primary);
  padding: 0.1rem 0.3rem;
  border-radius: 4px;
  font-size: 0.85em;
}
</style>
