import { ref } from 'vue'
import { defineStore } from 'pinia'
import { flowApi, type Flow, type FlowSummary } from '@/services/flowApi'

const message = (e: unknown) => (e instanceof Error ? e.message : String(e))

export const useFlowStore = defineStore('flows', () => {
  const flows = ref<FlowSummary[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const busyId = ref<string | null>(null)

  async function load() {
    loading.value = true
    error.value = null
    try {
      flows.value = await flowApi.list()
    } catch (e) {
      error.value = message(e)
    } finally {
      loading.value = false
    }
  }

  /** Runs an action on one flow, tracks which row is busy, refreshes the list, rethrows so dialogs can show the error. */
  async function act<T>(id: string | null, run: () => Promise<T>): Promise<T> {
    busyId.value = id
    error.value = null
    try {
      const result = await run()
      flows.value = await flowApi.list()
      return result
    } catch (e) {
      error.value = message(e)
      throw e
    } finally {
      busyId.value = null
    }
  }

  const create = (name: string, description: string) => act<Flow>(null, () => flowApi.create(name, description))
  const duplicate = (id: string) => act<Flow>(id, () => flowApi.duplicate(id))
  const setEnabled = (id: string, enabled: boolean) => act<Flow>(id, () => (enabled ? flowApi.enable(id) : flowApi.disable(id)))
  const remove = (id: string, confirmName: string) => act<void>(id, () => flowApi.remove(id, confirmName))

  return { flows, loading, error, busyId, load, create, duplicate, setEnabled, remove }
})
