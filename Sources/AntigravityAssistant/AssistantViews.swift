import AppKit
import SwiftUI

// MARK: - Unified Pure-White Light Theme Palette

private enum LightPalette {
    static let panelSurfaceTop = Color.white.opacity(0.985)
    static let panelSurfaceBottom = Color(red: 0.985, green: 0.989, blue: 0.996).opacity(0.985)
    static let panelBorder = Color.black.opacity(0.10)

    static let cardFill = Color(red: 0.962, green: 0.969, blue: 0.982)
    static let cardBorder = Color.black.opacity(0.07)

    static let rowIdleFill = Color(red: 0.968, green: 0.974, blue: 0.985)
    static let rowIdleBorder = Color.black.opacity(0.065)
    static let rowActiveFill = Color(red: 0.932, green: 0.962, blue: 1.0)
    static let rowDoneFill = Color(red: 0.926, green: 0.982, blue: 0.948)

    static let inkPrimary = Color(red: 0.09, green: 0.12, blue: 0.18)
    static let inkSecondary = Color(red: 0.28, green: 0.33, blue: 0.42)
    static let inkMuted = Color(red: 0.45, green: 0.50, blue: 0.58)
    static let divider = Color.black.opacity(0.08)

    static let brandBlue = Color(red: 0.09, green: 0.45, blue: 0.91)
    static let pinBlue = Color(red: 0.07, green: 0.49, blue: 0.89)
    static let doneGreen = Color(red: 0.09, green: 0.65, blue: 0.35)
    static let iconCircleFill = Color.black.opacity(0.055)
}

// MARK: - Animated GPT-Style Orb

struct AgentOrbView: View {
    let phase: AgentPhase
    var size: CGFloat = 26

    var body: some View {
        TimelineView(.animation(minimumInterval: phase.isWorking ? 0.04 : 0.12)) { context in
            let t = context.date.timeIntervalSinceReferenceDate
            let wave = (sin(t * (phase.isWorking ? 3.2 : 1.6)) + 1.0) * 0.5
            let haloScale = 1.05 + 0.33 * wave
            let angle = (t * 190.0).truncatingRemainder(dividingBy: 360.0)

            ZStack {
                Circle()
                    .fill(phase.accentColor.opacity(phase.isWorking ? 0.30 : 0.14))
                    .frame(width: size * haloScale, height: size * haloScale)
                    .blur(radius: phase.isWorking ? 5 : 3)

                if phase.isWorking {
                    Circle()
                        .strokeBorder(
                            AngularGradient(
                                gradient: Gradient(colors: [
                                    phase.accentColor.opacity(0.1),
                                    phase.accentColor,
                                    Color.white.opacity(0.9),
                                    phase.accentColor.opacity(0.2)
                                ]),
                                center: .center
                            ),
                            lineWidth: 2.2
                        )
                        .frame(width: size, height: size)
                        .rotationEffect(.degrees(angle))
                }

                Circle()
                    .fill(
                        RadialGradient(
                            colors: [
                                Color.white.opacity(0.92),
                                phase.accentColor,
                                phase.accentColor.opacity(0.75)
                            ],
                            center: .topLeading,
                            startRadius: 1,
                            endRadius: size * 0.65
                        )
                    )
                    .frame(width: size * 0.72, height: size * 0.72)
                    .shadow(color: phase.accentColor.opacity(0.45), radius: 3, x: 0, y: 0)
            }
            .frame(width: size * 1.3, height: size * 1.3)
        }
    }
}

// MARK: - Mini Circular Progress Gauge

struct MiniRingGauge: View {
    let percent: Double
    let color: Color
    var size: CGFloat = 16

