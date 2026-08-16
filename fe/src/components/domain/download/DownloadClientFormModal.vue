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
  <Modal
    :visible="visible"
    :title="editingClient ? 'Edit Download Client' : 'Add Download Client'"
    :showClose="false"
    size="lg"
    @close="closeModal"
  >
    <template #header>
      <ModalHeader
        :title="
          (editingClient ? 'Edit Download Client' : 'Add Download Client') +
          ' - ' +
          formData.type.toUpperCase()
        "
        :icon="PhDownload"
        @close="closeModal"
      />
    </template>

    <template #default>
      <ModalBody>
        <form @submit.prevent="handleSubmit">
          <!-- Activation -->
          <FormSection title="Activation" :icon="PhToggleRight">
            <div class="checkbox-group">
              <Checkbox v-model="formData.isEnabled">
                <strong>Enable</strong>
                <small>Enable this download client</small>
              </Checkbox>
            </div>
          </FormSection>

          <FormSection title="Basic" :icon="PhInfo">
            <div class="form-group">
              <label for="name">Name *</label>
              <input
                id="name"
                v-model="formData.name"
                type="text"
                required
                placeholder="e.g., SABnzbd, qBittorrent"
              />
            </div>

            <div class="form-group">
              <label for="type">Type *</label>
              <select id="type" v-model="formData.type" required @change="onTypeChange">
                <option value="qbittorrent">qBittorrent</option>
                <option value="transmission">Transmission</option>
                <option value="sabnzbd">SABnzbd</option>
                <option value="nzbget">NZBGet</option>
              </select>
            </div>

            <div class="form-group">
              <label for="host">Host *</label>
              <input
                id="host"
                v-model="formData.host"
                type="text"
                required
                :placeholder="getHostPlaceholder()"
              />
            </div>

            <div class="form-group">
              <label for="port">Port *</label>
              <input
                id="port"
                v-model.number="formData.port"
                type="number"
                required
                min="1"
                max="65535"
                :placeholder="getPortPlaceholder()"
              />
              <small>{{ getPortHelpText() }}</small>
            </div>

            <div class="form-group">
              <label for="downloadPath">Download Path</label>
              <input
                id="downloadPath"
                v-model="formData.downloadPath"
                type="text"
                placeholder="Leave blank to use client's default"
              />
              <small
                >Optional: Override the download client's default save path. Leave blank to use the
                client's configured download directory.</small
              >
            </div>

            <div class="checkbox-group">
              <Checkbox v-model="formData.useSSL">
                <strong>Use SSL</strong>
                <small>{{ `Use secure connection when connecting to ${formData.type}` }}</small>
              </Checkbox>
            </div>

            <div class="form-group" v-if="formData.type === 'transmission'">
              <label for="urlBase">URL Base</label>
              <input
                id="urlBase"
                v-model="formData.urlBase"
                type="text"
                placeholder="/transmission/rpc"
              />
              <small
                >RPC path for the Transmission endpoint. Default is <code>/transmission/rpc</code>.
                Some seedbox providers use a custom path (e.g. <code>/rpc</code>).</small
              >
            </div>
          </FormSection>

          <!-- Authentication -->
          <FormSection title="Authentication" :icon="PhLock" v-if="requiresAuth">
            <div class="form-group" v-if="requiresApiKey">
              <label for="apiKey">API Key *</label>
              <PasswordInput
                id="apiKey"
                v-model="formData.apiKey"
                autocomplete="off"
                placeholder="********"
                required
                class="admin-input"
              />
            </div>

            <div v-else>
              <div class="form-group">
                <label for="username">Username</label>
                <input
                  id="username"
                  v-model="formData.username"
                  type="text"
                  placeholder="admin"
                  :required="formData.type === 'nzbget'"
                />
                <small v-if="formData.type === 'nzbget'"
                  >Required when NZBGet authentication is enabled.</small
                >
              </div>

              <div class="form-group">
                <label for="password">Password</label>
                <PasswordInput
                  id="password"
                  v-model="formData.password"
                  :placeholder="
                    props.editingClient && !formData.password ? '(Saved password)' : '********'
                  "
                  :required="formData.type === 'nzbget'"
                  class="admin-input"
                />
                <small v-if="formData.type === 'nzbget'"
                  >Use the NZBGet RPC password (default: nzbget).</small
                >
              </div>
            </div>
          </FormSection>

          <!-- Category & Tags -->
          <FormSection :title="isUsenet ? 'Category' : 'Category & Tags'" :icon="PhTag">
            <div class="form-group">
              <label for="category">Category</label>
              <input
                id="category"
                v-model="formData.category"
                type="text"
                :placeholder="isUsenet ? 'e.g., audiobooks' : 'e.g., audiobooks'"
              />
              <small>{{ getCategoryHelp() }}</small>
            </div>

            <div class="form-group" v-if="!isUsenet">
              <label for="tags">Tags</label>
              <input
                id="tags"
                v-model="formData.tags"
                type="text"
                placeholder="Leave blank to use with all series"
              />
              <small
                >Only use this download client for series with at least one matching tag. Leave
                blank to use with all series.</small
              >
            </div>
          </FormSection>

          <!-- Priority -->
          <FormSection title="Priority" :icon="PhSortAscending">
            <div class="form-group">
              <label for="recentPriority">Recent Priority</label>
              <select id="recentPriority" v-model="formData.recentPriority">
                <option value="default">Default</option>
                <option value="last">Last</option>
                <option value="first">First</option>
              </select>
              <small
                >Priority to use when grabbing episodes that aired within the last 14 days</small
              >
            </div>

            <div class="form-group">
              <label for="olderPriority">Older Priority</label>
              <select id="olderPriority" v-model="formData.olderPriority">
                <option value="default">Default</option>
                <option value="last">Last</option>
                <option value="first">First</option>
              </select>
              <small>Priority to use when grabbing episodes that aired over 14 days ago</small>
            </div>
          </FormSection>

          <!-- Client Specific Settings -->
          <FormSection title="Completed Download Handling" :icon="PhCheckSquare">
            <div class="form-group">
              <label for="removeCompletedDownloads">Completed Download Action</label>
              <select id="removeCompletedDownloads" v-model="formData.removeCompletedDownloads">
                <option value="none">None - Keep in client</option>
                <option value="remove">Remove - Remove from client</option>
                <option value="remove_and_delete">
                  Remove and Delete - Remove from client and delete files
                </option>
              </select>
              <small
                >Action to take after a download is successfully imported. "Remove and Delete" will
                delete the downloaded files from the download client after import.</small
              >
            </div>

            <div class="checkbox-group" v-if="isUsenet">
              <Checkbox v-model="formData.removeCompleted">
                <strong>Remove Completed (Legacy)</strong>
                <small>Remove imported downloads from download client history</small>
              </Checkbox>
            </div>

            <div class="checkbox-group" v-if="isUsenet">
              <Checkbox v-model="formData.removeFailed">
                <strong>Remove Failed (Legacy)</strong>
                <small>Remove failed downloads from download client history</small>
              </Checkbox>
            </div>
          </FormSection>

          <FormSection
            title="Advanced Settings"
            :icon="PhWrench"
            v-if="formData.type === 'qbittorrent'"
          >
            <div class="form-group">
              <label for="initialState">Initial State</label>
              <select id="initialState" v-model="formData.initialState">
                <option value="default">Default</option>
                <option value="start">Start</option>
                <option value="forceStart">Force Start</option>
                <option value="pause">Pause</option>
              </select>
              <small
                >Initial state for torrents added to qBittorrent. Note that Forced Torrents do not
                abide by seed restrictions</small
              >
            </div>

            <div class="checkbox-group">
              <Checkbox v-model="formData.sequentialOrder">
                <strong>Sequential Order</strong>
                <small>Download in sequential order (qBittorrent 4.1.0+)</small>
              </Checkbox>
            </div>

            <div class="checkbox-group">
              <Checkbox v-model="formData.firstAndLastFirst">
                <strong>First and Last First</strong>
                <small>Download first and last pieces first (qBittorrent 4.1.0+)</small>
              </Checkbox>
            </div>

            <div class="form-group">
              <label for="contentLayout">Content Layout</label>
              <select id="contentLayout" v-model="formData.contentLayout">
                <option value="default">Default</option>
                <option value="original">Original</option>
                <option value="subfolder">Create Subfolder</option>
                <option value="nosubfolder">Don't Create Subfolder</option>
              </select>
              <small
                >Whether to use qBittorrent's configured content layout. Use qBittorrent's 4.3.2
                layout if the original layout from the torrent cannot be used (Default = Original
                layout)</small
              >
            </div>
          </FormSection>

          <!--
            Mappings belong to the client that owns them, so this is a read-out and not a picker.
            It used to be a multi-select whose help text promised "if none selected, no mapping
            will be applied" — the exact opposite of what happens. Every mapping created against
            this client applies to it, selected or not, so a correct mapping looked inert here and
            invited the reader to go hunting for a setting that was never the problem.
          -->
          <FormSection title="Remote Path Mappings" :icon="PhFolder" v-if="editingClient?.id">
            <div class="form-group">
              <label>Mappings applied to this client</label>
              <ul v-if="mappingsForClient.length" class="mapping-readout">
                <li v-for="m in mappingsForClient" :key="m.id">
                  <span class="mapping-name">{{ m.name }}</span>
                  <code>{{ m.remotePath }}</code>
                  <PhArrowRight :size="12" />
                  <code>{{ m.localPath }}</code>
                </li>
              </ul>
              <p v-else class="mapping-readout-empty">
                No remote path mappings apply to this client. Add one if this client reports paths
                that differ from the ones Bookmarkarr can open.
              </p>
              <small
                >Every mapping created for this client is applied automatically. Manage them under
                Settings → Download Clients → Remote Path Mappings.</small
              >
            </div>
          </FormSection>
        </form>
      </ModalBody>
    </template>

    <template #footer>
      <ModalFooter :showCancel="false">
        <template #left>
          <button type="button" class="btn btn-danger" @click="handleDelete" v-if="editingClient">
            <PhTrash /> Delete
          </button>
          <button type="button" class="cancel-button" @click="closeModal"><PhX /> Cancel</button>
        </template>
        <template #default>
          <button type="button" class="btn btn-info" @click="testConnection" :disabled="testing">
            <PhSpinner v-if="testing" class="ph-spin" />
            <PhGear v-else /> {{ testing ? 'Testing...' : 'Test' }}
          </button>
          <button type="button" class="btn btn-primary" @click="handleSubmit" :disabled="saving">
            <PhSpinner v-if="saving" class="ph-spin" />
            <PhFloppyDisk v-else /> {{ saving ? 'Saving...' : 'Save' }}
          </button>
        </template>
      </ModalFooter>
    </template>
  </Modal>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import PasswordInput from '@/components/form/PasswordInput.vue'
