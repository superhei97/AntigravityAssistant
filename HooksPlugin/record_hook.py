#!/usr/bin/env python3
"""Official Antigravity Lifecycle Hook handler that updates live session state."""
import json
import os
import sys
import time


def main() -> None:
    event = sys.argv[1] if len(sys.argv) > 1 else "Unknown"
    try:
        payload = json.loads(sys.stdin.read() or "{}")
    except Exception:
        payload = {}

    tool_call = payload.get("toolCall") or {}
    tool_name = tool_call.get("name")
    args = tool_call.get("args") or {}
    detail = args.get("toolSummary") or args.get("Path") or args.get("query")
    if isinstance(detail, str) and "/" in detail and not args.get("toolSummary"):
        detail = os.path.basename(detail)

    state = {
        "event": event,
        "timestamp": time.time(),
        "conversationId": payload.get("conversationId"),
        "modelName": payload.get("modelName"),
        "toolName": tool_name,
        "toolDetail": detail,
        "terminationReason": payload.get("terminationReason"),
        "fullyIdle": payload.get("fullyIdle"),
        "error": payload.get("error"),
    }

    out_dir = os.path.expanduser("~/Library/Application Support/AgyAssistant")
    os.makedirs(out_dir, exist_ok=True)
    tmp_path = os.path.join(out_dir, "hook_state.json.tmp")
    final_path = os.path.join(out_dir, "hook_state.json")
    with open(tmp_path, "w", encoding="utf-8") as fp:
        json.dump(state, fp, ensure_ascii=False)
    os.replace(tmp_path, final_path)

    if event == "PreToolUse":
        print(json.dumps({"decision": "allow"}))
    elif event == "Stop":
        print(json.dumps({"decision": "stop"}))
    else:
        print("{}")


if __name__ == "__main__":
    main()
