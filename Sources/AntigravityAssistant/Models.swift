import Foundation
import SwiftUI

// MARK: - Agent Session Phase

enum AgentPhase: Equatable {
    case idle
    case thinking
    case runningTool(name: String, detail: String?)
    case waitingUser(detail: String?)
    case completed
    case interrupted
    case paused
    case error(detail: String?)

    var isWorking: Bool {
        switch self {
        case .thinking, .runningTool:
            return true
        default:
            return false
        }
    }

    var isPriorityActive: Bool {
        switch self {
        case .thinking, .runningTool, .waitingUser:
            return true
        default:
            return false
        }
    }

    /// Only returns a label when actively working, waiting for confirmation, or errored.
    /// Returns `nil` for idle / completed / interrupted / paused so finished/stopped labels are never shown.
    var liveDisplayLabel: String? {
        switch self {
        case .thinking:
            return "思考中"
        case .runningTool(let name, _):
            return "执行 · \(Self.friendlyToolName(name))"
        case .waitingUser:
            return "等待你确认"
        case .error:
            return "执行异常"
        case .idle, .completed, .interrupted, .paused:
            return nil
        }
    }

    var shortLabel: String {
        switch self {
        case .idle:
            return "待命"
        case .thinking:
            return "思考中"
        case .runningTool(let name, _):
            return "执行 · \(Self.friendlyToolName(name))"
        case .waitingUser:
            return "等待你确认"
        case .completed:
            return "已完成"
        case .interrupted:
            return "已中断"
        case .paused:
            return "已休眠"
        case .error:
            return "执行异常"
        }
    }

    var detailText: String? {
        switch self {
        case .runningTool(_, let detail):
            return detail
        case .waitingUser(let detail):
            return detail
        case .error(let detail):
            return detail
        default:
            return nil
        }
    }

    var accentColor: Color {
        switch self {
        case .idle:
            return Color(red: 0.44, green: 0.50, blue: 0.60)
        case .thinking:
            return Color(red: 0.10, green: 0.46, blue: 0.94)
        case .runningTool:
            return Color(red: 0.48, green: 0.28, blue: 0.90)
        case .waitingUser:
            return Color(red: 0.86, green: 0.50, blue: 0.04)
        case .completed:
            return Color(red: 0.08, green: 0.64, blue: 0.34)
        case .interrupted:
            return Color(red: 0.84, green: 0.42, blue: 0.14)
        case .paused:
            return Color(red: 0.48, green: 0.53, blue: 0.60)
        case .error:
            return Color(red: 0.86, green: 0.18, blue: 0.22)
        }
    }

    static func friendlyToolName(_ raw: String) -> String {
        switch raw {
        case "run_command": return "终端命令"
        case "read_file", "view_file": return "读取文件"
        case "write_file": return "创建文件"
        case "edit_file": return "修改代码"
        case "search_web": return "网页搜索"
        case "ask_question": return "提问确认"
        case "invoke_subagent": return "子 Agent"
        case "swarm": return "Swarm 协作"
        case "schedule": return "定时任务"
        case "manage_task": return "后台任务"
        default:
            if raw.hasPrefix("mcp_") || raw.contains("__") {
                let tail = raw.components(separatedBy: "__").last ?? raw
                return "MCP·\(tail)"
            }
            return raw
        }
    }
}

// MARK: - Conversation & Invocation Records

struct InvocationRecord: Identifiable, Equatable {
    let id: String
    let conversationId: String
    let timestamp: Date
    let modelName: String
    let inputTokens: Int
    let cacheReadTokens: Int
    let outputTokens: Int

    var totalContextTokens: Int {
        inputTokens + cacheReadTokens
    }

    var totalTokens: Int {
        inputTokens + cacheReadTokens + outputTokens
    }

    var isGeminiFamily: Bool {
        let lower = modelName.lowercased()
        if lower.contains("claude") || lower.contains("gpt") || lower.contains("sonnet") || lower.contains("opus") {
            return false
        }
        return true
    }

    /// Relative quota weight per model tier for local consumption delta tracking
    var creditWeight: Double {
        let lower = modelName.lowercased()
        if lower.contains("flash") || lower.contains("lite") {
            return 0.35
        } else if lower.contains("carbon") || lower.contains("pro") || lower.contains("ultra") {
            return 1.15
        } else {
            return 1.0
        }
    }
}

