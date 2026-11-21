<script setup lang="ts">
/**
 * A labelled input with a leading icon (house rule: fields have icons).
 *   <IconField v-model="name" label="Name" icon="edit" testid="field-name" />
 * Adapted from UI templates/components/icon-form-fields (token names mapped to this app's theme).
 */
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'

withDefaults(
  defineProps<{
    modelValue: string
    label: string
    icon: IconName
    maxlength?: number
    disabled?: boolean
    testid?: string
    placeholder?: string
    invalid?: boolean
    type?: 'text' | 'password'
  }>(),
  { maxlength: undefined, disabled: false, testid: undefined, placeholder: undefined, invalid: false, type: 'text' },
)
defineEmits<{ 'update:modelValue': [value: string] }>()
</script>

<template>
  <label class="block">
    <span class="mb-1 block text-xs font-semibold text-ink-soft">{{ label }}</span>
    <span class="relative block">
      <AppIcon :name="icon" :size="15" class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted" />
      <input
        :value="modelValue"
        :type="type"
        :maxlength="maxlength"
        :disabled="disabled"
        :placeholder="placeholder"
        :data-testid="testid"
        :aria-invalid="invalid ? 'true' : undefined"
        :autocomplete="type === 'password' ? 'new-password' : 'off'"
        spellcheck="false"
        class="min-h-[38px] w-full rounded-control border border-line-strong bg-surface py-2 pl-9 pr-3 text-sm outline-none placeholder:text-muted focus:border-ink focus:ring-1 focus:ring-ink disabled:opacity-50 aria-[invalid=true]:border-2 aria-[invalid=true]:border-dashed aria-[invalid=true]:border-ink"
        @input="$emit('update:modelValue', ($event.target as HTMLInputElement).value)"
      />
    </span>
  </label>
</template>
