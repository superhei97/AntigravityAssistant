import AppKit
import Combine
import SwiftUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let settings = AppSettings()
    private lazy var monitor = TranscriptMonitor(settings: settings)
    private let panelController = PanelWindowController()

    private var statusItem: NSStatusItem?
    private var lastRenderedMenuPhase: AgentPhase? = nil
    private var cancellables = Set<AnyCancellable>()

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Run as accessory agent (no cluttered Dock icon, lives on Desktop + Menu Bar)
        NSApp.setActivationPolicy(.accessory)
        if let iconURL = Bundle.main.url(forResource: "AppIcon", withExtension: "icns"),
           let appIcon = NSImage(contentsOf: iconURL) {
            NSApp.applicationIconImage = appIcon
        }
        NotificationManager.shared.prepareIfNeeded()

        monitor.refreshNow()
        let rootView = AssistantRootView(
            monitor: monitor,
            settings: settings,
            panelController: panelController
        )
        panelController.setupPanel(
            rootView: rootView,
            settings: settings,
            initialTotalPriorityCount: monitor.priorityConversations.count,
            initialRunningPriorityCount: monitor.runningPriorityConversations.count
        )

        setupMenuBarItem()
        monitor.start()

        Publishers.CombineLatest(monitor.$quota, monitor.$conversations)
            .receive(on: RunLoop.main)
            .sink { [weak self] quota, _ in
                self?.updateMenuBarTitle(quota: quota)
            }
            .store(in: &cancellables)

        Publishers.CombineLatest3(
            monitor.$conversations,
            monitor.$pinnedConversationIds,
            monitor.$unreadCompletedConversationIds
        )
            .receive(on: RunLoop.main)
            .sink { [weak self] _, _, _ in
                guard let self else { return }
                self.panelController.updateCompactPriorityState(
                    totalCount: self.monitor.priorityConversations.count,
                    runningCount: self.monitor.runningPriorityConversations.count
                )
            }
            .store(in: &cancellables)
    }

    private func setupMenuBarItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = item.button {
            button.image = PixelRobotSprite.makeMenuBarIcon(phase: .idle)
            button.imagePosition = .imageLeading
            button.title = " --%"
            button.font = NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        }

        let menu = NSMenu()
        let toggleFloatItem = NSMenuItem(
            title: "显示 / 隐藏桌面悬浮窗 (⌥A)",
            action: #selector(handleTogglePanel),
            keyEquivalent: ""
        )
        toggleFloatItem.target = self
        menu.addItem(toggleFloatItem)

        let expandItem = NSMenuItem(
            title: "展开详细 Remaining 面板",
            action: #selector(handleExpandPanel),
            keyEquivalent: ""
        )
        expandItem.target = self
        menu.addItem(expandItem)

        let syncClipboardItem = NSMenuItem(
            title: "📋 从剪贴板同步官方 Usage",
            action: #selector(handleSyncPasteboard),
            keyEquivalent: ""
        )
        syncClipboardItem.target = self
        menu.addItem(syncClipboardItem)

        let openAgyItem = NSMenuItem(
            title: "打开 Antigravity 当前会话",
            action: #selector(handleOpenAntigravity),
            keyEquivalent: ""
        )
        openAgyItem.target = self
        menu.addItem(openAgyItem)

        menu.addItem(.separator())

        let resetPosItem = NSMenuItem(
            title: "重置悬浮窗到右上角",
            action: #selector(handleResetPosition),
            keyEquivalent: ""
        )
        resetPosItem.target = self
        menu.addItem(resetPosItem)

        menu.addItem(.separator())

        let quitItem = NSMenuItem(
            title: "退出 Antigravity 使用助手",
            action: #selector(handleQuit),
            keyEquivalent: "q"
        )
        quitItem.target = self
        menu.addItem(quitItem)

        item.menu = menu
        self.statusItem = item
    }

    private func updateMenuBarTitle(quota: QuotaMetrics) {
        guard let button = statusItem?.button else { return }
        let rem5h = String(format: "%.1f%%", quota.fiveHourRemainingPercent)
        let remWk = String(format: "%.1f%%", quota.weeklyRemainingPercent)
        let rawPhase = monitor.primaryConversation?.phase ?? .idle
        let effectivePhase: AgentPhase = (rawPhase.liveDisplayLabel != nil) ? rawPhase : .idle
        if lastRenderedMenuPhase != effectivePhase {
            lastRenderedMenuPhase = effectivePhase
            button.image = PixelRobotSprite.makeMenuBarIcon(phase: effectivePhase)
            button.imagePosition = .imageLeading
        }
        button.title = " \(rem5h)·\(remWk)"
    }

    @objc private func handleSyncPasteboard() {
        monitor.syncQuotaFromPasteboard()
    }

    @objc private func handleTogglePanel() {
        panelController.toggleVisibility()
    }

    @objc private func handleExpandPanel() {
        panelController.showAndExpand()
    }

    @objc private func handleOpenAntigravity() {
        monitor.jumpToPrimaryConversation()
    }

    @objc private func handleResetPosition() {
        if !panelController.isVisible {
            panelController.toggleVisibility()
        }
        panelController.resetPositionToDefault()
    }

    @objc private func handleQuit() {
        NSApplication.shared.terminate(nil)
    }
}

