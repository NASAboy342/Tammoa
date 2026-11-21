/** The short "how data moves between cards" lesson shown in the Input tab and the guides. Generic: nothing here is about one card. */
export const DATA_BASICS: { icon: 'arrow-right' | 'edit' | 'zap' | 'code'; title: string; text: string; example: string }[] = [
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
]
