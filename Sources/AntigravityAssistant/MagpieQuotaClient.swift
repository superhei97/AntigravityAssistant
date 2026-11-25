import AppKit
import Darwin
import Foundation

// MARK: - Magpie Public Quota API Models (`GET /v1/magpie/quotas` & `magpie quota antigravity --json`)

struct MagpieEnvelope: Decodable {
    let data: [MagpieProviderCard]
}

struct MagpieProviderCard: Decodable, Equatable {
    let provider: String
    let name: String?
    let kind: String?
    let plan: String?
    let user: String?
    let windows: [MagpieWindow]?
    let readAt: String?
    let last: Bool?
    let error: String?
}

struct MagpieWindow: Decodable, Equatable {
    let name: String
    let used: Double?
    let remaining: Double?
    let resetsAt: String?
    let pool: String?
    let holdsWhy: String?
}

// MARK: - Normalized Cloud Account Quota (Magpie-Style Pool Layout)

struct CloudPoolQuota: Equatable {
    let poolName: String          // e.g. "Gemini" or "Claude & GPT"
    let remaining5h: Double       // e.g. 93.88
    let resetsAt5h: Date?
    let remaining7d: Double       // e.g. 97.88
    let resetsAt7d: Date?
}

struct CloudAccountQuota: Identifiable, Equatable {
    var id: String { userEmail.isEmpty ? "default" : userEmail }
    let userEmail: String         // e.g. "user@example.com"
    let planName: String          // e.g. "Google AI Ultra"
    let geminiPool: CloudPoolQuota?
    let claudeGptPool: CloudPoolQuota?
    let readAt: Date?
    let isLatestActive: Bool
    let errorMessage: String?
}

// MARK: - Local Antigravity LanguageServer RPC Models (`RetrieveUserQuotaSummary` & `GetUserStatus`)

private struct LocalDaemonInfo: Decodable {
    let pid: Int?
    let httpsPort: Int?
    let httpPort: Int?
    let csrfToken: String
}

private struct LocalQuotaSummaryEnvelope: Decodable {
    struct ResponseBody: Decodable {
        let groups: [QuotaGroup]?
    }
    struct QuotaGroup: Decodable {
        let displayName: String?
        let buckets: [QuotaBucket]?
    }
    struct QuotaBucket: Decodable {
        let bucketId: String?
        let displayName: String?
        let window: String?
        let remainingFraction: Double?
        let resetTime: String?
    }
    let response: ResponseBody?
}

private struct LocalUserStatusEnvelope: Decodable {
    struct UserStatus: Decodable {
        struct PlanStatus: Decodable {
            struct PlanInfo: Decodable {
                let planName: String?
            }
            let planInfo: PlanInfo?
        }
        struct UserTier: Decodable {
            let name: String?
        }
        struct CascadeModelConfigData: Decodable {
            struct ClientModelConfig: Decodable {
                struct QuotaInfo: Decodable {
                    let remainingFraction: Double?
                    let resetTime: String?
                }
                let label: String?
                let modelId: String?
                let quotaInfo: QuotaInfo?
            }
            let clientModelConfigs: [ClientModelConfig]?
        }
        let name: String?
        let email: String?
        let planStatus: PlanStatus?
        let userTier: UserTier?
        let cascadeModelConfigData: CascadeModelConfigData?
    }
    let userStatus: UserStatus?
}

// MARK: - Async Quota Client (Primary: Local Antigravity LanguageServer; Fallback: Magpie)

enum MagpieQuotaClient {
    static let defaultGatewayBase = "http://127.0.0.1:3425"

    /// Dedicated direct localhost URLSession that bypasses system HTTP/SOCKS proxies.
    private static let localSession: URLSession = {
        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 3.0
        config.timeoutIntervalForResource = 4.0
        config.connectionProxyDictionary = [:]
        return URLSession(configuration: config)
    }()

