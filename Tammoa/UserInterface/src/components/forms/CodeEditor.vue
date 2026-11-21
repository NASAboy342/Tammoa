<script setup lang="ts">
/**
 * A code editor (CodeMirror 6): line numbers, bracket matching and auto-close, indentation (Tab / Shift-Tab, Esc then Tab
 * to leave the field), undo/redo, syntax highlighting for shell, JavaScript, Python and JSON. Themed with the app's
 * colourless tokens, so it follows light/dark and highlights with weight and italics instead of hues.
 * Presentational: v-model in, v-model out.
 */
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { Compartment, EditorState, type Extension } from '@codemirror/state'
import { EditorView, drawSelection, highlightActiveLine, highlightActiveLineGutter, keymap, lineNumbers, placeholder as placeholderExt } from '@codemirror/view'
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands'
import { closeBrackets, closeBracketsKeymap } from '@codemirror/autocomplete'
import { HighlightStyle, StreamLanguage, bracketMatching, indentOnInput, indentUnit, syntaxHighlighting } from '@codemirror/language'
import { javascript } from '@codemirror/lang-javascript'
import { python } from '@codemirror/lang-python'
import { json } from '@codemirror/lang-json'
import { shell } from '@codemirror/legacy-modes/mode/shell'
import { tags as t } from '@lezer/highlight'

const props = withDefaults(defineProps<{ modelValue: string; language?: string; label: string; placeholder?: string; readOnly?: boolean }>(), {
  language: 'text',
  placeholder: undefined,
  readOnly: false,
})
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const host = ref<HTMLElement | null>(null)
let view: EditorView | null = null
const languageSlot = new Compartment()
const readOnlySlot = new Compartment()

const theme = EditorView.theme({
  '&': { color: 'var(--ink)', backgroundColor: 'var(--surface)', fontSize: '12px', height: '100%' },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': { fontFamily: 'var(--font-mono)', lineHeight: '1.55', overflow: 'auto' },
  '.cm-content': { caretColor: 'var(--ink)', padding: '6px 0' },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--ink)' },
  '.cm-gutters': { backgroundColor: 'var(--surface-muted)', color: 'var(--muted)', border: 'none', borderRight: '1px solid var(--line)' },
  '.cm-activeLine': { backgroundColor: 'var(--surface-hover)' },
  '.cm-activeLineGutter': { backgroundColor: 'var(--surface-hover)', color: 'var(--ink)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection': { backgroundColor: 'var(--line-strong)' },
  '.cm-matchingBracket, &.cm-focused .cm-matchingBracket': { outline: '1px solid var(--ink)', backgroundColor: 'transparent' },
  '.cm-nonmatchingBracket': { outline: '1px dashed var(--ink)', backgroundColor: 'transparent' },
  '.cm-placeholder': { color: 'var(--muted)' },
})

// Colourless highlighting: structure is shown with weight, style and tone, never with hue (extra-minimal template).
const highlight = HighlightStyle.define([
  { tag: [t.keyword, t.controlKeyword, t.definitionKeyword, t.moduleKeyword, t.operatorKeyword, t.modifier], fontWeight: '700' },
  { tag: [t.string, t.special(t.string), t.regexp], color: 'var(--ink-soft)' },
  { tag: [t.number, t.bool, t.null, t.atom], fontWeight: '700', color: 'var(--ink-soft)' },
  { tag: [t.comment, t.lineComment, t.blockComment], color: 'var(--muted)', fontStyle: 'italic' },
  { tag: [t.function(t.variableName), t.function(t.propertyName), t.definition(t.variableName), t.className], textDecoration: 'underline', textDecorationColor: 'var(--line-strong)' },
  { tag: [t.propertyName, t.attributeName], fontWeight: '600' },
  { tag: [t.punctuation, t.bracket, t.separator, t.operator], color: 'var(--muted)' },
  { tag: t.invalid, textDecoration: 'underline wavy' },
])

function languageExtension(language: string): Extension {
  switch (language) {
    case 'javascript':
      return [javascript(), indentUnit.of('  ')]
    case 'python':
      return [python(), indentUnit.of('    ')]
    case 'json':
      return [json(), indentUnit.of('  ')]
    case 'shell':
      return [StreamLanguage.define(shell), indentUnit.of('  ')]
    default:
      return [indentUnit.of('  ')]
  }
}

const readOnlyExtension = (readOnly: boolean) => [EditorState.readOnly.of(readOnly), EditorView.editable.of(!readOnly)]

onMounted(() => {
  view = new EditorView({
    parent: host.value!,
    state: EditorState.create({
      doc: props.modelValue,
      extensions: [
        lineNumbers(),
        highlightActiveLineGutter(),
        highlightActiveLine(),
        history(),
        drawSelection(),
        indentOnInput(),
        bracketMatching(),
        closeBrackets(),
        syntaxHighlighting(highlight),
        EditorView.lineWrapping,
        EditorView.contentAttributes.of({ 'aria-label': props.label, spellcheck: 'false', autocapitalize: 'off', autocorrect: 'off' }),
        keymap.of([...closeBracketsKeymap, ...defaultKeymap, ...historyKeymap, indentWithTab]),
        props.placeholder ? placeholderExt(props.placeholder) : [],
        theme,
        languageSlot.of(languageExtension(props.language)),
        readOnlySlot.of(readOnlyExtension(props.readOnly)),
        EditorView.updateListener.of((u) => {
          if (u.docChanged) emit('update:modelValue', u.state.doc.toString())
        }),
      ],
    }),
  })
})

watch(
  () => props.modelValue,
  (value) => {
    if (view && value !== view.state.doc.toString()) view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } })
  },
)
watch(
  () => props.language,
  (language) => view?.dispatch({ effects: languageSlot.reconfigure(languageExtension(language)) }),
)
watch(
  () => props.readOnly,
  (readOnly) => view?.dispatch({ effects: readOnlySlot.reconfigure(readOnlyExtension(readOnly)) }),
)
onBeforeUnmount(() => view?.destroy())

defineExpose({ focus: () => view?.focus() })
</script>

<template>
  <div ref="host" class="h-full w-full" data-testid="code-editor" />
</template>
