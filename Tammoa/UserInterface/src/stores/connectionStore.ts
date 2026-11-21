import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { connectionApi, type ConnectionInfo, type ConnectionTestResult, type ProviderInfo, type SaveConnection } from '@/services/connectionApi'

const message = (e: unknown) => (e instanceof Error ? e.message : String(e))

export const useConnectionStore = defineStore('connections', () => {
  const connections = ref<ConnectionInfo[]>([])
  const providers = ref<ProviderInfo[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  /** Last test result per connection id (kept only in memory). */
  const tests = ref<Record<string, ConnectionTestResult & { testing?: boolean }>>({})

  const byProvider = computed(() => (provider: string) => connections.value.filter((c) => c.provider === provider))

  async function load() {
    loading.value = true
    error.value = null
    try {
      const [list, provs] = await Promise.all([connectionApi.list(), providers.value.length ? Promise.resolve(providers.value) : connectionApi.providers()])
      connections.value = list
      providers.value = provs
    } catch (e) {
      error.value = message(e)
    } finally {
      loading.value = false
    }
  }

  async function create(c: SaveConnection) {
    const created = await connectionApi.create(c)
    await load()
    return created
  }

  async function update(id: string, c: SaveConnection) {
    const updated = await connectionApi.update(id, c)
    delete tests.value[id] // the address or key may have changed
    await load()
    return updated
  }

  async function remove(id: string) {
    await connectionApi.remove(id)
    delete tests.value[id]
    await load()
  }

  async function test(id: string) {
    tests.value = { ...tests.value, [id]: { ok: false, models: [], testing: true } }
    try {
      tests.value = { ...tests.value, [id]: await connectionApi.test(id) }
    } catch (e) {
      tests.value = { ...tests.value, [id]: { ok: false, models: [], error: message(e) } }
    }
  }

  return { connections, providers, loading, error, tests, byProvider, load, create, update, remove, test }
})