@main
@MainActor
struct AntigravityAssistantApp {
    private static var retainedDelegate: AppDelegate?

    static func main() {
        let args = CommandLine.arguments
        if let exportIdx = args.firstIndex(of: "--export-iconset"), exportIdx + 1 < args.count {
            let outURL = URL(fileURLWithPath: args[exportIdx + 1], isDirectory: true)
            do {
                try PixelRobotSprite.exportAppIconset(to: outURL)
                print("Exported iconset to \(outURL.path)")
            } catch {
                fputs("Failed to export iconset: \(error)\n", stderr)
                exit(1)
            }
            return
        }

        if let demoIdx = args.firstIndex(of: "--export-demo-images"), demoIdx + 1 < args.count {
            let app = NSApplication.shared
            app.setActivationPolicy(.prohibited)

            let outDir = URL(fileURLWithPath: args[demoIdx + 1], isDirectory: true)
            try? FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)

            let settings = AppSettings()
            let savedOpacity = settings.pillOpacity
            settings.pillOpacity = 1.0
            defer { settings.pillOpacity = savedOpacity }
            let monitor = TranscriptMonitor(settings: settings)
            monitor.loadSyntheticDemoState()
            let panelCtrl = PanelWindowController()

            func saveViewPNG<V: View>(_ view: V, width: CGFloat, height: CGFloat, filename: String) {
                let pad: CGFloat = 16
                let totalW = width + pad * 2
                let totalH = height + pad * 2
                let wrapped = view
                    .frame(width: width, height: height)
                    .padding(pad)
                    .background(Color(red: 0.945, green: 0.955, blue: 0.975))
                    .environment(\.colorScheme, .light)

                let rect = NSRect(x: -10000, y: -10000, width: totalW, height: totalH)
                let window = NSWindow(
                    contentRect: rect,
                    styleMask: [.borderless],
                    backing: .buffered,
                    defer: false
                )
                window.appearance = NSAppearance(named: .aqua)
                window.backgroundColor = NSColor(red: 0.945, green: 0.955, blue: 0.975, alpha: 1.0)
                window.isReleasedWhenClosed = false

                let hosting = NSHostingView(rootView: wrapped)
                hosting.frame = NSRect(x: 0, y: 0, width: totalW, height: totalH)
                window.contentView = hosting
                window.orderBack(nil)

                hosting.needsLayout = true
                hosting.layoutSubtreeIfNeeded()
                RunLoop.current.run(until: Date().addingTimeInterval(0.20))
                hosting.layoutSubtreeIfNeeded()

                let scale: CGFloat = 2.0
                if let rep = NSBitmapImageRep(
                    bitmapDataPlanes: nil,
                    pixelsWide: Int(totalW * scale),
                    pixelsHigh: Int(totalH * scale),
                    bitsPerSample: 8,
                    samplesPerPixel: 4,
                    hasAlpha: true,
                    isPlanar: false,
                    colorSpaceName: .deviceRGB,
                    bytesPerRow: 0,
                    bitsPerPixel: 0
                ) {
                    rep.size = NSSize(width: totalW, height: totalH)
                    hosting.cacheDisplay(in: hosting.bounds, to: rep)
                    if let png = rep.representation(using: .png, properties: [:]) {
                        try? png.write(to: outDir.appendingPathComponent(filename))
                    }
                }
                window.orderOut(nil)
            }

            // 1. Compact Floating Mascot + Quota Capsule + Folded Priority Tier
            saveViewPNG(
                CompactFloatingPill(monitor: monitor, settings: settings, panelController: panelCtrl),
                width: 216,
                height: 240,
                filename: "demo-mascot.png"
            )

            // 2. Expanded Dashboard with Quota Overview + Grouped Sessions
            panelCtrl.isQuotaExpanded = true
            panelCtrl.showSettings = false
            saveViewPNG(
                ExpandedAssistantHUD(monitor: monitor, settings: settings, panelController: panelCtrl),
                width: PanelWindowController.expandedSize.width,
                height: PanelWindowController.expandedSize.height,
                filename: "demo-dashboard.png"
            )

            // 3. Expanded Settings Panel
            panelCtrl.showSettings = true
            saveViewPNG(
                ExpandedAssistantHUD(monitor: monitor, settings: settings, panelController: panelCtrl),
                width: PanelWindowController.expandedSize.width,
                height: PanelWindowController.expandedSize.height,
                filename: "demo-settings.png"
            )

            print("Exported synthetic demo images to \(outDir.path)")
            return
        }

