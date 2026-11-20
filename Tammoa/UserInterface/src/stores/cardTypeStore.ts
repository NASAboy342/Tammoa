import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { cardApi, type CardTypeInfo } from '@/services/cardApi'
import { ICONS, type IconName } from '@/components/icons/icons'

export const useCardTypeStore = defineStore('cardTypes', () => {
  const types = ref<CardTypeInfo[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const byType = computed(() => Object.fromEntries(types.value.map((t) => [t.type, t])) as Record<string, CardTypeInfo>)

  async function load() {
    if (types.value.length || loading.value) return
    loading.value = true
    error.value = null
    try {
      types.value = await cardApi.listTypes()
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      loading.value = false
    }
  }

  /** The backend names the icon; fall back to a generic one if the UI doesn't have it. */
  const iconFor = (type: string): IconName => {
    const name = byType.value[type]?.icon
    return name && name in ICONS ? (name as IconName) : 'zap'
  }

  return { types, byType, loading, error, load, iconFor }
})
