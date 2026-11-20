<script setup lang="ts">
import { onMounted } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import { useAppStore } from '@/stores/appStore'

const app = useAppStore()
onMounted(app.load)
</script>

<template>
  <section class="mx-auto max-w-xl px-4 py-16 text-center" data-testid="home">
    <AppIcon name="git-branch" :size="64" class="mx-auto" />
    <h1 class="mt-3 text-4xl font-bold tracking-tight">Tammoa</h1>
    <p class="mx-auto mt-2 max-w-md text-ink-soft">
      Build AI-agent flows from cards: triggers, models, scripts, loops.
    </p>

    <div class="mt-8 rounded-card border border-line p-4 text-left">
      <div class="mb-3 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-widest text-muted">
        <AppIcon name="activity" :size="13" /> Backend
      </div>
      <div class="flex items-center justify-between gap-3">
        <span
          v-if="app.loading"
          class="inline-flex items-center gap-1.5 rounded-md bg-surface-muted px-2 py-1 text-xs font-semibold text-ink-soft"
          data-testid="status-loading"
        >
          <AppIcon name="loader" :size="13" class="animate-spin" /> Connecting
        </span>
        <span
          v-else-if="app.error"
          class="inline-flex items-center gap-1.5 rounded-md bg-ink px-2 py-1 text-xs font-semibold text-on-ink"
          data-testid="status-error"
        >
          <AppIcon name="x-circle" :size="13" /> {{ app.error }}
        </span>
        <span
          v-else-if="app.info"
          class="inline-flex items-center gap-1.5 rounded-md border border-ink px-2 py-1 text-xs font-semibold"
          data-testid="status-ok"
        >
          <AppIcon name="check-circle" :size="13" />
          <span data-testid="version">{{ app.info.name }} v{{ app.info.version }}</span>
        </span>
        <button
          type="button"
          class="inline-flex min-h-7 items-center gap-2 rounded-control border border-line-strong px-2.5 text-[11px] font-semibold hover:bg-surface-hover disabled:opacity-40"
          :disabled="app.loading"
          data-testid="refresh"
          @click="app.load()"
        >
          <AppIcon name="refresh" :size="14" /> Refresh
        </button>
      </div>
    </div>
  </section>
</template>
