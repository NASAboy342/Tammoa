import { ref } from 'vue'
import { defineStore } from 'pinia'

/** The backend tells us when the stored text of a Data card changed (a run wrote it, or another window saved it).
 *  Components that show stored text watch `version` and reload what they show. */
export const useDataStore = defineStore('data', () => {
  const version = ref(0)
  const last = ref<{ flowId: string; cardId: string } | null>(null)

  function changed(flowId: string, cardId: string) {
    last.value = { flowId, cardId }
    version.value++
  }

  return { version, last, changed }
})