import { Modal, ModalHeader, ModalBody, ModalFooter } from '@/components/feedback'
import {
  PhX,
  PhTrash,
  PhSpinner,
  PhGear,
  PhFloppyDisk,
  PhDownload,
  PhInfo,
  PhLock,
  PhTag,
  PhSortAscending,
  PhCheckSquare,
  PhWrench,
  PhFolder,
  PhToggleRight,
  PhArrowRight,
} from '@phosphor-icons/vue'
import Checkbox from '@/components/form/Checkbox.vue'
import FormSection from '@/components/settings/FormSection.vue'
import type { DownloadClientConfiguration, DownloadClientSettings } from '@/types'
import { useToast } from '@/services/toastService'
import { useConfigurationStore } from '@/stores/configuration'
import { getRemotePathMappings, testDownloadClient } from '@/services/api'
import { logger } from '@/utils/logger'
import type { RemotePathMapping } from '@/types'

interface Props {
  visible: boolean
  editingClient: DownloadClientConfiguration | null
}

interface Emits {
  (e: 'close'): void
  (e: 'saved'): void
  (e: 'delete', id: string): void
}

const props = defineProps<Props>()
const emit = defineEmits<Emits>()
const configStore = useConfigurationStore()
const toast = useToast()

const saving = ref(false)
const testing = ref(false)

