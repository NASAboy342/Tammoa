import { ref } from 'vue'
import { defineStore } from 'pinia'

export type Theme = 'light' | 'dark'

export const useThemeStore = defineStore('theme', () => {
  const stored = (() => {
    try {
      return localStorage.getItem('tammoa-theme')
    } catch {
      return null
    }
  })()
  const theme = ref<Theme>(stored === 'dark' ? 'dark' : 'light')

  function apply() {
    document.documentElement.dataset.theme = theme.value
  }
  function toggle() {
    theme.value = theme.value === 'dark' ? 'light' : 'dark'
    try {
      localStorage.setItem('tammoa-theme', theme.value)
    } catch {
      /* ignore */
    }
    apply()
  }
  apply()

  return { theme, toggle }
})
