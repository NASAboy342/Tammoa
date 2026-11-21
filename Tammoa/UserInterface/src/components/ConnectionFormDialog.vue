<script setup lang="ts">
/** Create or edit a connection (name, provider, address, optional API key). The saved key is never shown: leave the box empty to keep it. */
import { computed, ref, watch } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import IconField from '@/components/forms/IconField.vue'
import IconSelect from '@/components/forms/IconSelect.vue'
import ModalShell from '@/components/ModalShell.vue'
import type { ConnectionInfo, ConnectionProvider, ProviderInfo, SaveConnection } from '@/services/connectionApi'

const props = defineProps<{ providers: ProviderInfo[]; existing?: ConnectionInfo | null; busy: boolean; error: string | null }>()
const emit = defineEmits<{ submit: [value: SaveConnection]; cancel: [] }>()

const provider = ref<ConnectionProvider>(props.existing?.provider ?? props.providers[0]?.provider ?? 'ollama')
const info = computed(() => props.providers.find((p) => p.provider === provider.value))
const name = ref(props.existing?.name ?? '')
const baseUrl = ref(props.existing?.baseUrl ?? info.value?.defaultBaseUrl ?? '')
const apiKey = ref('')
const clearKey = ref(false)
const editing = computed(() => !!props.existing)
const valid = computed(() => name.value.trim().length > 0 && /^https?:\/\/\S+$/.test(baseUrl.value.trim()))

// a new connection follows the provider's default address until the user types their own
let touchedUrl = !!props.existing
watch(provider, () => {
  if (!touchedUrl) baseUrl.value = info.value?.defaultBaseUrl ?? ''
})

function submit() {
  if (!valid.value || props.busy) return
  emit('submit', {
    name: name.value.trim(),
    provider: provider.value,
    baseUrl: baseUrl.value.trim(),
    apiKey: apiKey.value.trim() || undefined,
    clearApiKey: clearKey.value || undefined,
  })
}
</script>

<template>
  <ModalShell testid="connection-dialog" labelledby="connection-title" @close="emit('cancel')">
    <form @submit.prevent="submit">
      <h2 id="connection-title" class="mb-4 flex items-center gap-2 text-lg font-bold tracking-tight">
        <AppIcon name="plug" :size="18" /> {{ editing ? 'Edit connection' : 'New connection' }}
      </h2>
      <div class="space-y-3">
        <IconField v-model="name" label="Name" icon="edit" :maxlength="100" placeholder="Home server" testid="conn-name" />
        <IconSelect v-model="provider" label="Provider" icon="bot" :options="providers.map((p) => ({ value: p.provider, label: p.name }))" testid="conn-provider" />
        <IconField :model-value="baseUrl" label="Address (URL)" icon="globe" placeholder="http://localhost:11434" testid="conn-url" @update:model-value="touchedUrl = true; baseUrl = $event" />
        <div>
          <IconField v-model="apiKey" type="password" label="API key (optional)" icon="key" :placeholder="existing?.hasKey ? 'A key is saved. Leave empty to keep it.' : 'No key'" :disabled="clearKey" testid="conn-key" />
          <p v-if="info?.keyHelp" class="mt-1 text-[11px] text-muted">{{ info.keyHelp }}</p>
          <p class="mt-1 flex items-start gap-1.5 text-[11px] text-muted"><AppIcon name="info" :size="12" class="mt-0.5 shrink-0" /> The key is stored in plain text on this computer (in a folder only you can open) and is never shown again or put in exported flows.</p>
          <button
            v-if="existing?.hasKey"
            type="button"
            role="switch"
            :aria-checked="clearKey"
            class="mt-2 inline-flex min-h-8 items-center gap-1.5 rounded-md border border-ink px-2 text-[11px] font-semibold"
            :class="clearKey ? 'bg-ink text-on-ink' : 'bg-transparent text-ink'"
            data-testid="conn-clear-key"
            @click="clearKey = !clearKey; apiKey = ''"
          >
            <AppIcon :name="clearKey ? 'check' : 'trash'" :size="13" /> Remove the saved key
          </button>
        </div>
      </div>
      <p v-if="error" class="mt-3 flex items-start gap-1.5 text-xs font-semibold" data-testid="conn-error"><AppIcon name="alert" :size="14" class="mt-0.5" /> {{ error }}</p>
      <div class="mt-5 flex justify-end gap-2">
        <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover" data-testid="conn-cancel" @click="emit('cancel')">
          <AppIcon name="x" :size="15" /> Cancel
        </button>
        <button type="submit" :disabled="!valid || busy" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90 disabled:opacity-40" data-testid="conn-save">
          <AppIcon :name="busy ? 'loader' : 'save'" :size="15" :class="busy ? 'animate-spin' : ''" /> {{ editing ? 'Save' : 'Create' }}
        </button>
      </div>
    </form>
  </ModalShell>
</template>
