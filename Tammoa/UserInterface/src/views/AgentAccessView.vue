<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import AppIcon from '@/components/icons/AppIcon.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import OutputViewer from '@/components/OutputViewer.vue'
import { agentInstructions, maskToken } from '@/helpers/agentAccess'
import { agentApi } from '@/services/agentApi'
import { useAgentStore } from '@/stores/agentStore'

const store = useAgentStore()
const access = computed(() => store.access)
const showToken = ref(false)
const confirmNewToken = ref(false)
const copied = ref<string | null>(null)
const skill = ref<string | null>(null)
const skillError = ref<string | null>(null)
const skillBusy = ref(false)

onMounted(store.load)

async function copy(key: string, text: string) {
  try {
    await navigator.clipboard.writeText(text)
    copied.value = key
    setTimeout(() => {
      if (copied.value === key) copied.value = null
    }, 1500)
  } catch {
    /* clipboard blocked (permissions): the text is on screen to select */
  }
}

async function loadSkill() {
  skillBusy.value = true
  skillError.value = null
  try {
    skill.value = await agentApi.skill()
  } catch (e) {
    skillError.value = e instanceof Error ? e.message : String(e)
  } finally {
    skillBusy.value = false
  }
}

async function copySkill() {
  if (skill.value === null) await loadSkill()
  if (skill.value !== null) await copy('skill', skill.value)
}

async function renewToken() {
  await store.newToken()
  confirmNewToken.value = false
}

const iconBtn = 'inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-control border border-line-strong hover:bg-surface-hover disabled:opacity-40'
const textBtn = 'inline-flex min-h-9 items-center gap-2 rounded-control border border-line-strong px-3.5 text-xs font-semibold hover:bg-surface-hover disabled:opacity-40'
</script>

