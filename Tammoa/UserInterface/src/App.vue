<script setup lang="ts">
import { RouterView } from 'vue-router'
import AppIcon from '@/components/icons/AppIcon.vue'
import { useThemeStore } from '@/stores/themeStore'

const theme = useThemeStore()
</script>

<template>
  <div class="flex h-full flex-col">
    <header class="flex items-center gap-2 border-b border-line px-4 py-2.5">
      <span class="inline-flex items-center gap-2 text-base font-bold tracking-tight" data-testid="brand">
        <AppIcon name="git-branch" :size="22" /> Tammoa
      </span>
      <span class="flex-1" />
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
