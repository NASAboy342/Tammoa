<script setup lang="ts">
/** A plain confirmation (for things less drastic than deleting a flow, which asks for its name instead). */
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import ModalShell from '@/components/ModalShell.vue'

withDefaults(
  defineProps<{ title: string; message: string; confirmLabel: string; confirmIcon?: IconName; busy?: boolean; error?: string | null; danger?: boolean }>(),
  { confirmIcon: 'check', busy: false, error: null, danger: false },
)
const emit = defineEmits<{ confirm: []; cancel: [] }>()
</script>

<template>
  <ModalShell testid="confirm-dialog" labelledby="confirm-title" @close="emit('cancel')">
    <h2 id="confirm-title" class="mb-2 flex items-center gap-2 text-lg font-bold tracking-tight"><AppIcon name="alert" :size="18" /> {{ title }}</h2>
    <p class="mb-4 text-sm text-ink-soft" data-testid="confirm-message">{{ message }}</p>
    <p v-if="error" class="mb-3 flex items-start gap-1.5 text-xs font-semibold" data-testid="confirm-error"><AppIcon name="alert" :size="14" class="mt-0.5" /> {{ error }}</p>
    <div class="flex justify-end gap-2">
      <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover" data-testid="confirm-cancel" @click="emit('cancel')">
        <AppIcon name="x" :size="15" /> Cancel
      </button>
      <button
        type="button"
        :disabled="busy"
        class="inline-flex min-h-9 items-center gap-2 rounded-control px-3.5 text-xs font-semibold disabled:opacity-40"
        :class="danger ? 'border-2 border-ink hover:bg-surface-hover' : 'border border-ink bg-ink text-on-ink hover:opacity-90'"
        data-testid="confirm-ok"
        @click="emit('confirm')"
      >
        <AppIcon :name="busy ? 'loader' : confirmIcon" :size="15" :class="busy ? 'animate-spin' : ''" /> {{ confirmLabel }}
      </button>
    </div>
  </ModalShell>
</template>
