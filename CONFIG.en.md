# Configuration & Permissions Guide

[简体中文](./CONFIG.md)

## 1. Local Data Sources (100% Local & Privacy-First)

`Antigravity Assistant` runs completely locally on your Mac and never uploads any transcripts, credentials, or local settings externally. By default, it reads standard local Antigravity files in read-only mode:

- `~/.gemini/antigravity/conversation_summaries.db`: Opened with `SQLITE_OPEN_READONLY` to extract official session titles, workspace project names (`workspace_uris`), and run/idle states.
- `~/.gemini/antigravity/brain/*/.system_generated/logs/transcript.jsonl`: Incrementally cached (mtime + size) to track active turns, tool calls, and token usage over the past 7 days.

## 2. macOS Accessibility Permission (Session Jump & Dual-Side Delete)

To jump directly to a conversation inside `Antigravity.app` (or delete a session on both sides), grant Accessibility permission once:

1. Click **Authorize** in the dashboard banner or open **System Settings → Privacy & Security → Accessibility**.
2. Enable **Antigravity 使用助手** (`AntigravityAssistant.app`).
3. **Rebuilding from source**: Because ad-hoc code signatures change on rebuild, if session jumping stops working after running `./scripts/build_app.sh`, remove the old entry in Accessibility settings and re-add the newly built `.app`.

## 3. Quota Synchronization Modes

1. **Magpie Live Cloud Quota (Automatic)**:
   - Automatically connects to `http://127.0.0.1:3425/v1/magpie/quotas` or the `magpie` CLI (`/Applications/Magpie.app/Contents/MacOS/magpie`, `~/.local/bin/magpie`, `/opt/homebrew/bin/magpie`).
   - Supports multi-account switching and one-click upstream cloud refresh.
2. **Clipboard Calibration + Local Estimation (Fallback)**:
   - Without Magpie, copy your official Usage text and click **Sync Official Usage from Clipboard** in the menu bar menu to anchor local `5h` and `7d` buckets.

## 4. Optional Lifecycle Hooks Plugin

In **Settings (⚙️)**, click **Enable** next to **Lifecycle Hooks** to install a lightweight local hook plugin under `~/.gemini/config/plugins/agy-assistant-hooks` that writes real-time lifecycle events to `~/Library/Application Support/AgyAssistant/hook_state.json`. Click **Remove** anytime to uninstall cleanly.

## 5. Global Shortcut & Launch at Login

- **Global Hotkey**: Press `⌥ + A` (`Option + A`) anywhere to show or hide the floating mascot.
- **Launch at Login**: Move `AntigravityAssistant.app` to `/Applications` and add it under **System Settings → General → Login Items**.
