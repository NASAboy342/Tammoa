import { ref } from 'vue'
import { defineStore } from 'pinia'
import { agentApi, type AgentAccess } from '@/services/agentApi'

export const useAgentStore = defineStore('agent', () => {
  const access = ref<AgentAccess | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function run(work: () => Promise<AgentAccess>) {
    error.value = null
    try {
      access.value = await work()
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  }

  async function load() {
    loading.value = true
    await run(() => agentApi.get())
    loading.value = false
  }
  const update = (value: { enabled?: boolean; allowRun?: boolean }) => run(() => agentApi.update(value))
  const newToken = () => run(() => agentApi.newToken())

  return { access, loading, error, load, update, newToken }
})
