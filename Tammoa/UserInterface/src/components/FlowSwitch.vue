<script setup lang="ts">
/** Enable/disable toggle. Clickable, so it carries an icon: power. On = inverted (ink fill), off = outline. */
import AppIcon from '@/components/icons/AppIcon.vue'

defineProps<{ enabled: boolean; busy?: boolean; testid?: string }>()
defineEmits<{ toggle: [] }>()
</script>

<template>
  <button
    type="button"
    role="switch"
    :aria-checked="enabled"
    :disabled="busy"
    :title="enabled ? 'Disable flow' : 'Enable flow'"
    :aria-label="enabled ? 'Disable flow' : 'Enable flow'"
    :data-testid="testid"
    class="inline-flex min-h-7 items-center gap-1.5 rounded-md border border-ink px-2 text-[11px] font-semibold disabled:opacity-40"
    :class="enabled ? 'bg-ink text-on-ink' : 'bg-transparent text-ink'"
    @click="$emit('toggle')"
  >
    <AppIcon :name="busy ? 'loader' : 'power'" :size="13" :class="busy ? 'animate-spin' : ''" />
    {{ enabled ? 'Enabled' : 'Disabled' }}
  </button>
</template>
