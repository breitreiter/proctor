"""A scripted model endpoint: the vendor agents' counterpart to nb's Mock provider.

Speaks Anthropic Messages (/v1/messages), OpenAI Responses (/v1/responses) and OpenAI Chat Completions
(/v1/chat/completions), streamed or not. Stateless: the script is in the prompt.

  STUB:run=<command>  first answer: one call to the agent's shell tool running <command>;
                      once a tool result is in the conversation: "tools: <every tool name the request offered>"
  STUB:fail           every request is a 500
  STUB:hang           the tool call, then the answer never comes

A request with no tools (a title, a summary) gets "ok". Any other path is a 404, which is also what Codex's
WebSocket upgrade gets, so it falls back to HTTP.
"""

import json
import re
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = 8765
SHELLS = {
    "Bash": lambda c: {"command": c, "description": "run the stub's command"},
    "exec_command": lambda c: {"cmd": c},
    "shell_command": lambda c: {"command": c},
    "shell": lambda c: {"command": ["bash", "-lc", c]},
    "run_shell_command": lambda c: {"command": c},
}
SCRIPT = re.compile(r'STUB:(run|fail|hang)(?:=((?:[^"\\]|\\.)*))?')


def tool_names(tools):
    """Every tool a request offers, in its dialect: Anthropic's name, Chat's function.name, Responses' name or type."""
    return [t.get("name") or (t.get("function") or {}).get("name") or t.get("type") for t in tools or []]


def has_tool_result(body):
    for m in body.get("messages") or []:
        if m.get("role") == "tool":
            return True
        if isinstance(m.get("content"), list) and any(c.get("type") == "tool_result" for c in m["content"] if isinstance(c, dict)):
            return True
    return any(isinstance(i, dict) and i.get("type") == "function_call_output" for i in body.get("input") or [])


