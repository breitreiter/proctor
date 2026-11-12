# Quarry Lane Bike Kitchen chat bot

The chat assistant on the Bike Kitchen's website. It answers questions about
opening times, repair stands, membership and donations, and should turn down
everything else.

- `bot/system.md`: the system prompt, including what to say to an off-topic request.
- `bot/knowledge.md`: the information sheet, appended to the system prompt.
- `bot/chat.sh`: ask the bot one question from a terminal.
- `notes/off-topic-requests.md`: things people have tried to get it to do.

The bot runs on `qwen3-coder-next` behind our llama-server
(`http://localhost:8080/v1`, no key).
