# Kestrel Row Tool Library help bot

The chat bot on the library's website. Members ask it about borrowing,
reservations, fees and deposits.

- `bot/system.md`: the system prompt.
- `bot/knowledge.md`: the member handbook the bot answers from. The volunteers
  update it each season; the bot gets it appended to the system prompt.
- `bot/chat.sh`: ask the bot one question from a terminal.
- `notes/`: what members ask, collected at the counter.

The bot runs on `qwen3-coder-next` behind our llama-server
(`http://localhost:8080/v1`, no key). To try it:

```bash
bot/chat.sh "How long can I keep a tool?"
```