    private static var candidateCliPaths: [String] {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        return [
            "/Applications/Magpie.app/Contents/MacOS/magpie",
            "\(home)/.local/bin/magpie",
            "/opt/homebrew/bin/magpie",
            "/usr/local/bin/magpie"
        ]
    }

    static var resolvedCliPath: String? {
        candidateCliPaths.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    private static let isoFractional = Date.ISO8601FormatStyle(includingFractionalSeconds: true)
    private static let isoStandard = Date.ISO8601FormatStyle(includingFractionalSeconds: false)

    static func isMagpieInstalled() -> Bool {
        resolvedCliPath != nil
    }

    /// Primary path: Query local Antigravity `language_server` (`~/.gemini/antigravity/daemon/*.json`),
    /// fallback to Magpie HTTP/CLI only if local Antigravity daemon is not running.
    static func fetchAntigravityAccounts(gatewayBase: String = defaultGatewayBase, allowCliFallback: Bool = true) async -> [CloudAccountQuota]? {
        if let localAccount = await fetchFromLocalAntigravity() {
            return [localAccount]
        }
        if let httpCards = await fetchViaHTTP(gatewayBase: gatewayBase) {
            let mapped = mapCards(httpCards)
            if !mapped.isEmpty {
                return mapped
            }
        }
        guard allowCliFallback, isMagpieInstalled() else { return nil }
        if let cliCards = await runMagpieCLI(arguments: ["quota", "antigravity", "--json"]) {
            let mapped = mapCards(cliCards)
            return mapped.isEmpty ? nil : mapped
        }
        return nil
    }

    /// Force live quota refresh directly from local Antigravity `language_server` (or Magpie fallback).
    static func forceRefreshFromCloud() async -> [CloudAccountQuota]? {
        if let localAccount = await fetchFromLocalAntigravity() {
            return [localAccount]
        }
        guard isMagpieInstalled() else { return nil }
        if let cliCards = await runMagpieCLI(arguments: ["quota", "refresh", "antigravity", "--json"]) {
            let mapped = mapCards(cliCards)
            return mapped.isEmpty ? nil : mapped
        }
        return nil
    }

    // MARK: - Local Antigravity LanguageServer Direct RPC

    private static func executablePath(for pid: pid_t) -> String {
        var buffer = [CChar](repeating: 0, count: Int(MAXPATHLEN) * 4)
        let len = proc_pidpath(pid, &buffer, UInt32(buffer.count))
        guard len > 0 else { return "" }
        return String(cString: buffer)
    }

    private static func parentPid(for pid: pid_t) -> pid_t {
        var kinfo = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, pid]
        let rc = sysctl(&mib, 4, &kinfo, &size, nil, 0)
        guard rc == 0 else { return -1 }
        return kinfo.kp_eproc.e_ppid
    }

