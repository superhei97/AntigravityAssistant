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

// MARK: - Async Magpie Client

enum MagpieQuotaClient {
    static let defaultGatewayBase = "http://127.0.0.1:3425"

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

    /// Fast path: Query Magpie's documented local HTTP gateway (`GET /v1/magpie/quotas`), fallback to CLI `--json`.
    static func fetchAntigravityAccounts(gatewayBase: String = defaultGatewayBase, allowCliFallback: Bool = true) async -> [CloudAccountQuota]? {
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

    /// Force live upstream cloud refresh (`magpie quota refresh antigravity --json`)
    static func forceRefreshFromCloud() async -> [CloudAccountQuota]? {
        guard isMagpieInstalled() else { return nil }
        if let cliCards = await runMagpieCLI(arguments: ["quota", "refresh", "antigravity", "--json"]) {
            let mapped = mapCards(cliCards)
            return mapped.isEmpty ? nil : mapped
        }
        return nil
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
        cards.map { card in
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
