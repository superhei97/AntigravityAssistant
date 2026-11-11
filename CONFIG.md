# 配置与权限说明

[English Version](./CONFIG.en.md)

## 1. 数据读取目录（纯本地只读，零隐私上传）

`Antigravity Assistant` 完全在本地运行，不向任何第三方服务器上传你的对话或配置数据。应用默认只读监听本机 Antigravity 标准目录：

- `~/.gemini/antigravity/conversation_summaries.db`：以 `SQLITE_OPEN_READONLY` 只读模式解析会话标题、所属项目目录名（`workspace_uris`）及运行/空闲状态。
- `~/.gemini/antigravity/brain/*/.system_generated/logs/transcript.jsonl`：增量解析最近 7 天会话状态、工具调用进度与 Token 消耗。

## 2. 辅助功能权限（用于一键定位与同步删除会话）

当你点击列表中的某个会话时，助手会通过 macOS 原生 `AXUIElement` 辅助功能接口在 `Antigravity.app` 侧边栏精准定位 `/c/<conversationId>`（侧边栏收起时自动回退快捷键搜索）。

1. 首次点击会话或点击面板内的「开启授权」按钮，系统会打开 **系统设置 → 隐私与安全性 → 辅助功能**。
2. 找到并开启 **Antigravity 使用助手** 开关即可。
3. **重编译提示**：如果你从源码重新运行 `./scripts/build_app.sh` 覆盖了应用，macOS TCC 可能会要求在「辅助功能」列表中删掉旧条目后重新勾选一次。

## 3. 配额数据源说明

助手按以下优先级自动获取 `5h` 与 `7d` 剩余配额：

1. **Magpie 实时云端配额（推荐）**：
   - 自动探测本地 `http://127.0.0.1:3425/v1/magpie/quotas` 或可执行文件（`/Applications/Magpie.app/Contents/MacOS/magpie`、`~/.local/bin/magpie`、`/opt/homebrew/bin/magpie`）。
   - 支持多账号切换，并可在展开的「配额概览」卡片中点击 **云端刷新** 强制拉取最新配额。
2. **剪贴板手动校准 + 本地增量推算（免依赖兜底）**：
   - 若未安装 Magpie，可在官方 Usage 页面复制包含百分比的文本，点击菜单栏图标 → **📋 从剪贴板同步官方 Usage** 对齐锚点，后续对话将按模型权重自动递减推算。

## 4. 可选 Lifecycle Hooks 增强

在展开面板右上角点击 **⚙️（提醒与设置）** → **Lifecycle Hooks 毫秒级直连** 点击「一键开启」，助手会在 `~/.gemini/config/plugins/agy-assistant-hooks` 生成官方规范的轻量本地 Hook 脚本，将状态更新写入 `~/Library/Application Support/AgyAssistant/hook_state.json`，实现毫秒级工具调用感知与后台任务空闲检测。随时可点击「移除」卸载。

## 5. 开机自启与全局快捷键

- **全局快捷键**：`⌥ + A` (`Option + A`) 一键显示 / 隐藏桌面悬浮窗。
- **开机自启**：将 `AntigravityAssistant.app` 放入 `/Applications` 后，前往 **系统设置 → 通用 → 登录项与扩展**，点击 `+` 添加即可。
