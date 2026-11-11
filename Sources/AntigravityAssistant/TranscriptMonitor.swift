import AppKit
import Combine
import Foundation
import SQLite3

// MARK: - Cached Transcript & Summary DB Entries

private struct SummaryDBEntry {
    let title: String?
    let projectName: String
    let isRunningInDB: Bool
    let isIdleInDB: Bool
    let nestingDepth: Int
    let killed: Bool
}

private struct CachedTranscript {
    let mtime: Date
    let fileSize: UInt64
    let rawSnapshot: ConversationSnapshot
    let invocations: [InvocationRecord]
}

// MARK: - Main Monitor ViewModel

@MainActor
final class TranscriptMonitor: ObservableObject {
    @Published private(set) var conversations: [ConversationSnapshot] = []
    @Published private(set) var pinnedConversationIds: Set<String> = []
    @Published private(set) var unreadCompletedConversationIds: Set<String> = []
    @Published private(set) var deletedConversationIds: Set<String> = []
    @Published private(set) var deletingConversationIds: Set<String> = []
    @Published private(set) var quota: QuotaMetrics = .empty
    @Published private(set) var cloudAccounts: [CloudAccountQuota] = []
    @Published var selectedAccountEmail: String? = nil
    @Published private(set) var isRefreshingCloud: Bool = false
    @Published private(set) var lastRefreshDate: Date = Date()
    @Published private(set) var nowTick: Date = Date()
    @Published private(set) var isHookPluginInstalled: Bool = false
    @Published private(set) var latestHookEvent: HookLiveEvent? = nil
    @Published private(set) var isAccessibilityTrusted: Bool = ConversationNavigator.isAccessibilityTrusted
    @Published private(set) var jumpingConversationId: String? = nil
    @Published var toastMessage: String? = nil

    let settings: AppSettings

    private var cache: [String: CachedTranscript] = [:]
    private var summariesCacheKey: String = ""
    private var summariesCache: [String: SummaryDBEntry] = [:]
    private var previousPhases: [String: AgentPhase] = [:]
    private var previousBackgroundBusy: [String: Bool] = [:]
    private var previousUserTurnCounts: [String: Int] = [:]
    private var hasCompletedInitialScan = false
    private var pendingJumpConversation: ConversationSnapshot? = nil
    private var scanTimer: Timer?
    private var clockTimer: Timer?
    private var magpieTimer: Timer?
    private var liveUITimer: Timer?
    private var isPollingLiveUI = false
    private var liveUIState: LiveAntigravityUIState? = nil
    private var lastCliFallbackPollAt: Date = .distantPast
    private var cancellables = Set<AnyCancellable>()

    private let antigravityRootDirectory: URL
    private let brainDirectory: URL
    private let conversationsDBDirectory: URL
    private let summariesDBURL: URL
    private let hookStateURL: URL
    private let hookPluginDir: URL

    private nonisolated static let isoStyleFractional = Date.ISO8601FormatStyle(includingFractionalSeconds: true)
    private nonisolated static let isoStyleStandard = Date.ISO8601FormatStyle(includingFractionalSeconds: false)

    init(settings: AppSettings) {
        self.settings = settings
        let home = FileManager.default.homeDirectoryForCurrentUser
        let agyRoot = home.appendingPathComponent(".gemini/antigravity", isDirectory: true)
        self.antigravityRootDirectory = agyRoot
        self.brainDirectory = agyRoot.appendingPathComponent("brain", isDirectory: true)
        self.conversationsDBDirectory = agyRoot.appendingPathComponent("conversations", isDirectory: true)
        self.summariesDBURL = agyRoot.appendingPathComponent("conversation_summaries.db")
        let appSupport = home.appendingPathComponent("Library/Application Support/AgyAssistant", isDirectory: true)
        self.hookStateURL = appSupport.appendingPathComponent("hook_state.json")
        self.hookPluginDir = home.appendingPathComponent(".gemini/config/plugins/agy-assistant-hooks", isDirectory: true)

        let defaults = UserDefaults.standard
        if let savedPins = defaults.stringArray(forKey: "pinnedConversationIds") {
            self.pinnedConversationIds = Set(savedPins)
        }
        if let savedUnreadDone = defaults.stringArray(forKey: "unreadCompletedConversationIds") {
            self.unreadCompletedConversationIds = Set(savedUnreadDone)
        }
        if let savedDeleted = defaults.stringArray(forKey: "deletedConversationIds") {
            self.deletedConversationIds = Set(savedDeleted)
        }

        checkHookPluginInstalled()

        NotificationManager.shared.onNotificationJumpRequested = { [weak self] convId in
            self?.jumpToConversationId(convId)
        }

        settings.objectWillChange
            .sink { [weak self] _ in
                DispatchQueue.main.async {
                    self?.recomputeQuota()
                }
            }
            .store(in: &cancellables)
    }

    var primaryConversation: ConversationSnapshot? {
        // Prefer actively working or waiting conversation first, then pinned, then most recently updated
        if let active = conversations.first(where: { $0.phase.isWorking }) {
            return active
        }
        if let waiting = conversations.first(where: {
            if case .waitingUser = $0.phase { return true }
            return false
        }) {
            return waiting
        }
        if let pinned = conversations.first(where: { pinnedConversationIds.contains($0.id) }) {
            return pinned
        }
        return conversations.first
    }

    func isPinned(_ conversationId: String) -> Bool {
        pinnedConversationIds.contains(conversationId)
    }

    func isUnreadCompleted(_ conversationId: String) -> Bool {
        unreadCompletedConversationIds.contains(conversationId)
    }

    func acknowledgeUnreadCompleted(_ conversationId: String) {
        if unreadCompletedConversationIds.remove(conversationId) != nil {
            persistUnreadCompletedIds()
        }
    }

    func togglePin(_ conv: ConversationSnapshot) {
        acknowledgeUnreadCompleted(conv.id)
        if pinnedConversationIds.contains(conv.id) {
            pinnedConversationIds.remove(conv.id)
            persistPinnedIds()
            showToast("已取消固定该会话")
        } else {
            pinnedConversationIds.insert(conv.id)
            persistPinnedIds()
            showToast("📌 已固定到优先事项（共 \(pinnedConversationIds.count) 个固定）")
        }
    }

    func clearAllPins() {
        guard !pinnedConversationIds.isEmpty else { return }
        pinnedConversationIds.removeAll()
        persistPinnedIds()
        showToast("已取消全部固定会话")
    }

    private func persistPinnedIds() {
        UserDefaults.standard.set(Array(pinnedConversationIds), forKey: "pinnedConversationIds")
    }

    private func persistUnreadCompletedIds() {
        let capped = Array(unreadCompletedConversationIds.prefix(200))
        UserDefaults.standard.set(capped, forKey: "unreadCompletedConversationIds")
    }

    private func persistDeletedIds() {
        let capped = Array(deletedConversationIds.prefix(600))
        UserDefaults.standard.set(capped, forKey: "deletedConversationIds")
    }

    var activeCloudAccount: CloudAccountQuota? {
        if let email = selectedAccountEmail,
           let match = cloudAccounts.first(where: { $0.userEmail == email }) {
            return match
        }
        if let latest = cloudAccounts.first(where: { $0.isLatestActive }) {
            return latest
        }
        return cloudAccounts.first
    }

