import AppKit
import ApplicationServices
import Carbon
import Foundation

enum NavigationOutcome: Equatable {
    case alreadyActive
    case jumpedViaDirectLink
    case jumpedViaPickerSearch
    case activatedAppOnly(reason: String)
    case missingAccessibilityPermission
    case appNotFound
}

struct LiveSidebarItem: Equatable, Sendable {
    let conversationId: String
    let title: String?
    let isRunning: Bool
    let isIdleInUI: Bool
}

struct LiveAntigravityUIState: Equatable, Sendable {
    var sidebarItems: [String: LiveSidebarItem] = [:]
    var openConversationId: String? = nil
    var openConversationCancelledBanner: Bool = false
    var openConversationHasStopButton: Bool = false
}

enum ConversationNavigator {
    static let antigravityBundleId = "com.google.antigravity"
    static let antigravityAppPath = "/Applications/Antigravity.app"

    static var isAccessibilityTrusted: Bool {
        // Passing nil options queries live TCC state cleanly without prompting
        AXIsProcessTrustedWithOptions(nil)
    }

    @discardableResult
    static func requestAccessibilityPermission(openSystemSettings: Bool = true) -> Bool {
        let promptKey = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        let options = [promptKey: true] as CFDictionary
        let trusted = AXIsProcessTrustedWithOptions(options)
        if !trusted && openSystemSettings {
            if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
                NSWorkspace.shared.open(url)
            }
        }
        return trusted
    }

    // MARK: - Background Live UI State Polling (Detects User Cancellation & Official Titles)

    static func pollLiveUIState() -> LiveAntigravityUIState? {
        guard isAccessibilityTrusted else { return nil }
        guard let pid = NSRunningApplication.runningApplications(withBundleIdentifier: antigravityBundleId).first?.processIdentifier else {
            return nil
        }

        let axApp = AXUIElementCreateApplication(pid)
        AXUIElementSetAttributeValue(axApp, "AXManualAccessibility" as CFString, kCFBooleanTrue)

        guard let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString),
              !windows.isEmpty else {
            return nil
        }

        var state = LiveAntigravityUIState()

        for win in windows {
            // 1. Scan active Hub WebArea + Sidebar
            if let hubWebArea = findHubWebArea(in: win, depth: 0) {
                let webURL = copyURLStringAttribute(hubWebArea) ?? ""
                if let cid = extractConversationId(fromURL: webURL) {
                    state.openConversationId = cid
                }

                if let sidebar = findSidebar(in: hubWebArea, depth: 0) {
                    collectSidebarRows(in: sidebar, depth: 0, into: &state.sidebarItems)
                }

                // If open conversation isn't in sidebar (e.g., sidebar collapsed), inspect open WebArea controls
                if let openCid = state.openConversationId, state.sidebarItems[openCid] == nil {
                    let activeCheck = inspectOpenWebAreaState(hubWebArea)
                    state.openConversationHasStopButton = activeCheck.hasStop
                    state.openConversationCancelledBanner = activeCheck.hasCancelledText
                }
            }
        }

        return state
    }

    // MARK: - Direct Jump to Target Conversation

    @MainActor
    static func jump(to conversation: ConversationSnapshot) async -> NavigationOutcome {
        guard FileManager.default.fileExists(atPath: antigravityAppPath) else {
            return .appNotFound
        }

        guard isAccessibilityTrusted else {
            _ = requestAccessibilityPermission(openSystemSettings: true)
            await activateOrLaunchAntigravity()
            return .missingAccessibilityPermission
        }

        guard let runningApp = await activateOrLaunchAntigravity() else {
            return .appNotFound
        }

        let pid = runningApp.processIdentifier
        let axApp = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(axApp, 0.8)
        AXUIElementSetAttributeValue(axApp, "AXManualAccessibility" as CFString, kCFBooleanTrue)

        await ensureWindowVisibleAndRaised(axApp: axApp, runningApp: runningApp)

        let targetPathFragment = "/c/\(conversation.id)"
        if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
            return .alreadyActive
        }

        // Fast Path 1: Direct Sidebar AXLink scan
        for attempt in 0..<3 {
            let scan = findTargetConversationLink(axApp: axApp, targetPathFragment: targetPathFragment)
            if scan.isAlreadyViewingTarget {
                return .alreadyActive
            }
            if let linkElement = scan.matchingLink,
               await pressAndVerify(linkElement, axApp: axApp, targetPathFragment: targetPathFragment) {
                return .jumpedViaDirectLink
            }
            if scan.hasWebArea && !scan.sidebarFound && attempt == 0 {
                // Uncollapse Sidebar if hidden
                if pressButtonInHub(axApp: axApp, matchingDescription: "Toggle Sidebar") {
                    try? await Task.sleep(nanoseconds: 150_000_000)
                    continue
                }
            }
            if scan.sidebarFound {
                break
            }
            try? await Task.sleep(nanoseconds: 80_000_000)
        }

        // Fast Path 2: Auto-expand collapsed Project / Conversations group in Sidebar (Antigravity 2.33+)
        if await expandSidebarSectionForProject(axApp: axApp, projectName: conversation.projectName) {
            let postExpandScan = findTargetConversationLink(axApp: axApp, targetPathFragment: targetPathFragment)
            if postExpandScan.isAlreadyViewingTarget {
                return .alreadyActive
            }
            if let linkElement = postExpandScan.matchingLink,
               await pressAndVerify(linkElement, axApp: axApp, targetPathFragment: targetPathFragment) {
                return .jumpedViaDirectLink
            }
        }

        // Fallback Path: Older conversation in deep history via `Cmd + K` FastPick
        let pickerSuccess = await navigateViaConversationPicker(
            axApp: axApp,
            runningApp: runningApp,
            conversation: conversation,
            targetPathFragment: targetPathFragment
        )
        if pickerSuccess {
            return .jumpedViaPickerSearch
        }

        return .activatedAppOnly(reason: "已切回 Antigravity，请在侧栏或 ⌘K 中查看该会话")
    }

    // MARK: - Delete Conversation Inside Antigravity UI

    @MainActor
    static func deleteConversationInAntigravity(conversationId: String) async -> Bool {
        guard isAccessibilityTrusted else { return false }
        guard let pid = NSRunningApplication.runningApplications(withBundleIdentifier: antigravityBundleId).first?.processIdentifier else {
            return false
        }

        let axApp = AXUIElementCreateApplication(pid)
        AXUIElementSetAttributeValue(axApp, "AXManualAccessibility" as CFString, kCFBooleanTrue)

        guard let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString),
              !windows.isEmpty else {
            return false
        }

        for win in windows {
            guard let hubWebArea = findHubWebArea(in: win, depth: 0),
                  let sidebar = findSidebar(in: hubWebArea, depth: 0),
                  let rowGroup = findSidebarRowContainer(in: sidebar, conversationId: conversationId, depth: 0) else {
                continue
            }

            // 1. If the conversation is actively running, click "Stop execution" first
            if let stopBtn = findButtonInSubtree(rowGroup, matchingDescriptions: ["Stop execution"], maxDepth: 6) {
                _ = AXUIElementPerformAction(stopBtn, kAXPressAction as CFString)
                try? await Task.sleep(nanoseconds: 220_000_000)
            }

            // 2. Find the row's "More options" menu button (`AXPopUpButton`)
            guard let moreBtn = findElementInSubtree(
                rowGroup,
                role: "AXPopUpButton",
                matchingDescriptions: ["More options"],
                maxDepth: 6
            ) ?? findButtonInSubtree(rowGroup, matchingDescriptions: ["More options"], maxDepth: 6) else {
                continue
            }

            _ = AXUIElementPerformAction(moreBtn, "AXScrollToVisible" as CFString)
            AXUIElementSetMessagingTimeout(moreBtn, 0.8)
            _ = AXUIElementPerformAction(moreBtn, kAXShowMenuAction as CFString)

            // 3. Locate the opened AXMenu -> "Delete" / "Delete Permanently" AXMenuItem
            var deleteMenuItem: AXUIElement? = nil
            var openedMenu: AXUIElement? = nil
            for _ in 0..<8 {
                try? await Task.sleep(nanoseconds: 65_000_000)
                if let found = findDeleteMenuItem(in: win) ?? findDeleteMenuItem(in: axApp) {
                    openedMenu = found.menu
                    deleteMenuItem = found.item
                    break
                }
            }

            guard let menuItem = deleteMenuItem else {
                if let openedMenu {
                    _ = AXUIElementPerformAction(openedMenu, kAXCancelAction as CFString)
                }
                continue
            }

            AXUIElementSetMessagingTimeout(menuItem, 0.8)
            let menuPressErr = AXUIElementPerformAction(menuItem, kAXPressAction as CFString)
            if menuPressErr != .success {
                if let openedMenu {
                    _ = AXUIElementPerformAction(openedMenu, kAXCancelAction as CFString)
                }
                continue
            }

            // 4. Confirm the "Delete Conversation" modal ("Delete" / "Delete Permanently" AXButton)
            for _ in 0..<12 {
                try? await Task.sleep(nanoseconds: 75_000_000)
                if let confirmBtn = findModalDeleteConfirmButton(in: hubWebArea) ?? findModalDeleteConfirmButton(in: win) {
                    if AXUIElementPerformAction(confirmBtn, kAXPressAction as CFString) == .success {
                        return true
                    }
                }
            }

            return true
        }

        return false
    }

    private static func findSidebarRowContainer(
        in element: AXUIElement,
        conversationId: String,
        depth: Int
    ) -> AXUIElement? {
        guard depth <= 28 else { return nil }
        guard let children = copyElementArrayAttribute(element, kAXChildrenAttribute as CFString),
              !children.isEmpty else {
            return nil
        }

        for child in children {
            if copyStringAttribute(child, kAXRoleAttribute as CFString) == "AXLink",
               let u = copyURLStringAttribute(child),
               extractConversationId(fromURL: u) == conversationId {
                return element
            }
        }

        for child in children {
            if let hit = findSidebarRowContainer(in: child, conversationId: conversationId, depth: depth + 1) {
                return hit
            }
        }
        return nil
    }

    private static func findElementInSubtree(
        _ element: AXUIElement,
        role: String,
        matchingDescriptions: Set<String>,
        maxDepth: Int
    ) -> AXUIElement? {
        guard maxDepth >= 0 else { return nil }
        let elRole = copyStringAttribute(element, kAXRoleAttribute as CFString) ?? ""
        if elRole == role {
            let desc = copyStringAttribute(element, kAXDescriptionAttribute as CFString) ?? ""
            let title = copyStringAttribute(element, kAXTitleAttribute as CFString) ?? ""
            if matchingDescriptions.contains(desc) || matchingDescriptions.contains(title) {
                return element
            }
        }
        guard let children = copyElementArrayAttribute(element, kAXChildrenAttribute as CFString) else {
            return nil
        }
        for child in children {
            if let hit = findElementInSubtree(child, role: role, matchingDescriptions: matchingDescriptions, maxDepth: maxDepth - 1) {
                return hit
            }
        }
        return nil
    }

    private static func findButtonInSubtree(
        _ element: AXUIElement,
        matchingDescriptions: Set<String>,
        maxDepth: Int
    ) -> AXUIElement? {
        findElementInSubtree(element, role: "AXButton", matchingDescriptions: matchingDescriptions, maxDepth: maxDepth)
    }

    private static func findDeleteMenuItem(in root: AXUIElement) -> (menu: AXUIElement, item: AXUIElement)? {
        var queue: [(AXUIElement, Int, AXUIElement?)] = [(root, 0, nil)]
        var head = 0
        let targetLabels: Set<String> = ["Delete", "Delete Permanently", "删除", "永久删除"]

        while head < queue.count && head < 1500 {
            let (cur, d, currentMenu) = queue[head]
            head += 1

            let role = copyStringAttribute(cur, kAXRoleAttribute as CFString) ?? ""
            if role == "AXMenuBar" { continue }

            let nextMenu = (role == "AXMenu") ? cur : currentMenu
            if role == "AXMenuItem" {
                let title = (copyStringAttribute(cur, kAXTitleAttribute as CFString) ?? "")
                    .trimmingCharacters(in: .whitespacesAndNewlines)
                let desc = (copyStringAttribute(cur, kAXDescriptionAttribute as CFString) ?? "")
                    .trimmingCharacters(in: .whitespacesAndNewlines)
                if targetLabels.contains(title) || targetLabels.contains(desc) {
                    if let menu = nextMenu {
                        return (menu, cur)
                    }
                }
            }

            if d < 12, let cs = copyElementArrayAttribute(cur, kAXChildrenAttribute as CFString) {
                for c in cs { queue.append((c, d + 1, nextMenu)) }
            }
        }
        return nil
    }

    private static func findModalDeleteConfirmButton(in root: AXUIElement) -> AXUIElement? {
        var queue: [(AXUIElement, Int)] = [(root, 0)]
        var head = 0
        let confirmLabels: Set<String> = ["Delete", "Delete Permanently", "删除", "永久删除"]

        while head < queue.count && head < 2200 {
            let (cur, d) = queue[head]
            head += 1

            let role = copyStringAttribute(cur, kAXRoleAttribute as CFString) ?? ""
            let desc = copyStringAttribute(cur, kAXDescriptionAttribute as CFString) ?? ""
            if role == "AXMenuBar" || desc == "Sidebar" { continue }

            if role == "AXButton" {
                let title = (copyStringAttribute(cur, kAXTitleAttribute as CFString) ?? "")
                    .trimmingCharacters(in: .whitespacesAndNewlines)
                let trimmedDesc = desc.trimmingCharacters(in: .whitespacesAndNewlines)
                if confirmLabels.contains(title) || confirmLabels.contains(trimmedDesc) {
                    return cur
                }
            }

            if d < 24, let cs = copyElementArrayAttribute(cur, kAXChildrenAttribute as CFString) {
                for c in cs { queue.append((c, d + 1)) }
            }
        }
        return nil
    }

    // MARK: - App & Window Activation

    @MainActor
    @discardableResult
    static func activateOrLaunchAntigravity() async -> NSRunningApplication? {
        if let existing = NSRunningApplication.runningApplications(withBundleIdentifier: antigravityBundleId).first {
            NSApp?.yieldActivation(to: existing)
        }

        // On macOS 14+, NSRunningApplication.activate() from a non-active accessory panel is ignored.
        // NSWorkspace.openApplication reliably brings Antigravity to the foreground across spaces/apps.
        let appURL = URL(fileURLWithPath: antigravityAppPath)
        let config = NSWorkspace.OpenConfiguration()
        config.activates = true

        let launched: NSRunningApplication? = await withCheckedContinuation { continuation in
            NSWorkspace.shared.openApplication(at: appURL, configuration: config) { app, _ in
                continuation.resume(returning: app)
            }
        }

        for _ in 0..<18 {
            if let app = launched ?? NSRunningApplication.runningApplications(withBundleIdentifier: antigravityBundleId).first,
               app.isFinishedLaunching {
                if app.isActive {
                    return app
                }
            }
            try? await Task.sleep(nanoseconds: 45_000_000)
        }
        return launched ?? NSRunningApplication.runningApplications(withBundleIdentifier: antigravityBundleId).first
    }

    @MainActor
    private static func ensureWindowVisibleAndRaised(axApp: AXUIElement, runningApp: NSRunningApplication) async {
        let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString) ?? []

        if windows.isEmpty {
            if let url = URL(string: "antigravity://focus") {
                NSWorkspace.shared.open(url)
                try? await Task.sleep(nanoseconds: 320_000_000)
            }
        } else {
            for win in windows {
                var minRef: CFTypeRef?
                if AXUIElementCopyAttributeValue(win, kAXMinimizedAttribute as CFString, &minRef) == .success,
                   let isMin = minRef as? Bool, isMin {
                    AXUIElementSetAttributeValue(win, kAXMinimizedAttribute as CFString, kCFBooleanFalse)
                }
            }
            if let primaryWin = windows.first {
                AXUIElementPerformAction(primaryWin, kAXRaiseAction as CFString)
            }
        }

        AXUIElementSetAttributeValue(axApp, "AXManualAccessibility" as CFString, kCFBooleanTrue)
    }

    // MARK: - Targeted AX Tree Inspectors

    private static func findHubWebArea(in element: AXUIElement, depth: Int) -> AXUIElement? {
        guard depth <= 18 else { return nil }
        let role = copyStringAttribute(element, kAXRoleAttribute as CFString) ?? ""
        if role == "AXMenuBar" { return nil }
        if role == "AXWebArea" {
            let url = copyURLStringAttribute(element) ?? ""
            if url.contains("127.0.0.1:") || url.contains("localhost:") {
                return element
            }
            return nil
        }
        guard let children = copyElementArrayAttribute(element, kAXChildrenAttribute as CFString) else {
            return nil
        }
        for child in children {
            if let found = findHubWebArea(in: child, depth: depth + 1) {
                return found
            }
        }
        return nil
    }

    private static func findSidebar(in element: AXUIElement, depth: Int) -> AXUIElement? {
        guard depth <= 16 else { return nil }
        let desc = copyStringAttribute(element, kAXDescriptionAttribute as CFString) ?? ""
        if desc == "Sidebar" {
            return element
        }
        guard let children = copyElementArrayAttribute(element, kAXChildrenAttribute as CFString) else {
            return nil
        }
        for child in children {
            if let found = findSidebar(in: child, depth: depth + 1) {
                return found
            }
        }
        return nil
    }

    private static func collectSidebarRows(
        in element: AXUIElement,
        depth: Int,
        into map: inout [String: LiveSidebarItem]
    ) {
        guard depth <= 22 else { return }
        guard let children = copyElementArrayAttribute(element, kAXChildrenAttribute as CFString),
              !children.isEmpty else {
            return
        }

        // Check if this container directly holds a conversation `AXLink` (`/c/<uuid>`)
        var rowConvId: String? = nil
        var rowTitle: String? = nil

        for child in children {
            if copyStringAttribute(child, kAXRoleAttribute as CFString) == "AXLink",
               let u = copyURLStringAttribute(child),
               let cid = extractConversationId(fromURL: u) {
                rowConvId = cid
                let desc = copyStringAttribute(child, kAXDescriptionAttribute as CFString)?
                    .trimmingCharacters(in: .whitespacesAndNewlines)
                if let desc, !desc.isEmpty {
                    rowTitle = desc
                }
                break
            }
        }

        if let cid = rowConvId {
            var hasStop = false
            var hasArchiveOrIdle = false

            func inspectRowButtons(_ node: AXUIElement, _ sd: Int) {
                guard sd <= 6 else { return }
                let role = copyStringAttribute(node, kAXRoleAttribute as CFString) ?? ""
                if role == "AXButton" {
                    let desc = copyStringAttribute(node, kAXDescriptionAttribute as CFString) ?? ""
                    if desc == "Stop execution" {
                        hasStop = true
                    } else if desc == "Archive conversation" || desc == "Restore conversation" {
                        hasArchiveOrIdle = true
                    }
                }
                if let subs = copyElementArrayAttribute(node, kAXChildrenAttribute as CFString) {
                    for s in subs { inspectRowButtons(s, sd + 1) }
                }
            }

            inspectRowButtons(element, 0)
            map[cid] = LiveSidebarItem(
                conversationId: cid,
                title: rowTitle,
                isRunning: hasStop,
                isIdleInUI: hasArchiveOrIdle && !hasStop
            )
            return
        }

        for child in children {
            collectSidebarRows(in: child, depth: depth + 1, into: &map)
        }
    }

    private static func inspectOpenWebAreaState(_ webArea: AXUIElement) -> (hasStop: Bool, hasCancelledText: Bool) {
        var hasStop = false
        var hasCancelled = false
        var queue: [(AXUIElement, Int)] = [(webArea, 0)]
        var head = 0

        while head < queue.count && head < 1800 {
            let (node, d) = queue[head]
            head += 1
            let role = copyStringAttribute(node, kAXRoleAttribute as CFString) ?? ""
            if role == "AXButton" {
                let desc = copyStringAttribute(node, kAXDescriptionAttribute as CFString) ?? ""
                let title = copyStringAttribute(node, kAXTitleAttribute as CFString) ?? ""
                if desc.localizedCaseInsensitiveContains("Stop") || title.localizedCaseInsensitiveContains("Stop") {
                    hasStop = true
                }
            } else if role == "AXStaticText" {
                let val = copyStringAttribute(node, kAXValueAttribute as CFString) ?? ""
                if val.contains("User cancelled agent execution") {
                    hasCancelled = true
                }
            }
            if d < 26, let cs = copyElementArrayAttribute(node, kAXChildrenAttribute as CFString) {
                for c in cs { queue.append((c, d + 1)) }
            }
        }
        return (hasStop, hasCancelled)
    }

    private struct TargetLinkScan {
        var hasWebArea = false
        var sidebarFound = false
        var isAlreadyViewingTarget = false
        var isConvoPickerOpen = false
        var matchingLink: AXUIElement? = nil
    }

    private static func isViewingTargetConversation(axApp: AXUIElement, targetPathFragment: String) -> Bool {
        guard let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString) else {
            return false
        }
        for win in windows {
            if let hubWebArea = findHubWebArea(in: win, depth: 0),
               let webURL = copyURLStringAttribute(hubWebArea),
               webURL.contains(targetPathFragment) {
                return true
            }
        }
        return false
    }

    private static func isConvoPickerPlaceholder(_ placeholder: String) -> Bool {
        placeholder.localizedCaseInsensitiveContains("Search for conversations")
            || placeholder.localizedCaseInsensitiveContains("Search conversations")
    }

    private static func findTargetConversationLink(axApp: AXUIElement, targetPathFragment: String) -> TargetLinkScan {
        var out = TargetLinkScan()
        guard let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString) else {
            return out
        }

        for win in windows {
            guard let hubWebArea = findHubWebArea(in: win, depth: 0) else { continue }
            out.hasWebArea = true

            if let webURL = copyURLStringAttribute(hubWebArea), webURL.contains(targetPathFragment) {
                out.isAlreadyViewingTarget = true
                return out
            }

            // Priority 1: Scan Sidebar first (fast, ~400 nodes max)
            if let sidebar = findSidebar(in: hubWebArea, depth: 0) {
                out.sidebarFound = true
                if let link = findLinkInSubtree(sidebar, targetPathFragment: targetPathFragment, maxDepth: 28) {
                    out.matchingLink = link
                    return out
                }
            }

            // Priority 2: Scan top-level Hub controls (open tabs, picker input) with tight depth cap
            var queue: [(AXUIElement, Int)] = [(hubWebArea, 0)]
            var head = 0
            while head < queue.count && head < 600 {
                let (cur, d) = queue[head]
                head += 1

                let role = copyStringAttribute(cur, kAXRoleAttribute as CFString) ?? ""
                let desc = copyStringAttribute(cur, kAXDescriptionAttribute as CFString) ?? ""
                if desc == "Sidebar" || role == "AXHeading" { continue }

                if role == (kAXTextFieldRole as String) {
                    if let ph = copyStringAttribute(cur, kAXPlaceholderValueAttribute as CFString),
                       isConvoPickerPlaceholder(ph) {
                        out.isConvoPickerOpen = true
                    }
                } else if role == "AXLink" {
                    if let u = copyURLStringAttribute(cur), u.contains(targetPathFragment) {
                        out.matchingLink = cur
                        return out
                    }
                }

                if d < 16, let cs = copyElementArrayAttribute(cur, kAXChildrenAttribute as CFString) {
                    for c in cs { queue.append((c, d + 1)) }
                }
            }
        }
        return out
    }

    private static func findLinkInSubtree(
        _ element: AXUIElement,
        targetPathFragment: String,
        maxDepth: Int
    ) -> AXUIElement? {
        guard maxDepth >= 0 else { return nil }
        let role = copyStringAttribute(element, kAXRoleAttribute as CFString) ?? ""
        if role == "AXLink",
           let u = copyURLStringAttribute(element),
           u.contains(targetPathFragment) {
            return element
        }
        guard let children = copyElementArrayAttribute(element, kAXChildrenAttribute as CFString) else {
            return nil
        }
        for child in children {
            if let hit = findLinkInSubtree(child, targetPathFragment: targetPathFragment, maxDepth: maxDepth - 1) {
                return hit
            }
        }
        return nil
    }

    private static func pressButtonInHub(axApp: AXUIElement, matchingDescription: String) -> Bool {
        guard let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString) else {
            return false
        }
        for win in windows {
            guard let hubWebArea = findHubWebArea(in: win, depth: 0) else { continue }
            if let btn = findElementInSubtree(
                hubWebArea,
                role: "AXButton",
                matchingDescriptions: [matchingDescription],
                maxDepth: 20
            ) {
                return AXUIElementPerformAction(btn, kAXPressAction as CFString) == .success
            }
        }
        return false
    }

    private static func isElementExpanded(_ element: AXUIElement) -> Bool? {
        var val: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXExpandedAttribute as CFString, &val) == .success,
              let b = val as? Bool else {
            return nil
        }
        return b
    }

    private static func findSidebarButtons(in sidebar: AXUIElement) -> [(element: AXUIElement, title: String)] {
        var results: [(AXUIElement, String)] = []
        func walk(_ el: AXUIElement, _ d: Int) {
            guard d <= 12 else { return }
            if copyStringAttribute(el, kAXRoleAttribute as CFString) == "AXButton" {
                let t = copyStringAttribute(el, kAXTitleAttribute as CFString) ?? ""
                if !t.isEmpty {
                    results.append((el, t))
                }
            }
            for c in copyElementArrayAttribute(el, kAXChildrenAttribute as CFString) ?? [] {
                walk(c, d + 1)
            }
        }
        walk(sidebar, 0)
        return results
    }

    @MainActor
    private static func expandSidebarSectionForProject(axApp: AXUIElement, projectName: String) async -> Bool {
        guard let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString) else {
            return false
        }
        for win in windows {
            guard let hubWebArea = findHubWebArea(in: win, depth: 0),
                  let sidebar = findSidebar(in: hubWebArea, depth: 0) else { continue }

            var didExpandAny = false
            var buttons = findSidebarButtons(in: sidebar)

            if projectName == "default" || projectName.isEmpty {
                if let convSec = buttons.first(where: { $0.title == "Conversations" }),
                   isElementExpanded(convSec.element) == false {
                    _ = AXUIElementPerformAction(convSec.element, "AXScrollToVisible" as CFString)
                    if AXUIElementPerformAction(convSec.element, kAXPressAction as CFString) == .success {
                        didExpandAny = true
                        try? await Task.sleep(nanoseconds: 140_000_000)
                    }
                }
            } else {
                // 1. Ensure "Projects" top group is expanded
                if let projRoot = buttons.first(where: { $0.title == "Projects" }),
                   isElementExpanded(projRoot.element) == false {
                    _ = AXUIElementPerformAction(projRoot.element, "AXScrollToVisible" as CFString)
                    if AXUIElementPerformAction(projRoot.element, kAXPressAction as CFString) == .success {
                        didExpandAny = true
                        try? await Task.sleep(nanoseconds: 130_000_000)
                        buttons = findSidebarButtons(in: sidebar)
                    }
                }

                // 2. If target project isn't visible yet and "See all (...)" exists, expand project list
                if !buttons.contains(where: { $0.title.caseInsensitiveCompare(projectName) == .orderedSame }),
                   let seeAll = buttons.first(where: { $0.title.lowercased().hasPrefix("see all") }) {
                    _ = AXUIElementPerformAction(seeAll.element, "AXScrollToVisible" as CFString)
                    if AXUIElementPerformAction(seeAll.element, kAXPressAction as CFString) == .success {
                        didExpandAny = true
                        try? await Task.sleep(nanoseconds: 130_000_000)
                        buttons = findSidebarButtons(in: sidebar)
                    }
                }

                // 3. Expand the specific project folder (e.g. "backend-service", "web-client")
                if let projBtn = buttons.first(where: { $0.title.caseInsensitiveCompare(projectName) == .orderedSame }),
                   isElementExpanded(projBtn.element) == false {
                    _ = AXUIElementPerformAction(projBtn.element, "AXScrollToVisible" as CFString)
                    if AXUIElementPerformAction(projBtn.element, kAXPressAction as CFString) == .success {
                        didExpandAny = true
                        try? await Task.sleep(nanoseconds: 150_000_000)
                    }
                }
            }

            if didExpandAny {
                return true
            }
        }
        return false
    }

    @MainActor
    private static func pressAndVerify(
        _ link: AXUIElement, axApp: AXUIElement, targetPathFragment: String
    ) async -> Bool {
        _ = AXUIElementPerformAction(link, "AXScrollToVisible" as CFString)
        guard AXUIElementPerformAction(link, kAXPressAction as CFString) == .success else { return false }
        for _ in 0..<8 {
            try? await Task.sleep(nanoseconds: 65_000_000)
            if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
                return true
            }
        }
        return false
    }

    // MARK: - Conversation Picker (`Cmd + K`) Fallback

    @MainActor
    private static func navigateViaConversationPicker(
        axApp: AXUIElement,
        runningApp: NSRunningApplication,
        conversation: ConversationSnapshot,
        targetPathFragment: String
    ) async -> Bool {
        if !runningApp.isActive {
            _ = await activateOrLaunchAntigravity()
        }

        if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
            return true
        }

        // Prefer clicking the toolbar "Search Conversations" button if present, else Cmd+K
        if !pressButtonInHub(axApp: axApp, matchingDescription: "Search Conversations") {
            postKeyShortcut(virtualKey: CGKeyCode(kVK_ANSI_K), flags: .maskCommand)
        }
        try? await Task.sleep(nanoseconds: 200_000_000)

        let query = cleanPickerQuery(for: conversation)
        guard !query.isEmpty else {
            postKeyShortcut(virtualKey: CGKeyCode(kVK_Escape), flags: [])
            return false
        }

        var focusedValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(axApp, kAXFocusedUIElementAttribute as CFString, &focusedValue) == .success,
              let focusedValue, CFGetTypeID(focusedValue) == AXUIElementGetTypeID() else {
            return false
        }
        let focused = unsafeBitCast(focusedValue, to: AXUIElement.self)
        let ph = copyStringAttribute(focused, kAXPlaceholderValueAttribute as CFString) ?? ""
        guard isConvoPickerPlaceholder(ph) else { return false }

        postKeyShortcut(virtualKey: CGKeyCode(kVK_ANSI_A), flags: .maskCommand)
        let fullQuery = ph.localizedCaseInsensitiveContains("commands") ? "conversations: \(query)" : query
        sendUnicodeText(fullQuery)
        try? await Task.sleep(nanoseconds: 240_000_000)

        postKeyShortcut(virtualKey: CGKeyCode(kVK_Return), flags: [])
        for _ in 0..<8 {
            try? await Task.sleep(nanoseconds: 70_000_000)
            if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
                return true
            }
        }
        return false
    }

    private static func cleanPickerQuery(for conversation: ConversationSnapshot) -> String {
        let raw = conversation.title.isEmpty ? conversation.searchQuery : conversation.title
        let stripped = raw
            .replacingOccurrences(of: "…", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return stripped.count > 28 ? String(stripped.prefix(28)) : stripped
    }

    private static func extractConversationId(fromURL url: String) -> String? {
        guard let range = url.range(of: "/c/") else { return nil }
        let tail = url[range.upperBound...]
        let id = tail.split(whereSeparator: { $0 == "?" || $0 == "/" || $0 == "#" }).first.map(String.init) ?? ""
        return id.count >= 32 ? id : nil
    }

    // MARK: - Low-Level AX & CGEvent Helpers

    private static func copyStringAttribute(_ element: AXUIElement, _ attr: CFString) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attr, &value) == .success, let value else {
            return nil
        }
        return value as? String
    }

    private static func copyURLStringAttribute(_ element: AXUIElement) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, kAXURLAttribute as CFString, &value) == .success,
              let value else {
            return nil
        }
        if CFGetTypeID(value) == CFURLGetTypeID() {
            let cfURL = unsafeBitCast(value, to: CFURL.self)
            return CFURLGetString(cfURL) as String?
        }
        if let nsurl = value as? NSURL {
            return nsurl.absoluteString
        }
        if let str = value as? String {
            return str
        }
        return nil
    }

    private static func copyElementArrayAttribute(_ element: AXUIElement, _ attr: CFString) -> [AXUIElement]? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attr, &value) == .success, let value else {
            return nil
        }
        return value as? [AXUIElement]
    }

    private static func postKeyShortcut(virtualKey: CGKeyCode, flags: CGEventFlags) {
        guard let src = CGEventSource(stateID: .combinedSessionState),
              let down = CGEvent(keyboardEventSource: src, virtualKey: virtualKey, keyDown: true),
              let up = CGEvent(keyboardEventSource: src, virtualKey: virtualKey, keyDown: false) else {
            return
        }
        down.flags = flags
        up.flags = flags
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
    }

    private static func sendUnicodeText(_ text: String) {
        guard let src = CGEventSource(stateID: .combinedSessionState) else { return }
        let utf16 = Array(text.utf16)
        let chunkSize = 8
        var idx = 0
        while idx < utf16.count {
            let end = min(idx + chunkSize, utf16.count)
            var slice = Array(utf16[idx..<end])
            if let down = CGEvent(keyboardEventSource: src, virtualKey: 0, keyDown: true),
               let up = CGEvent(keyboardEventSource: src, virtualKey: 0, keyDown: false) {
                down.keyboardSetUnicodeString(stringLength: slice.count, unicodeString: &slice)
                up.keyboardSetUnicodeString(stringLength: slice.count, unicodeString: &slice)
                down.post(tap: .cghidEventTap)
                up.post(tap: .cghidEventTap)
            }
            idx = end
        }
    }
}
