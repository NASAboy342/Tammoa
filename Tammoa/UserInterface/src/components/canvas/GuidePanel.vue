<script setup lang="ts">
/**
 * A card's tutorial: what it does, how its input arrives, which fields it outputs, and worked examples you can apply to the card
 * with one click. Everything shown is data from the backend (the guide of the card type); nothing here is card-specific.
 */
import { ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import { DATA_BASICS } from '@/helpers/dataBasics'
import { tpl } from '@/helpers/fields'
import type { CardTypeInfo, GuideExample } from '@/services/cardApi'
import DataView from './DataView.vue'
import CodeSnippet from './CodeSnippet.vue'
import FieldExplorer from './FieldExplorer.vue'

const props = defineProps<{ info?: CardTypeInfo }>()
const emit = defineEmits<{ apply: [example: GuideExample] }>()

const open = ref<Record<number, boolean>>({ 0: true })
const basics = ref(false)
const guide = () => props.info?.guide
</script>

<template>
  <div v-if="guide()" class="space-y-3" data-testid="guide">
    <p class="text-xs leading-relaxed text-ink-soft" data-testid="guide-overview">{{ guide()!.overview }}</p>

    <section class="rounded-card border border-line p-2.5" data-testid="guide-input">
      <h3 class="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="arrow-down" :size="12" /> How its input arrives</h3>
      <p class="text-xs leading-relaxed text-ink-soft">{{ guide()!.inputNote }}</p>
    </section>

    <section v-if="guide()!.outputExample" data-testid="guide-output">
      <h3 class="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="arrow-up" :size="12" /> What it outputs</h3>
      <p class="mb-1 text-[11px] text-muted">Example data. The card after this one reads these as <code class="font-mono">{{ tpl('input.…') }}</code>.</p>
      <FieldExplorer :value="guide()!.outputExample" root="input" testid="guide-output-fields" />
    </section>

    <section data-testid="guide-examples">
      <h3 class="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="book" :size="12" /> Examples</h3>
      <ul class="space-y-1.5">
        <li v-for="(ex, i) in guide()!.examples" :key="ex.title" class="rounded-card border border-line" data-testid="guide-example">
          <button
            type="button"
            class="flex w-full items-center gap-2 px-2.5 py-2 text-left text-xs font-semibold hover:bg-surface-hover"
            :aria-expanded="!!open[i]"
            data-testid="guide-example-toggle"
            @click="open[i] = !open[i]"
          >
            <AppIcon :name="open[i] ? 'chevron-up' : 'chevron-down'" :size="13" />
            <span class="min-w-0 flex-1">{{ ex.title }}</span>
          </button>
          <div v-if="open[i]" class="space-y-2 border-t border-line p-2.5" data-testid="guide-example-body">
            <p class="text-xs leading-relaxed text-ink-soft">{{ ex.description }}</p>
            <div v-if="ex.sampleInput !== undefined">
              <p class="mb-1 text-[11px] font-semibold text-muted">This example assumes the input is:</p>
              <DataView :value="ex.sampleInput" root="input" testid="guide-sample-input" />
            </div>
            <p v-if="ex.runInput !== undefined" class="text-[11px] text-muted">
              and the Run input is: <code class="rounded bg-surface-muted px-1 font-mono text-ink">{{ typeof ex.runInput === 'string' ? ex.runInput : JSON.stringify(ex.runInput) }}</code>
            </p>
            <CodeSnippet v-if="ex.code" :code="ex.code" :language="ex.language ?? 'text'" :label="ex.title" />
            <p v-if="ex.result" class="flex items-start gap-1.5 text-xs leading-relaxed" data-testid="guide-result">
              <AppIcon name="arrow-right" :size="13" class="mt-0.5 shrink-0" /> <span>{{ ex.result }}</span>
            </p>
            <button
              v-if="ex.params"
              type="button"
              class="inline-flex min-h-8 items-center gap-1.5 rounded-control border border-ink bg-ink px-3 text-[11px] font-semibold text-on-ink hover:opacity-90"
              title="Fill in this card's settings with the example (you can edit them afterwards)"
              data-testid="guide-apply"
              @click="emit('apply', ex)"
            >
              <AppIcon name="plus" :size="13" /> Use this example
            </button>
          </div>
        </li>
      </ul>
    </section>

    <section v-if="guide()!.tips?.length" data-testid="guide-tips">
      <h3 class="mb-1.5 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted"><AppIcon name="bulb" :size="12" /> Good to know</h3>
      <ul class="space-y-1">
        <li v-for="t in guide()!.tips" :key="t" class="flex items-start gap-1.5 text-xs leading-relaxed text-ink-soft"><AppIcon name="check" :size="12" class="mt-0.5 shrink-0 text-muted" /> <span>{{ t }}</span></li>
      </ul>
    </section>

    <section class="rounded-card border border-line">
      <button type="button" class="flex w-full items-center gap-2 px-2.5 py-2 text-left text-xs font-semibold hover:bg-surface-hover" :aria-expanded="basics" data-testid="guide-basics-toggle" @click="basics = !basics">
        <AppIcon :name="basics ? 'chevron-up' : 'chevron-down'" :size="13" /> How data moves between cards
      </button>
      <ul v-if="basics" class="space-y-2.5 border-t border-line p-2.5" data-testid="guide-basics">
        <li v-for="b in DATA_BASICS" :key="b.title" class="flex items-start gap-2">
          <AppIcon :name="b.icon" :size="13" class="mt-0.5 shrink-0 text-muted" />
          <div class="min-w-0 text-xs leading-relaxed">
            <p class="font-semibold">{{ b.title }}</p>
            <p class="text-ink-soft">{{ b.text }}</p>
            <code class="mt-0.5 inline-block break-all rounded bg-surface-muted px-1 font-mono text-[11px]">{{ b.example }}</code>
          </div>
        </li>
      </ul>
    </section>
  </div>
  <p v-else class="flex items-center gap-1.5 text-xs text-muted" data-testid="guide-none"><AppIcon name="info" :size="13" /> This card has no guide.</p>
</template>