struct ConversationSnapshot: Identifiable, Equatable {
    let id: String
    let title: String
    let projectName: String
    let searchQuery: String
    let modelName: String
    let phase: AgentPhase
    let lastUpdatedAt: Date
    let activeTurnStartedAt: Date?
    let stepCount: Int
    let userTurnCount: Int
    let invocationCount: Int
    let contextUsedTokens: Int
    let latestInputTokens: Int
    let latestCacheReadTokens: Int
    let totalOutputTokens: Int
    let backgroundBusy: Bool

    var cacheHitRatio: Double {
        let denom = latestInputTokens + latestCacheReadTokens
        guard denom > 0 else { return 0 }
        return Double(latestCacheReadTokens) / Double(denom)
    }

    func contextRemainingPercent(maxTokens: Int) -> Double {
        guard maxTokens > 0 else { return 100 }
        let usedRatio = Double(contextUsedTokens) / Double(maxTokens)
        return max(0, min(100, (1.0 - usedRatio) * 100.0))
    }
}

// MARK: - Synced Official Quota Anchor

struct QuotaSyncAnchor: Codable, Equatable {
    let syncedAt: Date
    var fiveHourRemainingPercent: Double
    var fiveHourResetAt: Date
    var weeklyRemainingPercent: Double
    var weeklyResetAt: Date
}

// MARK: - Quota & Remaining Snapshot (Aligned with Antigravity Gemini Models UI)

struct QuotaMetrics: Equatable {
    let fiveHourRemainingPercent: Double
    let fiveHourResetDate: Date?
    let fiveHourStepsUsed: Int
    let fiveHourTokensUsed: Int
    let isFiveHourSynced: Bool

    let weeklyRemainingPercent: Double
    let weeklyResetDate: Date?
    let weeklyStepsUsed: Int
    let isWeeklySynced: Bool

    let todayInvocations: Int
    let todayUserTurns: Int
    let todayTokens: Int
    let todayConversations: Int

    static let empty = QuotaMetrics(
        fiveHourRemainingPercent: 100,
        fiveHourResetDate: nil,
        fiveHourStepsUsed: 0,
        fiveHourTokensUsed: 0,
        isFiveHourSynced: false,
        weeklyRemainingPercent: 100,
        weeklyResetDate: nil,
        weeklyStepsUsed: 0,
        isWeeklySynced: false,
        todayInvocations: 0,
        todayUserTurns: 0,
        todayTokens: 0,
        todayConversations: 0
    )

    static func statusColor(for remainingPercent: Double) -> Color {
        if remainingPercent <= 18 {
            return Color(red: 0.86, green: 0.20, blue: 0.24)
        } else if remainingPercent <= 45 {
            return Color(red: 0.86, green: 0.50, blue: 0.04)
        } else {
            return Color(red: 0.08, green: 0.64, blue: 0.33)
        }
    }
}

// MARK: - Optional Hook Event State

struct HookLiveEvent: Codable, Equatable {
    let event: String
    let timestamp: Double
    let conversationId: String?
    let modelName: String?
    let toolName: String?
    let toolDetail: String?
    let terminationReason: String?
    let fullyIdle: Bool?
    let error: String?
}

// MARK: - Plan Presets & Settings

enum PlanPreset: String, CaseIterable, Identifiable {
    case standard = "standard"
    case pro = "pro"
    case ultra = "ultra"
    case custom = "custom"

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .standard: return "Baseline / Standard"
        case .pro: return "Google AI Pro (推荐)"
        case .ultra: return "Google AI Ultra"
        case .custom: return "自定义容量权重"
        }
    }

    /// Weighted Gemini step units corresponding to 100% of a 5-hour bucket
    var default5hCapacity: Int {
        switch self {
        case .standard: return 2200
        case .pro: return 6600
        case .ultra: return 16000
        case .custom: return 6600
        }
    }

    /// Weighted Gemini step units corresponding to 100% of a 7-day weekly bucket
    var defaultWeeklyCapacity: Int {
        switch self {
        case .standard: return 6000
        case .pro: return 16600
        case .ultra: return 45000
        case .custom: return 16600
        }
    }
}

@MainActor
final class AppSettings: ObservableObject {
    private let defaults = UserDefaults.standard

    @Published var planPreset: PlanPreset {
        didSet {
            defaults.set(planPreset.rawValue, forKey: "v2_planPreset")
            if planPreset != .custom {
                capacity5hUnits = planPreset.default5hCapacity
                capacityWeeklyUnits = planPreset.defaultWeeklyCapacity
            }
        }
    }