const defaultFormData = {
  name: '',
  type: 'qbittorrent' as 'qbittorrent' | 'transmission' | 'sabnzbd' | 'nzbget',
  host: '',
  port: 8080,
  username: '',
  password: '',
  apiKey: '',
  downloadPath: '',
  useSSL: false,
  isEnabled: true,
  category: '',
  tags: '',
  recentPriority: 'default',
  olderPriority: 'default',
  removeCompleted: false,
  removeFailed: false,
  removeCompletedDownloads: 'none',
  initialState: 'default',
  sequentialOrder: false,
  firstAndLastFirst: false,
  contentLayout: 'default',
  urlBase: '',
  settings: {},
}

const formData = ref({ ...defaultFormData })

const remotePathMappings = ref<RemotePathMapping[]>([])

/** Mirrors the server, which resolves mappings by the mapping's own downloadClientId. */
const mappingsForClient = computed(() => {
  const clientId = props.editingClient?.id
  if (!clientId) return []
  return remotePathMappings.value.filter((m) => String(m.downloadClientId) === String(clientId))
})

const loadRemotePathMappings = async () => {
  try {
    remotePathMappings.value = await getRemotePathMappings()
  } catch (e) {
    logger.debug('Failed to load remote path mappings', e)
    remotePathMappings.value = []
  }
}

