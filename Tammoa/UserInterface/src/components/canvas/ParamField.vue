<script setup lang="ts">
/** One card parameter, drawn from the backend's description of it (text, textarea, number, bool). */
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import type { CardParamInfo } from '@/services/cardApi'

const props = defineProps<{ param: CardParamInfo; modelValue: unknown }>()
const emit = defineEmits<{ 'update:modelValue': [value: unknown] }>()

const icon: Record<CardParamInfo['kind'], IconName> = { text: 'edit', textarea: 'terminal', number: 'hash', bool: 'check' }
const text = () => (props.modelValue === undefined || props.modelValue === null ? '' : String(props.modelValue))
const field =
  'w-full rounded-control border border-line-strong bg-surface py-2 pl-9 pr-3 text-xs outline-none placeholder:text-muted focus:border-ink focus:ring-1 focus:ring-ink'

function onNumber(e: Event) {
  const raw = (e.target as HTMLInputElement).value
  emit('update:modelValue', raw === '' ? '' : Number(raw))
}
</script>

<template>
  <div class="block" :data-testid="`param-${param.key}`">
    <span class="mb-1 block text-[11px] font-semibold text-ink-soft">
      {{ param.label }}<span v-if="param.required" class="text-muted"> *</span>
    </span>

    <button
      v-if="param.kind === 'bool'"
      type="button"
      role="switch"
      :aria-checked="modelValue === true"
      class="inline-flex min-h-8 items-center gap-1.5 rounded-md border border-ink px-2 text-[11px] font-semibold"
      :class="modelValue === true ? 'bg-ink text-on-ink' : 'bg-transparent text-ink'"
      :data-testid="`param-input-${param.key}`"
      @click="emit('update:modelValue', modelValue !== true)"
    >
      <AppIcon :name="modelValue === true ? 'check' : 'x'" :size="13" /> {{ modelValue === true ? 'On' : 'Off' }}
    </button>

    <span v-else class="relative block">
      <AppIcon :name="icon[param.kind]" :size="14" class="pointer-events-none absolute left-3 text-muted" :class="param.kind === 'textarea' ? 'top-2.5' : 'top-1/2 -translate-y-1/2'" />
      <textarea
        v-if="param.kind === 'textarea'"
        :value="text()"
        rows="4"
        spellcheck="false"
        :placeholder="param.placeholder"
        :class="[field, 'resize-y font-mono']"
        :data-testid="`param-input-${param.key}`"
        @input="emit('update:modelValue', ($event.target as HTMLTextAreaElement).value)"
      />
      <input
        v-else-if="param.kind === 'number'"
        :value="text()"
        type="number"
        :class="field"
        :data-testid="`param-input-${param.key}`"
        @input="onNumber"
      />
      <input
        v-else
        :value="text()"
        type="text"
        spellcheck="false"
        autocomplete="off"
        :placeholder="param.placeholder"
        :class="field"
        :data-testid="`param-input-${param.key}`"
        @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      />
    </span>
    <span v-if="param.help" class="mt-1 block text-[11px] text-muted">{{ param.help }}</span>
  </div>
</template>
