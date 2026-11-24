import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

const getVersion = vi.fn()
const getSettings = vi.fn()
const saveSettings = vi.fn()
const checkUpdate = vi.fn()
vi.mock('@/services/appApi', () => ({ appApi: { getVersion: (...a: unknown[]) => getVersion(...a) } }))
vi.mock('@/services/supportApi', () => ({
  supportApi: { getSettings: (...a: unknown[]) => getSettings(...a), saveSettings: (...a: unknown[]) => saveSettings(...a), checkUpdate: (...a: unknown[]) => checkUpdate(...a) },
}))

import { useAppStore } from '../appStore'

describe('appStore start', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    for (const m of [getVersion, getSettings, saveSettings, checkUpdate]) m.mockReset()
    getVersion.mockResolvedValue({ name: 'Tammoa', version: '0.1.0' })
  })

  it('does NOT ask GitHub unless the user switched the check on', async () => {
    getSettings.mockResolvedValue({ checkUpdatesAtStart: false })
    const app = useAppStore()
    await app.start()
    expect(app.info?.version).toBe('0.1.0')
    expect(checkUpdate).not.toHaveBeenCalled()
  })

  it('checks once at start when it is on, and keeps the answer', async () => {
    getSettings.mockResolvedValue({ checkUpdatesAtStart: true })
    checkUpdate.mockResolvedValue({ currentVersion: '0.1.0', latestVersion: '0.2.0', updateAvailable: true, url: 'https://github.com/x/y/releases' })
    const app = useAppStore()
    await app.start()
    expect(checkUpdate).toHaveBeenCalledTimes(1)
    expect(app.update?.updateAvailable).toBe(true)
  })

  it('a failed settings load does not stop the app, and does not trigger a check', async () => {
    getSettings.mockRejectedValue(new Error('settings are unreadable'))
    const app = useAppStore()
    await app.start()
    expect(app.info?.version).toBe('0.1.0')
    expect(app.settingsError).toBe('settings are unreadable')
    expect(checkUpdate).not.toHaveBeenCalled()
  })

  it('a failed update check becomes a message on the result, never an app error', async () => {
    getSettings.mockResolvedValue({ checkUpdatesAtStart: true })
    checkUpdate.mockRejectedValue(new Error('Request failed (500)'))
    const app = useAppStore()
    await app.start()
    expect(app.update).toMatchObject({ updateAvailable: false, error: 'Request failed (500)', currentVersion: '0.1.0' })
    expect(app.error).toBeNull()
    expect(app.checkingUpdate).toBe(false)
  })

  it('saves the switch and shows what the backend answered', async () => {
    saveSettings.mockResolvedValue({ checkUpdatesAtStart: true })
    const app = useAppStore()
    await app.setCheckUpdatesAtStart(true)
    expect(saveSettings).toHaveBeenCalledWith({ checkUpdatesAtStart: true })
    expect(app.settings?.checkUpdatesAtStart).toBe(true)
  })

  it('a failed save is reported and the old value stays', async () => {
    getSettings.mockResolvedValue({ checkUpdatesAtStart: false })
    saveSettings.mockRejectedValue(new Error('disk full'))
    const app = useAppStore()
    await app.loadSettings()
    await app.setCheckUpdatesAtStart(true)
    expect(app.settingsError).toBe('disk full')
    expect(app.settings?.checkUpdatesAtStart).toBe(false)
  })
})