    var activePoolForPrimarySession: CloudPoolQuota? {
        guard let acct = activeCloudAccount else { return nil }
        let modelLower = (primaryConversation?.modelName ?? "gemini").lowercased()
        let isClaudeOrGpt = modelLower.contains("claude") || modelLower.contains("gpt") || modelLower.contains("sonnet") || modelLower.contains("opus")
        if isClaudeOrGpt, let cPool = acct.claudeGptPool {
            return cPool
        }
        return acct.geminiPool ?? acct.claudeGptPool
    }

    var workingConversationCount: Int {
        conversations.filter { $0.phase.isWorking }.count
    }

    private func prioritySortRank(for conv: ConversationSnapshot) -> Int {
        if conv.phase.isPriorityActive || conv.backgroundBusy {
            return 0
        }
        if unreadCompletedConversationIds.contains(conv.id) {
            return 1
        }
        return 2
    }

    var priorityConversations: [ConversationSnapshot] {
        var items: [ConversationSnapshot] = []
        for conv in conversations {
            let inPriority = conv.phase.isPriorityActive
                || conv.backgroundBusy
                || pinnedConversationIds.contains(conv.id)
                || unreadCompletedConversationIds.contains(conv.id)
            if inPriority {
                items.append(conv)
            }
        }
        items.sort { lhs, rhs in
            let lRank = prioritySortRank(for: lhs)
            let rRank = prioritySortRank(for: rhs)
            if lRank != rRank {
                return lRank < rRank
            }
            return lhs.lastUpdatedAt > rhs.lastUpdatedAt
        }
        return items
    }

    var runningPriorityConversations: [ConversationSnapshot] {
        priorityConversations.filter {
            $0.phase.isPriorityActive || $0.backgroundBusy || unreadCompletedConversationIds.contains($0.id)
        }
    }

    var groupedSessionSections: [SessionGroupSection] {
        var priorityItems: [ConversationSnapshot] = []
        var bucketOrder: [String] = []
        var bucketTitles: [String: String] = [:]
        var bucketItems: [String: [ConversationSnapshot]] = [:]

        for conv in conversations {
            let isPinned = pinnedConversationIds.contains(conv.id)
            let isUnreadDone = unreadCompletedConversationIds.contains(conv.id)
            if conv.phase.isPriorityActive || conv.backgroundBusy || isPinned || isUnreadDone {
                priorityItems.append(conv)
            } else {
                let bucket = TimeFormatters.dayBucketUTC8(for: conv.lastUpdatedAt, now: nowTick)
                if bucketItems[bucket.key] == nil {
                    bucketOrder.append(bucket.key)
                    bucketTitles[bucket.key] = bucket.label
                    bucketItems[bucket.key] = []
                }
                bucketItems[bucket.key]?.append(conv)
            }
        }

        priorityItems.sort { lhs, rhs in
            let lRank = prioritySortRank(for: lhs)
            let rRank = prioritySortRank(for: rhs)
            if lRank != rRank {
                return lRank < rRank
            }
            return lhs.lastUpdatedAt > rhs.lastUpdatedAt
        }

        var sections: [SessionGroupSection] = []
        if !priorityItems.isEmpty {
            sections.append(
                SessionGroupSection(
                    id: "priority",
                    title: "优先事项",
                    isPriority: true,
                    conversations: priorityItems
                )
            )
        }

        for key in bucketOrder {
            if let list = bucketItems[key], !list.isEmpty {
                sections.append(
                    SessionGroupSection(
                        id: key,
                        title: bucketTitles[key] ?? key,
                        isPriority: false,
                        conversations: list
                    )
                )
            }
        }
        return sections
    }

    func start() {
        refreshNow()
        Task { await pollMagpieQuota(forceCloudRefresh: false) }
        pollLiveAntigravityUI()

        scanTimer?.invalidate()
        scanTimer = Timer.scheduledTimer(withTimeInterval: 1.2, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.refreshNow()
            }
        }