    var body: some View {
        ZStack {
            Circle()
                .stroke(Color.black.opacity(0.10), lineWidth: 2.4)
            Circle()
                .trim(from: 0, to: CGFloat(max(0.02, min(1.0, percent / 100.0))))
                .stroke(color, style: StrokeStyle(lineWidth: 2.4, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
        .frame(width: size, height: size)
    }
}

// MARK: - Root Container View

struct AssistantRootView: View {
    @ObservedObject var monitor: TranscriptMonitor
    @ObservedObject var settings: AppSettings
    @ObservedObject var panelController: PanelWindowController

    var body: some View {
        ZStack {
            if panelController.isExpanded {
                    ExpandedAssistantHUD(
                        monitor: monitor,
                        settings: settings,
                        panelController: panelController
                    )
                    .opacity(settings.pillOpacity)
                    .transition(.opacity.combined(with: .scale(scale: 0.96, anchor: .topTrailing)))
            } else {
                CompactFloatingPill(
                    monitor: monitor,
                    settings: settings,
                    panelController: panelController
                )
                .transition(.opacity)
            }

            if panelController.isExpanded, let toast = monitor.toastMessage {
                VStack {
                    Spacer()
                    Text(toast)
                        .font(.system(size: 11.5, weight: .medium))
                        .foregroundColor(.white)
                        .lineLimit(1)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .background(
                            Capsule()
                                .fill(Color(red: 0.12, green: 0.15, blue: 0.22).opacity(0.90))
                        )
                        .shadow(color: Color.black.opacity(0.16), radius: 6, x: 0, y: 2)
                        .padding(.bottom, 14)
                }
                .transition(.move(edge: .bottom).combined(with: .opacity))
                .allowsHitTesting(false)
            }
        }
        .environment(\.colorScheme, .light)
        .animation(.spring(response: 0.25, dampingFraction: 0.85), value: panelController.isExpanded)
        .animation(.easeInOut(duration: 0.16), value: panelController.isCompactPriorityExpanded)
        .animation(.easeInOut(duration: 0.18), value: monitor.unreadCompletedConversationIds)
        .animation(.easeInOut(duration: 0.18), value: panelController.isQuotaExpanded)
        .animation(.easeInOut(duration: 0.18), value: panelController.showSettings)
        .animation(.easeInOut(duration: 0.16), value: panelController.confirmingDeleteConvId)
        .animation(.easeInOut(duration: 0.2), value: monitor.toastMessage)
    }
}

// MARK: - Compact Desktop Mascot (Pixel Art Antigravity Robot + Light Quota Capsule + Default-Folded Light Priority Tier)

struct CompactFloatingPill: View {
    @ObservedObject var monitor: TranscriptMonitor
    @ObservedObject var settings: AppSettings
    @ObservedObject var panelController: PanelWindowController

    private var activeConv: ConversationSnapshot? {
        monitor.primaryConversation
    }

    private var orbPhase: AgentPhase {
        guard let p = activeConv?.phase, p.liveDisplayLabel != nil else {
            return .idle
        }
        return p
    }

    private var rem5h: Double {
        monitor.quota.fiveHourRemainingPercent
    }

    private var remWeekly: Double {
        monitor.quota.weeklyRemainingPercent
    }

    private var allPriorityItems: [ConversationSnapshot] {
        monitor.priorityConversations
    }

    private var runningPriorityItems: [ConversationSnapshot] {
        monitor.runningPriorityConversations
    }

    private var displayedPriorityItems: [ConversationSnapshot] {
        panelController.isCompactPriorityExpanded ? allPriorityItems : runningPriorityItems
    }

    private var compactActiveTag: String? {
        if monitor.workingConversationCount > 1 {
            return "\(monitor.workingConversationCount)并行"
        }
        switch orbPhase {
        case .thinking:
            return "思考中"
        case .runningTool:
            return "执行中"
        case .waitingUser:
            return "待确认"
        case .error:
            return "异常"
        default:
            return nil
        }
    }

    var body: some View {
        VStack(spacing: 6) {
            // 1. Mascot + Light 5h/7d Quota Capsule
            // Strictly ONLY clicking the Robot Icon expands full details; blank areas never expand details.
            VStack(spacing: 5) {
                ZStack(alignment: .topTrailing) {
                    PixelRobotMascotView(phase: orbPhase, height: 74, animateFloat: true)
                        .opacity(settings.pillOpacity)
                        .shadow(color: Color.black.opacity(0.24), radius: 5, x: 0, y: 2.5)
                        .contentShape(Rectangle())
                        .overlay(
                            WindowDragArea(
                                onTap: { panelController.toggleExpanded() },
                                onMove: { dx, dy in panelController.moveBy(deltaX: dx, deltaY: dy) },
                                onEndDrag: { panelController.finishDrag() }
                            )
                        )
                        .contextMenu { compactContextMenuItems }
                        .help("点击机器人展开详情面板 · 按住可跨屏幕/桌面拖动")

                    if let tag = compactActiveTag {
                        Text(tag)
                            .font(.system(size: 9, weight: .bold, design: .rounded))
                            .foregroundColor(.white)
                            .padding(.horizontal, 5.5)
                            .padding(.vertical, 2)
                            .background(
                                Capsule()
                                    .fill(orbPhase.accentColor)
                                    .overlay(Capsule().stroke(Color.white.opacity(0.55), lineWidth: 0.7))
                            )
                            .shadow(color: Color.black.opacity(0.20), radius: 3, x: 0, y: 1.5)
                            .offset(x: 20, y: 0)
                            .allowsHitTesting(false)
                    }
                }
                .frame(height: 80)

                // Pure-White Mini Quota Capsule directly beneath the Pixel Robot (5h | 7d)
                ZStack {
                    Capsule()
                        .fill(
                            LinearGradient(
                                colors: [LightPalette.panelSurfaceTop, LightPalette.panelSurfaceBottom],
                                startPoint: .top,
                                endPoint: .bottom
                            )
                        )

                    Capsule()
                        .strokeBorder(LightPalette.panelBorder, lineWidth: 0.9)

                    HStack(spacing: 0) {
                        HStack(spacing: 3.5) {
                            Circle()
                                .fill(QuotaMetrics.statusColor(for: rem5h))
                                .frame(width: 5.5, height: 5.5)
                            Text(String(format: "5h %.0f%%", rem5h))
                                .font(.system(size: 9.8, weight: .bold, design: .monospaced))
                                .foregroundColor(LightPalette.inkPrimary)
                        }
                        .frame(maxWidth: .infinity)

                        Rectangle()
                            .fill(Color.black.opacity(0.12))
                            .frame(width: 1, height: 12)

                        HStack(spacing: 3.5) {
                            Circle()
                                .fill(QuotaMetrics.statusColor(for: remWeekly))
                                .frame(width: 5.5, height: 5.5)
                            Text(String(format: "7d %.0f%%", remWeekly))
                                .font(.system(size: 9.8, weight: .bold, design: .monospaced))
                                .foregroundColor(LightPalette.inkPrimary)
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .padding(.horizontal, 6)
                }
                .frame(width: 136, height: 29)
                .opacity(settings.pillOpacity)
                .shadow(color: Color.black.opacity(0.14), radius: 5, x: 0, y: 2)
                .contentShape(Capsule())
                .overlay(
                    WindowDragArea(
                        onTap: nil,
                        onMove: { dx, dy in panelController.moveBy(deltaX: dx, deltaY: dy) },
                        onEndDrag: { panelController.finishDrag() }
                    )
                )
                .contextMenu { compactContextMenuItems }
                .help("按住可跨屏幕/桌面拖动（点击上方机器人图标展开详情）")
            }

            // 2. Priority Conversations Tier (`优先事项`) — Default Folded (shows running + unread-completed sessions)
            if !allPriorityItems.isEmpty {
                compactPriorityTierCard
                    .opacity(settings.pillOpacity)
            }
        }
        .padding(.top, 4)
        .frame(width: PanelWindowController.compactWidth)
        .frame(maxHeight: .infinity, alignment: .top)
    }

    @ViewBuilder
    private var compactContextMenuItems: some View {
        Button("展开会话与配额面板") {
            panelController.showAndExpand()
        }
        Button("打开当前 Antigravity 会话") {
            monitor.jumpToPrimaryConversation()
        }
        Button("立即刷新配额") {
            monitor.refreshNow()
            Task { await monitor.pollMagpieQuota(forceCloudRefresh: false) }
        }
        Divider()
        Button("重置挂件到当前屏幕右上角") {
            panelController.resetPositionToDefault()
        }
    }

    private var compactPriorityCountText: String {
        if !panelController.isCompactPriorityExpanded && runningPriorityItems.count != allPriorityItems.count {
            return "\(runningPriorityItems.count)/\(allPriorityItems.count)"
        }
        return "\(allPriorityItems.count)"
    }

    private var compactPriorityTierCard: some View {
        let items = displayedPriorityItems

        return ZStack {
            RoundedRectangle(cornerRadius: 13, style: .continuous)
                .fill(
                    LinearGradient(
                        colors: [LightPalette.panelSurfaceTop, LightPalette.panelSurfaceBottom],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )

            RoundedRectangle(cornerRadius: 13, style: .continuous)
                .strokeBorder(LightPalette.panelBorder, lineWidth: 0.9)

            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 4) {
                    Image(systemName: "bolt.fill")
                        .font(.system(size: 8.5, weight: .bold))
                        .foregroundColor(LightPalette.brandBlue)
                    Text("优先事项")
                        .font(.system(size: 9.8, weight: .bold))
                        .foregroundColor(LightPalette.brandBlue)
                    Text(compactPriorityCountText)
                        .font(.system(size: 9, weight: .semibold, design: .monospaced))
                        .foregroundColor(LightPalette.inkMuted)
                    Spacer()
                    Image(systemName: panelController.isCompactPriorityExpanded ? "chevron.down" : "chevron.right")
                        .font(.system(size: 8.5, weight: .bold))
                        .foregroundColor(LightPalette.inkMuted)
                }
                .padding(.horizontal, 3)
                .frame(height: 18)
                .contentShape(Rectangle())
                .overlay(
                    WindowDragArea(
                        onTap: { panelController.toggleCompactPriorityExpanded() },
                        onMove: { dx, dy in panelController.moveBy(deltaX: dx, deltaY: dy) },
                        onEndDrag: { panelController.finishDrag() }
                    )
                )
                .help(panelController.isCompactPriorityExpanded ? "点击折叠（仅展示运行中与待查看项目）" : "点击展开查看全部优先事项")

                if !items.isEmpty {
                    if items.count > 4 {
                        ScrollView(.vertical, showsIndicators: false) {
                            VStack(spacing: 4) {
                                ForEach(items) { conv in
                                    compactPriorityRow(conv: conv)
                                }
                            }
                        }
                        .frame(height: 4 * 27 + 3 * 4)
                    } else {
                        VStack(spacing: 4) {
                            ForEach(items) { conv in
                                compactPriorityRow(conv: conv)
                            }
                        }
                    }
                }
            }
            .padding(.horizontal, 7)
            .padding(.vertical, 7)
        }
        .frame(width: 196)
        .shadow(color: Color.black.opacity(0.15), radius: 6, x: 0, y: 2.5)
    }

    private func compactPriorityRow(conv: ConversationSnapshot) -> some View {
        let liveLabel = conv.phase.liveDisplayLabel
        let isUnreadDone = monitor.isUnreadCompleted(conv.id)
        let rowFill: Color = liveLabel != nil
            ? LightPalette.rowActiveFill
            : (isUnreadDone ? LightPalette.rowDoneFill : LightPalette.rowIdleFill)
        let rowStroke: Color = liveLabel != nil
            ? conv.phase.accentColor.opacity(0.30)
            : (isUnreadDone ? LightPalette.doneGreen.opacity(0.34) : LightPalette.rowIdleBorder)
        let helpStr: String = isUnreadDone
            ? "刚完成（点击查看并移出优先事项）：\(conv.title) (\(conv.projectName))"
            : "点击在 Antigravity 中打开：\(conv.title) (\(conv.projectName))"

        return Button {
            monitor.jumpToConversation(conv)
        } label: {
            HStack(spacing: 6) {
                if liveLabel != nil {
                    Circle()
                        .fill(conv.phase.accentColor)
                        .frame(width: 6, height: 6)
                } else if isUnreadDone {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.system(size: 8.5, weight: .bold))
                        .foregroundColor(LightPalette.doneGreen)
                } else {
                    Image(systemName: "pin.fill")
                        .font(.system(size: 8, weight: .semibold))
                        .foregroundColor(LightPalette.pinBlue)
                }

                Text(conv.title)
                    .font(.system(size: 10.5, weight: .medium))
                    .foregroundColor(LightPalette.inkPrimary)
                    .lineLimit(1)

                Spacer(minLength: 2)

                if liveLabel != nil {
                    if conv.phase.isWorking, let started = conv.activeTurnStartedAt {
                        let elapsed = max(0, monitor.nowTick.timeIntervalSince(started))
                        Text(TimeFormatters.formatDuration(elapsed))
                            .font(.system(size: 9, weight: .semibold, design: .monospaced))
                            .foregroundColor(conv.phase.accentColor)
                    } else if case .waitingUser = conv.phase {
                        Text("待确认")
                            .font(.system(size: 9, weight: .semibold))
                            .foregroundColor(conv.phase.accentColor)
                    } else if case .error = conv.phase {
                        Text("异常")
                            .font(.system(size: 9, weight: .semibold))
                            .foregroundColor(conv.phase.accentColor)
                    }
                }
            }
            .padding(.horizontal, 7.5)
            .frame(height: 27)
            .background(
                RoundedRectangle(cornerRadius: 7.5, style: .continuous)
                    .fill(rowFill)
                    .overlay(
                        RoundedRectangle(cornerRadius: 7.5, style: .continuous)
                            .stroke(rowStroke, lineWidth: 0.75)
                    )
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(helpStr)
    }
}

// MARK: - Expanded HUD Card (Pure-White Light Surface)

struct ExpandedAssistantHUD: View {
    @ObservedObject var monitor: TranscriptMonitor
    @ObservedObject var settings: AppSettings
    @ObservedObject var panelController: PanelWindowController

    private var activeConv: ConversationSnapshot? {
        monitor.primaryConversation
    }

    private var orbPhase: AgentPhase {
        guard let p = activeConv?.phase, p.liveDisplayLabel != nil else {
            return .idle
        }
        return p
    }

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 22, style: .continuous)
                .fill(
                    LinearGradient(
                        colors: [LightPalette.panelSurfaceTop, LightPalette.panelSurfaceBottom],
                        startPoint: .top,
                        endPoint: .bottom
                    )
                )

            RoundedRectangle(cornerRadius: 22, style: .continuous)
                .strokeBorder(LightPalette.panelBorder, lineWidth: 1.0)

            VStack(spacing: 10) {
                headerBar

                if panelController.showSettings {
                    settingsContent
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    mainDashboardContent
                        .frame(maxWidth: .infinity, maxHeight: .infinity)
                }
            }
            .padding(14)
        }
        .frame(width: PanelWindowController.expandedSize.width, height: PanelWindowController.expandedSize.height)
    }

    // MARK: - Top Header Bar (with Pin, Refresh, Settings Gear, Collapse)

    private var headerBar: some View {
        HStack(spacing: 7) {
            HStack(spacing: 8) {
                PixelRobotMascotView(phase: orbPhase, height: 37, animateFloat: false)
                VStack(alignment: .leading, spacing: 1) {
                    HStack(spacing: 5) {
                        Text("Antigravity 使用助手")
                            .font(.system(size: 13, weight: .bold, design: .rounded))
                            .foregroundColor(LightPalette.inkPrimary)
                        Text("⌥A")
                            .font(.system(size: 9.5, weight: .semibold, design: .monospaced))
                            .foregroundColor(LightPalette.inkSecondary)
                            .padding(.horizontal, 4.5)
                            .padding(.vertical, 1)
                            .background(RoundedRectangle(cornerRadius: 4).fill(Color.black.opacity(0.055)))
                    }
                    Text("更新于 \(TimeFormatters.formatUTC8(monitor.lastRefreshDate, includeSeconds: true))")
                        .font(.system(size: 9.5))
                        .foregroundColor(LightPalette.inkMuted)
                }
                Spacer(minLength: 4)
            }
            .contentShape(Rectangle())
            .overlay(
                WindowDragArea(
                    onTap: { panelController.setExpanded(false) },
                    onMove: { dx, dy in panelController.moveBy(deltaX: dx, deltaY: dy) },
                    onEndDrag: { panelController.finishDrag() }
                )
            )
            .help("点击收起为桌面小机器人 · 按住可跨屏幕拖动")

            // 1. Pin (Always on Top)
            Button {
                settings.alwaysOnTop.toggle()
                panelController.updateAlwaysOnTop(settings.alwaysOnTop)
            } label: {
                Image(systemName: settings.alwaysOnTop ? "pin.fill" : "pin.slash")
                    .font(.system(size: 11.5, weight: .semibold))
                    .foregroundColor(settings.alwaysOnTop ? LightPalette.brandBlue : LightPalette.inkMuted)
                    .frame(width: 24, height: 24)
                    .background(
                        Circle().fill(
                            settings.alwaysOnTop
                                ? LightPalette.brandBlue.opacity(0.12)
                                : LightPalette.iconCircleFill
                        )
                    )
            }
            .buttonStyle(.plain)
            .help(settings.alwaysOnTop ? "已置顶悬浮（点击取消置顶）" : "点击开启置顶悬浮")

            // 2. Refresh Status & Quota
            Button {
                monitor.refreshNow()
                Task { await monitor.pollMagpieQuota(forceCloudRefresh: false) }
                monitor.showToast("已刷新实时状态与配额")
            } label: {
                Image(systemName: "arrow.clockwise")
                    .font(.system(size: 11.5, weight: .semibold))
                    .foregroundColor(LightPalette.inkSecondary)
                    .frame(width: 24, height: 24)
                    .background(Circle().fill(LightPalette.iconCircleFill))
            }
            .buttonStyle(.plain)
            .help("立即刷新")

            // 3. Settings & Alerts Button
            Button {
                panelController.showSettings.toggle()
            } label: {
                Image(systemName: "gearshape.fill")
                    .font(.system(size: 11.5, weight: .semibold))
                    .foregroundColor(panelController.showSettings ? LightPalette.brandBlue : LightPalette.inkSecondary)
                    .frame(width: 24, height: 24)
                    .background(
                        Circle().fill(
                            panelController.showSettings
                                ? LightPalette.brandBlue.opacity(0.14)
                                : LightPalette.iconCircleFill
                        )
                    )
            }
            .buttonStyle(.plain)
            .help(panelController.showSettings ? "返回会话与配额面板" : "提醒与设置")

            // 4. Collapse to Floating Mascot
            Button {
                panelController.setExpanded(false)
            } label: {
                Image(systemName: "chevron.up.circle.fill")
                    .font(.system(size: 16))
                    .foregroundColor(LightPalette.inkSecondary.opacity(0.85))
            }
            .buttonStyle(.plain)
            .help("收起为桌面小机器人")
        }
    }

    // MARK: - Unified Main Dashboard (Collapsible Quota Overview + Grouped Sessions List)

    private var mainDashboardContent: some View {
        VStack(alignment: .leading, spacing: 9) {
            collapsibleQuotaSection
            sessionsListSection
        }
    }

    // MARK: 1. Collapsible Quota Overview Card (Default Collapsed, Click to Expand)

    @ViewBuilder
    private var collapsibleQuotaSection: some View {
        if let cloudAcct = monitor.activeCloudAccount {
            collapsibleMagpieQuotaCard(acct: cloudAcct)
        } else {
            collapsibleFallbackQuotaCard
        }
    }

    private func collapsibleMagpieQuotaCard(acct: CloudAccountQuota) -> some View {
        let primaryPool = monitor.activePoolForPrimarySession ?? acct.geminiPool ?? acct.claudeGptPool
        let rem5h = primaryPool?.remaining5h ?? monitor.quota.fiveHourRemainingPercent
        let rem7d = primaryPool?.remaining7d ?? monitor.quota.weeklyRemainingPercent
        let poolLabel = primaryPool?.poolName ?? "Gemini"
        let c5 = QuotaMetrics.statusColor(for: rem5h)
        let c7 = QuotaMetrics.statusColor(for: rem7d)

        return VStack(alignment: .leading, spacing: 8) {
            // Summary Row (Always Visible — Click to Fold / Unfold)
            HStack(spacing: 7) {
                Button {
                    panelController.isQuotaExpanded.toggle()
                } label: {
                    HStack(spacing: 7) {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 11.5))
                            .foregroundColor(LightPalette.doneGreen)

                        Text("配额概览 · \(poolLabel)")
                            .font(.system(size: 11.5, weight: .semibold))
                            .foregroundColor(LightPalette.inkPrimary)
                            .lineLimit(1)

                        Spacer(minLength: 2)

                        HStack(spacing: 4) {
                            MiniRingGauge(percent: rem5h, color: c5, size: 12)
                            Text(String(format: "5h %.1f%%", rem5h))
                                .font(.system(size: 10.5, weight: .bold, design: .monospaced))
                                .foregroundColor(c5)
                        }

                        Text("·")
                            .foregroundColor(LightPalette.inkMuted.opacity(0.6))

                        HStack(spacing: 4) {
                            MiniRingGauge(percent: rem7d, color: c7, size: 12)
                            Text(String(format: "7d %.1f%%", rem7d))
                                .font(.system(size: 10.5, weight: .bold, design: .monospaced))
                                .foregroundColor(c7)
                        }

                        Image(systemName: panelController.isQuotaExpanded ? "chevron.up" : "chevron.down")
                            .font(.system(size: 10, weight: .bold))
                            .foregroundColor(LightPalette.inkMuted)
                            .padding(.leading, 2)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help(panelController.isQuotaExpanded ? "点击收起配额详情" : "点击展开查看完整账号与各模型池配额明细")
            }

            // Expanded Detail Drawer
            if panelController.isQuotaExpanded {
                Divider().background(LightPalette.divider)

                HStack(spacing: 6) {
                    if monitor.cloudAccounts.count > 1 {
                        Menu {
                            ForEach(monitor.cloudAccounts) { item in
                                Button("\(item.userEmail) (\(item.planName))") {
                                    monitor.selectedAccountEmail = item.userEmail
                                }
                            }
                        } label: {
                            Text(acct.userEmail)
                                .font(.system(size: 11, weight: .semibold))
                                .foregroundColor(LightPalette.inkPrimary)
                                .lineLimit(1)
                        }
                        .menuStyle(.borderlessButton)
                    } else {
                        Text(acct.userEmail)
                            .font(.system(size: 11, weight: .semibold))
                            .foregroundColor(LightPalette.inkPrimary)
                            .lineLimit(1)
                    }

                    Text(acct.planName)
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundColor(LightPalette.inkSecondary)
                        .padding(.horizontal, 5.5)
                        .padding(.vertical, 1.5)
                        .background(RoundedRectangle(cornerRadius: 4).fill(Color.black.opacity(0.06)))

                    Spacer()

                    Button {
                        Task { await monitor.pollMagpieQuota(forceCloudRefresh: true) }
                    } label: {
                        HStack(spacing: 3) {
                            Image(systemName: monitor.isRefreshingCloud ? "hourglass" : "arrow.clockwise")
                            Text(monitor.isRefreshingCloud ? "刷新中" : "云端刷新")
                        }
                        .font(.system(size: 9.5, weight: .semibold))
                        .foregroundColor(LightPalette.brandBlue)
                        .padding(.horizontal, 6.5)
                        .padding(.vertical, 2.5)
                        .background(Capsule().fill(LightPalette.brandBlue.opacity(0.11)))
                    }
                    .buttonStyle(.plain)
                    .disabled(monitor.isRefreshingCloud)
                }

                if let gemini = acct.geminiPool {
                    magpiePoolRow(title: "Gemini", pool: gemini)
                }

                if let claude = acct.claudeGptPool {
                    magpiePoolRow(title: "Claude & GPT", pool: claude)
                }

                if let readAt = acct.readAt {
                    Text("实时同步自 Magpie · 读取于 \(TimeFormatters.formatUTC8(readAt, includeSeconds: true))")
                        .font(.system(size: 9))
                        .foregroundColor(LightPalette.inkMuted)
                }
            }
        }
        .padding(.horizontal, 10.5)
        .padding(.vertical, 8.5)
        .background(cardBackground)
    }

    private func magpiePoolRow(title: String, pool: CloudPoolQuota) -> some View {
        let c5 = QuotaMetrics.statusColor(for: pool.remaining5h)
        let c7 = QuotaMetrics.statusColor(for: pool.remaining7d)

        return VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 8) {
                Text(title)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(LightPalette.inkPrimary)
                    .frame(width: 78, alignment: .leading)

                HStack(spacing: 4) {
                    Text("5h")
                        .font(.system(size: 9.5, weight: .medium, design: .monospaced))
                        .foregroundColor(LightPalette.inkMuted)
                    miniInlineBar(percent: pool.remaining5h, color: c5)
                        .frame(width: 34, height: 4.5)
                    Text(String(format: "%.1f%%", pool.remaining5h))
                        .font(.system(size: 10.5, weight: .bold, design: .monospaced))
                        .foregroundColor(LightPalette.inkPrimary)
                }

                Spacer(minLength: 4)

                HStack(spacing: 4) {
                    Text("7d")
                        .font(.system(size: 9.5, weight: .medium, design: .monospaced))
                        .foregroundColor(LightPalette.inkMuted)
                    miniInlineBar(percent: pool.remaining7d, color: c7)
                        .frame(width: 34, height: 4.5)
                    Text(String(format: "%.1f%%", pool.remaining7d))
                        .font(.system(size: 10.5, weight: .bold, design: .monospaced))
                        .foregroundColor(LightPalette.inkPrimary)
                }
            }

            HStack(spacing: 6) {
                if let r5 = pool.resetsAt5h {
                    let wait5 = max(0, r5.timeIntervalSince(monitor.nowTick))
                    Text("5h 刷新: \(TimeFormatters.formatDuration(wait5)) (\(TimeFormatters.formatUTC8(r5)))")
                }
                Spacer()
                if let r7 = pool.resetsAt7d {
                    let wait7 = max(0, r7.timeIntervalSince(monitor.nowTick))
                    Text("7d 刷新: \(TimeFormatters.formatDuration(wait7))")
                }
            }
            .font(.system(size: 9))
            .foregroundColor(LightPalette.inkMuted)
        }
        .padding(7.5)
        .background(
            RoundedRectangle(cornerRadius: 9, style: .continuous)
                .fill(Color.white)
                .overlay(
                    RoundedRectangle(cornerRadius: 9, style: .continuous)
                        .stroke(Color.black.opacity(0.06), lineWidth: 0.7)
                )
        )
    }

