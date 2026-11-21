<script setup lang="ts">
/** Settings of the selected card (drawn from the backend's param descriptors) plus what its last run produced. */
import { computed } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import type { CardTypeInfo } from '@/services/cardApi'
import type { NodeRun } from '@/services/runApi'
import type { DraftNode } from '@/helpers/flowDraft'
import { defaultParams } from '@/helpers/flowDraft'
import { formatDuration, nodeStatusIcon, statusLabel } from '@/helpers/runHelpers'
import IconField from '@/components/forms/IconField.vue'
import ExpandableText from '@/components/ExpandableText.vue'
import OutputViewer from '@/components/OutputViewer.vue'
import ParamField from './ParamField.vue'

const props = defineProps<{ node: DraftNode; info?: CardTypeInfo; icon: IconName; run?: NodeRun; now?: number }>()
const emit = defineEmits<{ rename: [name: string]; param: [key: string, value: unknown]; remove: [] }>()

/** What the card will actually use: the descriptor defaults overlaid with what the user set. */
const values = computed(() => ({ ...(props.info ? defaultParams(props.info) : {}), ...props.node.params }))
</script>

<template>
  <div class="space-y-3" data-testid="inspector">
    <div class="flex items-start gap-2">
      <span class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-control border border-line-strong"><AppIcon :name="icon" :size="16" /></span>
      <div class="min-w-0 flex-1">
        <p class="truncate text-sm font-semibold" :title="info?.name ?? node.type">{{ info?.name ?? node.type }}</p>
        <ExpandableText v-if="info?.description" :text="info.description" :lines="2" testid="inspector-description" />
      </div>
      <button type="button" class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover" title="Delete card" aria-label="Delete card" data-testid="inspector-delete" @click="emit('remove')">
        <AppIcon name="trash" :size="14" />
      </button>
    </div>

    <!-- the result comes first: after a run it is what you came to read -->
    <div v-if="run && run.status !== 'pending'" class="space-y-2 rounded-card border border-line p-3" data-testid="inspector-result">
      <div class="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted">
        <AppIcon :name="nodeStatusIcon[run.status]" :size="13" :class="run.status === 'running' ? 'animate-spin' : ''" />
        {{ statusLabel(run.status) }}
        <span class="ml-auto font-normal normal-case tracking-normal">{{ formatDuration(run.startedAt, run.finishedAt, now) }}</span>
      </div>
      <div v-if="run.error" class="flex items-start gap-1.5" data-testid="inspector-error">
        <AppIcon name="alert" :size="13" class="mt-0.5 shrink-0" />
        <ExpandableText :text="run.error" :lines="4" strong />
      </div>
      <OutputViewer v-if="run.output !== undefined" :value="run.output" />
    </div>

    <IconField :model-value="node.name" label="Card name" icon="edit" :maxlength="100" testid="inspector-name" @update:model-value="emit('rename', $event)" />

    <ParamField v-for="p in info?.params ?? []" :key="p.key" :param="p" :model-value="values[p.key]" :values="values" @update:model-value="emit('param', p.key, $event)" />

  </div>
</template>
