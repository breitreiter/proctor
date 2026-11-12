# Millbeck Allotment Society help bot

The chat bot on the society's website. Plot holders and people on the waiting
list ask it about rent, rules and the site.

- `bot/system.md`: the system prompt, as it runs today.
- `bot/system-draft.md`: a rewrite of the system prompt, not live yet.
- `bot/knowledge.md`: the society handbook, appended to the system prompt.
- `bot/chat.sh`: ask the bot one question from a terminal
  (`BOT_PROMPT=bot/system-draft.md bot/chat.sh "..."` to try the draft).
- `docs/questions-log.md`: what people have been asking it.

The bot runs on `qwen3-coder-next` behind our llama-server
(`http://localhost:8080/v1`, no key).
