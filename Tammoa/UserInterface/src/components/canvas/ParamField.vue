<script setup lang="ts">
/**
 * One card parameter, drawn from the backend's description of it. Kinds: text, textarea, number, bool, select
 * (fixed options, or options the backend loads, e.g. the models of a connection), code (editor) and connection.
 * There is no card-specific code here: a new card type only needs a descriptor on the backend.
 */
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import IconSelect from '@/components/forms/IconSelect.vue'
import CodeField from '@/components/forms/CodeField.vue'
import { cardApi, type CardParamInfo, type ParamOption } from '@/services/cardApi'
import StoredField from './StoredField.vue'
import { useConnectionStore } from '@/stores/connectionStore'
import { editorLanguage } from '@/helpers/layout'

/** `card` is only needed by parameters whose value lives outside the flow (kind "stored"). */
const props = defineProps<{ param: CardParamInfo; modelValue: unknown; values: Record<string, unknown>; card?: { flowId: string; id: string; name: string; saved: boolean } }>()
const emit = defineEmits<{ 'update:modelValue': [value: unknown]; 'open-guide': [] }>()

const connections = useConnectionStore()
const icon: Record<CardParamInfo['kind'], IconName> = { text: 'edit', textarea: 'edit', number: 'hash', bool: 'check', select: 'list', code: 'code', connection: 'plug', stored: 'database' }
const text = () => (props.modelValue === undefined || props.modelValue === null ? '' : String(props.modelValue))
const str = () => (typeof props.modelValue === 'string' ? props.modelValue : '')
const field =
  'w-full rounded-control border border-line-strong bg-surface py-2 pl-9 pr-3 text-xs outline-none placeholder:text-muted focus:border-ink focus:ring-1 focus:ring-ink'

function onNumber(e: Event) {
  const raw = (e.target as HTMLInputElement).value
  emit('update:modelValue', raw === '' ? '' : Number(raw))
}

// ---- dynamic options (e.g. models of the chosen connection)
const dynamic = ref<ParamOption[]>([])
const optionsLoading = ref(false)
const optionsError = ref<string | null>(null)
const dependsOn = computed(() => (props.param.optionsDependsOn ? String(props.values[props.param.optionsDependsOn] ?? '') : ''))

async function loadOptions() {
  const source = props.param.optionsSource
  if (!source) return
  dynamic.value = []
  optionsError.value = null
  if (props.param.optionsDependsOn && !dependsOn.value) return
  optionsLoading.value = true
  try {
    dynamic.value = await cardApi.options(source, dependsOn.value)
  } catch (e) {
    optionsError.value = e instanceof Error ? e.message : String(e)
  } finally {
    optionsLoading.value = false
  }
}
watch(dependsOn, loadOptions, { immediate: true })

const selectOptions = computed(() => {
  const base = props.param.optionsSource ? dynamic.value : (props.param.options ?? [])
  const current = str()
  // keep a saved value visible even if the list no longer has it (e.g. a model that was removed)
  return current && !base.some((o) => o.value === current) && !optionsLoading.value
    ? [...base, { value: current, label: `${current} (not available)` }]
    : base
})

const providerConnections = computed(() => connections.connections.filter((c) => !props.param.provider || c.provider === props.param.provider))
const connectionOptions = computed(() => {
  const current = str()
  const base = providerConnections.value.map((c) => ({ value: c.id, label: c.name }))
  return current && !base.some((o) => o.value === current) && !connections.loading ? [...base, { value: current, label: '(deleted connection)' }] : base
})
if (props.param.kind === 'connection' && !connections.connections.length && !connections.loading) void connections.load()
</script>