const normalizeHost = (value: string): string => {
  const trimmed = (value || '').trim()
  if (!trimmed) return ''

  const withoutScheme = trimmed.replace(/^[a-z]+:\/\//i, '')
  const withoutTrailingSlashes = withoutScheme.replace(/\/+$/, '')
  const firstSlash = withoutTrailingSlashes.indexOf('/')

  return firstSlash >= 0 ? withoutTrailingSlashes.slice(0, firstSlash) : withoutTrailingSlashes
}

const isUsenet = computed(() => {
  return formData.value.type === 'sabnzbd' || formData.value.type === 'nzbget'
})

const requiresAuth = computed(() => {
  return true // All clients require some form of auth
})

const requiresApiKey = computed(() => {
  return formData.value.type === 'sabnzbd'
})

const getHostPlaceholder = () => {
  const placeholders: Record<string, string> = {
    qbittorrent: 'qbittorrent.tld.com',
    transmission: 'transmission.tld.com',
    sabnzbd: 'sabnzbd.tld.com',
    nzbget: 'nzbget.tld.com',
  }
  return placeholders[formData.value.type] || 'localhost'
}

const getPortPlaceholder = () => {
  const ports: Record<string, number> = {
    qbittorrent: 8080,
    transmission: 9091,
    sabnzbd: 8080,
    nzbget: 6789,
  }
  return ports[formData.value.type]?.toString() || '8080'
}

const getPortHelpText = () => {
  const hints: Record<string, string> = {
    transmission:
      'RPC port (default: 9091). This is not the web UI port if you changed it separately.',
    qbittorrent: 'Web UI port (default: 8080). Found in qBittorrent → Options → Web UI.',
    sabnzbd: 'Web interface port (default: 8080). Found in SABnzbd → Config → General.',
    nzbget: 'Web interface port (default: 6789). Found in NZBGet → Settings → Connection.',
  }
  return hints[formData.value.type] || 'Port the download client is listening on.'
}

const getCategoryHelp = () => {
  if (isUsenet.value) {
    return 'Adding a category specific to Bookmarkarr avoids conflicts with unrelated non-Bookmarkarr downloads. Using a category is optional, but strongly recommended.'
  }
  return 'Adding a category specific to Bookmarkarr avoids conflicts with unrelated downloads.'
}

const onTypeChange = () => {
  // Update default port when type changes
  const defaultPorts: Record<string, number> = {
    qbittorrent: 8080,
    transmission: 9091,
    sabnzbd: 8080,
    nzbget: 6789,
  }
  formData.value.port = defaultPorts[formData.value.type] || 8080

  if (formData.value.type === 'sabnzbd') {
    formData.value.username = ''
    formData.value.password = ''
  } else {
    formData.value.apiKey = ''
  }
}

// Watch for editing client changes
watch(
  () => props.editingClient,
  (newClient) => {
    if (newClient) {
      const settings = newClient.settings as DownloadClientSettings
      formData.value = {
        name: newClient.name,
        type: newClient.type,
        host: normalizeHost(newClient.host),
        port: newClient.port,
        username: newClient.username || '',
        password: newClient.password || '',
        apiKey: (settings?.apiKey as string) || '',
        downloadPath: newClient.downloadPath,
        useSSL: newClient.useSSL,
        isEnabled: newClient.isEnabled,
        category: (settings?.category as string) || '',
        tags: (settings?.tags as string) || '',
        recentPriority: (settings?.recentPriority as string) || 'default',
        olderPriority: (settings?.olderPriority as string) || 'default',
        removeCompleted: (settings?.removeCompleted as boolean) || false,
        removeFailed: (settings?.removeFailed as boolean) || false,
        removeCompletedDownloads:
          newClient.removeCompletedDownloads ||
          (settings?.removeCompletedDownloads as string) ||
          'none',
        initialState: (settings?.initialState as string) || 'default',
        sequentialOrder: (settings?.sequentialOrder as boolean) || false,
        firstAndLastFirst: (settings?.firstAndLastFirst as boolean) || false,
        contentLayout: (settings?.contentLayout as string) || 'default',
        urlBase: (settings?.urlBase as string) || '',
        settings: newClient.settings || {},
      }
      // Loaded so the section can read out which mappings already apply to this client.
      void loadRemotePathMappings()
    } else {
      formData.value = { ...defaultFormData }
    }
  },
  { immediate: true },
)

const closeModal = () => {
  formData.value = { ...defaultFormData }
  emit('close')
}

const testConnection = async () => {
  testing.value = true
  try {
    // Build config for testing with proper settings structure.
    // When editing an existing client, include id so the backend can reuse
    // saved credentials (for example password/apiKey) if the form input is left blank.
    const configToTest: Partial<DownloadClientConfiguration> = {
      ...(props.editingClient?.id ? { id: props.editingClient.id } : {}),
      name: formData.value.name,
      type: formData.value.type,
      host: normalizeHost(formData.value.host),
      port: formData.value.port,
      username: formData.value.username || '',
      password: formData.value.password || '',
      downloadPath: formData.value.downloadPath || '',
      useSSL: formData.value.useSSL,
      isEnabled: formData.value.isEnabled,
      removeCompletedDownloads: formData.value.removeCompletedDownloads,
      settings: {
        ...(formData.value.type === 'sabnzbd' && formData.value.apiKey
          ? { apiKey: formData.value.apiKey }
          : {}),
        ...(formData.value.type === 'transmission' && formData.value.urlBase
          ? { urlBase: formData.value.urlBase }
          : {}),
        ...(formData.value.category && { category: formData.value.category }),
        ...(formData.value.tags && { tags: formData.value.tags }),
        recentPriority: formData.value.recentPriority,
        olderPriority: formData.value.olderPriority,
        removeCompleted: formData.value.removeCompleted,
        removeFailed: formData.value.removeFailed,
        initialState: formData.value.initialState,
        sequentialOrder: formData.value.sequentialOrder,
        firstAndLastFirst: formData.value.firstAndLastFirst,
        contentLayout: formData.value.contentLayout,
      },
    }

    const result = await testDownloadClient(configToTest)
    if (result.success) {
      toast.success(
        'Connection successful',
        result.message || 'Download client connection test successful',
      )
    } else {
      toast.error('Connection failed', result.message || 'Failed to connect to download client')
    }
  } catch (error) {
    logger.error('Failed to test download client connection:', error)
    toast.error('Test failed', 'An error occurred while testing the download client connection')
  } finally {
    testing.value = false
  }
}

const handleSubmit = async () => {
  saving.value = true
  try {
    // Generate a simple UUID fallback
    const generateId = () => {
      return `${Date.now()}-${Math.random().toString(36).substr(2, 9)}`
    }

    const clientConfig: DownloadClientConfiguration = {
      id: props.editingClient?.id || generateId(),
      name: formData.value.name,
      type: formData.value.type,
      host: normalizeHost(formData.value.host),
      port: formData.value.port,
      username: formData.value.username || '',
      password: formData.value.password || '',
      downloadPath: formData.value.downloadPath || '',
      useSSL: formData.value.useSSL,
      isEnabled: formData.value.isEnabled,
      removeCompletedDownloads: formData.value.removeCompletedDownloads,
      settings: {
        ...(formData.value.type === 'sabnzbd' && formData.value.apiKey
          ? { apiKey: formData.value.apiKey }
          : {}),
        ...(formData.value.type === 'transmission' && formData.value.urlBase
          ? { urlBase: formData.value.urlBase }
          : {}),
        ...(formData.value.category && { category: formData.value.category }),
        ...(formData.value.tags && { tags: formData.value.tags }),
        recentPriority: formData.value.recentPriority,
        olderPriority: formData.value.olderPriority,
        removeCompleted: formData.value.removeCompleted,
        removeFailed: formData.value.removeFailed,
        initialState: formData.value.initialState,
        sequentialOrder: formData.value.sequentialOrder,
        firstAndLastFirst: formData.value.firstAndLastFirst,
        contentLayout: formData.value.contentLayout,
      },
    }

    await configStore.saveDownloadClientConfiguration(clientConfig)
    toast.success(
      'Saved',
      `Download client ${props.editingClient ? 'updated' : 'created'} successfully`,
    )

    emit('saved')
    closeModal()
  } catch (error) {
    logger.error('Failed to save download client:', error)
    toast.error(
      'Save failed',
      `Failed to save download client: ${error instanceof Error ? error.message : 'Unknown error'}`,
    )
  } finally {
    saving.value = false
  }
}

const handleDelete = () => {
  if (props.editingClient) {
    emit('delete', props.editingClient.id)
    closeModal()
  }
}
</script>

<style scoped>
/* Modal-specific styling moved to shared `modals.css` */
.modal-body {
  padding: 2rem;
  overflow-y: auto;
  flex: 1;
}

.form-section {
  margin-bottom: 2rem;
}

.form-section:last-child {
  margin-bottom: 0;
}

.form-section h3 {
  color: var(--text-primary);
  font-size: 1.1rem;
  margin: 0 0 1rem 0;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid var(--border-color);
}

.form-group {
  margin-bottom: 1.5rem;
}

.form-group:last-child {
  margin-bottom: 0;
}

.form-group label {
  display: block;
  color: var(--text-primary);
  font-weight: 500;
  font-size: 0.95rem;
}

.form-group input,
.form-group select {
  width: 100%;
  padding: 0.75rem;
  background-color: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  color: var(--text-primary);
  font-size: 0.95rem;
  transition: all 0.2s;
}

.form-group input:focus,
.form-group select:focus {
  outline: none;
  border-color: var(--brand-focus);
  box-shadow: 0 0 0 3px rgba(var(--brand-rgb), 0.1);
}

.form-group small {
  display: block;
  margin-top: 0.5rem;
  color: #999;
  font-size: 0.85rem;
}

.mapping-readout {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.375rem;
}

.mapping-readout li {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.375rem;
  padding: 0.5rem 0.625rem;
  border: 1px solid var(--border-color);
  border-radius: var(--btn-radius, 8px);
  background: var(--muted-bg);
  font-size: 0.85rem;
}

.mapping-readout .mapping-name {
  font-weight: 600;
  margin-right: 0.25rem;
}

.mapping-readout code {
  /* Paths are the point of this readout, so let them wrap rather than clip. */
  overflow-wrap: anywhere;
  font-size: 0.8rem;
}

.mapping-readout-empty {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.85rem;
}

/* Download client modal overrides */
.checkbox-group label:hover {
  border-color: var(--brand-500);
  background-color: var(--bg-secondary);
}
.checkbox-group label span {
  flex: 1;
}

/* modal-footer styles are centralized in src/assets/modals.css; this modal prefers space-between layout */
.modal-footer {
  justify-content: space-between;
}

/* Buttons are centralized in `src/assets/buttons.css`. Use `.btn`, `.btn-danger` and layout helpers here when necessary. */

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-danger:hover:not(:disabled) {
  background-color: #d32f2f;
  transform: translateY(-1px);
}

/* Button color variants centralized in `src/assets/modals.css` - use `.btn` / `.btn-primary` */

.ph-spin {
  animation: spin 1s linear infinite;
}

/* @keyframes spin is centralized in src/assets/animations.css */

@media (max-width: 768px) {
  .modal-overlay {
    padding: 1rem;
  }

  .modal-footer {
    flex-wrap: wrap;
  }

  .btn {
    flex: 1;
    justify-content: center;
    min-width: 120px;
  }

  .btn-danger {
    flex-basis: 100%;
  }
}
</style>
