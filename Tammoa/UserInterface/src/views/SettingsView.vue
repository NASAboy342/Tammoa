<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import { formatBytes, readBackupText, restoreLines, updateLine } from '@/helpers/support'
import { formatWhen } from '@/helpers/flowHelpers'
import { supportApi, type BackupList, type RestoreResult } from '@/services/supportApi'
import { useAppStore } from '@/stores/appStore'
import { useFlowStore } from '@/stores/flowStore'
import { useConnectionStore } from '@/stores/connectionStore'

const app = useAppStore()
const flows = useFlowStore()
const connections = useConnectionStore()

const backups = ref<BackupList | null>(null)
const backupBusy = ref(false)
const backupError = ref<string | null>(null)
const made = ref<string | null>(null)
const restoreResult = ref<RestoreResult | null>(null)
const restoreError = ref<string | null>(null)
const restoreBusy = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const copied = ref(false)

const line = computed(() => updateLine(app.update))

onMounted(async () => {
  if (!app.settings) void app.loadSettings()
  await loadBackups()
})

async function loadBackups() {
  try {
    backups.value = await supportApi.listBackups()
  } catch (e) {
    backupError.value = e instanceof Error ? e.message : String(e)
  }
}

async function backUp() {
  backupBusy.value = true
  backupError.value = null
  made.value = null
  try {
    const file = await supportApi.createBackup()
    made.value = `Saved ${file.fileName} (${file.flows} flow${file.flows === 1 ? '' : 's'}, ${file.connections} connection${file.connections === 1 ? '' : 's'}, ${formatBytes(file.size)}).`
    await loadBackups()
  } catch (e) {
    backupError.value = e instanceof Error ? e.message : String(e)
  } finally {
    backupBusy.value = false
  }
}

async function afterRestore(result: RestoreResult) {
  restoreResult.value = result
  await Promise.all([flows.load(), connections.load(), loadBackups()]) // the lists elsewhere show what came back
}

async function restoreNamed(fileName: string) {
  restoreBusy.value = true
  restoreError.value = null
  restoreResult.value = null
  try {
    await afterRestore(await supportApi.restoreFile(fileName))
  } catch (e) {
    restoreError.value = e instanceof Error ? e.message : String(e)
  } finally {
    restoreBusy.value = false
  }
}

async function onFileChosen(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = '' // choosing the same file again must work
  if (!file) return
  restoreError.value = null
  restoreResult.value = null
  const read = readBackupText(await file.text())
  if (!read.ok) {
    restoreError.value = read.error
    return
  }
  restoreBusy.value = true
  try {
    await afterRestore(await supportApi.restoreDocument(read.document))
  } catch (e) {
    restoreError.value = e instanceof Error ? e.message : String(e)
  } finally {
    restoreBusy.value = false
  }
}

async function openFolder() {
  backupError.value = null
  try {
    await supportApi.openBackupFolder()
  } catch (e) {
    backupError.value = e instanceof Error ? e.message : String(e)
  }
}

async function copyFolder() {
  if (!backups.value) return
  try {
    await navigator.clipboard.writeText(backups.value.folder)
    copied.value = true
    setTimeout(() => (copied.value = false), 1500)
  } catch {
    /* clipboard blocked: the path is on screen to select */
  }
}

const iconBtn = 'inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover disabled:opacity-40'
const textBtn = 'inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover disabled:opacity-40'
</script>

