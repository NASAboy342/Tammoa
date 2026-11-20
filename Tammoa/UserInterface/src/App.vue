<script setup lang="ts">
import { onMounted } from 'vue'
import { RouterLink, RouterView } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import { useAppStore } from '@/stores/appStore'
import { useThemeStore } from '@/stores/themeStore'

const theme = useThemeStore()
const app = useAppStore()
onMounted(app.load)
</script>

<template>
  <div class="flex h-full flex-col">
    <header class="flex items-center gap-2 border-b border-line px-4 py-2.5">
      <RouterLink :to="{ name: 'flows' }" class="inline-flex items-center gap-2 text-base font-bold tracking-tight" data-testid="brand">
        <AppIcon name="git-branch" :size="22" /> Tammoa
      </RouterLink>
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