    private func miniInlineBar(percent: Double, color: Color) -> some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.black.opacity(0.09))
                Capsule()
                    .fill(color)
                    .frame(width: max(3, geo.size.width * CGFloat(max(0, min(100, percent)) / 100.0)))
            }
        }
    }

    private var collapsibleFallbackQuotaCard: some View {
        let q = monitor.quota
        let rem5h = q.fiveHourRemainingPercent
        let remWk = q.weeklyRemainingPercent
        let c5 = QuotaMetrics.statusColor(for: rem5h)
        let c7 = QuotaMetrics.statusColor(for: remWk)

        return VStack(alignment: .leading, spacing: 8) {
            Button {
                panelController.isQuotaExpanded.toggle()
            } label: {
                HStack(spacing: 7) {
                    Image(systemName: "bolt.circle.fill")
                        .font(.system(size: 11.5))
                        .foregroundColor(c5)
                    Text("配额概览 · Gemini")
                        .font(.system(size: 11.5, weight: .semibold))
                        .foregroundColor(LightPalette.inkPrimary)
                    Spacer()
                    Text(String(format: "5h %.0f%% · 7d %.0f%%", rem5h, remWk))
                        .font(.system(size: 10.5, weight: .bold, design: .monospaced))
                        .foregroundColor(c5)
                    Image(systemName: panelController.isQuotaExpanded ? "chevron.up" : "chevron.down")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(LightPalette.inkMuted)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)

            if panelController.isQuotaExpanded {
                Divider().background(LightPalette.divider)
                HStack {
                    if let r5 = q.fiveHourResetDate {
                        Text("5h 刷新: \(TimeFormatters.formatUTC8(r5))")
                    } else {
                        Text("5h 满血待命")
                    }
                    Spacer()
                    Button("粘贴同步官方 Usage") {
                        monitor.syncQuotaFromPasteboard()
                    }
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(c7)
                    .buttonStyle(.plain)
                }
                .font(.system(size: 10))
                .foregroundColor(LightPalette.inkSecondary)
            }
        }
        .padding(.horizontal, 10.5)
        .padding(.vertical, 8.5)
        .background(cardBackground)
    }

    // MARK: 3. Grouped Sessions List Section (优先事项 / 今天 / 昨天 / 星期xx)

    private var sessionsListSection: some View {
        let sections = monitor.groupedSessionSections

        return VStack(alignment: .leading, spacing: 7) {
            if !monitor.isAccessibilityTrusted {
                HStack(spacing: 8) {
                    Image(systemName: "hand.tap.fill")
                        .font(.system(size: 11.5))
                        .foregroundColor(Color(red: 0.84, green: 0.50, blue: 0.04))
                    Text("重编译后需在系统设置重新开关一次辅助功能权限")
                        .font(.system(size: 10, weight: .medium))
                        .foregroundColor(LightPalette.inkPrimary)
                        .lineLimit(1)
                    Spacer()
                    Button("开启授权") {
                        monitor.promptAccessibilityPermission()
                    }
                    .buttonStyle(.borderedProminent)
                    .tint(LightPalette.brandBlue)
                    .controlSize(.mini)
                }
                .padding(7.5)
                .background(
                    RoundedRectangle(cornerRadius: 9, style: .continuous)
                        .fill(Color.orange.opacity(0.10))
                        .overlay(
                            RoundedRectangle(cornerRadius: 9, style: .continuous)
                                .stroke(Color.orange.opacity(0.28), lineWidth: 0.8)
                        )
                )
            }

            ScrollView(.vertical, showsIndicators: false) {
                LazyVStack(alignment: .leading, spacing: 10) {
                    ForEach(sections) { section in
                        VStack(alignment: .leading, spacing: 5.5) {
                            // Group Header: 优先事项 / 今天 / 昨天 / 星期xx
                            HStack(spacing: 5) {
                                if section.isPriority {
                                    Image(systemName: "bolt.fill")
                                        .font(.system(size: 9.5, weight: .bold))
                                        .foregroundColor(LightPalette.brandBlue)
                                }
                                Text(section.title)
                                    .font(.system(size: 10.5, weight: .bold))
                                    .foregroundColor(
                                        section.isPriority
                                            ? LightPalette.brandBlue
                                            : LightPalette.inkMuted
                                    )
                                Text("\(section.conversations.count)")
                                    .font(.system(size: 9.5, weight: .semibold, design: .monospaced))
                                    .foregroundColor(
                                        section.isPriority
                                            ? LightPalette.brandBlue.opacity(0.85)
                                            : LightPalette.inkMuted.opacity(0.75)
                                    )
                                Spacer()
                                if section.isPriority && !monitor.pinnedConversationIds.isEmpty {
                                    Button("取消全部固定 (\(monitor.pinnedConversationIds.count))") {
                                        monitor.clearAllPins()
                                    }
                                    .font(.system(size: 9.5, weight: .medium))
                                    .foregroundColor(LightPalette.brandBlue)
                                    .buttonStyle(.plain)
                                }
                            }
                            .padding(.horizontal, 3)

                            ForEach(section.conversations) { conv in
                                sessionRowView(conv: conv)
                            }
                        }
                    }
                }
            }
        }
    }

    private func sessionRowView(conv: ConversationSnapshot) -> some View {
        let isPinned = monitor.isPinned(conv.id)
        let isUnreadDone = monitor.isUnreadCompleted(conv.id)
        let isConfirmingDelete = (panelController.confirmingDeleteConvId == conv.id)
        let liveLabel = conv.phase.liveDisplayLabel
        let isDefaultProj = (conv.projectName == "default")
        let projFg: Color = isDefaultProj ? LightPalette.inkMuted : LightPalette.brandBlue
        let projBg: Color = isDefaultProj ? Color.black.opacity(0.055) : LightPalette.brandBlue.opacity(0.11)
        let rowFill: Color = liveLabel != nil
            ? LightPalette.rowActiveFill
            : (isUnreadDone ? LightPalette.rowDoneFill : LightPalette.rowIdleFill)
        let rowStroke: Color = {
            if isPinned { return LightPalette.pinBlue.opacity(0.32) }
            if liveLabel != nil { return conv.phase.accentColor.opacity(0.32) }
            if isUnreadDone { return LightPalette.doneGreen.opacity(0.35) }
            return LightPalette.rowIdleBorder
        }()

        return HStack(spacing: 7) {
            Button {
                panelController.clearDeleteConfirmation()
                monitor.jumpToConversation(conv)
            } label: {
                HStack(spacing: 8) {
                    if liveLabel != nil {
                        Circle()
                            .fill(conv.phase.accentColor)
                            .frame(width: 7.5, height: 7.5)
                    } else if isUnreadDone {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 10.5, weight: .semibold))
                            .foregroundColor(LightPalette.doneGreen)
                    }

                    VStack(alignment: .leading, spacing: 2.5) {
                        Text(conv.title)
                            .font(.system(size: 11.5, weight: .semibold))
                            .foregroundColor(LightPalette.inkPrimary)
                            .lineLimit(1)

                        HStack(spacing: 6) {
                            if let liveLabel {
                                Text(liveLabel)
                                    .fontWeight(.medium)
                                    .foregroundColor(conv.phase.accentColor)
                                if conv.phase.isWorking, let started = conv.activeTurnStartedAt {
                                    let elapsed = max(0, monitor.nowTick.timeIntervalSince(started))
                                    Text("· \(TimeFormatters.formatDuration(elapsed))")
                                        .foregroundColor(LightPalette.inkSecondary)
                                }
                                Text("· \(TimeFormatters.formatUTC8(conv.lastUpdatedAt))")
                                    .font(.system(size: 9.5, design: .monospaced))
                            } else {
                                Text(TimeFormatters.formatUTC8(conv.lastUpdatedAt))
                                    .font(.system(size: 9.5, design: .monospaced))
                            }

                            Text(conv.projectName)
                                .font(.system(size: 9.2, weight: .medium, design: .monospaced))
                                .foregroundColor(projFg)
                                .lineLimit(1)
                                .truncationMode(.middle)
                                .padding(.horizontal, 5.5)
                                .padding(.vertical, 1)
                                .background(
                                    RoundedRectangle(cornerRadius: 4, style: .continuous)
                                        .fill(projBg)
                                )
                        }
                        .font(.system(size: 9.8))
                        .foregroundColor(LightPalette.inkMuted)
                    }

                    Spacer(minLength: 4)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("点击在 Antigravity 中打开此会话")

            // Multi-session Pin toggle
            Button {
                panelController.clearDeleteConfirmation()
                monitor.togglePin(conv)
            } label: {
                Image(systemName: isPinned ? "pin.fill" : "pin")
                    .font(.system(size: 10, weight: .semibold))
                    .foregroundColor(isPinned ? LightPalette.pinBlue : LightPalette.inkMuted)
                    .frame(width: 22, height: 22)
                    .background(
                        Circle().fill(
                            isPinned ? LightPalette.pinBlue.opacity(0.13) : Color.black.opacity(0.048)
                        )
                    )
            }
            .buttonStyle(.plain)
            .help(isPinned ? "取消固定此会话" : "固定到优先事项（支持同时固定多个）")

            // Dual-side Delete (2-tap inline confirm to prevent accidental deletion)
            Button {
                if isConfirmingDelete {
                    panelController.clearDeleteConfirmation()
                    monitor.deleteConversationBothSides(conv)
                } else {
                    panelController.requestDeleteConfirmation(for: conv.id)
                }
            } label: {
                if isConfirmingDelete {
                    HStack(spacing: 2.5) {
                        Image(systemName: "trash.fill")
                            .font(.system(size: 9, weight: .bold))
                        Text("删?")
                            .font(.system(size: 9.5, weight: .bold))
                    }
                    .foregroundColor(.white)
                    .padding(.horizontal, 6.5)
                    .frame(height: 22)
                    .background(Capsule().fill(Color(red: 0.88, green: 0.20, blue: 0.24)))
                } else {
                    Image(systemName: "trash")
                        .font(.system(size: 10, weight: .medium))
                        .foregroundColor(LightPalette.inkMuted)
                        .frame(width: 22, height: 22)
                        .background(Circle().fill(Color.black.opacity(0.048)))
                }
            }
            .buttonStyle(.plain)
            .help(isConfirmingDelete ? "再次点击同步删除 Antigravity 与助手两边会话" : "删除两边会话")
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 7.5)
        .background(
            RoundedRectangle(cornerRadius: 10.5, style: .continuous)
                .fill(rowFill)
                .overlay(
                    RoundedRectangle(cornerRadius: 10.5, style: .continuous)
                        .stroke(rowStroke, lineWidth: 0.8)
                )
        )
    }

    // MARK: - Settings View (Toggled via Top-Right Gear Button)

    private var settingsContent: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack {
                Button {
                    panelController.showSettings = false
                } label: {
                    HStack(spacing: 4) {
                        Image(systemName: "chevron.left")
                            .font(.system(size: 10.5, weight: .bold))
                        Text("返回主面板")
                            .font(.system(size: 11.5, weight: .semibold))
                    }
                    .foregroundColor(LightPalette.brandBlue)
                }
                .buttonStyle(.plain)

                Spacer()

                Text("提醒与设置")
                    .font(.system(size: 11.5, weight: .bold))
                    .foregroundColor(LightPalette.inkSecondary)
            }
            .padding(.horizontal, 2)

            ScrollView(.vertical, showsIndicators: false) {
                VStack(alignment: .leading, spacing: 10) {
                    // Notification Settings
                    VStack(alignment: .leading, spacing: 8) {
                        Text("状态与 Remaining 提醒")
                            .font(.system(size: 11.5, weight: .bold))
                            .foregroundColor(LightPalette.inkPrimary)

                        Toggle("任务回复完成时横幅提醒", isOn: $settings.notifyOnComplete)
                            .toggleStyle(.switch)
                            .controlSize(.small)

                        HStack {
                            Text("完成提醒最小时长过滤")
                                .font(.system(size: 11))
                                .foregroundColor(LightPalette.inkSecondary)
                            Spacer()
                            Text("\(Int(settings.minTurnDurationForAlert))s")
                                .font(.system(size: 11, weight: .semibold, design: .monospaced))
                            Stepper("", value: $settings.minTurnDurationForAlert, in: 0...60, step: 2)
                                .labelsHidden()
                                .controlSize(.small)
                        }

                        Toggle("等待确认 / 执行异常立即提醒", isOn: $settings.notifyOnWaitingOrError)
                            .toggleStyle(.switch)
                            .controlSize(.small)

                        Toggle("低 Remaining 配额预警", isOn: $settings.notifyOnLowRemaining)
                            .toggleStyle(.switch)
                            .controlSize(.small)

                        HStack {
                            Toggle("提示音效", isOn: $settings.soundEnabled)
                                .toggleStyle(.switch)
                                .controlSize(.small)
                            Spacer()
                            Button("🔊 试听") {
                                NotificationManager.shared.playSound(named: "Glass", enabled: true)
                            }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                        }
                    }
                    .font(.system(size: 11.5))
                    .foregroundColor(LightPalette.inkPrimary)
                    .padding(11)
                    .background(cardBackground)

                    // Permissions, Hooks & Window Controls
                    VStack(alignment: .leading, spacing: 8) {
                        Text("会话精准跳转、Hooks 增强 & 窗口控制")
                            .font(.system(size: 11.5, weight: .bold))
                            .foregroundColor(LightPalette.inkPrimary)

                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(monitor.isAccessibilityTrusted ? "✅ 会话定位与同步删除已就绪 (辅助功能)" : "🔒 会话定位与同步删除 (需开启辅助功能)")
                                    .font(.system(size: 11, weight: .semibold))
                                Text("点击列表中任意会话可在 Antigravity 内打开，点击删除可同步清理两边会话。")
                                    .font(.system(size: 9.5))
                                    .foregroundColor(LightPalette.inkMuted)
                            }
                            Spacer()
                            if !monitor.isAccessibilityTrusted {
                                Button("去授权") {
                                    monitor.promptAccessibilityPermission()
                                }
                                .buttonStyle(.borderedProminent)
                                .tint(LightPalette.brandBlue)
                                .controlSize(.small)
                            }
                        }

                        Divider().background(LightPalette.divider)

                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(monitor.isHookPluginInstalled ? "✅ Lifecycle Hooks 已启用" : "Lifecycle Hooks 毫秒级直连 (可选)")
                                    .font(.system(size: 11, weight: .semibold))
                                Text("开启后可捕获后台任务空闲度与毫秒级工具事件。")
                                    .font(.system(size: 9.5))
                                    .foregroundColor(LightPalette.inkMuted)
                            }
                            Spacer()
                            Button(monitor.isHookPluginInstalled ? "移除" : "一键开启") {
                                monitor.toggleHookPluginInstallation()
                            }
                            .buttonStyle(.bordered)
                            .controlSize(.small)
                        }

                        HStack {
                            Text("面板不透明度")
                                .font(.system(size: 11))
                                .foregroundColor(LightPalette.inkSecondary)
                            Slider(value: $settings.pillOpacity, in: 0.65...1.0)
                        }

                        HStack(spacing: 8) {
                            Button("重置悬浮位置") {
                                panelController.resetPositionToDefault()
                            }
                            .buttonStyle(.bordered)
                            .controlSize(.small)

                            Spacer()

                            Button("退出助手") {
                                NSApplication.shared.terminate(nil)
                            }
                            .buttonStyle(.borderedProminent)
                            .tint(Color(red: 0.88, green: 0.22, blue: 0.26))
                            .controlSize(.small)
                        }
                    }
                    .font(.system(size: 11.5))
                    .foregroundColor(LightPalette.inkPrimary)
                    .padding(11)
                    .background(cardBackground)
                }
            }
        }
    }

    // MARK: Shared Subviews

    private var cardBackground: some View {
        RoundedRectangle(cornerRadius: 13, style: .continuous)
            .fill(LightPalette.cardFill)
            .overlay(
                RoundedRectangle(cornerRadius: 13, style: .continuous)
                    .stroke(LightPalette.cardBorder, lineWidth: 0.8)
            )
    }
}
