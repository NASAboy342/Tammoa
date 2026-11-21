<script setup lang="ts">
/**
 * A labelled select with a leading icon and its own chevron. The native arrow is switched off (`appearance-none`):
 * WebKit (the macOS desktop window) ignores padding on a native select, so an icon would land on the text.
 * Adapted from UI templates/components/icon-form-fields (token names mapped to this app's theme).
 */
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'

withDefaults(
  defineProps<{
    modelValue: string
    label: string
    icon: IconName
    options: { value: string; label: string; disabled?: boolean }[]
    placeholder?: string
    disabled?: boolean
    invalid?: boolean
    testid?: string
  }>(),
  { placeholder: undefined, disabled: false, invalid: false, testid: undefined },
)
defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <label class="block">
    <span class="mb-1 block text-[11px] font-semibold text-ink-soft"><slot name="label">{{ label }}</slot></span>
    <span class="relative block">
      <AppIcon :name="icon" :size="15" class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
      <select
        :value="modelValue"
        :disabled="disabled"
        :data-testid="testid"
        :aria-invalid="invalid ? 'true' : undefined"
        class="min-h-[36px] w-full appearance-none rounded-control border border-line-strong bg-surface py-1.5 pl-9 pr-9 text-xs outline-none focus:border-ink focus:ring-1 focus:ring-ink disabled:opacity-50 aria-[invalid=true]:border-2 aria-[invalid=true]:border-dashed aria-[invalid=true]:border-ink"
        @change="$emit('update:modelValue', ($event.target as HTMLSelectElement).value)"
      >
        <option v-if="placeholder !== undefined" value="" disabled>{{ placeholder }}</option>
        <option v-for="o in options" :key="o.value" :value="o.value" :disabled="o.disabled">{{ o.label }}</option>
      </select>
      <AppIcon name="chevron-down" :size="15" class="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-muted" />
    </span>
  </label>
</template>
