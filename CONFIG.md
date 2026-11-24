# 配置与权限说明

[English Version](./CONFIG.en.md)

## 1. 数据读取目录（纯本地只读，零隐私上传）

`Antigravity Assistant` 完全在本地运行，不向任何第三方服务器上传你的对话或配置数据。应用默认只读监听本机 Antigravity 标准目录（macOS 为 `~/.gemini/antigravity`，Windows 为 `%USERPROFILE%\.gemini\antigravity`）：

- `conversation_summaries.db`：以 `SQLITE_OPEN_READONLY` 只读模式（Windows 调用系统原生 `C:\Windows\System32\winsqlite3.dll`，零文件锁占用）解析会话标题、所属项目目录名（`workspace_uris`）及运行/空闲状态。
- `brain/*/.system_generated/logs/transcript.jsonl`：以共享只读方式增量解析最近 7 天会话状态、工具调用进度与 Token 消耗。

本地设置与锚点存储位置：
- **Windows**：`%LOCALAPPDATA%\AgyAssistant\settings.json`
- **macOS**：`NSUserDefaults` (`com.antigravity.usage-assistant`)

## 2. 会话一键定位与同步删除机制

- **Windows（原生免授权）**：
  - 使用系统内置 **Windows UI Automation** 接口自动连接 `Antigravity.exe`，在侧边栏定位 `/c/<conversationId>` 链接并触发跳转；若侧边栏已收起，则自动发送 `Ctrl + K` Unicode 搜索回退定位。
  - 点击二次确认删除时，同步触发侧边栏删除并将本地会话目录安全移入 **Windows 回收站**。
  - *注：若你的 `Antigravity.exe` 以管理员身份运行，请同样以管理员权限运行 `AntigravityAssistant.exe` 以避免 Windows UIPI 跨权限拦截。*
- **macOS（辅助功能权限）**：
  1. 首次点击会话或点击面板内的「开启授权」按钮，系统会打开 **系统设置 → 隐私与安全性 → 辅助功能**。
  2. 找到并开启 **Antigravity 使用助手** 开关即可。
  3. **重编译提示**：如果你从源码重新运行 `./scripts/build_app.sh` 覆盖了应用，macOS TCC 可能会要求在「辅助功能」列表中删掉旧条目后重新勾选一次。

## 3. 配额数据源说明

助手按以下优先级自动获取 `5h` 与 `7d` 剩余配额：

1. **内置 Google 官方实盘配额直连（Windows 推荐，Magpie 同源协议）**：
   - 在展开面板的「配额概览」卡片或「⚙️ 提醒与设置」中点击 **🌐 Google 登录直连实盘配额**，在浏览器完成一次 `OAuth 2.0 + PKCE` 授权后，助手会直接调用 Google Code Assist `retrieveUserQuotaSummary` 接口，实时读取 **Gemini** 与 **Claude & GPT** 的 `5h` 和 `7d` 剩余百分比与刷新时间。
   - 也支持复制 `1//` 开头的 Refresh Token 或 Magpie / CLIProxyAPI 账号 JSON 后点击 **📋 粘贴 Token / Usage** 一键导入。Refresh Token 采用 Windows 系统级 `DPAPI (CurrentUser)` 加密保存于 `%LOCALAPPDATA%\AgyAssistant\oauth_accounts.json`。
2. **Magpie 本地网关云端配额（自动兼容）**：
   - 自动探测本地 [Magpie](https://github.com/yetone/magpie) `http://127.0.0.1:3425/v1/magpie/quotas` 或本机 `magpie` 可执行文件，支持多账号切换与 **云端刷新**。
3. **剪贴板手动校准 + 本地增量推算（免登录兜底）**：
   - 若暂不登录账号，可在官方 Usage 弹窗复制包含 `Weekly limit` / `5h limit` 百分比的文本，点击 **📋 粘贴 Token / Usage** 对齐锚点，后续对话按模型权重递减推算。

## 4. 可选 Lifecycle Hooks 增强

在展开面板右上角点击 **⚙️（提醒与设置）** → **Lifecycle Hooks** 点击「一键开启」，助手会在 `~/.gemini/config/plugins/agy-assistant-hooks`（Windows 为 `%USERPROFILE%\.gemini\config\plugins\agy-assistant-hooks`）生成跨平台轻量本地 Hook 脚本，自动将实时事件写入：
- **Windows**：`%LOCALAPPDATA%\AgyAssistant\hook_state.json`
- **macOS**：`~/Library/Application Support/AgyAssistant/hook_state.json`

从而实现毫秒级工具调用感知与后台任务空闲检测。随时可点击「移除」干净卸载。

## 5. 开机自启与全局快捷键

- **全局快捷键**：
  - **Windows**：`Alt + A` 一键显示 / 隐藏桌面悬浮窗。
  - **macOS**：`⌥ + A` (`Option + A`) 一键显示 / 隐藏桌面悬浮窗。
- **开机自启**：
  - **Windows**：直接在面板 **⚙️ 提醒与设置** 或右键系统托盘菜单中开启 **开机自动启动**（自动写入当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`）。
  - **macOS**：将 `AntigravityAssistant.app` 放入 `/Applications` 后，前往 **系统设置 → 通用 → 登录项与扩展**，点击 `+` 添加即可。