<template>
  <section class="mx-auto max-w-3xl px-4 py-8" data-testid="agent-access">
    <h1 class="mb-1 flex items-center gap-2 text-2xl font-bold tracking-tight"><AppIcon name="bot" :size="22" /> Agent access</h1>
    <p class="mb-5 text-xs text-muted">Let an AI assistant (Claude Code, for example) build flows in this app. You give it the address, the token and the skill; it then creates and edits flows through the local API, so they appear in your list.</p>

    <p v-if="store.error" class="mb-3 flex items-center gap-1.5 rounded-control border-2 border-dashed border-ink p-2.5 text-xs font-semibold" data-testid="agent-error">
      <AppIcon name="alert" :size="14" /> {{ store.error }}
    </p>
    <p v-if="store.loading && !access" class="flex items-center gap-2 text-muted"><AppIcon name="loader" :size="15" class="animate-spin" /> Loading</p>

    <template v-if="access">
      <!-- switches -->
      <div class="mb-4 space-y-2 rounded-card border border-line p-3.5">
        <button
          type="button"
          role="switch"
          :aria-checked="access.enabled"
          class="flex w-full items-center gap-3 rounded-control p-1 text-left hover:bg-surface-hover"
          data-testid="agent-enabled"
          @click="store.update({ enabled: !access.enabled })"
        >
          <AppIcon :name="access.enabled ? 'check-circle' : 'circle'" :size="18" />
          <span class="min-w-0 flex-1">
            <span class="block text-sm font-semibold">Agent access is {{ access.enabled ? 'on' : 'off' }}</span>
            <span class="block text-[11px] text-muted">Off: an agent that sends the token is refused. On: it can read the cards and create or edit flows.</span>
          </span>
        </button>
        <button
          type="button"
          role="switch"
          :aria-checked="access.allowRun"
          :disabled="!access.enabled"
          class="flex w-full items-center gap-3 rounded-control p-1 text-left hover:bg-surface-hover disabled:opacity-40"
          data-testid="agent-allow-run"
          @click="store.update({ allowRun: !access.allowRun })"
        >
          <AppIcon :name="access.allowRun ? 'check-circle' : 'circle'" :size="18" />
          <span class="min-w-0 flex-1">
            <span class="block text-sm font-semibold">Let the agent run flows: {{ access.allowRun ? 'yes' : 'no' }}</span>
            <span class="block text-[11px] text-muted">Running starts scripts, web requests and AI models on this computer. Keep it off to press Run yourself.</span>
          </span>
        </button>
      </div>

      <!-- address and token -->
      <div class="mb-4 space-y-3 rounded-card border border-line p-3.5">
        <div class="flex items-center gap-2" data-testid="agent-url-row">
          <AppIcon name="globe" :size="16" class="text-muted" />
          <div class="min-w-0 flex-1">
            <p class="text-[11px] font-semibold text-ink-soft">Address</p>
            <p class="select-all truncate font-mono text-xs" data-testid="agent-url">{{ access.url }}</p>
          </div>
          <button type="button" :class="iconBtn" title="Copy the address" aria-label="Copy the address" data-testid="agent-copy-url" @click="copy('url', access.url)">
            <AppIcon :name="copied === 'url' ? 'check' : 'copy'" :size="14" />
          </button>
        </div>
        <div class="flex items-center gap-2" data-testid="agent-token-row">
          <AppIcon name="key" :size="16" class="text-muted" />
          <div class="min-w-0 flex-1">
            <p class="text-[11px] font-semibold text-ink-soft">Token</p>
            <p class="select-all truncate font-mono text-xs" data-testid="agent-token">{{ showToken ? access.token : maskToken(access.token) }}</p>
          </div>
          <button type="button" :class="iconBtn" :title="showToken ? 'Hide the token' : 'Show the token'" :aria-label="showToken ? 'Hide the token' : 'Show the token'" data-testid="agent-toggle-token" @click="showToken = !showToken">
            <AppIcon name="eye" :size="14" />
          </button>
          <button type="button" :class="iconBtn" title="Copy the token" aria-label="Copy the token" data-testid="agent-copy-token" @click="copy('token', access.token)">
            <AppIcon :name="copied === 'token' ? 'check' : 'copy'" :size="14" />
          </button>
          <button type="button" :class="iconBtn" title="Make a new token" aria-label="Make a new token" data-testid="agent-new-token" @click="confirmNewToken = true">
            <AppIcon name="refresh" :size="14" />
          </button>
        </div>
        <button
          type="button"
          class="inline-flex min-h-9 items-center gap-2 rounded-control border border-ink bg-ink px-3.5 text-xs font-semibold text-on-ink hover:opacity-90"
          data-testid="agent-copy-all"
          @click="copy('all', agentInstructions(access))"
        >
          <AppIcon :name="copied === 'all' ? 'check' : 'copy'" :size="15" /> {{ copied === 'all' ? 'Copied' : 'Copy for the agent' }}
        </button>
        <p class="text-[11px] text-muted">Paste it to the assistant. It contains the address, the token and where to read the skill.</p>
      </div>

      <!-- the skill -->
      <div class="mb-4 rounded-card border border-line p-3.5">
        <p class="mb-1 flex items-center gap-2 text-sm font-semibold"><AppIcon name="book" :size="16" /> Skill</p>
        <p class="mb-3 text-[11px] text-muted">
          How an assistant builds flows for this version of Tammoa, with the live list of cards. Copy it and save it as <span class="font-mono">.claude/skills/tammoa-flows/SKILL.md</span>
          (or just give the assistant the address and token: it can read it itself).
        </p>
        <div class="flex flex-wrap gap-2">
          <button type="button" :class="textBtn" :disabled="skillBusy" data-testid="agent-copy-skill" @click="copySkill">
            <AppIcon :name="skillBusy ? 'loader' : copied === 'skill' ? 'check' : 'copy'" :size="15" :class="skillBusy ? 'animate-spin' : ''" /> {{ copied === 'skill' ? 'Copied' : 'Copy the skill' }}
          </button>
          <button type="button" :class="textBtn" :disabled="skillBusy" data-testid="agent-view-skill" @click="loadSkill">
            <AppIcon name="eye" :size="15" /> View
          </button>
        </div>
        <p v-if="skillError" class="mt-2 flex items-center gap-1.5 text-xs font-semibold" data-testid="agent-skill-error"><AppIcon name="alert" :size="14" /> {{ skillError }}</p>
        <div v-if="skill !== null" class="mt-3"><OutputViewer :value="skill" title="Skill (Markdown)" /></div>
      </div>

      <p class="flex items-start gap-2 rounded-control border border-dashed border-line-strong p-2.5 text-[11px] text-muted" data-testid="agent-note">
        <AppIcon name="info" :size="14" class="mt-0.5 shrink-0" />
        <span>Anyone with the token can create and edit your flows; only you can switch running on. It cannot delete flows, change connections or read their keys. Tammoa's local API itself stays reachable by programs on this computer, as it always was.</span>
      </p>
    </template>

    <ConfirmDialog
      v-if="confirmNewToken"
      title="Make a new token"
      message="The old token stops working at once. Copy the new one to the assistant afterwards."
      confirm-label="Make a new token"
      confirm-icon="refresh"
      @confirm="renewToken"
      @cancel="confirmNewToken = false"
    />
  </section>
</template>