<template>
  <section class="mx-auto max-w-3xl px-4 py-8" data-testid="settings">
    <h1 class="mb-1 flex items-center gap-2 text-2xl font-bold tracking-tight"><AppIcon name="sliders" :size="22" /> Settings</h1>
    <p class="mb-5 text-xs text-muted">Updates and backups.</p>

    <!-- updates -->
    <div class="mb-4 rounded-card border border-line p-3.5" data-testid="settings-updates">
      <p class="mb-2 flex items-center gap-2 text-sm font-semibold"><AppIcon name="download" :size="16" /> Updates</p>
      <p class="mb-3 text-xs text-ink-soft">You have version <span class="font-mono" data-testid="settings-version">{{ app.info?.version ?? '…' }}</span>.</p>
      <div class="flex flex-wrap items-center gap-2">
        <button type="button" :class="textBtn" :disabled="app.checkingUpdate" data-testid="check-updates" @click="app.checkUpdates()">
          <AppIcon :name="app.checkingUpdate ? 'loader' : 'refresh'" :size="15" :class="app.checkingUpdate ? 'animate-spin' : ''" /> Check for updates
        </button>
      </div>
      <p v-if="line" class="mt-3 flex items-start gap-1.5 text-xs font-semibold" data-testid="update-result">
        <AppIcon :name="line.icon" :size="14" class="mt-0.5 shrink-0" /> <span>{{ line.text }}</span>
      </p>
      <p v-if="app.update?.updateAvailable && app.update.url" class="mt-1 flex min-w-0 items-center gap-2 text-[11px] text-muted" data-testid="update-url-row">
        <AppIcon name="globe" :size="12" /> <span class="select-all truncate font-mono" data-testid="update-url">{{ app.update.url }}</span>
      </p>
      <button
        type="button"
        role="switch"
        :aria-checked="app.settings?.checkUpdatesAtStart ?? false"
        :disabled="!app.settings"
        class="mt-3 flex w-full items-center gap-3 rounded-control p-1 text-left hover:bg-surface-hover disabled:opacity-40"
        data-testid="auto-update"
        @click="app.setCheckUpdatesAtStart(!app.settings?.checkUpdatesAtStart)"
      >
        <AppIcon :name="app.settings?.checkUpdatesAtStart ? 'check-circle' : 'circle'" :size="18" />
        <span class="min-w-0 flex-1">
          <span class="block text-sm font-semibold">Check when the app opens: {{ app.settings?.checkUpdatesAtStart ? 'on' : 'off' }}</span>
          <span class="block text-[11px] text-muted">On: once at start the app asks GitHub for the latest release (nothing about you is sent). Off: only when you press the button.</span>
        </span>
      </button>
      <p v-if="app.settingsError" class="mt-2 flex items-center gap-1.5 text-xs font-semibold" data-testid="settings-error"><AppIcon name="alert" :size="14" /> {{ app.settingsError }}</p>
    </div>

    <!-- backup -->
    <div class="mb-4 rounded-card border border-line p-3.5" data-testid="settings-backup">
      <p class="mb-1 flex items-center gap-2 text-sm font-semibold"><AppIcon name="save" :size="16" /> Backup</p>
      <p class="mb-3 text-[11px] text-muted">
        One file with your flows (and their Data card texts), your connections and shared texts. <strong>Not in it:</strong> API keys (type them again after a restore), run history and the agent token.
        Restoring never overwrites: what is already here is left as it is.
      </p>
      <div class="flex flex-wrap gap-2">
        <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90 disabled:opacity-40" :disabled="backupBusy" data-testid="backup-now" @click="backUp">
          <AppIcon :name="backupBusy ? 'loader' : 'save'" :size="15" :class="backupBusy ? 'animate-spin' : ''" /> Back up now
        </button>
        <button type="button" :class="textBtn" :disabled="restoreBusy" data-testid="backup-choose-file" @click="fileInput?.click()">
          <AppIcon name="file-plus" :size="15" /> Restore from a file…
        </button>
        <input ref="fileInput" type="file" accept=".json,application/json" class="sr-only" tabindex="-1" aria-hidden="true" data-testid="backup-file-input" @change="onFileChosen" />
      </div>

      <p v-if="made" class="mt-3 flex items-start gap-1.5 text-xs font-semibold" data-testid="backup-made"><AppIcon name="check-circle" :size="14" class="mt-0.5 shrink-0" /> {{ made }}</p>
      <p v-if="backupError" class="mt-3 flex items-start gap-1.5 text-xs font-semibold" data-testid="backup-error"><AppIcon name="alert" :size="14" class="mt-0.5 shrink-0" /> {{ backupError }}</p>

      <div v-if="backups" class="mt-3 flex min-w-0 items-center gap-2" data-testid="backup-folder-row">
        <AppIcon name="folder" :size="15" class="shrink-0 text-muted" />
        <p class="min-w-0 flex-1 select-all truncate font-mono text-[11px]" :title="backups.folder" data-testid="backup-folder">{{ backups.folder }}</p>
        <button type="button" :class="iconBtn" title="Copy the folder path" aria-label="Copy the folder path" data-testid="backup-copy-folder" @click="copyFolder"><AppIcon :name="copied ? 'check' : 'copy'" :size="14" /></button>
        <button type="button" :class="iconBtn" title="Show the folder" aria-label="Show the folder" data-testid="backup-open-folder" @click="openFolder"><AppIcon name="folder" :size="14" /></button>
      </div>

      <ul v-if="backups?.files.length" class="mt-2 space-y-1.5" data-testid="backup-list">
        <li v-for="f in backups.files" :key="f.fileName" class="flex flex-wrap items-center gap-2 rounded-control border border-line p-2" :data-testid="`backup-row-${f.fileName}`">
          <AppIcon name="file-text" :size="15" class="shrink-0 text-muted" />
          <div class="min-w-0 flex-1 basis-40">
            <p class="truncate font-mono text-[11px]" :title="f.fileName">{{ f.fileName }}</p>
            <p class="text-[11px] text-muted">{{ formatWhen(f.createdAt) }} · {{ f.flows }} flow{{ f.flows === 1 ? '' : 's' }} · {{ f.connections }} connection{{ f.connections === 1 ? '' : 's' }} · {{ formatBytes(f.size) }}</p>
          </div>
          <button type="button" class="inline-flex min-h-7 items-center gap-1.5 rounded-control border border-line-strong px-2.5 text-[11px] font-semibold hover:bg-surface-hover disabled:opacity-40" :disabled="restoreBusy" :data-testid="`backup-restore-${f.fileName}`" @click="restoreNamed(f.fileName)">
            <AppIcon name="refresh" :size="13" /> Restore
          </button>
        </li>
      </ul>
      <p v-else-if="backups" class="mt-2 flex items-center gap-1.5 text-[11px] text-muted" data-testid="backup-none"><AppIcon name="info" :size="13" /> No backups yet.</p>

      <p v-if="restoreError" class="mt-3 flex items-start gap-1.5 text-xs font-semibold" data-testid="restore-error"><AppIcon name="alert" :size="14" class="mt-0.5 shrink-0" /> {{ restoreError }}</p>
      <div v-if="restoreResult" class="mt-3 rounded-control border border-line-strong p-2.5 text-xs" data-testid="restore-result">
        <p v-for="l in restoreLines(restoreResult)" :key="l" class="flex items-start gap-1.5"><AppIcon name="check-circle" :size="13" class="mt-0.5 shrink-0" /> {{ l }}</p>
        <p v-for="p in restoreResult.problems" :key="p" class="mt-1 flex items-start gap-1.5 font-semibold" data-testid="restore-problem"><AppIcon name="alert" :size="13" class="mt-0.5 shrink-0" /> {{ p }}</p>
      </div>
    </div>
  </section>
</template>
