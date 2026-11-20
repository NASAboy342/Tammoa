<script setup lang="ts">
/** Recent runs of this flow, newest first. Selecting one shows its card states on the canvas. */
import AppIcon from '@/components/icons/AppIcon.vue'
import type { FlowRun } from '@/services/runApi'
import { formatDuration, runStatusIcon, statusLabel } from '@/helpers/runHelpers'
import { formatWhen } from '@/helpers/flowHelpers'

defineProps<{ runs: FlowRun[]; activeId: string | null; now?: number }>()
const emit = defineEmits<{ select: [id: string]; cancel: [id: string] }>()
</script>

<template>
  <div data-testid="runs">
    <div class="mb-2 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="activity" :size="13" /> Runs</div>
    <p v-if="!runs.length" class="flex items-center gap-1.5 text-xs text-muted" data-testid="runs-empty"><AppIcon name="clock" :size="13" /> No runs yet</p>
    <ul v-else class="space-y-1.5">
      <li v-for="run in runs.slice(0, 20)" :key="run.id" class="flex items-center gap-1.5" :data-testid="`run-row-${run.id}`" :data-status="run.status">
        <button
          type="button"
          class="flex min-w-0 flex-1 items-center gap-2 rounded-control border px-2 py-1.5 text-left text-xs hover:bg-surface-hover"
          :class="run.id === activeId ? 'border-2 border-ink' : 'border-line-strong'"
          :data-testid="`run-select-${run.id}`"
          @click="emit('select', run.id)"
        >
          <AppIcon :name="runStatusIcon[run.status]" :size="14" :class="run.status === 'running' ? 'animate-spin' : ''" />
          <span class="font-semibold">{{ statusLabel(run.status) }}</span>
          <span class="truncate text-muted">{{ formatWhen(run.startedAt) }}</span>
          <span class="ml-auto shrink-0 text-muted">{{ formatDuration(run.startedAt, run.finishedAt, now) }}</span>
        </button>
        <button
          v-if="run.status === 'running'"
          type="button"
          class="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-control border-2 border-ink hover:bg-surface-hover"
          title="Cancel run"
          aria-label="Cancel run"
          :data-testid="`run-cancel-${run.id}`"
          @click="emit('cancel', run.id)"
        >
          <AppIcon name="stop" :size="13" />
        </button>
      </li>
    </ul>
  </div>
</template>
