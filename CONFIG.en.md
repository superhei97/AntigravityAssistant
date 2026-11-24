# Configuration & Permissions Guide

[简体中文](./CONFIG.md)

## 1. Local Data Sources (100% Local & Privacy-First)

`Antigravity Assistant` runs completely locally on your machine and never uploads any transcripts, credentials, or local settings externally. By default, it inspects standard local Antigravity files (`~/.gemini/antigravity` on macOS, `%USERPROFILE%\.gemini\antigravity` on Windows) in lock-free read-only mode:

- `conversation_summaries.db`: Opened with `SQLITE_OPEN_READONLY` (via built-in `C:\Windows\System32\winsqlite3.dll` on Windows and `libsqlite3` on macOS) to extract official session titles, workspace project names (`workspace_uris`), and run/idle states.
- `brain/*/.system_generated/logs/transcript.jsonl`: Incrementally cached (`mtime + size`) with shared read/write/delete flags to track active turns, tool calls, and token usage over the past 7 days.

Local preferences storage:
- **Windows**: `%LOCALAPPDATA%\AgyAssistant\settings.json`
- **macOS**: `NSUserDefaults` (`com.antigravity.usage-assistant`)

## 2. Session Jump & Dual-Side Delete

- **Windows (Zero-Config UI Automation)**:
  - Uses native **Windows UI Automation** to locate `/c/<conversationId>` inside `Antigravity.exe` (automatically falling back to Unicode `Ctrl + K` search if the sidebar is collapsed).
  - 2-tap deletion triggers UI removal and moves local session artifacts safely to the **Windows Recycle Bin**.
  - *Note: If `Antigravity.exe` is running elevated as Administrator, launch `AntigravityAssistant.exe` with the same elevation so Windows UIPI permits cross-window input.*
- **macOS (Accessibility Permission)**:
  1. Click **Authorize** in the dashboard banner or open **System Settings → Privacy & Security → Accessibility**.
  2. Enable **Antigravity 使用助手** (`AntigravityAssistant.app`).
  3. **Rebuilding from source**: Because ad-hoc code signatures change on rebuild, if session jumping stops working after running `./scripts/build_app.sh`, remove the old entry in Accessibility settings and re-add the newly built `.app`.

## 3. Quota Synchronization Modes

1. **Built-in Direct Google Cloud Quota (Windows, Magpie Protocol)**:
   - Click **🌐 Google Login** in the Quota Overview card or **Settings (⚙️)** to authorize once via `OAuth 2.0 + PKCE`, or paste a `1//` refresh token / Magpie / CLIProxyAPI JSON via **📋 Paste Token / Usage**.
   - Queries Google Code Assist `retrieveUserQuotaSummary` directly for live **Gemini** and **Claude & GPT** `5h` and `7d` pools. Refresh tokens are encrypted locally via Windows `DPAPI (CurrentUser)` at `%LOCALAPPDATA%\AgyAssistant\oauth_accounts.json`.
2. **Magpie Local Gateway (Automatic)**:
   - Automatically connects to [Magpie](https://github.com/yetone/magpie) (`http://127.0.0.1:3425/v1/magpie/quotas` or local `magpie` CLI), supporting multi-account switching and one-click upstream cloud refresh.
3. **Clipboard Calibration + Local Estimation (Fallback)**:
   - Without signing in, copy your official Usage popup text (`Weekly limit` / `5h limit`) and click **📋 Paste Token / Usage** to anchor local `5h` and `7d` buckets.

## 4. Optional Lifecycle Hooks Plugin

In **Settings (⚙️)**, click **Enable** next to **Lifecycle Hooks** to install a cross-platform local hook plugin under `~/.gemini/config/plugins/agy-assistant-hooks` (`%USERPROFILE%\.gemini\config\plugins\agy-assistant-hooks` on Windows) that writes real-time lifecycle events to:
- **Windows**: `%LOCALAPPDATA%\AgyAssistant\hook_state.json`
- **macOS**: `~/Library/Application Support/AgyAssistant/hook_state.json`

Click **Remove** anytime to uninstall cleanly.

## 5. Global Shortcut & Launch at Login

- **Global Hotkey**:
  - **Windows**: Press `Alt + A` anywhere to show or hide the floating mascot.
  - **macOS**: Press `⌥ + A` (`Option + A`) anywhere to show or hide the floating mascot.
- **Launch at Login**:
  - **Windows**: Toggle **Launch at Startup** directly inside **Settings (⚙️)** or the system tray context menu (registers under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).
  - **macOS**: Move `AntigravityAssistant.app` to `/Applications` and add it under **System Settings → General → Login Items**.
