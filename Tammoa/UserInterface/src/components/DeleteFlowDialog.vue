<script setup lang="ts">
/** Strict delete confirmation: the user must type the exact flow name before the delete button unlocks. */
import { computed, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import IconField from '@/components/forms/IconField.vue'
import ModalShell from '@/components/ModalShell.vue'
import { canConfirmDelete } from '@/helpers/flowHelpers'

const props = defineProps<{ flowName: string; busy: boolean; error: string | null }>()
const emit = defineEmits<{ confirm: [typed: string]; cancel: [] }>()

const typed = ref('')
const unlocked = computed(() => canConfirmDelete(props.flowName, typed.value))

function confirm() {
  if (unlocked.value && !props.busy) emit('confirm', typed.value)
}
</script>

<template>
  <ModalShell testid="delete-dialog" labelledby="delete-title" @close="emit('cancel')">
    <form @submit.prevent="confirm">
      <h2 id="delete-title" class="mb-2 flex items-center gap-2 text-lg font-bold tracking-tight">
        <AppIcon name="alert" :size="18" /> Delete flow
      </h2>
      <p class="mb-4 text-sm text-ink-soft">
        This permanently deletes the flow and its cards. It cannot be undone. Type
        <code class="rounded-md bg-surface-muted px-1.5 py-0.5 font-mono text-xs text-ink" data-testid="delete-name">{{ flowName }}</code>
        to confirm.
      </p>
      <IconField v-model="typed" label="Flow name" icon="trash" testid="delete-input" :placeholder="flowName" />
      <p v-if="error" class="mt-3 flex items-center gap-1.5 text-xs font-semibold" data-testid="delete-error">
        <AppIcon name="alert" :size="14" /> {{ error }}
      </p>
      <div class="mt-5 flex justify-end gap-2">
        <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover" data-testid="delete-cancel" @click="emit('cancel')">
          <AppIcon name="x" :size="15" /> Keep
        </button>
        <button type="submit" :disabled="!unlocked || busy" class="inline-flex min-h-9 items-center gap-2 rounded-control border-2 border-ink px-3.5 text-xs font-semibold hover:bg-surface-hover disabled:opacity-40" data-testid="delete-confirm">
          <AppIcon :name="busy ? 'loader' : 'trash'" :size="15" :class="busy ? 'animate-spin' : ''" /> Delete forever
        </button>
      </div>
    </form>
  </ModalShell>
</template>