    @Published var capacity5hUnits: Int {
        didSet { defaults.set(max(100, capacity5hUnits), forKey: "v2_capacity5hUnits") }
    }

    @Published var capacityWeeklyUnits: Int {
        didSet { defaults.set(max(300, capacityWeeklyUnits), forKey: "v2_capacityWeeklyUnits") }
    }

    @Published var maxContextTokens: Int {
        didSet { defaults.set(max(50_000, maxContextTokens), forKey: "maxContextTokens") }
    }

    @Published var notifyOnComplete: Bool {
        didSet { defaults.set(notifyOnComplete, forKey: "notifyOnComplete") }
    }

    @Published var minTurnDurationForAlert: Double {
        didSet { defaults.set(minTurnDurationForAlert, forKey: "minTurnDurationForAlert") }
    }

    @Published var notifyOnWaitingOrError: Bool {
        didSet { defaults.set(notifyOnWaitingOrError, forKey: "notifyOnWaitingOrError") }
    }

    @Published var notifyOnLowRemaining: Bool {
        didSet { defaults.set(notifyOnLowRemaining, forKey: "notifyOnLowRemaining") }
    }

    @Published var lowQuotaThreshold: Double {
        didSet { defaults.set(lowQuotaThreshold, forKey: "lowQuotaThreshold") }
    }

    @Published var lowContextThreshold: Double {
        didSet { defaults.set(lowContextThreshold, forKey: "lowContextThreshold") }
    }

    @Published var soundEnabled: Bool {
        didSet { defaults.set(soundEnabled, forKey: "soundEnabled") }
    }

    @Published var alwaysOnTop: Bool {
        didSet { defaults.set(alwaysOnTop, forKey: "alwaysOnTop") }
    }

    @Published var pillOpacity: Double {
        didSet { defaults.set(pillOpacity, forKey: "pillOpacity") }
    }

    @Published var syncAnchor: QuotaSyncAnchor? {
        didSet {
            if let anchor = syncAnchor,
               let data = try? JSONEncoder().encode(anchor) {
                defaults.set(data, forKey: "v2_quotaSyncAnchor")
            } else {
                defaults.removeObject(forKey: "v2_quotaSyncAnchor")
            }
        }
    }

    init() {
        let presetRaw = defaults.string(forKey: "v2_planPreset") ?? PlanPreset.pro.rawValue
        let preset = PlanPreset(rawValue: presetRaw) ?? .pro
        self.planPreset = preset

        let saved5h = defaults.integer(forKey: "v2_capacity5hUnits")
        self.capacity5hUnits = saved5h > 0 ? saved5h : preset.default5hCapacity

        let savedWeekly = defaults.integer(forKey: "v2_capacityWeeklyUnits")
        self.capacityWeeklyUnits = savedWeekly > 0 ? savedWeekly : preset.defaultWeeklyCapacity

        let savedCtx = defaults.integer(forKey: "maxContextTokens")
        self.maxContextTokens = savedCtx > 0 ? savedCtx : 1_000_000

        self.notifyOnComplete = defaults.object(forKey: "notifyOnComplete") as? Bool ?? true
        self.minTurnDurationForAlert = defaults.object(forKey: "minTurnDurationForAlert") as? Double ?? 4.0
        self.notifyOnWaitingOrError = defaults.object(forKey: "notifyOnWaitingOrError") as? Bool ?? true
        self.notifyOnLowRemaining = defaults.object(forKey: "notifyOnLowRemaining") as? Bool ?? true
        self.lowQuotaThreshold = defaults.object(forKey: "lowQuotaThreshold") as? Double ?? 20.0
        self.lowContextThreshold = defaults.object(forKey: "lowContextThreshold") as? Double ?? 25.0
        self.soundEnabled = defaults.object(forKey: "soundEnabled") as? Bool ?? true
        self.alwaysOnTop = defaults.object(forKey: "alwaysOnTop") as? Bool ?? true
        self.pillOpacity = defaults.object(forKey: "pillOpacity") as? Double ?? 0.96

        if let data = defaults.data(forKey: "v2_quotaSyncAnchor"),
           let decoded = try? JSONDecoder().decode(QuotaSyncAnchor.self, from: data) {
            self.syncAnchor = decoded
        } else {
            self.syncAnchor = nil
        }
    }

