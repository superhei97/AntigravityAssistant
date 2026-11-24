#!/usr/bin/env python3
"""Official Antigravity Lifecycle Hook handler that updates live session state (macOS & Windows)."""
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
    if isinstance(detail, str) and ("/" in detail or "\\" in detail) and not args.get("toolSummary"):
        detail = os.path.basename(detail.replace("\\", "/"))

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

    if sys.platform == "win32":
        local_app_data = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~\\AppData\\Local")
        out_dir = os.path.join(local_app_data, "AgyAssistant")
    else:
        out_dir = os.path.expanduser("~/Library/Application Support/AgyAssistant")

    os.makedirs(out_dir, exist_ok=True)
    tmp_path = os.path.join(out_dir, f"hook_state.{os.getpid()}.tmp")
    final_path = os.path.join(out_dir, "hook_state.json")
    with open(tmp_path, "w", encoding="utf-8") as fp:
        json.dump(state, fp, ensure_ascii=False)
    for attempt in range(4):
        try:
            os.replace(tmp_path, final_path)
            break
        except OSError:
            if attempt == 3:
                try:
                    os.remove(tmp_path)
                except OSError:
                    pass
            else:
                time.sleep(0.015)

    if event == "PreToolUse":
        print(json.dumps({"decision": "allow"}))
    elif event == "Stop":
        print(json.dumps({"decision": "stop"}))
    else:
        print("{}")


if __name__ == "__main__":
    main()