def decide(body, raw):
    """("text", str) or ("tool", name, args) or ("fail",) or ("hang",)."""
    names = tool_names(body.get("tools"))
    found = SCRIPT.search(raw)
    verb = found.group(1) if found else None
    if verb == "fail":
        return ("fail",)
    if not names:
        return ("text", "ok")
    if has_tool_result(body):
        return ("hang",) if verb == "hang" else ("text", "tools: " + ", ".join(n for n in names if n))
    shell = next((n for n in names if n in SHELLS), None)
    if shell is None or verb is None:
        return ("text", "tools: " + ", ".join(n for n in names if n))
    command = json.loads('"' + (found.group(2) or "true") + '"') if verb == "run" else "true"
    return ("tool", shell, SHELLS[shell](command))


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        sys.stderr.write("stub: " + (fmt % args) + "\n")

    def do_GET(self):
        self.reply(404, {"error": {"message": "the stub serves POST only"}})

    def do_POST(self):
        raw = self.rfile.read(int(self.headers.get("Content-Length") or 0)).decode()
        body = json.loads(raw or "{}")
        path = self.path.split("?")[0].rstrip("/")
        dialect = next((d for d in ("messages", "responses", "chat/completions") if path.endswith("/" + d)), None)
        if dialect is None:
            return self.reply(404, {"error": {"message": f"no such path: {self.path}"}})
        what = decide(body, raw)
        if what[0] == "fail":
            return self.reply(500, {"error": {"type": "api_error", "message": "the stub fails as scripted"}})
        if what[0] == "hang":
            time.sleep(3600)
            return
        model = body.get("model") or "stub"
        stream = bool(body.get("stream"))
        {"messages": self.anthropic, "responses": self.openai_responses, "chat/completions": self.chat}[dialect](what, model, stream)

    # ---- Anthropic Messages ----

    def anthropic(self, what, model, stream):
        if what[0] == "tool":
            block = {"type": "tool_use", "id": "toolu_stub1", "name": what[1], "input": what[2]}
            stop = "tool_use"
        else:
            block = {"type": "text", "text": what[1]}
            stop = "end_turn"
        usage = {"input_tokens": 10, "output_tokens": 5, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0}
        message = {"id": "msg_stub", "type": "message", "role": "assistant", "model": model, "content": [block],
                   "stop_reason": stop, "stop_sequence": None, "usage": usage}
        if not stream:
            return self.reply(200, message)
        start = dict(message, content=[], stop_reason=None, usage=dict(usage, output_tokens=0))
        events = [("message_start", {"type": "message_start", "message": start})]
        if block["type"] == "tool_use":
            events.append(("content_block_start", {"type": "content_block_start", "index": 0, "content_block": dict(block, input={})}))
            events.append(("content_block_delta", {"type": "content_block_delta", "index": 0,
                                                   "delta": {"type": "input_json_delta", "partial_json": json.dumps(block["input"])}}))
        else:
            events.append(("content_block_start", {"type": "content_block_start", "index": 0, "content_block": {"type": "text", "text": ""}}))
            events.append(("content_block_delta", {"type": "content_block_delta", "index": 0, "delta": {"type": "text_delta", "text": block["text"]}}))
        events.append(("content_block_stop", {"type": "content_block_stop", "index": 0}))
        events.append(("message_delta", {"type": "message_delta", "delta": {"stop_reason": stop, "stop_sequence": None}, "usage": {"output_tokens": 5}}))
        events.append(("message_stop", {"type": "message_stop"}))
        self.sse(events)

    # ---- OpenAI Responses ----

    def openai_responses(self, what, model, stream):
        if what[0] == "tool":
            item = {"type": "function_call", "id": "fc_stub1", "call_id": "call_stub1", "name": what[1],
                    "arguments": json.dumps(what[2]), "status": "completed"}
        else:
            item = {"type": "message", "id": "msg_stub1", "role": "assistant", "status": "completed",
                    "content": [{"type": "output_text", "text": what[1], "annotations": []}]}
        usage = {"input_tokens": 10, "input_tokens_details": {"cached_tokens": 0}, "output_tokens": 5,
                 "output_tokens_details": {"reasoning_tokens": 0}, "total_tokens": 15}
        response = {"id": "resp_stub", "object": "response", "created_at": int(time.time()), "status": "completed",
                    "model": model, "output": [item], "usage": usage}
        if not stream:
            return self.reply(200, response)
        events = [("response.created", {"type": "response.created", "response": dict(response, status="in_progress", output=[])})]
        events.append(("response.output_item.added", {"type": "response.output_item.added", "output_index": 0,
                                                      "item": dict(item, status="in_progress") if item["type"] == "message" else dict(item, arguments="", status="in_progress")}))
        if item["type"] == "message":
            events.append(("response.output_text.delta", {"type": "response.output_text.delta", "item_id": item["id"], "output_index": 0,
                                                          "content_index": 0, "delta": what[1]}))
        else:
            events.append(("response.function_call_arguments.delta", {"type": "response.function_call_arguments.delta", "item_id": item["id"],
                                                                      "output_index": 0, "delta": item["arguments"]}))
        events.append(("response.output_item.done", {"type": "response.output_item.done", "output_index": 0, "item": item}))
        events.append(("response.completed", {"type": "response.completed", "response": response}))
        self.sse([(name, dict(data, sequence_number=n)) for n, (name, data) in enumerate(events)])

    # ---- OpenAI Chat Completions ----

    def chat(self, what, model, stream):
        if what[0] == "tool":
            call = {"id": "call_stub1", "type": "function", "function": {"name": what[1], "arguments": json.dumps(what[2])}}
            message, finish = {"role": "assistant", "content": None, "tool_calls": [call]}, "tool_calls"
        else:
            message, finish = {"role": "assistant", "content": what[1]}, "stop"
        usage = {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}
        base = {"id": "chatcmpl-stub", "created": int(time.time()), "model": model}
        if not stream:
            return self.reply(200, dict(base, object="chat.completion", usage=usage,
                                        choices=[{"index": 0, "message": message, "finish_reason": finish}]))
        delta = {"role": "assistant"}
        if "tool_calls" in message:
            delta["tool_calls"] = [dict(message["tool_calls"][0], index=0)]
        else:
            delta["content"] = message["content"]
        chunk = dict(base, object="chat.completion.chunk")
        events = [(None, dict(chunk, choices=[{"index": 0, "delta": delta, "finish_reason": None}])),
                  (None, dict(chunk, choices=[{"index": 0, "delta": {}, "finish_reason": finish}])),
                  (None, dict(chunk, choices=[], usage=usage))]
        self.sse(events, done=True)

    # ---- the wire ----

    def reply(self, status, obj):
        data = json.dumps(obj).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def sse(self, events, done=False):
        lines = []
        for name, data in events:
            if name:
                lines.append(f"event: {name}")
            lines.append("data: " + json.dumps(data))
            lines.append("")
        if done:
            lines += ["data: [DONE]", ""]
        data = ("\n".join(lines) + "\n").encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