<template>
  <div class="block" :data-testid="`param-${param.key}`">
    <span v-if="param.kind !== 'select' && param.kind !== 'connection' && param.kind !== 'stored'" class="mb-1 block text-[11px] font-semibold text-ink-soft">
      {{ param.label }}<span v-if="param.required" class="text-muted"> *</span>
    </span>

    <StoredField v-if="param.kind === 'stored' && card" :flow-id="card.flowId" :card-id="card.id" :card-name="card.name" :saved="card.saved" :label="param.label" />

    <button
      v-else-if="param.kind === 'bool'"
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

    <CodeField
      v-else-if="param.kind === 'code'"
      :model-value="str()"
      :label="param.label"
      :language="editorLanguage(param, values)"
      :placeholder="param.codePlaceholders?.[editorLanguage(param, values)] ?? param.placeholder"
      :hint="param.codeHelp?.[editorLanguage(param, values)]"
      :testid="`param-input-${param.key}`"
      @update:model-value="emit('update:modelValue', $event)"
      @open-guide="emit('open-guide')"
    />

    <IconSelect
      v-else-if="param.kind === 'select'"
      :model-value="str()"
      :label="param.label"
      :icon="param.optionsSource ? 'bot' : icon.select"
      :options="selectOptions"
      :placeholder="param.optionsSource ? (optionsLoading ? 'Loading models…' : dependsOn ? 'Choose…' : 'Choose a connection first') : param.required && param.defaultValue === undefined ? 'Choose…' : undefined"
      :disabled="optionsLoading || (!!param.optionsSource && !!param.optionsDependsOn && !dependsOn)"
      :invalid="!!optionsError"
      :testid="`param-input-${param.key}`"
      @update:model-value="emit('update:modelValue', $event)"
    >
      <template #label>
        {{ param.label }}<span v-if="param.required" class="text-muted"> *</span>
        <button
          v-if="param.optionsSource"
          type="button"
          class="ml-1 inline-flex h-5 w-5 items-center justify-center rounded-control align-middle hover:bg-surface-hover"
          title="Reload the list"
          aria-label="Reload the list"
          :data-testid="`param-reload-${param.key}`"
          @click.prevent="loadOptions"
        >
          <AppIcon :name="optionsLoading ? 'loader' : 'refresh'" :size="11" :class="optionsLoading ? 'animate-spin' : ''" />
        </button>
      </template>
    </IconSelect>

    <IconSelect
      v-else-if="param.kind === 'connection'"
      :model-value="str()"
      :label="param.label"
      icon="plug"
      :options="connectionOptions"
      placeholder="Choose a connection…"
      :testid="`param-input-${param.key}`"
      @update:model-value="emit('update:modelValue', $event)"
    >
      <template #label>{{ param.label }}<span v-if="param.required" class="text-muted"> *</span></template>
    </IconSelect>

    <span v-else class="relative block">
      <AppIcon :name="icon[param.kind]" :size="14" class="pointer-events-none absolute left-3 text-muted" :class="param.kind === 'textarea' ? 'top-2.5' : 'top-1/2 -translate-y-1/2'" />
      <textarea
        v-if="param.kind === 'textarea'"
        :value="text()"
        rows="3"
        spellcheck="false"
        :placeholder="param.placeholder"
        :class="[field, 'min-h-16 resize-y font-mono']"
        :data-testid="`param-input-${param.key}`"
        @input="emit('update:modelValue', ($event.target as HTMLTextAreaElement).value)"
      />
      <input
        v-else-if="param.kind === 'number'"
        :value="text()"
        type="number"
        :placeholder="param.defaultValue !== undefined ? String(param.defaultValue) : undefined"
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
        :title="text() || undefined"
        @input="emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      />
    </span>

    <p v-if="optionsError" class="mt-1 flex items-start gap-1.5 text-[11px] font-semibold" :data-testid="`param-error-${param.key}`">
      <AppIcon name="alert" :size="12" class="mt-0.5" /> {{ optionsError }}
    </p>
    <RouterLink
      v-if="param.kind === 'connection'"
      :to="{ name: 'connections' }"
      class="mt-1 inline-flex items-center gap-1 text-[11px] font-semibold text-ink-soft hover:text-ink"
      data-testid="manage-connections"
    >
      <AppIcon name="plug" :size="12" /> {{ providerConnections.length ? 'Manage connections' : 'No connection yet: create one' }}
    </RouterLink>
    <span v-if="param.help" class="mt-1 block text-[11px] text-muted">{{ param.help }}</span>
  </div>
</template>
