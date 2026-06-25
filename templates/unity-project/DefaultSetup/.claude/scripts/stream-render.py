#!/usr/bin/env python3
"""Pretty-print Claude CLI stream-json (stdin) for the backlog loop.

Port of BlazeSurvivor's Render-ClaudeStreamLine: turns the raw --output-format
stream-json firehose into readable, colored console output (session header,
assistant prose, tool calls, tool output, done line). The raw JSON is still
written to the log file by `tee` upstream; this only affects what humans see.

Usage:  claude ... | tee raw.log | stream-render.py [--provider claude] [--effort high] [--no-color]
"""
import sys
import json
import argparse

p = argparse.ArgumentParser()
p.add_argument("--provider", default="claude")
p.add_argument("--effort", default="")
p.add_argument("--no-color", action="store_true")
args = p.parse_args()

USE_COLOR = sys.stdout.isatty() and not args.no_color

C = {
    "reset": "\033[0m", "dim": "\033[90m", "cyan": "\033[36m", "white": "\033[37m",
    "green": "\033[32m", "yellow": "\033[33m", "darkcyan": "\033[36m", "gray": "\033[37m",
}


def col(s, c):
    return f"{C[c]}{s}{C['reset']}" if USE_COLOR else s


state = {"open_text": False, "header_printed": False}


def finish_text_line():
    if state["open_text"]:
        sys.stdout.write("\n")
        state["open_text"] = False


def speaker_block(label, body, label_c="cyan", body_c="white"):
    finish_text_line()
    if body is None or str(body).strip() == "":
        print(col(f"{label}:", label_c))
        return
    lines = str(body).splitlines()
    sys.stdout.write(col(f"{label}: ", label_c))
    print(col(lines[0], body_c))
    for ln in lines[1:]:
        if ln.strip() == "":
            continue
        sys.stdout.write(col("  ", "dim"))
        print(col(ln, body_c))


def session_header(workdir, model, provider, effort, approval, sandbox, session_id):
    if state["header_printed"]:
        return
    state["header_printed"] = True
    print()
    print(col("--------", "dim"))
    for lbl, v in (("workdir", workdir), ("model", model), ("provider", provider),
                   ("effort", effort), ("approval", approval), ("sandbox", sandbox)):
        print(col(f"{lbl}: {v}", "gray"))
    if session_id:
        print(col(f"session id: {session_id}", "gray"))
    print(col("--------", "dim"))


def normalize_tool(name):
    if name in ("Bash", "PowerShell", "run_shell_command"):
        return "exec"
    return (name or "").lower()


def tool_body(payload):
    if not isinstance(payload, dict):
        return "" if payload is None else json.dumps(payload, ensure_ascii=False)
    cmd = payload.get("command")
    desc = payload.get("description")
    if cmd:
        return f"{cmd}\n# {desc}" if desc else str(cmd)
    return json.dumps(payload, ensure_ascii=False, separators=(",", ":"))


def stream_text(text):
    if not text:
        return
    if not state["open_text"]:
        sys.stdout.write(col(f"{args.provider}: ", "cyan"))
        state["open_text"] = True
    sys.stdout.write(col(text, "white"))


def render(obj):
    t = obj.get("type")
    if t == "system":
        if obj.get("subtype") == "init":
            session_header(obj.get("cwd", ""), obj.get("model", ""), args.provider,
                           args.effort or "default", obj.get("permissionMode", ""),
                           "n/a", obj.get("session_id", ""))
    elif t == "assistant":
        msg = obj.get("message") or {}
        for block in (msg.get("content") or []):
            if isinstance(block, dict) and block.get("type") == "tool_use":
                speaker_block(normalize_tool(block.get("name", "")),
                              tool_body(block.get("input")), "green", "white")
    elif t == "user":
        tur = obj.get("tool_use_result")
        if tur:
            status = "error" if tur.get("is_error") else ("interrupted" if tur.get("interrupted") else "ok")
            speaker_block("result", status, "green", "white")
            speaker_block("stdout", tur.get("stdout", ""), "darkcyan", "white")
            if tur.get("stderr"):
                speaker_block("stderr", tur.get("stderr", ""), "darkcyan", "yellow")
    elif t == "stream_event":
        ev = obj.get("event") or {}
        if ev.get("type") == "content_block_delta":
            delta = ev.get("delta") or {}
            if delta.get("type") == "text_delta":
                stream_text(delta.get("text", ""))
        elif ev.get("type") == "content_block_stop":
            finish_text_line()
    elif t == "result":
        finish_text_line()
        status = obj.get("subtype") or ("error" if obj.get("is_error") else "completed")
        speaker_block("done", f"Claude {status}", "green", "white")


for line in sys.stdin:
    line = line.rstrip("\n")
    if line.strip() == "":
        continue
    if not line.lstrip().startswith("{"):
        speaker_block("info", line, "dim", "gray")
        continue
    try:
        obj = json.loads(line)
    except Exception:
        speaker_block("info", line, "dim", "gray")
        continue
    try:
        render(obj)
    except Exception:
        pass
    sys.stdout.flush()

finish_text_line()
