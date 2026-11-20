<script setup lang="ts">
import { computed, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import IconField from '@/components/forms/IconField.vue'
import ModalShell from '@/components/ModalShell.vue'

const props = defineProps<{ busy: boolean; error: string | null }>()
const emit = defineEmits<{ submit: [name: string, description: string]; cancel: [] }>()

const name = ref('')
const description = ref('')
const valid = computed(() => name.value.trim().length > 0)

function submit() {
  if (valid.value && !props.busy) emit('submit', name.value.trim(), description.value.trim())
}
</script>

<template>
  <ModalShell testid="create-dialog" labelledby="create-title" @close="emit('cancel')">
    <form @submit.prevent="submit">
      <h2 id="create-title" class="mb-4 flex items-center gap-2 text-lg font-bold tracking-tight">
        <AppIcon name="plus" :size="18" /> New flow
      </h2>
      <div class="space-y-3">
        <IconField v-model="name" label="Name" icon="git-branch" :maxlength="100" testid="create-name" placeholder="Daily report" />
        <IconField v-model="description" label="Description (optional)" icon="edit" :maxlength="500" testid="create-description" />
      </div>
      <p v-if="error" class="mt-3 flex items-center gap-1.5 text-xs font-semibold" data-testid="create-error">
        <AppIcon name="alert" :size="14" /> {{ error }}
      </p>
      <div class="mt-5 flex justify-end gap-2">
        <button type="button" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover" data-testid="create-cancel" @click="emit('cancel')">
          <AppIcon name="x" :size="15" /> Cancel
        </button>
        <button type="submit" :disabled="!valid || busy" class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90 disabled:opacity-40" data-testid="create-submit">
          <AppIcon :name="busy ? 'loader' : 'plus'" :size="15" :class="busy ? 'animate-spin' : ''" /> Create
        </button>
      </div>
    </form>
  </ModalShell>
</template>