        liveUITimer?.invalidate()
        liveUITimer = Timer.scheduledTimer(withTimeInterval: 1.8, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.pollLiveAntigravityUI()
            }
        }

        magpieTimer?.invalidate()
        magpieTimer = Timer.scheduledTimer(withTimeInterval: 10.0, repeats: true) { [weak self] _ in
            Task { @MainActor in
                await self?.pollMagpieQuota(forceCloudRefresh: false)
            }
        }

        clockTimer?.invalidate()
        clockTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                self.nowTick = Date()
                let trusted = ConversationNavigator.isAccessibilityTrusted
                if self.isAccessibilityTrusted != trusted {
                    self.isAccessibilityTrusted = trusted
                    if trusted, let pending = self.pendingJumpConversation {
                        self.pendingJumpConversation = nil
                        self.jumpToConversation(pending)
                    }
                }
            }
        }
    }

    func stop() {
        scanTimer?.invalidate()
        liveUITimer?.invalidate()
        magpieTimer?.invalidate()
        clockTimer?.invalidate()
    }

    private func pollLiveAntigravityUI() {
        guard !isPollingLiveUI else { return }
        let trusted = ConversationNavigator.isAccessibilityTrusted
        if isAccessibilityTrusted != trusted {
            isAccessibilityTrusted = trusted
        }
        guard trusted else { return }

        isPollingLiveUI = true
        Task { @MainActor [weak self] in
            let state = await Task.detached(priority: .utility) {
                ConversationNavigator.pollLiveUIState()
            }.value
            guard let self else { return }
            self.isPollingLiveUI = false
            if let state, self.liveUIState != state {
                self.liveUIState = state
                self.refreshNow()
            }
        }
    }

    func pollMagpieQuota(forceCloudRefresh: Bool) async {
        if forceCloudRefresh {
            guard !isRefreshingCloud else { return }
            isRefreshingCloud = true
            defer { isRefreshingCloud = false }
            if let refreshed = await MagpieQuotaClient.forceRefreshFromCloud() {
                self.cloudAccounts = refreshed
                recomputeQuota()
                showToast("✅ 已从 Google 云端刷新真实配额")
            } else if let fetched = await MagpieQuotaClient.fetchAntigravityAccounts(allowCliFallback: true) {
                self.cloudAccounts = fetched
                recomputeQuota()
                showToast("已同步 Magpie 本地网关配额")
            } else {
                showToast("未检测到 Magpie 响应")
            }
            return
        }

        let now = Date()
        let allowCli = now.timeIntervalSince(lastCliFallbackPollAt) >= 60
        if allowCli { lastCliFallbackPollAt = now }

        if let accounts = await MagpieQuotaClient.fetchAntigravityAccounts(allowCliFallback: allowCli) {
            if self.cloudAccounts != accounts {
                self.cloudAccounts = accounts
                recomputeQuota()
            }
        }
    }

    func showToast(_ text: String) {
        toastMessage = text
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.2) { [weak self] in
            if self?.toastMessage == text {
                self?.toastMessage = nil
            }
        }
    }

    // MARK: - Refresh Pipeline

    func refreshNow() {
        let fm = FileManager.default
        let now = Date()
        let sevenDaysAgo = now.addingTimeInterval(-7.5 * 86400)

        let hookEvent = loadHookState(now: now)
        self.latestHookEvent = hookEvent
        let summaryMap = loadConversationSummariesIfNeeded()

        guard let entries = try? fm.contentsOfDirectory(
            at: brainDirectory,
            includingPropertiesForKeys: [.isDirectoryKey, .contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ) else {
            return
        }

        var updatedSnapshots: [ConversationSnapshot] = []
        var allInvocations: [InvocationRecord] = []

        for folder in entries {
            let convId = folder.lastPathComponent
            // Conversation IDs are UUIDs (length 36)
            guard convId.count >= 32,
                  convId != "tempmediaStorage",
                  !deletedConversationIds.contains(convId) else { continue }

            let summaryEntry = summaryMap[convId]
            // Skip nested subagent trajectories and killed/deleted trajectories
            if let s = summaryEntry, (s.nestingDepth > 0 || s.killed) {
                continue
            }

            let transcriptURL = folder
                .appendingPathComponent(".system_generated/logs/transcript.jsonl")
            guard fm.fileExists(atPath: transcriptURL.path),
                  let attrs = try? fm.attributesOfItem(atPath: transcriptURL.path),
                  let mtime = attrs[.modificationDate] as? Date,
                  let size = attrs[.size] as? UInt64 else {
                continue
            }

            // Skip transcripts untouched for > 7.5 days to keep startup & memory instant
            guard mtime >= sevenDaysAgo else { continue }

            let rawSnap: ConversationSnapshot
            let invList: [InvocationRecord]

            if let existing = cache[convId],
               existing.mtime == mtime,
               existing.fileSize == size {
                rawSnap = existing.rawSnapshot
                invList = existing.invocations
            } else {
                if let newlyParsed = Self.parseTranscriptFile(
                    convId: convId,
                    url: transcriptURL,
                    mtime: mtime,
                    fileSize: size
                ) {
                    cache[convId] = newlyParsed
                    rawSnap = newlyParsed.rawSnapshot
                    invList = newlyParsed.invocations
                } else {
                    continue
                }
            }

            let effectiveSnap = Self.adjustPhaseForStaleness(
                snapshot: rawSnap,
                mtime: mtime,
                summaryEntry: summaryEntry,
                hookEvent: hookEvent,
                liveUI: liveUIState,
                now: now
            )

            updatedSnapshots.append(effectiveSnap)
            allInvocations.append(contentsOf: invList)
        }

        updatedSnapshots.sort { $0.lastUpdatedAt > $1.lastUpdatedAt }
        self.conversations = updatedSnapshots
        self.lastRefreshDate = now

        recomputeQuota(from: allInvocations, now: now)
        evaluatePhaseTransitions(snapshots: updatedSnapshots, now: now)
        hasCompletedInitialScan = true
    }

    private func recomputeQuota(from explicitInvocations: [InvocationRecord]? = nil, now: Date = Date()) {
        let allInvocations: [InvocationRecord]
        if let explicitInvocations {
            allInvocations = explicitInvocations
        } else {
            allInvocations = cache.values.flatMap { $0.invocations }
        }

        let geminiInvocations = allInvocations
            .filter { $0.isGeminiFamily }
            .sorted { $0.timestamp < $1.timestamp }

        let cap5h = Double(max(100, settings.capacity5hUnits))
        let capWeekly = Double(max(300, settings.capacityWeeklyUnits))
        let anchor = settings.syncAnchor

        // 1. Compute 5-Hour & Weekly Remaining (Prioritize Live Cloud Quota via Magpie if connected)
        let fiveHourPct: Double
        let fiveHourResetAt: Date?
        let fiveHourSteps: Int
        let fiveHourTokens: Int
        let is5hSynced: Bool

        let weeklyPct: Double
        let weeklyResetAt: Date?
        let weeklySteps: Int
        let isWeeklySynced: Bool

        if let cloudPool = activePoolForPrimarySession {
            let bucketStart5h = (cloudPool.resetsAt5h ?? now).addingTimeInterval(-5 * 3600)
            let inBucket5h = geminiInvocations.filter { $0.timestamp >= bucketStart5h && $0.timestamp <= now }
            let bucketStart7d = (cloudPool.resetsAt7d ?? now).addingTimeInterval(-7 * 86400)
            let inBucket7d = geminiInvocations.filter { $0.timestamp >= bucketStart7d && $0.timestamp <= now }

            fiveHourPct = max(0.0, min(100.0, cloudPool.remaining5h))
            fiveHourResetAt = cloudPool.resetsAt5h
            fiveHourSteps = inBucket5h.count
            fiveHourTokens = inBucket5h.reduce(0) { $0 + $1.totalTokens }
            is5hSynced = true

            weeklyPct = max(0.0, min(100.0, cloudPool.remaining7d))
            weeklyResetAt = cloudPool.resetsAt7d
            weeklySteps = inBucket7d.count
            isWeeklySynced = true
        } else {
            if let anchor, now < anchor.fiveHourResetAt {
                let bucketStart = anchor.fiveHourResetAt.addingTimeInterval(-5 * 3600)
                let inBucket = geminiInvocations.filter { $0.timestamp >= bucketStart && $0.timestamp <= now }
                let deltaAfterSync = geminiInvocations
                    .filter { $0.timestamp > anchor.syncedAt && $0.timestamp <= now }
                    .reduce(0.0) { $0 + $1.creditWeight }

                fiveHourPct = max(0.0, min(100.0, anchor.fiveHourRemainingPercent - (deltaAfterSync / cap5h) * 100.0))
                fiveHourResetAt = anchor.fiveHourResetAt
                fiveHourSteps = inBucket.count
                fiveHourTokens = inBucket.reduce(0) { $0 + $1.totalTokens }
                is5hSynced = true
            } else {
                let cutoff = anchor?.fiveHourResetAt ?? now.addingTimeInterval(-7 * 86400)
                let candidates = geminiInvocations.filter { $0.timestamp >= cutoff && $0.timestamp <= now }
                let sim = Self.simulateFixedBucket(invocations: candidates, windowSeconds: 5 * 3600, now: now)
                fiveHourPct = max(0.0, min(100.0, 100.0 - (sim.usedWeight / cap5h) * 100.0))
                fiveHourResetAt = sim.resetDate
                fiveHourSteps = sim.stepCount
                fiveHourTokens = sim.tokenCount
                is5hSynced = false
            }

            if let anchor, now < anchor.weeklyResetAt {
                let bucketStart = anchor.weeklyResetAt.addingTimeInterval(-7 * 86400)
                let inBucket = geminiInvocations.filter { $0.timestamp >= bucketStart && $0.timestamp <= now }
                let deltaAfterSync = geminiInvocations
                    .filter { $0.timestamp > anchor.syncedAt && $0.timestamp <= now }
                    .reduce(0.0) { $0 + $1.creditWeight }

                weeklyPct = max(0.0, min(100.0, anchor.weeklyRemainingPercent - (deltaAfterSync / capWeekly) * 100.0))
                weeklyResetAt = anchor.weeklyResetAt
                weeklySteps = inBucket.count
                isWeeklySynced = true
            } else {
                let cutoff = anchor?.weeklyResetAt ?? now.addingTimeInterval(-14 * 86400)
                let candidates = geminiInvocations.filter { $0.timestamp >= cutoff && $0.timestamp <= now }
                let sim = Self.simulateFixedBucket(invocations: candidates, windowSeconds: 7 * 86400, now: now)
                weeklyPct = max(0.0, min(100.0, 100.0 - (sim.usedWeight / capWeekly) * 100.0))
                weeklyResetAt = sim.resetDate
                weeklySteps = sim.stepCount
                isWeeklySynced = false
            }
        }

        // 3. Today's stats in UTC+8
        let startOfTodayUTC8 = TimeFormatters.utc8Calendar.startOfDay(for: now)
        let invToday = allInvocations.filter { $0.timestamp >= startOfTodayUTC8 }
        let todayTokens = invToday.reduce(0) { $0 + $1.totalTokens }
        let todayConvIds = Set(invToday.map { $0.conversationId })
        let todayTurns = conversations
            .filter { $0.lastUpdatedAt >= startOfTodayUTC8 }
            .reduce(0) { $0 + $1.userTurnCount }

        let newQuota = QuotaMetrics(
            fiveHourRemainingPercent: fiveHourPct,
            fiveHourResetDate: fiveHourResetAt,
            fiveHourStepsUsed: fiveHourSteps,
            fiveHourTokensUsed: fiveHourTokens,
            isFiveHourSynced: is5hSynced,
            weeklyRemainingPercent: weeklyPct,
            weeklyResetDate: weeklyResetAt,
            weeklyStepsUsed: weeklySteps,
            isWeeklySynced: isWeeklySynced,
            todayInvocations: invToday.count,
            todayUserTurns: todayTurns,
            todayTokens: todayTokens,
            todayConversations: todayConvIds.count
        )
        self.quota = newQuota

        if hasCompletedInitialScan {
            NotificationManager.shared.notifyLowQuotaIfNeeded(
                remainingPercent: min(fiveHourPct, weeklyPct),
                settings: settings
            )
        }
    }

    private static func simulateFixedBucket(
        invocations: [InvocationRecord],
        windowSeconds: TimeInterval,
        now: Date
    ) -> (usedWeight: Double, stepCount: Int, tokenCount: Int, resetDate: Date?) {
        var bucketStart: Date? = nil
        var weight: Double = 0
        var steps = 0
        var tokens = 0

        for inv in invocations {
            if let start = bucketStart, inv.timestamp < start.addingTimeInterval(windowSeconds) {
                weight += inv.creditWeight
                steps += 1
                tokens += inv.totalTokens
            } else {
                bucketStart = inv.timestamp
                weight = inv.creditWeight
                steps = 1
                tokens = inv.totalTokens
            }
        }

        if let start = bucketStart {
            let end = start.addingTimeInterval(windowSeconds)
            if now < end {
                return (weight, steps, tokens, end)
            }
        }
        // Bucket already expired and refreshed to 100%!
        return (0, 0, 0, nil)
    }

    func syncQuotaFromPasteboard() {
        guard let str = NSPasteboard.general.string(forType: .string), !str.isEmpty else {
            showToast("剪贴板为空，请先在 Usage 页复制文字")
            return
        }
        if settings.syncFromOfficialUsageText(str) {
            recomputeQuota()
            showToast("✅ 已从剪贴板识别并对齐官方 Usage！")
        } else {
            showToast("未识别到百分比，请复制含 Weekly / Five Hour 的文字")
        }
    }

    private func evaluatePhaseTransitions(snapshots: [ConversationSnapshot], now: Date) {
        var unreadChanged = false

        for snap in snapshots {
            let prev = previousPhases[snap.id]
            let prevBgBusy = previousBackgroundBusy[snap.id] ?? false
            let prevTurns = previousUserTurnCounts[snap.id]

            previousPhases[snap.id] = snap.phase
            previousBackgroundBusy[snap.id] = snap.backgroundBusy
            previousUserTurnCounts[snap.id] = snap.userTurnCount

            let isNowActive = snap.phase.isPriorityActive || snap.backgroundBusy
            if isNowActive {
                if unreadCompletedConversationIds.remove(snap.id) != nil {
                    unreadChanged = true
                }
            }

            guard hasCompletedInitialScan else {
                continue
            }

            let wasActive = (prev?.isPriorityActive == true) || prevBgBusy
            let fastTurnCompleted: Bool = {
                if let prevTurns, snap.userTurnCount > prevTurns, case .completed = snap.phase, !snap.backgroundBusy {
                    return true
                }
                return false
            }()

            if (wasActive || fastTurnCompleted), case .completed = snap.phase, !snap.backgroundBusy {
                if !pinnedConversationIds.contains(snap.id) {
                    if unreadCompletedConversationIds.insert(snap.id).inserted {
                        unreadChanged = true
                    }
                }
            }

            guard let prev, prev != snap.phase else {
                continue
            }

            switch snap.phase {
            case .completed:
                if prev.isWorking {
                    let duration = snap.activeTurnStartedAt.map { now.timeIntervalSince($0) }
                    NotificationManager.shared.notifyTurnCompleted(
                        conversation: snap,
                        duration: duration,
                        settings: settings
                    )
                }
            case .waitingUser(let detail):
                NotificationManager.shared.notifyWaitingForUser(
                    conversation: snap,
                    detail: detail,
                    settings: settings
                )
            case .error(let detail):
                NotificationManager.shared.notifyError(
                    conversation: snap,
                    detail: detail,
                    settings: settings
                )
            default:
                break
            }
        }

        if unreadChanged {
            persistUnreadCompletedIds()
        }
    }

    // MARK: - Transcript JSONL Parser

    private nonisolated static func parseDate(_ str: String?) -> Date? {
        guard let str else { return nil }
        if let d = try? isoStyleFractional.parse(str) { return d }
        return try? isoStyleStandard.parse(str)
    }

    private nonisolated static func parseTranscriptFile(
        convId: String,
        url: URL,
        mtime: Date,
        fileSize: UInt64
    ) -> CachedTranscript? {
        guard let content = try? String(contentsOf: url, encoding: .utf8) else {
            return nil
        }

        let lines = content.split(whereSeparator: \.isNewline)
        guard !lines.isEmpty else { return nil }

        var title: String? = nil
        var searchQuery: String? = nil
        var detectedModel = "Gemini"
        var userTurns = 0
        var lastUserTurnAt: Date? = nil
        var stepCount = 0
        var latestInTokens = 0
        var latestCacheTokens = 0
        var totalOutTokens = 0
        var invocations: [InvocationRecord] = []

        var lastStepType = ""
        var lastStepStatus = ""
        var lastPlannerToolCalls: [(name: String, detail: String?)] = []
        var genericCountSinceLastPlanner = 0
        var lastErrorText: String? = nil

        for rawLine in lines {
            guard let data = rawLine.data(using: .utf8),
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                continue
            }

            stepCount += 1
            let type = json["type"] as? String ?? ""
            let status = json["status"] as? String ?? ""
            let createdAt = parseDate(json["created_at"] as? String) ?? mtime
            let stepIdx = json["step_index"] as? Int ?? stepCount

            lastStepType = type
            lastStepStatus = status

            if status == "ERROR" {
                lastErrorText = (json["content"] as? String)?.prefix(80).description
            }

            if type == "USER_INPUT" {
                userTurns += 1
                lastUserTurnAt = createdAt
                lastPlannerToolCalls = []
                genericCountSinceLastPlanner = 0

                if let text = json["content"] as? String {
                    if title == nil {
                        if let extracted = extractUserRequestLine(from: text) {
                            searchQuery = String(extracted.prefix(26))
                            title = extracted.count > 34 ? String(extracted.prefix(34)) + "…" : extracted
                        }
                    }
                    if let newModel = extractModelSelection(from: text) {
                        detectedModel = newModel
                    }
                }
            } else if type == "PLANNER_RESPONSE" {
                let inTok = json["input_tokens"] as? Int ?? 0
                let cacheTok = json["cache_read_tokens"] as? Int ?? 0
                let outTok = json["output_tokens"] as? Int ?? 0

                if inTok > 0 || cacheTok > 0 {
                    latestInTokens = inTok
                    latestCacheTokens = cacheTok
                }
                totalOutTokens += outTok

                invocations.append(
                    InvocationRecord(
                        id: "\(convId)-\(stepIdx)",
                        conversationId: convId,
                        timestamp: createdAt,
                        modelName: detectedModel,
                        inputTokens: inTok,
                        cacheReadTokens: cacheTok,
                        outputTokens: outTok
                    )
                )

                var parsedTools: [(name: String, detail: String?)] = []
                if let toolArr = json["tool_calls"] as? [[String: Any]] {
                    for t in toolArr {
                        let tName = t["name"] as? String ?? "tool"
                        let args = t["args"] as? [String: Any]
                        let detail = extractToolDetail(toolName: tName, args: args)
                        parsedTools.append((tName, detail))
                    }
                }
                lastPlannerToolCalls = parsedTools
                genericCountSinceLastPlanner = 0
            } else if type == "GENERIC" {
                genericCountSinceLastPlanner += 1
            }
        }

        // Determine raw phase from transcript tail
        var phase: AgentPhase
        if lastStepStatus == "ERROR" {
            phase = .error(detail: lastErrorText)
        } else if lastStepType == "USER_INPUT" {
            phase = .thinking
        } else if lastStepType == "PLANNER_RESPONSE" {
            if lastPlannerToolCalls.isEmpty {
                phase = .completed
            } else if let askTool = lastPlannerToolCalls.first(where: { $0.name == "ask_question" }) {
                phase = .waitingUser(detail: askTool.detail ?? "Agent 发起了问题确认")
            } else if let firstTool = lastPlannerToolCalls.first {
                phase = .runningTool(name: firstTool.name, detail: firstTool.detail)
            } else {
                phase = .thinking
            }
        } else if lastStepType == "GENERIC" {
            if !lastPlannerToolCalls.isEmpty && genericCountSinceLastPlanner < lastPlannerToolCalls.count {
                let nextTool = lastPlannerToolCalls[genericCountSinceLastPlanner]
                if nextTool.name == "ask_question" {
                    phase = .waitingUser(detail: nextTool.detail ?? "Agent 发起了问题确认")
                } else {
                    phase = .runningTool(name: nextTool.name, detail: nextTool.detail)
                }
            } else {
                phase = .thinking
            }
        } else {
            phase = .idle
        }

        let baseSnapshot = ConversationSnapshot(
            id: convId,
            title: title ?? "会话 \(convId.prefix(8))",
            projectName: "default",
            searchQuery: searchQuery ?? "",
            modelName: detectedModel,
            phase: phase,
            lastUpdatedAt: mtime,
            activeTurnStartedAt: lastUserTurnAt,
            stepCount: stepCount,
            userTurnCount: userTurns,
            invocationCount: invocations.count,
            contextUsedTokens: latestInTokens + latestCacheTokens,
            latestInputTokens: latestInTokens,
            latestCacheReadTokens: latestCacheTokens,
            totalOutputTokens: totalOutTokens,
            backgroundBusy: false
        )

        return CachedTranscript(
            mtime: mtime,
            fileSize: fileSize,
            rawSnapshot: baseSnapshot,
            invocations: invocations
        )
    }

    private nonisolated static func adjustPhaseForStaleness(
        snapshot: ConversationSnapshot,
        mtime: Date,
        summaryEntry: SummaryDBEntry?,
        hookEvent: HookLiveEvent?,
        liveUI: LiveAntigravityUIState?,
        now: Date
    ) -> ConversationSnapshot {
        var phase = snapshot.phase
        var title = snapshot.title
        var projectName = summaryEntry?.projectName ?? snapshot.projectName
        var model = snapshot.modelName
        var bgBusy = snapshot.backgroundBusy

        // 0. Enrich from Antigravity's official conversation_summaries.db (title, projectName, & idle/cancelled status)
        if let summaryEntry {
            if let dbTitle = summaryEntry.title, !dbTitle.isEmpty {
                title = dbTitle
            }
            if phase.isWorking && summaryEntry.isIdleInDB && !summaryEntry.isRunningInDB && now.timeIntervalSince(mtime) >= 2.0 {
                phase = .interrupted
                bgBusy = false
            }
        }
        if projectName.isEmpty {
            projectName = "default"
        }

        // 1. Enrich from live Lifecycle Hook if matching conversation & fresh
        if let hook = hookEvent,
           hook.conversationId == snapshot.id,
           hook.timestamp >= mtime.timeIntervalSince1970 - 2.0 {
            if let hm = hook.modelName, !hm.isEmpty, hm != "auto" {
                model = hm
            }
            switch hook.event {
            case "PreInvocation", "PostInvocation":
                if now.timeIntervalSince1970 - hook.timestamp < 300 {
                    phase = .thinking
                }
            case "PreToolUse":
                if let tn = hook.toolName, now.timeIntervalSince1970 - hook.timestamp < 300 {
                    if tn == "ask_question" {
                        phase = .waitingUser(detail: hook.toolDetail)
                    } else {
                        phase = .runningTool(name: tn, detail: hook.toolDetail)
                    }
                }
            case "Stop":
                bgBusy = (hook.fullyIdle == false)
                switch hook.terminationReason {
                case "ERROR":
                    phase = .error(detail: hook.error)
                case "MAX_TOKEN_BUDGET_EXCEEDED":
                    phase = .error(detail: "Token 预算或配额已达上限")
                case "USER_CANCELED":
                    phase = .interrupted
                default:
                    phase = .completed
                }
            default:
                break
            }
        }

        // 2. Enrich from live Antigravity UI (official sidebar title + instant user cancellation backfill)
        if let liveUI {
            if let item = liveUI.sidebarItems[snapshot.id] {
                if let liveTitle = item.title, !liveTitle.isEmpty {
                    title = liveTitle
                }
                if phase.isWorking && item.isIdleInUI && !item.isRunning && now.timeIntervalSince(mtime) >= 2.0 {
                    // Transcript ended mid-turn without final PLANNER_RESPONSE, and UI shows idle (Archive button, no Stop button) -> User cancelled!
                    phase = .interrupted
                    bgBusy = false
                }
            } else if liveUI.openConversationId == snapshot.id {
                if liveUI.openConversationCancelledBanner {
                    if phase.isWorking {
                        phase = .interrupted
                        bgBusy = false
                    }
                } else if phase.isWorking && !liveUI.openConversationHasStopButton && now.timeIntervalSince(mtime) >= 2.5 {
                    phase = .interrupted
                    bgBusy = false
                }
            }
        }

        // 3. Fallback timeout if Antigravity app is closed while transcript ended mid-turn
        if phase.isWorking && now.timeIntervalSince(mtime) > 240 {
            if let item = liveUI?.sidebarItems[snapshot.id], item.isRunning {
                // Still genuinely running in UI (e.g. long command) — keep working state
            } else {
                phase = .interrupted
            }
        }

        return ConversationSnapshot(
            id: snapshot.id,
            title: title,
            projectName: projectName,
            searchQuery: snapshot.searchQuery,
            modelName: model,
            phase: phase,
            lastUpdatedAt: mtime,
            activeTurnStartedAt: snapshot.activeTurnStartedAt,
            stepCount: snapshot.stepCount,
            userTurnCount: snapshot.userTurnCount,
            invocationCount: snapshot.invocationCount,
            contextUsedTokens: snapshot.contextUsedTokens,
            latestInputTokens: snapshot.latestInputTokens,
            latestCacheReadTokens: snapshot.latestCacheReadTokens,
            totalOutputTokens: snapshot.totalOutputTokens,
            backgroundBusy: bgBusy
        )
    }

    private nonisolated static func extractUserRequestLine(from raw: String) -> String? {
        var body = raw
        if let startRange = raw.range(of: "<USER_REQUEST>"),
           let endRange = raw.range(of: "</USER_REQUEST>") {
            body = String(raw[startRange.upperBound..<endRange.lowerBound])
        }
        let cleaned = body
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .first { !$0.isEmpty && !$0.hasPrefix("<") && !$0.hasPrefix("![") }

        guard let line = cleaned, !line.isEmpty else { return nil }
        return line
    }

    private nonisolated static func extractModelSelection(from raw: String) -> String? {
        guard let marker = raw.range(of: "Model Selection` from ") else { return nil }
        let sub = raw[marker.upperBound...]
        guard let toRange = sub.range(of: " to ") else { return nil }
        let afterTo = sub[toRange.upperBound...]
        if let dot = afterTo.firstIndex(of: ".") {
            let name = String(afterTo[..<dot]).trimmingCharacters(in: .whitespacesAndNewlines)
            return name.isEmpty ? nil : name
        }
        return nil
    }

    private nonisolated static func extractToolDetail(toolName: String, args: [String: Any]?) -> String? {
        guard let args else { return nil }
        func cleanQuotes(_ val: Any?) -> String? {
            guard let s = val as? String else { return nil }
            return s.trimmingCharacters(in: CharacterSet(charactersIn: "\"' \n"))
        }
        if let summary = cleanQuotes(args["toolSummary"]), !summary.isEmpty {
            return summary
        }
        if let path = cleanQuotes(args["Path"]), !path.isEmpty {
            return (path as NSString).lastPathComponent
        }
        if let query = cleanQuotes(args["query"]), !query.isEmpty {
            return query
        }
        if let role = cleanQuotes(args["Role"]), !role.isEmpty {
            return role
        }
        return nil
    }

    // MARK: - Read-Only Antigravity Summaries SQLite Reader (Project Name, Official Title, & Run Status)

    private func loadConversationSummariesIfNeeded() -> [String: SummaryDBEntry] {
        let fm = FileManager.default
        let dbPath = summariesDBURL.path
        guard fm.fileExists(atPath: dbPath) else { return summariesCache }

        let walPath = dbPath + "-wal"
        let dbAttrs = try? fm.attributesOfItem(atPath: dbPath)
        let walAttrs = try? fm.attributesOfItem(atPath: walPath)
        let dbMtime = (dbAttrs?[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0
        let walMtime = (walAttrs?[.modificationDate] as? Date)?.timeIntervalSince1970 ?? 0
        let walSize = (walAttrs?[.size] as? UInt64) ?? 0
        let key = "\(dbMtime)-\(walMtime)-\(walSize)"

        if key == summariesCacheKey && !summariesCache.isEmpty {
            return summariesCache
        }

        var db: OpaquePointer? = nil
        let flags = SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX
        guard sqlite3_open_v2(dbPath, &db, flags, nil) == SQLITE_OK, let db else {
            if let db { sqlite3_close(db) }
            return summariesCache
        }
        defer { sqlite3_close(db) }
        sqlite3_busy_timeout(db, 80)

        let sql = "SELECT conversation_id, title, workspace_uris, status, nesting_depth, killed FROM conversation_summaries;"
        var stmt: OpaquePointer? = nil
        guard sqlite3_prepare_v2(db, sql, -1, &stmt, nil) == SQLITE_OK, let stmt else {
            return summariesCache
        }
        defer { sqlite3_finalize(stmt) }

        var parsed: [String: SummaryDBEntry] = [:]
        while sqlite3_step(stmt) == SQLITE_ROW {
            guard let cidPtr = sqlite3_column_text(stmt, 0) else { continue }
            let cid = String(cString: cidPtr)
            let rawTitle = sqlite3_column_text(stmt, 1).map { String(cString: $0).trimmingCharacters(in: .whitespacesAndNewlines) }
            let wsJSON = sqlite3_column_text(stmt, 2).map { String(cString: $0) } ?? ""
            let status = sqlite3_column_text(stmt, 3).map { String(cString: $0) } ?? ""
            let nesting = Int(sqlite3_column_int(stmt, 4))
            let killed = sqlite3_column_int(stmt, 5) != 0

            let proj = Self.extractProjectName(fromWorkspaceURIsJSON: wsJSON)
            parsed[cid] = SummaryDBEntry(
                title: (rawTitle?.isEmpty == false) ? rawTitle : nil,
                projectName: proj,
                isRunningInDB: status == "CASCADE_RUN_STATUS_RUNNING",
                isIdleInDB: status == "CASCADE_RUN_STATUS_IDLE",
                nestingDepth: nesting,
                killed: killed
            )
        }

        summariesCacheKey = key
        summariesCache = parsed
        return parsed
    }

    private nonisolated static func extractProjectName(fromWorkspaceURIsJSON raw: String) -> String {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return "default" }

        if let data = trimmed.data(using: .utf8),
           let arr = try? JSONSerialization.jsonObject(with: data) as? [String],
           let firstURI = arr.first, !firstURI.isEmpty {
            if let url = URL(string: firstURI) {
                let name = url.lastPathComponent.removingPercentEncoding ?? url.lastPathComponent
                if !name.isEmpty && name != "/" {
                    return name
                }
            }
            let clean = firstURI.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            if let last = clean.split(separator: "/").last, !last.isEmpty {
                return String(last).removingPercentEncoding ?? String(last)
            }
        }
        return "default"
    }

    // MARK: - Optional Official Lifecycle Hooks Integration

    private func loadHookState(now: Date) -> HookLiveEvent? {
        guard let data = try? Data(contentsOf: hookStateURL),
              let decoded = try? JSONDecoder().decode(HookLiveEvent.self, from: data) else {
            return nil
        }
        return decoded
    }

    func checkHookPluginInstalled() {
        let manifest = hookPluginDir.appendingPathComponent("hooks.json")
        isHookPluginInstalled = FileManager.default.fileExists(atPath: manifest.path)
    }

    func toggleHookPluginInstallation() {
        let fm = FileManager.default
        if isHookPluginInstalled {
            try? fm.removeItem(at: hookPluginDir)
            checkHookPluginInstalled()
            showToast("已移除 Lifecycle Hooks 插件")
            return
        }

        do {
            try fm.createDirectory(at: hookPluginDir, withIntermediateDirectories: true)

            let pluginJson = """
            {
              "name": "agy-assistant-hooks",
              "description": "Pushes real-time Antigravity lifecycle status events to the macOS Floating Assistant."
            }
            """
            try pluginJson.write(
                to: hookPluginDir.appendingPathComponent("plugin.json"),
                atomically: true,
                encoding: .utf8
            )

            let hooksJson = """
            {
              "agy-floating-assistant": {
                "SessionStart": [
                  { "type": "command", "command": "/usr/bin/python3 ./record_hook.py SessionStart", "timeout": 3 }
                ],
                "PreInvocation": [
                  { "type": "command", "command": "/usr/bin/python3 ./record_hook.py PreInvocation", "timeout": 3 }
                ],
                "PreToolUse": [
                  {
                    "matcher": "*",
                    "hooks": [
                      { "type": "command", "command": "/usr/bin/python3 ./record_hook.py PreToolUse", "timeout": 3 }
                    ]
                  }
                ],
                "PostToolUse": [
                  {
                    "matcher": "*",
                    "hooks": [
                      { "type": "command", "command": "/usr/bin/python3 ./record_hook.py PostToolUse", "timeout": 3 }
                    ]
                  }
                ],
                "Stop": [
                  { "type": "command", "command": "/usr/bin/python3 ./record_hook.py Stop", "timeout": 3 }
                ]
              }
            }
            """
            try hooksJson.write(
                to: hookPluginDir.appendingPathComponent("hooks.json"),
                atomically: true,
                encoding: .utf8
            )

            let pyScript = """
            #!/usr/bin/env python3
            import json, os, sys, time

            def main():
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
            """
            let scriptURL = hookPluginDir.appendingPathComponent("record_hook.py")
            try pyScript.write(to: scriptURL, atomically: true, encoding: .utf8)
            try fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: scriptURL.path)

            checkHookPluginInstalled()
            showToast("已启用 Lifecycle Hooks 毫秒级同步")
        } catch {
            showToast("安装失败: \(error.localizedDescription)")
        }
    }

    // MARK: - Direct Conversation Jump & Quick Actions

    func promptAccessibilityPermission() {
        let trusted = ConversationNavigator.requestAccessibilityPermission(openSystemSettings: true)
        isAccessibilityTrusted = trusted
        if trusted {
            showToast("✅ 辅助功能权限已就绪")
        } else {
            showToast("请在系统设置「辅助功能」中勾选 Antigravity 使用助手")
        }
    }

    func jumpToPrimaryConversation() {
        if let primary = primaryConversation {
            jumpToConversation(primary)
        } else {
            activateAntigravityApp()
        }
    }

    func jumpToConversationId(_ convId: String) {
        acknowledgeUnreadCompleted(convId)
        if let match = conversations.first(where: { $0.id == convId }) {
            jumpToConversation(match)
        } else {
            refreshNow()
            if let match = conversations.first(where: { $0.id == convId }) {
                jumpToConversation(match)
            } else {
                activateAntigravityApp()
            }
        }
    }

    func jumpToConversation(_ conv: ConversationSnapshot) {
        acknowledgeUnreadCompleted(conv.id)
        guard jumpingConversationId == nil else { return }
        isAccessibilityTrusted = ConversationNavigator.isAccessibilityTrusted

        if !isAccessibilityTrusted {
            pendingJumpConversation = conv
            _ = ConversationNavigator.requestAccessibilityPermission(openSystemSettings: true)
            activateAntigravityApp()
            showToast("🔒 请在系统设置开启「辅助功能」权限，授权后自动打开该会话")
            return
        }

        jumpingConversationId = conv.id
        Task { @MainActor in
            defer { self.jumpingConversationId = nil }
            let outcome = await ConversationNavigator.jump(to: conv)
            switch outcome {
            case .alreadyActive:
                self.showToast("✨ 当前已在该目标会话")
            case .jumpedViaDirectLink, .jumpedViaPickerSearch:
                self.showToast("已打开会话：\(conv.title)")
            case .activatedAppOnly(let reason):
                self.showToast(reason)
            case .missingAccessibilityPermission:
                self.pendingJumpConversation = conv
                self.isAccessibilityTrusted = false
                self.showToast("🔒 开启「辅助功能」权限后将自动跳转到该会话")
            case .appNotFound:
                self.showToast("未在 /Applications 找到 Antigravity.app")
            }
        }
    }

    func activateAntigravityApp() {
        let appURL = URL(fileURLWithPath: "/Applications/Antigravity.app")
        if FileManager.default.fileExists(atPath: appURL.path) {
            NSWorkspace.shared.openApplication(
                at: appURL,
                configuration: NSWorkspace.OpenConfiguration(),
                completionHandler: nil
            )
        } else {
            showToast("未在 /Applications 找到 Antigravity.app")
        }
    }

    // MARK: - Dual-Side Conversation Deletion (Antigravity UI + Local Storage & Assistant)

    func deleteConversationBothSides(_ conv: ConversationSnapshot) {
        let convId = conv.id
        guard !deletingConversationIds.contains(convId) else { return }
        deletingConversationIds.insert(convId)

        // 1. Immediately remove from Assistant UI, pins, and caches
        deletedConversationIds.insert(convId)
        persistDeletedIds()

        if pinnedConversationIds.remove(convId) != nil {
            persistPinnedIds()
        }
        if unreadCompletedConversationIds.remove(convId) != nil {
            persistUnreadCompletedIds()
        }
        cache.removeValue(forKey: convId)
        previousPhases.removeValue(forKey: convId)
        previousBackgroundBusy.removeValue(forKey: convId)
        previousUserTurnCounts.removeValue(forKey: convId)
        conversations.removeAll { $0.id == convId }

        Task { @MainActor [weak self] in
            guard let self else { return }
            defer { self.deletingConversationIds.remove(convId) }

            // 2. Delete inside Antigravity.app (Sidebar More options -> Delete -> Confirm modal)
            let uiDeleted = await ConversationNavigator.deleteConversationInAntigravity(conversationId: convId)

            // Allow Antigravity's internal SQLite / trajectory delete handler to release file handles
            try? await Task.sleep(nanoseconds: 350_000_000)

            // 3. Clean up local brain & conversation DB files so neither side retains stale copies
            let brainDir = self.brainDirectory
            let convDBDir = self.conversationsDBDirectory
            let rootDir = self.antigravityRootDirectory
            await Task.detached(priority: .utility) {
                Self.removeLocalConversationArtifacts(
                    convId: convId,
                    brainDirectory: brainDir,
                    conversationsDBDirectory: convDBDir,
                    antigravityRootDirectory: rootDir
                )
            }.value

            if uiDeleted {
                self.showToast("🗑 已同步删除两边会话")
            } else {
                self.showToast("🗑 已删除会话及本地记录")
            }
        }
    }

    private nonisolated static func removeLocalConversationArtifacts(
        convId: String,
        brainDirectory: URL,
        conversationsDBDirectory: URL,
        antigravityRootDirectory: URL
    ) {
        guard convId.count >= 32, !convId.contains("/"), !convId.contains("..") else { return }
        let fm = FileManager.default

        // 1. Brain directory (`~/.gemini/antigravity/brain/<convId>`)
        let brainFolder = brainDirectory.appendingPathComponent(convId, isDirectory: true)
        if fm.fileExists(atPath: brainFolder.path) {
            if (try? fm.trashItem(at: brainFolder, resultingItemURL: nil)) == nil {
                try? fm.removeItem(at: brainFolder)
            }
        }

        // 2. Conversation SQLite files (`~/.gemini/antigravity/conversations/<convId>.db*`)
        for suffix in [".db", ".db-shm", ".db-wal", ".pb"] {
            let dbFile = conversationsDBDirectory.appendingPathComponent("\(convId)\(suffix)")
            if fm.fileExists(atPath: dbFile.path) {
                try? fm.removeItem(at: dbFile)
            }
        }

        // 3. Annotations if any (`~/.gemini/antigravity/annotations/<convId>*`)
        let annotationsDir = antigravityRootDirectory.appendingPathComponent("annotations", isDirectory: true)
        if let items = try? fm.contentsOfDirectory(at: annotationsDir, includingPropertiesForKeys: nil) {
            for item in items where item.lastPathComponent.hasPrefix(convId) {
                try? fm.removeItem(at: item)
            }
        }
    }

    func copyCommandAndActivate(_ command: String) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(command, forType: .string)
        showToast("已复制 \(command)，正在切回 Antigravity")
        activateAntigravityApp()
    }

    /// Populates purely synthetic open-source demo data for documentation screenshot rendering (never reads user files).
    func loadSyntheticDemoState() {
        let now = Date()
        self.nowTick = now
        self.lastRefreshDate = now
        self.isAccessibilityTrusted = true
        self.isHookPluginInstalled = true

        let demoConvs: [ConversationSnapshot] = [
            ConversationSnapshot(
                id: "demo-conv-01",
                title: "Refactor Swift Concurrency Pipeline",
                projectName: "swift-runtime",
                searchQuery: "Refactor Swift Concurrency",
                modelName: "Gemini",
                phase: .runningTool(name: "run_command", detail: "Unit test execution"),
                lastUpdatedAt: now.addingTimeInterval(-12),
                activeTurnStartedAt: now.addingTimeInterval(-48),
                stepCount: 24,
                userTurnCount: 4,
                invocationCount: 16,
                contextUsedTokens: 42000,
                latestInputTokens: 12000,
                latestCacheReadTokens: 30000,
                totalOutputTokens: 4800,
                backgroundBusy: false
            ),
            ConversationSnapshot(
                id: "demo-conv-02",
                title: "Implement Non-Activating NSPanel Hit Mask",
                projectName: "antigravity-assistant",
                searchQuery: "Implement Non-Activating NSPanel",
                modelName: "Gemini",
                phase: .completed,
                lastUpdatedAt: now.addingTimeInterval(-320),
                activeTurnStartedAt: now.addingTimeInterval(-390),
                stepCount: 18,
                userTurnCount: 3,
                invocationCount: 12,
                contextUsedTokens: 31000,
                latestInputTokens: 8000,
                latestCacheReadTokens: 23000,
                totalOutputTokens: 3200,
                backgroundBusy: false
            ),
            ConversationSnapshot(
                id: "demo-conv-03",
                title: "Design 16-Bit Pixel Robot Mascot",
                projectName: "antigravity-assistant",
                searchQuery: "Design 16-Bit Pixel Robot",
                modelName: "Gemini",
                phase: .completed,
                lastUpdatedAt: now.addingTimeInterval(-1800),
                activeTurnStartedAt: nil,
                stepCount: 30,
                userTurnCount: 5,
                invocationCount: 20,
                contextUsedTokens: 54000,
                latestInputTokens: 14000,
                latestCacheReadTokens: 40000,
                totalOutputTokens: 6100,
                backgroundBusy: false
            ),
            ConversationSnapshot(
                id: "demo-conv-04",
                title: "Add GitHub Actions Release Workflow",
                projectName: "ci-automation",
                searchQuery: "Add GitHub Actions Release",
                modelName: "Gemini",
                phase: .completed,
                lastUpdatedAt: now.addingTimeInterval(-5400),
                activeTurnStartedAt: nil,
                stepCount: 14,
                userTurnCount: 2,
                invocationCount: 9,
                contextUsedTokens: 19000,
                latestInputTokens: 6000,
                latestCacheReadTokens: 13000,
                totalOutputTokens: 2100,
                backgroundBusy: false
            ),
            ConversationSnapshot(
                id: "demo-conv-05",
                title: "Local Markdown Knowledge Graph Note",
                projectName: "default",
                searchQuery: "Local Markdown Knowledge",
                modelName: "Gemini",
                phase: .completed,
                lastUpdatedAt: now.addingTimeInterval(-93600),
                activeTurnStartedAt: nil,
                stepCount: 10,
                userTurnCount: 2,
                invocationCount: 6,
                contextUsedTokens: 15000,
                latestInputTokens: 5000,
                latestCacheReadTokens: 10000,
                totalOutputTokens: 1400,
                backgroundBusy: false
            )
        ]

        self.conversations = demoConvs
        self.pinnedConversationIds = ["demo-conv-03"]
        self.unreadCompletedConversationIds = ["demo-conv-02"]
        self.deletedConversationIds = []
        self.cloudAccounts = [
            CloudAccountQuota(
                userEmail: "developer@example.com",
                planName: "Google AI Pro",
                geminiPool: CloudPoolQuota(
                    poolName: "Gemini",
                    remaining5h: 94.2,
                    resetsAt5h: now.addingTimeInterval(3 * 3600 + 42 * 60),
                    remaining7d: 97.6,
                    resetsAt7d: now.addingTimeInterval(5 * 86400 + 14 * 3600)
                ),
                claudeGptPool: CloudPoolQuota(
                    poolName: "Claude & GPT",
                    remaining5h: 98.5,
                    resetsAt5h: now.addingTimeInterval(4 * 3600 + 15 * 60),
                    remaining7d: 99.1,
                    resetsAt7d: now.addingTimeInterval(6 * 86400 + 8 * 3600)
                ),
                readAt: now,
                isLatestActive: true,
                errorMessage: nil
            )
        ]
        self.quota = QuotaMetrics(
            fiveHourRemainingPercent: 94.2,
            fiveHourResetDate: now.addingTimeInterval(3 * 3600 + 42 * 60),
            fiveHourStepsUsed: 18,
            fiveHourTokensUsed: 142000,
            isFiveHourSynced: true,
            weeklyRemainingPercent: 97.6,
            weeklyResetDate: now.addingTimeInterval(5 * 86400 + 14 * 3600),
            weeklyStepsUsed: 86,
            isWeeklySynced: true,
            todayInvocations: 42,
            todayUserTurns: 12,
            todayTokens: 320000,
            todayConversations: 4
        )
    }
}
