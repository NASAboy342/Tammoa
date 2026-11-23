<script setup lang="ts">
/**
 * The selected card, in four tabs:
 *  Settings: its parameters (drawn from the backend's descriptors)
 *  Input:    what it received in a run (as usable fields with copy buttons, or raw JSON), the Run input, and, before any run, the
 *            fields the connected card will provide
 *  Output:   what it produced, i.e. what the next card can use
 *  Guide:    the card's tutorial with examples you can apply
 * The status and error of the last run stay visible above the tabs.
 */
import { computed, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import type { CardTypeInfo, GuideExample } from '@/services/cardApi'
import type { NodeRun } from '@/services/runApi'
import type { DraftNode } from '@/helpers/flowDraft'
import { defaultParams } from '@/helpers/flowDraft'
import { formatWhen } from '@/helpers/flowHelpers'
import { tpl } from '@/helpers/fields'
import { paramVisible, toolCallsOf } from '@/helpers/toolCalls'
import { formatDuration, nodeStatusIcon, roundsOf, statusLabel, type NodeRunPick } from '@/helpers/runHelpers'
import IconField from '@/components/forms/IconField.vue'
import ExpandableText from '@/components/ExpandableText.vue'
import ParamField from './ParamField.vue'
import DataView from './DataView.vue'
import FieldExplorer from './FieldExplorer.vue'
import GuidePanel from './GuidePanel.vue'
import LiveOutput from './LiveOutput.vue'
import RoundPicker from './RoundPicker.vue'
import ToolCallList from './ToolCallList.vue'
import DataCardsPanel from './DataCardsPanel.vue'

export type InspectorTab = 'settings' | 'input' | 'output' | 'guide'

const props = defineProps<{
  node: DraftNode
  info?: CardTypeInfo
  icon: IconName
  /** Status of this card in the selected run (for the status line). */
  run?: NodeRun
  /** The run data to show in Input / Output: the selected run's, or the newest run in which this card ran. */
  data?: NodeRunPick | null
  runInput?: unknown
  /** The cards connected to this card's input (to show the fields they will provide before anything has run). */
  incoming: { name: string; info?: CardTypeInfo }[]
  tab: InspectorTab
  now?: number
  flowId: string
  /** Is this card already in the saved flow? (A Data card needs that before its text can be stored.) */
  saved: boolean
  /** Changes when the flow was saved, so the list of Data cards refreshes. */
  savedKey?: unknown
}>()
const emit = defineEmits<{ rename: [name: string]; param: [key: string, value: unknown]; remove: []; 'update:tab': [tab: InspectorTab]; apply: [example: GuideExample] }>()

/** What the card will actually use: the descriptor defaults overlaid with what the user set. */
const values = computed(() => ({ ...(props.info ? defaultParams(props.info) : {}), ...props.node.params }))

const isTrigger = computed(() => !!props.info?.isTrigger)
// a card that ran several times (a loop) can be looked at round by round; null = follow the newest round
const chosenRound = ref<number | null>(null)
const rounds = computed(() => (props.data ? roundsOf(props.data.nodeRun) : []))
const shownRound = computed(() => (chosenRound.value === null ? rounds.value.at(-1) : rounds.value.find((r) => r.iteration === chosenRound.value) ?? rounds.value.at(-1)))
const realInput = computed(() => shownRound.value?.input)
const realOutput = computed(() => shownRound.value?.output)
const roundError = computed(() => (shownRound.value && !shownRound.value.current ? shownRound.value.error : undefined))
const dataNote = computed(() => (props.data ? `${props.data.fromActive ? 'From the selected run' : 'From an earlier run (this card did not run in the selected one)'}, ${formatWhen(props.data.run.startedAt)}` : ''))
const exampleOutput = (info?: CardTypeInfo) => info?.guide?.outputExample

const tabs: { id: InspectorTab; label: string; icon: IconName }[] = [
  { id: 'settings', label: 'Settings', icon: 'sliders' },
  { id: 'input', label: 'Input', icon: 'arrow-down' },
  { id: 'output', label: 'Output', icon: 'arrow-up' },
  { id: 'guide', label: 'Guide', icon: 'book' },
]
const hasData = (id: InspectorTab) => (id === 'input' ? realInput.value !== undefined : id === 'output' ? realOutput.value !== undefined : false)
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

    <!-- the state of the last run stays in view whichever tab is open -->
    <div v-if="run && run.status !== 'pending'" class="space-y-1.5 rounded-card border border-line p-2.5" data-testid="inspector-result">
      <div class="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted">
        <AppIcon :name="nodeStatusIcon[run.status]" :size="13" :class="run.status === 'running' ? 'animate-spin' : ''" />
        {{ statusLabel(run.status) }}
        <span class="ml-auto font-normal normal-case tracking-normal">{{ formatDuration(run.startedAt, run.finishedAt, now) }}</span>
      </div>
      <div v-if="run.error" class="flex items-start gap-1.5" data-testid="inspector-error">
        <AppIcon name="alert" :size="13" class="mt-0.5 shrink-0" />
        <ExpandableText :text="run.error" :lines="4" strong />
      </div>
      <!-- what a running card has produced so far (an AI model's thinking and answer), visible in every tab -->
      <LiveOutput v-if="run.status === 'running' && run.live" :live="run.live" :now="now" />
    </div>

    <div class="flex gap-1 border-b border-line" role="tablist" aria-label="Card details">
      <button
        v-for="t in tabs"
        :key="t.id"
        type="button"
        role="tab"
        :aria-selected="tab === t.id"
        class="relative -mb-px inline-flex min-h-8 items-center gap-1.5 border-b-2 px-2 text-[11px] font-semibold"
        :class="tab === t.id ? 'border-ink text-ink' : 'border-transparent text-ink-soft hover:text-ink'"
        :data-testid="`inspector-tab-${t.id}`"
        @click="emit('update:tab', t.id)"
      >
        <AppIcon :name="t.icon" :size="13" /> {{ t.label }}
        <span v-if="hasData(t.id)" class="h-1.5 w-1.5 rounded-full bg-ink" title="Real data from a run is available" data-testid="tab-has-data" />
      </button>
    </div>

    <!-- SETTINGS -->
    <div v-if="tab === 'settings'" class="space-y-3" role="tabpanel" data-testid="panel-settings">
      <IconField :model-value="node.name" label="Card name" icon="edit" :maxlength="100" testid="inspector-name" @update:model-value="emit('rename', $event)" />
      <ParamField
        v-for="p in (info?.params ?? []).filter((x) => paramVisible(x.key, values))"
        :key="p.key"
        :param="p"
        :model-value="values[p.key]"
        :values="values"
        :card="{ flowId, id: node.id, name: node.name, saved }"
        @update:model-value="emit('param', p.key, $event)"
        @open-guide="emit('update:tab', 'guide')"
      />
    </div>

    <!-- INPUT -->
    <div v-else-if="tab === 'input'" class="space-y-4" role="tabpanel" data-testid="panel-input">
      <section v-if="!isTrigger" data-testid="input-previous">
        <h3 class="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="arrow-down" :size="12" /> Input from the previous card</h3>
        <template v-if="realInput !== undefined">
          <RoundPicker v-model="chosenRound" :rounds="rounds" class="mb-1.5" />
          <p class="mb-1.5 text-[11px] text-muted" data-testid="input-source">{{ dataNote }}. Copy a reference and paste it into a setting.</p>
          <DataView :value="realInput" root="input" title="Input" testid="input-data" />
        </template>
        <template v-else-if="incoming.length">
          <p class="mb-1.5 flex items-start gap-1.5 text-[11px] text-muted" data-testid="input-expected-note">
            <AppIcon name="info" :size="12" class="mt-0.5 shrink-0" /> Not run yet. This is what the connected card usually provides (example data); run the flow to see the real input.
          </p>
          <div v-for="(c, i) in incoming" :key="`${c.name}-${i}`" class="mb-2" data-testid="input-expected">
            <p class="mb-1 text-[11px] font-semibold">From {{ c.name }}<span v-if="c.info" class="font-normal text-muted"> ({{ c.info.name }})</span></p>
            <FieldExplorer v-if="exampleOutput(c.info) !== undefined" :value="exampleOutput(c.info)" :root="incoming.length > 1 ? `input[${i}]` : 'input'" />
            <p v-else class="text-[11px] text-muted">That card does not describe its output yet. Run the flow to see it.</p>
          </div>
          <p v-if="incoming.length > 1" class="text-[11px] text-muted" data-testid="input-several">With several connections the input is a list, one entry per connection: <code class="font-mono">{{ tpl('input[0]') }}</code>, <code class="font-mono">{{ tpl('input[1]') }}</code>.</p>
        </template>
        <p v-else class="flex items-start gap-1.5 text-xs text-ink-soft" data-testid="input-unconnected">
          <AppIcon name="info" :size="13" class="mt-0.5 shrink-0" /> Nothing is connected to this card yet. Drag from the dot on the right of another card onto this one, then run the flow to see its input here.
        </p>
      </section>

      <DataCardsPanel :flow-id="flowId" :refresh-key="savedKey" />

      <section data-testid="input-run">
        <h3 class="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="zap" :size="12" /> Run input <span class="font-normal normal-case tracking-normal">(available in every card)</span></h3>
        <DataView v-if="runInput !== undefined" :value="runInput" root="run.input" title="Run input" testid="run-input-data" />
        <p v-else class="text-[11px] text-muted" data-testid="run-input-empty">Nothing was typed in the Run input box for the run shown. Type text or JSON there, then press Run: it appears here as <code class="font-mono">{{ tpl('run.input') }}</code>.</p>
      </section>

      <section class="rounded-card border border-line p-2.5" data-testid="input-how">
        <h3 class="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="bulb" :size="12" /> How this card reads its input</h3>
        <p class="text-xs leading-relaxed text-ink-soft">{{ info?.guide?.inputNote ?? `Use ${tpl('input.field')} in text settings.` }}</p>
        <button type="button" class="mt-2 inline-flex min-h-7 items-center gap-1.5 rounded-control border border-line-strong px-2 text-[11px] font-semibold hover:bg-surface-hover" data-testid="input-open-guide" @click="emit('update:tab', 'guide')">
          <AppIcon name="book" :size="12" /> Open the guide with examples
        </button>
      </section>
    </div>

    <!-- OUTPUT -->
    <div v-else-if="tab === 'output'" class="space-y-3" role="tabpanel" data-testid="inspector-output-panel">
      <RoundPicker v-model="chosenRound" :rounds="rounds" />
      <p v-if="roundError" class="flex items-start gap-1.5 text-xs font-semibold" data-testid="round-error"><AppIcon name="alert" :size="13" class="mt-0.5 shrink-0" /> This round failed: {{ roundError }}</p>
      <template v-if="realOutput !== undefined">
        <p class="text-[11px] text-muted" data-testid="output-source">{{ dataNote }}. The card after this one receives this as its input: copy a reference to use a field.</p>
        <ToolCallList v-if="toolCallsOf(realOutput).length" :calls="toolCallsOf(realOutput)" testid="output-tool-calls" />
        <DataView :value="realOutput" root="input" title="Output" testid="output-data" />
      </template>
      <template v-else-if="info?.guide?.outputExample !== undefined">
        <p class="flex items-start gap-1.5 text-[11px] text-muted" data-testid="output-example-note">
          <AppIcon name="info" :size="12" class="mt-0.5 shrink-0" /> This card has not run yet. This is the kind of output it produces (example data).
        </p>
        <FieldExplorer :value="info.guide.outputExample" root="input" />
      </template>
      <p v-else class="text-xs text-muted">Run the flow to see this card's output.</p>
    </div>

    <!-- GUIDE -->
    <div v-else role="tabpanel" data-testid="panel-guide">
      <GuidePanel :info="info" @apply="emit('apply', $event)" />
    </div>
  </div>
</template>