        if let syncIdx = args.firstIndex(of: "--sync-text"), syncIdx + 1 < args.count {
            let text = args[syncIdx + 1]
            let settings = AppSettings()
            let ok = settings.syncFromOfficialUsageText(text)
            print("Sync text result: \(ok)")
            return
        }

        if let jumpIdx = args.firstIndex(of: "--test-jump"), jumpIdx + 1 < args.count {
            let needle = args[jumpIdx + 1]
            let settings = AppSettings()
            let monitor = TranscriptMonitor(settings: settings)
            monitor.refreshNow()
            guard let target = monitor.conversations.first(where: {
                $0.id.contains(needle) || $0.title.localizedCaseInsensitiveContains(needle)
            }) else {
                print("No matching conversation for: \(needle)")
                exit(1)
            }
            let t0 = Date()
            let sem = DispatchSemaphore(value: 0)
            Task { @MainActor in
                let outcome = await ConversationNavigator.jump(to: target)
                print(String(format: "Jump to '%@' [%@] -> %@ in %.3fs", target.title, target.projectName, String(describing: outcome), Date().timeIntervalSince(t0)))
                sem.signal()
            }
            while sem.wait(timeout: .now() + 0.02) == .timedOut {
                RunLoop.current.run(until: Date().addingTimeInterval(0.02))
            }
            return
        }

        if args.contains("--self-test") {
            let settings = AppSettings()
            let monitor = TranscriptMonitor(settings: settings)
            monitor.refreshNow()
            let sem = DispatchSemaphore(value: 0)
            Task { @MainActor in
                await monitor.pollMagpieQuota(forceCloudRefresh: false)
                sem.signal()
            }
            while sem.wait(timeout: .now() + 0.02) == .timedOut {
                RunLoop.current.run(until: Date().addingTimeInterval(0.02))
            }
            if let liveState = ConversationNavigator.pollLiveUIState() {
                print("Live Sidebar Items: \(liveState.sidebarItems.count) | Open Conv: \(liveState.openConversationId ?? "none")")
            }
            monitor.start()
            RunLoop.current.run(until: Date().addingTimeInterval(0.25))
            monitor.stop()
            for sec in monitor.groupedSessionSections {
                let rows = sec.conversations.map { conv in
                    if let live = conv.phase.liveDisplayLabel {
                        return "\(conv.title) {\(conv.projectName)} [\(live)]"
                    }
                    return "\(conv.title) {\(conv.projectName)}"
                }.joined(separator: " | ")
                print("Group [\(sec.title)] (\(sec.conversations.count)): \(rows)")
            }
            let q = monitor.quota
            let now = Date()
            let r5Str = q.fiveHourResetDate.map { "\(TimeFormatters.formatDuration($0.timeIntervalSince(now))) (\(TimeFormatters.formatUTC8($0)))" } ?? "idle"
            let rwStr = q.weeklyResetDate.map { "\(TimeFormatters.formatDuration($0.timeIntervalSince(now))) (\(TimeFormatters.formatDateTimeUTC8($0)))" } ?? "idle"
            print("Self-Test v3 (Magpie Live) | Cloud Accounts: \(monitor.cloudAccounts.count) | Active: \(monitor.activeCloudAccount?.userEmail ?? "none") (\(monitor.activeCloudAccount?.planName ?? "-"))")
            print(String(format: "Gemini 5h Remaining: %.1f%% | Refresh: %@", q.fiveHourRemainingPercent, r5Str))
            print(String(format: "Gemini 7d Remaining: %.1f%% | Refresh: %@", q.weeklyRemainingPercent, rwStr))
            if let cPool = monitor.activeCloudAccount?.claudeGptPool {
                print(String(format: "Claude & GPT: 5h %.1f%% | 7d %.1f%%", cPool.remaining5h, cPool.remaining7d))
            }
            return
        }

        let app = NSApplication.shared
        let delegate = AppDelegate()
        retainedDelegate = delegate
        app.delegate = delegate
        app.run()
    }
}
