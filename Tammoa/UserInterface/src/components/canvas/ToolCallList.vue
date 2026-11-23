<script setup lang="ts">
/**
 * The tool calls an AI card made (while it runs, and in its final output): one compact row per call with the tool's icon, what it was
 * asked to do, whether it ran / was refused / failed, and how long it took. A row opens to show the full arguments and the result or reason.
 */
import { ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import ExpandableText from '@/components/ExpandableText.vue'
import OutputViewer from '@/components/OutputViewer.vue'
import { DECISION_ICON, callDetail, decisionLabel, inFlight, formatToolDuration, summarizeArgs, summarizeCalls, toolIcon, type ToolCall } from '@/helpers/toolCalls'

defineProps<{ calls: ToolCall[]; testid?: string }>()
const open = ref<Set<number>>(new Set())
const toggle = (i: number) => {
  const next = new Set(open.value)
  if (next.has(i)) next.delete(i)
  else next.add(i)
  open.value = next
}
</script>

<template>
  <section class="space-y-1" :data-testid="testid ?? 'tool-calls'">
    <h3 class="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted" data-testid="tool-calls-summary">
      <AppIcon name="zap" :size="12" /> Tools <span class="font-normal normal-case tracking-normal">{{ summarizeCalls(calls) }}</span>
    </h3>
    <ol class="space-y-1">
      <li v-for="(c, i) in calls" :key="i" class="rounded-control border" :class="inFlight(c) ? 'border-dashed border-line-strong' : c.decision === 'ran' ? 'border-line-strong' : c.decision === 'rejected' ? 'border-dashed border-ink border-2' : 'border-ink border-2'" :data-testid="`tool-call-${i}`" :data-decision="c.decision" :data-tool="c.name">
        <button type="button" class="flex min-h-8 w-full items-center gap-2 px-2 text-left text-[11px]" :aria-expanded="open.has(i)" :data-testid="`tool-call-toggle-${i}`" @click="toggle(i)">
          <AppIcon :name="toolIcon(c.name)" :size="14" class="shrink-0" />
          <span class="min-w-0 flex-1">
            <span class="block font-semibold">{{ c.name }}</span>
            <span class="block truncate text-muted" :title="summarizeArgs(c.arguments, 1000)">{{ summarizeArgs(c.arguments) }}</span>
          </span>
          <span class="inline-flex shrink-0 items-center gap-1 font-semibold" :data-testid="`tool-call-decision-${i}`">
            <AppIcon :name="DECISION_ICON[c.decision]" :size="13" :class="c.decision === 'running' ? 'animate-spin' : ''" /> {{ decisionLabel(c) }}
          </span>
          <span v-if="formatToolDuration(c.durationMs)" class="shrink-0 text-muted">{{ formatToolDuration(c.durationMs) }}</span>
          <AppIcon :name="open.has(i) ? 'chevron-up' : 'chevron-down'" :size="12" class="shrink-0" />
        </button>
        <div v-if="open.has(i)" class="space-y-1.5 border-t border-line p-2" :data-testid="`tool-call-detail-${i}`">
          <OutputViewer :value="c.arguments ?? {}" title="Arguments" />
          <template v-if="callDetail(c)">
            <p v-if="c.decision !== 'ran' && !inFlight(c)" class="flex items-start gap-1.5 text-[11px] font-semibold" :data-testid="`tool-call-reason-${i}`">
              <AppIcon :name="DECISION_ICON[c.decision]" :size="12" class="mt-0.5 shrink-0" />
              <ExpandableText :text="callDetail(c)" :lines="4" strong />
            </p>
            <OutputViewer v-else :value="callDetail(c)" title="Result" />
          </template>
        </div>
      </li>
    </ol>
  </section>
</template>
