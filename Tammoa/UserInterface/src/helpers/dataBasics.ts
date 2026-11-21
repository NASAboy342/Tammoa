/** The short "how data moves between cards" lesson shown in the Input tab and the guides. Generic: nothing here is about one card. */
export const DATA_BASICS: { icon: 'arrow-right' | 'edit' | 'zap' | 'code' | 'database' | 'refresh'; title: string; text: string; example: string }[] = [
  {
    icon: 'arrow-right',
    title: 'Each card receives the output of the card before it',
    text: 'It is called input. The Input tab shows what it really was in your last run.',
    example: '{{input.json.name}}',
  },
  {
    icon: 'zap',
    title: 'The Run input is available everywhere',
    text: 'Whatever you type in the Run input box, in every card, however far down the flow.',
    example: '{{run.input}}',
  },
  {
    icon: 'edit',
    title: 'In text settings, write the field in double braces',
    text: 'It is replaced by the value when the card runs (prompts, URLs, file paths, JSON, shell commands).',
    example: 'Hello {{input.json.name}}',
  },
  {
    icon: 'code',
    title: 'In JavaScript and Python code use the ready-made variables',
    text: 'JavaScript: input and runInput. Python: input_data and run_input. Shell: $TAMMOA_INPUT.',
    example: 'input.json.name   ·   input_data["json"]["name"]',
  },
  {
    icon: 'refresh',
    title: 'A connection can go back to an earlier card: that is a loop',
    text: 'The earlier card runs again with the new input, until an IF sends the flow the other way. Cards after the loop run once, when it is over. A card runs at most 50 times per run.',
    example: 'IF true → back to the card before it',
  },
  {
    icon: 'database',
    title: 'A Data card keeps reusable text, readable from any card without a trigger',
    text: 'Name the card, store text in it, and read it by its name. JSON text also gives its fields.',
    example: '{{data.Style}}   ·   {{data.Settings.city}}   ·   JavaScript: data.Style',
  },
]
