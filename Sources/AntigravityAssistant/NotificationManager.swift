import AppKit
import Foundation
import UserNotifications

@MainActor
final class NotificationManager: NSObject, UNUserNotificationCenterDelegate {
    static let shared = NotificationManager()

    var onNotificationJumpRequested: ((String) -> Void)?

    private var hasRequestedAuth = false
    private var lastLowQuotaAlertAt: Date?
    private var lastLowContextAlertByConv: [String: Date] = [:]

    private var isRunningInAppBundle: Bool {
        Bundle.main.bundleIdentifier != nil && Bundle.main.bundlePath.hasSuffix(".app")
    }

    func prepareIfNeeded() {
        guard isRunningInAppBundle, !hasRequestedAuth else { return }
        hasRequestedAuth = true
        let center = UNUserNotificationCenter.current()
        center.delegate = self
        center.requestAuthorization(options: [.alert, .sound, .badge]) { _, _ in }
    }

    func playSound(named soundName: String, enabled: Bool) {
        guard enabled else { return }
        if let sound = NSSound(named: NSSound.Name(soundName)) {
            sound.play()
        } else {
            NSSound.beep()
        }
    }

    func notifyTurnCompleted(conversation: ConversationSnapshot, duration: TimeInterval?, settings: AppSettings) {
        if let dur = duration, dur < settings.minTurnDurationForAlert {
            return
        }
        playSound(named: "Glass", enabled: settings.soundEnabled)
        guard settings.notifyOnComplete else { return }

        let durText = duration.map { " · 耗时 \(TimeFormatters.formatDuration($0))" } ?? ""
        sendBanner(
            title: "✨ Antigravity 已完成回复\(durText)",
            body: "\(conversation.title)（点击直接定位到此会话）",
            conversationId: conversation.id
        )
    }

    func notifyWaitingForUser(conversation: ConversationSnapshot, detail: String?, settings: AppSettings) {
        playSound(named: "Purr", enabled: settings.soundEnabled)
        guard settings.notifyOnWaitingOrError else { return }
        sendBanner(
            title: "🙋 Antigravity 等待你的确认",
            body: detail ?? conversation.title,
            conversationId: conversation.id
        )
    }

    func notifyError(conversation: ConversationSnapshot, detail: String?, settings: AppSettings) {
        playSound(named: "Basso", enabled: settings.soundEnabled)
        guard settings.notifyOnWaitingOrError else { return }
        sendBanner(
            title: "⚠️ Antigravity 会话执行异常",
            body: detail ?? conversation.title,
            conversationId: conversation.id
        )
    }

    func notifyLowQuotaIfNeeded(remainingPercent: Double, settings: AppSettings) {
        guard settings.notifyOnLowRemaining, remainingPercent <= settings.lowQuotaThreshold else { return }
        let now = Date()
        if let last = lastLowQuotaAlertAt, now.timeIntervalSince(last) < 1800 {
            return
        }
        lastLowQuotaAlertAt = now
        playSound(named: "Funk", enabled: settings.soundEnabled)
        sendBanner(
            title: "⚡️ 5h 配额 Remaining 预警 (\(Int(remainingPercent.rounded()))%)",
            body: "当前配额剩余 \(Int(remainingPercent.rounded()))%，建议合理规划高消耗任务。",
            conversationId: nil
        )
    }

    func notifyLowContextIfNeeded(conversation: ConversationSnapshot, remainingPercent: Double, settings: AppSettings) {
        guard settings.notifyOnLowRemaining, remainingPercent <= settings.lowContextThreshold else { return }
        let now = Date()
        if let last = lastLowContextAlertByConv[conversation.id], now.timeIntervalSince(last) < 1800 {
            return
        }
        lastLowContextAlertByConv[conversation.id] = now
        playSound(named: "Tink", enabled: settings.soundEnabled)
        sendBanner(
            title: "🧠 上下文 Remaining 剩余 \(Int(remainingPercent.rounded()))%",
            body: "「\(conversation.title)」上下文较长，点击跳转后发送 /compact 释放空间。",
            conversationId: conversation.id
        )
    }

    private func sendBanner(title: String, body: String, conversationId: String?) {
        if isRunningInAppBundle {
            prepareIfNeeded()
            let content = UNMutableNotificationContent()
            content.title = title
            content.body = body
            if let conversationId {
                content.userInfo = ["conversationId": conversationId]
            }
            let req = UNNotificationRequest(
                identifier: UUID().uuidString,
                content: content,
                trigger: nil
            )
            UNUserNotificationCenter.current().add(req, withCompletionHandler: nil)
        } else {
            let safeTitle = title.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
            let safeBody = body.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
            let proc = Process()
            proc.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
            proc.arguments = ["-e", "display notification \"\(safeBody)\" with title \"\(safeTitle)\""]
            try? proc.run()
        }
    }

    // MARK: - UNUserNotificationCenterDelegate

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification,
        withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void
    ) {
        completionHandler([.banner, .sound])
    }

    nonisolated func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse,
        withCompletionHandler completionHandler: @escaping () -> Void
    ) {
        let convId = response.notification.request.content.userInfo["conversationId"] as? String
        Task { @MainActor in
            if let convId, !convId.isEmpty {
                NotificationManager.shared.onNotificationJumpRequested?(convId)
            }
        }
        completionHandler()
    }
}
