<script setup lang="ts">
/** Settings of the selected card (drawn from the backend's param descriptors) plus what its last run produced. */
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import type { CardTypeInfo } from '@/services/cardApi'
import type { NodeRun } from '@/services/runApi'
import type { DraftNode } from '@/helpers/flowDraft'
import { formatDuration, nodeStatusIcon, prettyOutput, statusLabel } from '@/helpers/runHelpers'
import IconField from '@/components/forms/IconField.vue'
import ParamField from './ParamField.vue'

defineProps<{ node: DraftNode; info?: CardTypeInfo; icon: IconName; run?: NodeRun; now?: number }>()
const emit = defineEmits<{ rename: [name: string]; param: [key: string, value: unknown]; remove: [] }>()
</script>

<template>
  <div class="space-y-3" data-testid="inspector">
    <div class="flex items-center gap-2">
      <span class="inline-flex h-8 w-8 items-center justify-center rounded-control border border-line-strong"><AppIcon :name="icon" :size="16" /></span>
      <div class="min-w-0 flex-1">
        <p class="truncate text-sm font-semibold">{{ info?.name ?? node.type }}</p>
        <p class="truncate text-[11px] text-muted">{{ info?.description }}</p>
      </div>
      <button type="button" class="inline-flex h-8 w-8 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover" title="Delete card" aria-label="Delete card" data-testid="inspector-delete" @click="emit('remove')">
        <AppIcon name="trash" :size="14" />
      </button>
    </div>

    <IconField :model-value="node.name" label="Card name" icon="edit" :maxlength="100" testid="inspector-name" @update:model-value="emit('rename', $event)" />

    <ParamField v-for="p in info?.params ?? []" :key="p.key" :param="p" :model-value="node.params[p.key] ?? p.defaultValue" @update:model-value="emit('param', p.key, $event)" />

    <div v-if="run && run.status !== 'pending'" class="rounded-card border border-line p-3" data-testid="inspector-result">
      <div class="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted">
        <AppIcon :name="nodeStatusIcon[run.status]" :size="13" :class="run.status === 'running' ? 'animate-spin' : ''" />
        {{ statusLabel(run.status) }}
        <span class="ml-auto font-normal normal-case tracking-normal">{{ formatDuration(run.startedAt, run.finishedAt, now) }}</span>
      </div>
      <p v-if="run.error" class="flex items-start gap-1.5 text-xs font-semibold" data-testid="inspector-error"><AppIcon name="alert" :size="13" class="mt-0.5" /> {{ run.error }}</p>
      <pre v-if="run.output !== undefined" class="max-h-56 overflow-auto whitespace-pre-wrap break-words font-mono text-[11px] text-ink-soft" data-testid="inspector-output">{{ prettyOutput(run.output) }}</pre>
    </div>
  </div>
</template>