    func applyManualSync(
        fiveHourPercent: Double,
        fiveHourRefreshInSeconds: TimeInterval,
        weeklyPercent: Double,
        weeklyRefreshInSeconds: TimeInterval,
        now: Date = Date()
    ) {
        syncAnchor = QuotaSyncAnchor(
            syncedAt: now,
            fiveHourRemainingPercent: max(0, min(100, fiveHourPercent)),
            fiveHourResetAt: now.addingTimeInterval(max(60, fiveHourRefreshInSeconds)),
            weeklyRemainingPercent: max(0, min(100, weeklyPercent)),
            weeklyResetAt: now.addingTimeInterval(max(60, weeklyRefreshInSeconds))
        )
    }

    func clearSyncAnchor() {
        syncAnchor = nil
    }

    /// Parses text copied directly from Antigravity's Settings -> Usage ("Gemini Models") section
    @discardableResult
    func syncFromOfficialUsageText(_ rawText: String, now: Date = Date()) -> Bool {
        let lower = rawText.lowercased()
        guard lower.contains("%") else { return false }

        var parsedWeeklyPct: Double?
        var parsedWeeklySec: TimeInterval?
        var parsed5hPct: Double?
        var parsed5hSec: TimeInterval?

        // Split around "five hour" / "5-hour" vs "weekly" sections
        let weeklyRange = lower.range(of: "weekly limit") ?? lower.range(of: "weekly")
        let fiveHourRange = lower.range(of: "five hour limit") ?? lower.range(of: "5-hour") ?? lower.range(of: "five hour")

        if let wRange = weeklyRange, let fRange = fiveHourRange {
            if wRange.lowerBound < fRange.lowerBound {
                let weeklyChunk = String(lower[wRange.lowerBound..<fRange.lowerBound])
                let fiveChunk = String(lower[fRange.lowerBound...])
                parsedWeeklyPct = Self.firstPercent(in: weeklyChunk)
                parsedWeeklySec = Self.parseRefreshDuration(in: weeklyChunk)
                parsed5hPct = Self.firstPercent(in: fiveChunk)
                parsed5hSec = Self.parseRefreshDuration(in: fiveChunk)
            } else {
                let fiveChunk = String(lower[fRange.lowerBound..<wRange.lowerBound])
                let weeklyChunk = String(lower[wRange.lowerBound...])
                parsed5hPct = Self.firstPercent(in: fiveChunk)
                parsed5hSec = Self.parseRefreshDuration(in: fiveChunk)
                parsedWeeklyPct = Self.firstPercent(in: weeklyChunk)
                parsedWeeklySec = Self.parseRefreshDuration(in: weeklyChunk)
            }
        } else if let fRange = fiveHourRange {
            let fiveChunk = String(lower[fRange.lowerBound...])
            parsed5hPct = Self.firstPercent(in: fiveChunk)
            parsed5hSec = Self.parseRefreshDuration(in: fiveChunk)
        } else if let wRange = weeklyRange {
            let weeklyChunk = String(lower[wRange.lowerBound...])
            parsedWeeklyPct = Self.firstPercent(in: weeklyChunk)
            parsedWeeklySec = Self.parseRefreshDuration(in: weeklyChunk)
        }

        guard parsed5hPct != nil || parsedWeeklyPct != nil else { return false }

        let current5hPct = parsed5hPct ?? syncAnchor?.fiveHourRemainingPercent ?? 95
        let current5hSec = parsed5hSec ?? syncAnchor.map { max(300, $0.fiveHourResetAt.timeIntervalSince(now)) } ?? (5 * 3600)
        let currentWeeklyPct = parsedWeeklyPct ?? syncAnchor?.weeklyRemainingPercent ?? 98
        let currentWeeklySec = parsedWeeklySec ?? syncAnchor.map { max(3600, $0.weeklyResetAt.timeIntervalSince(now)) } ?? (7 * 86400)

        applyManualSync(
            fiveHourPercent: current5hPct,
            fiveHourRefreshInSeconds: current5hSec,
            weeklyPercent: currentWeeklyPct,
            weeklyRefreshInSeconds: currentWeeklySec,
            now: now
        )
        return true
    }