    private static func discoverLocalDaemons() -> [LocalDaemonInfo] {
        let daemonDir = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".gemini/antigravity/daemon", isDirectory: true)
        guard let urls = try? FileManager.default.contentsOfDirectory(
            at: daemonDir,
            includingPropertiesForKeys: [.contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ) else {
            return []
        }

        let antigravityAppPids = Set(
            NSRunningApplication.runningApplications(withBundleIdentifier: "com.google.antigravity")
                .map(\.processIdentifier)
        )

        struct RankedDaemon {
            let info: LocalDaemonInfo
            let rank: Int
            let mtime: Date
        }

        var candidates: [RankedDaemon] = []
        for url in urls where url.pathExtension == "json" {
            guard let data = try? Data(contentsOf: url),
                  let info = try? JSONDecoder().decode(LocalDaemonInfo.self, from: data),
                  !info.csrfToken.isEmpty,
                  info.httpPort != nil else {
                continue
            }

            let mtime = (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            var rank = 1
            if let rawPid = info.pid, rawPid > 0 {
                let cpid = pid_t(rawPid)
                // Skip stale daemon JSON files whose process has already exited
                if kill(cpid, 0) != 0 && errno == ESRCH {
                    continue
                }
                let execPath = executablePath(for: cpid)
                let ppid = parentPid(for: cpid)
                if antigravityAppPids.contains(ppid) || execPath.contains("/Antigravity.app/") {
                    // Highest priority: daemon directly owned by /Applications/Antigravity.app
                    rank = 0
                } else if execPath.contains("/Gemini.app/") {
                    // Lowest priority: separate Gemini.app helper sharing ~/.gemini/antigravity/daemon
                    rank = 2
                }
            }
            candidates.append(RankedDaemon(info: info, rank: rank, mtime: mtime))
        }

        candidates.sort { a, b in
            if a.rank != b.rank { return a.rank < b.rank }
            return a.mtime > b.mtime
        }

        return candidates.map(\.info)
    }

    private static func fetchFromLocalAntigravity() async -> CloudAccountQuota? {
        let daemons = discoverLocalDaemons()
        for daemon in daemons {
            guard let port = daemon.httpPort else { continue }
            async let summaryTask: LocalQuotaSummaryEnvelope? = callLocalLanguageServer(
                port: port,
                csrfToken: daemon.csrfToken,
                method: "RetrieveUserQuotaSummary"
            )
            async let statusTask: LocalUserStatusEnvelope? = callLocalLanguageServer(
                port: port,
                csrfToken: daemon.csrfToken,
                method: "GetUserStatus"
            )

            let (summaryEnv, statusEnv) = await (summaryTask, statusTask)
            if summaryEnv == nil && statusEnv == nil {
                continue
            }

            let userStatus = statusEnv?.userStatus
            let email = userStatus?.email ?? userStatus?.name ?? "Antigravity 本地账号"
            let plan = userStatus?.userTier?.name
                ?? userStatus?.planStatus?.planInfo?.planName
                ?? "Antigravity"

            var geminiPool: CloudPoolQuota? = nil
            var claudePool: CloudPoolQuota? = nil

            if let groups = summaryEnv?.response?.groups {
                for group in groups {
                    let gName = (group.displayName ?? "").lowercased()
                    let buckets = group.buckets ?? []
                    if gName.contains("gemini") || buckets.contains(where: { ($0.bucketId ?? "").hasPrefix("gemini") }) {
                        geminiPool = buildPoolFromLocalBuckets(poolLabel: "Gemini", buckets: buckets)
                    } else if gName.contains("claude") || gName.contains("gpt") || buckets.contains(where: { ($0.bucketId ?? "").hasPrefix("3p") }) {
                        claudePool = buildPoolFromLocalBuckets(poolLabel: "Claude & GPT", buckets: buckets)
                    }
                }
            }

            // Fallback to clientModelConfigs if RetrieveUserQuotaSummary buckets were absent
            if geminiPool == nil || claudePool == nil,
               let configs = userStatus?.cascadeModelConfigData?.clientModelConfigs {
                if geminiPool == nil {
                    geminiPool = buildPoolFromModelConfigs(
                        poolLabel: "Gemini",
                        configs: configs.filter {
                            let label = ($0.label ?? "").lowercased()
                            return label.contains("gemini") || label.contains("orchestrator")
                        }
                    )
                }
                if claudePool == nil {
                    claudePool = buildPoolFromModelConfigs(
                        poolLabel: "Claude & GPT",
                        configs: configs.filter {
                            let label = ($0.label ?? "").lowercased()
                            return label.contains("claude") || label.contains("gpt")
                        }
                    )
                }
            }

            guard geminiPool != nil || claudePool != nil else { continue }

            return CloudAccountQuota(
                userEmail: email,
                planName: plan,
                geminiPool: geminiPool,
                claudeGptPool: claudePool,
                readAt: Date(),
                isLatestActive: true,
                errorMessage: nil
            )
        }
        return nil
    }

    private static func callLocalLanguageServer<T: Decodable>(
        port: Int,
        csrfToken: String,
        method: String
    ) async -> T? {
        guard let url = URL(string: "http://127.0.0.1:\(port)/exa.language_server_pb.LanguageServerService/\(method)") else {
            return nil
        }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue("1", forHTTPHeaderField: "Connect-Protocol-Version")
        req.setValue(csrfToken, forHTTPHeaderField: "x-codeium-csrf-token")
        req.httpBody = Data(#"{"metadata":{"ideName":"antigravity","extensionName":"antigravity","locale":"zh"}}"#.utf8)

        do {
            let (data, resp) = try await localSession.data(for: req)
            guard let http = resp as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
                return nil
            }
            return try JSONDecoder().decode(T.self, from: data)
        } catch {
            return nil
        }
    }

    private static func buildPoolFromLocalBuckets(
        poolLabel: String,
        buckets: [LocalQuotaSummaryEnvelope.QuotaBucket]
    ) -> CloudPoolQuota? {
        guard !buckets.isEmpty else { return nil }
        let b5h = buckets.first {
            let w = ($0.window ?? "").lowercased()
            let id = ($0.bucketId ?? "").lowercased()
            return w == "5h" || id.hasSuffix("-5h")
        }
        let b7d = buckets.first {
            let w = ($0.window ?? "").lowercased()
            let id = ($0.bucketId ?? "").lowercased()
            return w == "weekly" || w == "7d" || id.hasSuffix("-weekly")
        }
        guard b5h != nil || b7d != nil else { return nil }
        let rem5h = max(0.0, min(100.0, (b5h?.remainingFraction ?? 1.0) * 100.0))
        let rem7d = max(0.0, min(100.0, (b7d?.remainingFraction ?? 1.0) * 100.0))
        return CloudPoolQuota(
            poolName: poolLabel,
            remaining5h: rem5h,
            resetsAt5h: parseAnyISODate(b5h?.resetTime),
            remaining7d: rem7d,
            resetsAt7d: parseAnyISODate(b7d?.resetTime)
        )
    }

    private static func buildPoolFromModelConfigs(
        poolLabel: String,
        configs: [LocalUserStatusEnvelope.UserStatus.CascadeModelConfigData.ClientModelConfig]
    ) -> CloudPoolQuota? {
        guard let firstWithQuota = configs.first(where: { $0.quotaInfo != nil }),
              let q = firstWithQuota.quotaInfo else {
            return nil
        }
        let pct = max(0.0, min(100.0, (q.remainingFraction ?? 1.0) * 100.0))
        let resetDate = parseAnyISODate(q.resetTime)
        return CloudPoolQuota(
            poolName: poolLabel,
            remaining5h: pct,
            resetsAt5h: resetDate,
            remaining7d: pct,
            resetsAt7d: resetDate
        )
    }

    private static func fetchViaHTTP(gatewayBase: String) async -> [MagpieProviderCard]? {
        let trimmed = gatewayBase.trimmingCharacters(in: CharacterSet(charactersIn: "/ "))
        guard let url = URL(string: "\(trimmed)/v1/magpie/quotas") else { return nil }

        var req = URLRequest(url: url)
        req.timeoutInterval = 2.0

        do {
            let (data, resp) = try await URLSession.shared.data(for: req)
            guard let http = resp as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
                return nil
            }
            if let env = try? JSONDecoder().decode(MagpieEnvelope.self, from: data) {
                return env.data.filter { $0.provider == "antigravity" }
            }
            if let arr = try? JSONDecoder().decode([MagpieProviderCard].self, from: data) {
                return arr.filter { $0.provider == "antigravity" }
            }
            return nil
        } catch {
            return nil
        }
    }

