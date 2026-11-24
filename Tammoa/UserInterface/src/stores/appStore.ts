import { ref } from 'vue'
import { defineStore } from 'pinia'
import { appApi, type AppVersionInfo } from '@/services/appApi'
import { supportApi, type AppSettings, type UpdateInfo } from '@/services/supportApi'

const message = (e: unknown) => (e instanceof Error ? e.message : String(e))

export const useAppStore = defineStore('app', () => {
  const info = ref<AppVersionInfo | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  const settings = ref<AppSettings | null>(null)
  const settingsError = ref<string | null>(null)
  const update = ref<UpdateInfo | null>(null)
  const checkingUpdate = ref(false)

  async function load() {
    loading.value = true
    error.value = null
    try {
      info.value = await appApi.getVersion()
    } catch (e) {
      error.value = message(e)
    } finally {
      loading.value = false
    }
  }

  async function loadSettings() {
    settingsError.value = null
    try {
      settings.value = await supportApi.getSettings()
    } catch (e) {
      settingsError.value = message(e)
    }
  }

  async function setCheckUpdatesAtStart(value: boolean) {
    settingsError.value = null
    try {
      settings.value = await supportApi.saveSettings({ checkUpdatesAtStart: value })
    } catch (e) {
      settingsError.value = message(e)
    }
  }

  /** Asks GitHub. A problem (offline, no release yet) comes back as `update.error`, never as an app error. */
  async function checkUpdates() {
    checkingUpdate.value = true
    try {
      update.value = await supportApi.checkUpdate()
    } catch (e) {
      update.value = { currentVersion: info.value?.version ?? '', updateAvailable: false, error: message(e) }
    } finally {
      checkingUpdate.value = false
    }
  }

  /** At start: the version, the settings, and (only when the user switched it on) one update check. */
  async function start() {
    await Promise.all([load(), loadSettings()])
    if (settings.value?.checkUpdatesAtStart) await checkUpdates()
  }

  return { info, loading, error, settings, settingsError, update, checkingUpdate, load, loadSettings, setCheckUpdatesAtStart, checkUpdates, start }
})
