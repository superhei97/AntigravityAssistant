# <img src="./icon.png" width="80" style="vertical-align: middle"> Antigravity Assistant

#### Choose Your Language: [简体中文](./README.md) | English

**A native macOS & Windows floating pixel-robot companion, real-time AI model quota watcher (`5h` & `7d`), and multi-session manager built for Google Antigravity.**

- **Native macOS App**: Built with `SwiftUI + AppKit`, living unobtrusively on your desktop and menu bar.
- **Native Windows App**: Built with `WPF + Win32 + UI Automation + winsqlite3.dll`, zero external runtime dependencies (`< 250 KB` standalone `.exe`), living on your desktop and system tray.

Zero Electron overhead and `< 0.1%` idle CPU across both platforms.

## Demo

<table>
  <tr>
    <td align="center">
      <strong>Floating Desktop Mascot</strong><br><br>
      <img src="./images/demo-mascot.png" alt="Floating Desktop Mascot" width="240">
    </td>
    <td align="center">
      <strong>Quota & Sessions Dashboard</strong><br><br>
      <img src="./images/demo-dashboard.png" alt="Quota & Sessions Dashboard" width="380">
    </td>
    <td align="center">
      <strong>Alerts & Settings <a href="./CONFIG.en.md">(Docs)</a></strong><br><br>
      <img src="./images/demo-settings.png" alt="Settings View" width="380">
    </td>
  </tr>
</table>

## System Requirements

![macOS](https://img.shields.io/badge/macOS-14.0%2B-brightgreen?logo=apple&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011%20(x64)-0078D4?logo=windows&logoColor=white)
![Apple Silicon](https://img.shields.io/badge/Apple_Silicon-arm64-blue?logo=apple&logoColor=white)
![Intel / AMD](https://img.shields.io/badge/x86__64-macOS%20%26%20Win64-blue?logo=intel&logoColor=white)
![Swift](https://img.shields.io/badge/Swift-5.9%2B-F05138?logo=swift&logoColor=white)
![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.8_(Built--in)-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green)

## Installation

### Option 1: Prebuilt Release (Recommended)

#### Windows (10 / 11 x64)
1. Download `AntigravityAssistant-windows-x64.zip` from [Releases](https://github.com/superhei97/AntigravityAssistant/releases/latest) (or build from source in seconds below).
2. Extract and launch `AntigravityAssistant.exe` directly — no extra SDKs or installers required.

#### macOS (14.0+)
1. Download `AntigravityAssistant-macos-arm64.zip` from [Releases](https://github.com/superhei97/AntigravityAssistant/releases/latest).
2. Extract and move `AntigravityAssistant.app` into `/Applications`, then launch it.

> [!NOTE]
> Since the macOS bundle is ad-hoc signed, if Gatekeeper blocks the first launch, clear the quarantine attribute once in Terminal:
> ```bash
> xattr -cr /Applications/AntigravityAssistant.app
> open /Applications/AntigravityAssistant.app
> ```

### Option 2: Build from Source

#### Build on Windows (Zero External SDK Required)
Uses the built-in 64-bit `.NET Framework` compiler (`csc.exe`) preinstalled on every Windows 10/11 machine:

```powershell
git clone https://github.com/superhei97/AntigravityAssistant.git
cd AntigravityAssistant
.\scripts\build_windows.ps1
.\dist\AntigravityAssistant.exe
```

#### Build on macOS
Requires Xcode Command Line Tools (`xcode-select --install`):

```bash
git clone https://github.com/superhei97/AntigravityAssistant.git
cd AntigravityAssistant
./scripts/build_app.sh
open ./dist/AntigravityAssistant.app
```

## Key Features

- **16-Bit Pixel Robot Mascot + Menu Bar / System Tray Live Readout**:
  - Hand-crafted pixel-art Antigravity CRT mascot whose antenna orb and facial expressions react to live agent states (`Idle`, `Thinking`, `Running Tool`, `Waiting Confirmation`, `Error`).
  - **Window-Level Click-Through & Non-Activating Panel**: Only clicking the robot icon expands the full dashboard. Surrounding blank areas pass mouse clicks `100%` straight through to underlying windows (`ignoresMouseEvents` on macOS, `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` on Windows) without stealing focus.
  - **Multi-Monitor & HiDPI Dragging**: Drag seamlessly across stacked displays (`PerMonitorV2` DPI-aware on Windows, all Spaces on macOS).
- **Real-Time `5h` & `7d` Quota Tracking**:
  - **Built-in Direct Google Cloud Quota (Windows, Magpie-Compatible Protocol)**: Sign in once via browser (`OAuth 2.0 + PKCE`, stored locally with Windows DPAPI encryption) or paste a `1//` refresh token / JSON export to poll official Google Code Assist `retrieveUserQuotaSummary` quotas directly — zero gateway setup needed.
  - Also automatically queries local [Magpie](https://github.com/yetone/magpie) quota gateway (`127.0.0.1:3425` or CLI) for exact **Gemini** and **Claude & GPT** `5h` / `7d` remaining percentages and reset countdowns, and supports one-click clipboard Usage text calibration.
- **Smart `⚡ Priority` Queue**:
  - Folded by default underneath the desktop mascot to show **only actively running and unread newly-completed tasks**; click the `Priority` header bar to unfold pinned (`📌`) sessions.
  - Unpinned tasks that finish running stay in `Priority` until you click to inspect them, then automatically move to `Today / Yesterday / Weekday` groups.
- **Workspace Project Badges, One-Click Jump & Dual-Side Delete**:
  - Read-only SQLite WAL inspection (`conversation_summaries.db` via native `sqlite3` on macOS and built-in `winsqlite3.dll` on Windows) extracts project names (falling back to `default`), detects user cancellations immediately, jumps directly to any conversation (`AXUIElement` on macOS, `UI Automation` + `Ctrl+K` fallback on Windows), and supports 2-tap synchronized deletion to Trash / Recycle Bin.

## Shortcuts & Interactions

| Target | Action |
| :--- | :--- |
| **Click Pixel Robot Mascot** | Expand / collapse the full quota & sessions dashboard |
| **Click Dashboard Header Blank Area** | Collapse back to the compact floating mascot |
| **Click `⚡ Priority` Header Bar** | Fold (active + unread finished only) / unfold all pinned sessions |
| **Click Any Conversation Row** | Activate `Antigravity` and jump directly to that session |
| **Drag Robot / Quota Pill / Header** | Reposition across monitors and persist coordinates automatically |
| **Global Hotkey `⌥ + A` (macOS) / `Alt + A` (Windows)** | Show / hide the floating desktop assistant anywhere |

## Configuration

See **[📖 Configuration Guide](./CONFIG.en.md)** for Accessibility / UI Automation details, quota synchronization, Lifecycle Hooks, and startup options.

## Credits

- Inspired by [wusimpl/AntigravityQuotaWatcher](https://github.com/wusimpl/AntigravityQuotaWatcher)

## License

[MIT License](./LICENSE)
