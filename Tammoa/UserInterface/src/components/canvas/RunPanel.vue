<script setup lang="ts">
/** Recent runs of this flow, newest first. Selecting one shows its card states on the canvas. Finished runs can be deleted or cleared. */
import { computed } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { FlowRun } from '@/services/runApi'
import { formatDuration, runStatusIcon, statusLabel } from '@/helpers/runHelpers'
import { formatWhen } from '@/helpers/flowHelpers'

const props = defineProps<{ runs: FlowRun[]; activeId: string | null; now?: number }>()
const emit = defineEmits<{ select: [id: string]; cancel: [id: string]; remove: [id: string]; clear: [] }>()

const finished = computed(() => props.runs.filter((r) => r.status !== 'running').length)
const tool = 'inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-control border hover:bg-surface-hover'
</script>

<template>
  <div data-testid="runs">
    <div class="mb-2 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted">
      <AppIcon name="activity" :size="13" /> Runs
      <span v-if="runs.length" class="font-normal normal-case tracking-normal" data-testid="runs-count">{{ runs.length }}</span>
      <span class="flex-1" />
      <button
        type="button"
        :disabled="finished === 0"
        class="inline-flex min-h-6 items-center gap-1 rounded-control border border-line-strong px-1.5 text-[11px] font-semibold normal-case tracking-normal text-ink hover:bg-surface-hover disabled:opacity-40"
        title="Delete all finished runs (running ones stay)"
        aria-label="Clear finished runs"
        data-testid="runs-clear"
        @click="emit('clear')"
      >
        <AppIcon name="eraser" :size="12" /> Clear
      </button>
    </div>
    <p v-if="!runs.length" class="flex items-center gap-1.5 text-xs text-muted" data-testid="runs-empty"><AppIcon name="clock" :size="13" /> No runs yet</p>
    <ul v-else class="space-y-1.5">
      <li v-for="run in runs.slice(0, 20)" :key="run.id" class="flex items-center gap-1.5" :data-testid="`run-row-${run.id}`" :data-status="run.status">
        <button
          type="button"
          class="flex min-w-0 flex-1 items-center gap-2 rounded-control border px-2 py-1.5 text-left text-xs hover:bg-surface-hover"
          :class="run.id === activeId ? 'border-2 border-ink' : 'border-line-strong'"
          :title="`${statusLabel(run.status)}${run.error ? ': ' + run.error : ''}`"
          :data-testid="`run-select-${run.id}`"
          @click="emit('select', run.id)"
        >
          <AppIcon :name="runStatusIcon[run.status]" :size="14" :class="run.status === 'running' ? 'animate-spin' : ''" />
          <span class="font-semibold">{{ statusLabel(run.status) }}</span>
          <span class="truncate text-muted">{{ formatWhen(run.startedAt) }}</span>
          <span class="ml-auto shrink-0 text-muted">{{ formatDuration(run.startedAt, run.finishedAt, now) }}</span>
        </button>
        <button v-if="run.status === 'running'" type="button" :class="[tool, 'border-2 border-ink']" title="Cancel run" aria-label="Cancel run" :data-testid="`run-cancel-${run.id}`" @click="emit('cancel', run.id)">
          <AppIcon name="stop" :size="13" />
        </button>
        <button v-else type="button" :class="[tool, 'border-line-strong']" title="Delete this run" aria-label="Delete this run" :data-testid="`run-delete-${run.id}`" @click="emit('remove', run.id)">
          <AppIcon name="trash" :size="13" />
        </button>
      </li>
    </ul>
    <p v-if="runs.length > 20" class="mt-1.5 text-[11px] text-muted">Showing the newest 20 of {{ runs.length }}.</p>
  </div>
</template>