    private static func firstPercent(in text: String) -> Double? {
        guard let regex = try? NSRegularExpression(pattern: #"(\d{1,3}(?:\.\d+)?)\s*%"#) else { return nil }
        let nsRange = NSRange(text.startIndex..<text.endIndex, in: text)
        guard let match = regex.firstMatch(in: text, range: nsRange),
              let r = Range(match.range(at: 1), in: text) else {
            return nil
        }
        return Double(text[r])
    }

    private static func parseRefreshDuration(in text: String) -> TimeInterval? {
        let days = extractUnit(pattern: #"(\d+)\s*(?:days?|d|天)"#, in: text) ?? 0
        let hours = extractUnit(pattern: #"(\d+)\s*(?:hours?|hrs?|h|小时)"#, in: text) ?? 0
        let mins = extractUnit(pattern: #"(\d+)\s*(?:minutes?|mins?|m|分钟|分)"#, in: text) ?? 0
        let total = TimeInterval(days * 86400 + hours * 3600 + mins * 60)
        return total > 0 ? total : nil
    }

    private static func extractUnit(pattern: String, in text: String) -> Int? {
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return nil }
        let nsRange = NSRange(text.startIndex..<text.endIndex, in: text)
        guard let match = regex.firstMatch(in: text, range: nsRange),
              let r = Range(match.range(at: 1), in: text) else {
            return nil
        }
        return Int(text[r])
    }
}

struct SessionGroupSection: Identifiable, Equatable {
    let id: String
    let title: String
    let isPriority: Bool
    let conversations: [ConversationSnapshot]
}

// MARK: - Formatting Helpers (UTC+8 Beijing Time)

enum TimeFormatters {
    static let utc8TimeZone = TimeZone(identifier: "Asia/Shanghai") ?? TimeZone(secondsFromGMT: 8 * 3600)!

    static let timeWithSecondsUTC8: DateFormatter = {
        let df = DateFormatter()
        df.timeZone = utc8TimeZone
        df.dateFormat = "HH:mm:ss"
        return df
    }()

    static let shortTimeUTC8: DateFormatter = {
        let df = DateFormatter()
        df.timeZone = utc8TimeZone
        df.dateFormat = "HH:mm"
        return df
    }()

    static let dateAndTimeUTC8: DateFormatter = {
        let df = DateFormatter()
        df.timeZone = utc8TimeZone
        df.dateFormat = "MM-dd HH:mm"
        return df
    }()

    static var utc8Calendar: Calendar = {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = utc8TimeZone
        return cal
    }()

    static func formatUTC8(_ date: Date, includeSeconds: Bool = false) -> String {
        let base = includeSeconds ? timeWithSecondsUTC8.string(from: date) : shortTimeUTC8.string(from: date)
        return "\(base) (UTC+8)"
    }

    static func formatDateTimeUTC8(_ date: Date) -> String {
        "\(dateAndTimeUTC8.string(from: date)) (UTC+8)"
    }

    static func chineseWeekdayUTC8(_ date: Date) -> String {
        let weekday = utc8Calendar.component(.weekday, from: date)
        switch weekday {
        case 1: return "星期日"
        case 2: return "星期一"
        case 3: return "星期二"
        case 4: return "星期三"
        case 5: return "星期四"
        case 6: return "星期五"
        case 7: return "星期六"
        default: return "星期一"
        }
    }

    static func dayBucketUTC8(for date: Date, now: Date) -> (key: String, label: String) {
        let cal = utc8Calendar
        let todayStart = cal.startOfDay(for: now)
        let targetStart = cal.startOfDay(for: date)
        let daysAgo = cal.dateComponents([.day], from: targetStart, to: todayStart).day ?? 0

        if daysAgo <= 0 {
            return ("day-0", "今天")
        } else if daysAgo == 1 {
            return ("day-1", "昨天")
        } else if daysAgo >= 2 && daysAgo <= 6 {
            return ("day-\(daysAgo)", chineseWeekdayUTC8(date))
        } else {
            return ("older", "更早")
        }
    }

    static func formatDuration(_ seconds: TimeInterval) -> String {
        let total = max(0, Int(seconds.rounded()))
        let d = total / 86400
        let h = (total % 86400) / 3600
        let m = (total % 3600) / 60
        let s = total % 60
        if d > 0 {
            return "\(d)天 \(h)小时"
        } else if h > 0 {
            return "\(h)小时 \(m)分"
        } else if m > 0 {
            return String(format: "%dm %02ds", m, s)
        } else {
            return "\(s)s"
        }
    }

    static func formatCompactTokens(_ count: Int) -> String {
        if count >= 1_000_000 {
            return String(format: "%.2fM", Double(count) / 1_000_000.0)
        } else if count >= 1_000 {
            return String(format: "%.1fK", Double(count) / 1_000.0)
        } else {
            return "\(count)"
        }
    }
}
