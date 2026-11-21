<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue'
import { RouterLink, RouterView } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import type { IconName } from '@/components/icons/icons'
import { useAppStore } from '@/stores/appStore'
import { useRunStore } from '@/stores/runStore'
import { useThemeStore } from '@/stores/themeStore'

const nav: { name: string; label: string; icon: IconName }[] = [
  { name: 'flows', label: 'Flows', icon: 'git-branch' },
  { name: 'connections', label: 'Connections', icon: 'plug' },
]
const theme = useThemeStore()
const app = useAppStore()
const runs = useRunStore()
onMounted(() => {
  void app.load()
  runs.connect() // live run state is pushed by the backend
})
onUnmounted(() => runs.disconnect())
</script>

<template>
  <div class="flex h-full flex-col">
    <header class="flex items-center gap-2 border-b border-line px-4 py-2.5">
      <RouterLink :to="{ name: 'flows' }" class="inline-flex items-center gap-2 text-base font-bold tracking-tight" data-testid="brand">
        <AppIcon name="git-branch" :size="22" /> Tammoa
      </RouterLink>
      <nav class="ml-3 flex items-center gap-1" aria-label="Main">
        <RouterLink
          v-for="item in nav"
          :key="item.name"
          :to="{ name: item.name }"
          class="inline-flex min-h-8 items-center gap-1.5 rounded-control border border-transparent px-2.5 text-xs font-semibold text-ink-soft hover:bg-surface-hover hover:text-ink"
          active-class="!border-ink !text-ink"
          :data-testid="`nav-${item.name}`"
        >
          <AppIcon :name="item.icon" :size="14" /> {{ item.label }}
        </RouterLink>
      </nav>
      <span class="flex-1" />
      <span
        class="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-semibold"
        :class="app.error ? 'bg-ink text-on-ink' : 'border border-ink'"
        :title="app.error ?? 'Backend connected'"
        data-testid="backend-status"
      >
        <AppIcon :name="app.error ? 'x-circle' : app.info ? 'check-circle' : 'loader'" :size="13" :class="!app.error && !app.info ? 'animate-spin' : ''" />
        <span v-if="app.info" data-testid="version">v{{ app.info.version }}</span>
        <span v-else-if="app.error">Offline</span>
      </span>
      <button
        type="button"
        class="inline-flex h-8 w-8 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover"
        :title="theme.theme === 'dark' ? 'Switch to light' : 'Switch to dark'"
        :aria-label="theme.theme === 'dark' ? 'Switch to light' : 'Switch to dark'"
        data-testid="theme-toggle"
        @click="theme.toggle()"
      >
        <AppIcon :name="theme.theme === 'dark' ? 'sun' : 'moon'" :size="15" />
      </button>
    </header>
    <main class="min-h-0 flex-1 overflow-auto">
      <RouterView />
    </main>
  </div>
</template>