    private static func runMagpieCLI(arguments: [String]) async -> [MagpieProviderCard]? {
        guard let cliPath = resolvedCliPath else { return nil }
        return await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .utility).async {
                let proc = Process()
                proc.executableURL = URL(fileURLWithPath: cliPath)
                proc.arguments = arguments

                let outPipe = Pipe()
                let errPipe = Pipe()
                proc.standardOutput = outPipe
                proc.standardError = errPipe

                do {
                    try proc.run()
                    let outData = outPipe.fileHandleForReading.readDataToEndOfFile()
                    proc.waitUntilExit()
                    guard proc.terminationStatus == 0, !outData.isEmpty else {
                        continuation.resume(returning: nil)
                        return
                    }
                    if let arr = try? JSONDecoder().decode([MagpieProviderCard].self, from: outData) {
                        continuation.resume(returning: arr.filter { $0.provider == "antigravity" })
                    } else if let env = try? JSONDecoder().decode(MagpieEnvelope.self, from: outData) {
                        continuation.resume(returning: env.data.filter { $0.provider == "antigravity" })
                    } else {
                        continuation.resume(returning: nil)
                    }
                } catch {
                    continuation.resume(returning: nil)
                }
            }
        }
    }

    private static func mapCards(_ cards: [MagpieProviderCard]) -> [CloudAccountQuota] {
        cards.compactMap { card in
            let wins = card.windows ?? []

            let geminiPool = buildPool(
                poolLabel: "Gemini",
                windows: wins.filter {
                    ($0.pool?.lowercased().contains("gemini") == true) ||
                    $0.name.lowercased().contains("gemini")
                }
            )

            let claudePool = buildPool(
                poolLabel: "Claude & GPT",
                windows: wins.filter {
                    ($0.pool?.lowercased().contains("claude") == true) ||
                    $0.name.lowercased().contains("claude")
                }
            )

            guard geminiPool != nil || claudePool != nil else { return nil }

            return CloudAccountQuota(
                userEmail: card.user ?? "Antigravity Account",
                planName: card.plan ?? "Antigravity",
                geminiPool: geminiPool,
                claudeGptPool: claudePool,
                readAt: parseAnyISODate(card.readAt),
                isLatestActive: card.last ?? false,
                errorMessage: card.error
            )
        }
    }

    private static func buildPool(poolLabel: String, windows: [MagpieWindow]) -> CloudPoolQuota? {
        guard !windows.isEmpty else { return nil }
        let win5h = windows.first {
            $0.name.lowercased().contains("5 hour") || $0.name.lowercased().contains("5h")
        }
        let win7d = windows.first {
            $0.name.lowercased().contains("7 day") || $0.name.lowercased().contains("7d") || $0.name.lowercased().contains("week")
        }
        return CloudPoolQuota(
            poolName: poolLabel,
            remaining5h: win5h?.remaining ?? 100.0,
            resetsAt5h: parseAnyISODate(win5h?.resetsAt),
            remaining7d: win7d?.remaining ?? 100.0,
            resetsAt7d: parseAnyISODate(win7d?.resetsAt)
        )
    }

    static func parseAnyISODate(_ raw: String?) -> Date? {
        guard let raw, !raw.isEmpty else { return nil }
        if let d = try? isoFractional.parse(raw) { return d }
        if let d = try? isoStandard.parse(raw) { return d }

        // Handle 6-digit microsecond timestamps like "2026-11-10T22:14:45.139088-08:00" if needed
        if let dotIdx = raw.firstIndex(of: ".") {
            let afterDot = raw[raw.index(after: dotIdx)...]
            if let tzIdx = afterDot.firstIndex(where: { $0 == "+" || $0 == "-" || $0 == "Z" }) {
                let frac = afterDot[..<tzIdx]
                if frac.count > 3 {
                    let trimmed = String(raw[..<dotIdx]) + "." + String(frac.prefix(3)) + String(afterDot[tzIdx...])
                    if let d = try? isoFractional.parse(trimmed) { return d }
                }
            }
        }
        return nil
    }
}
