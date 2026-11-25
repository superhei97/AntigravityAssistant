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
            || placeholder.localizedCaseInsensitiveContains("Search commands")
            || placeholder.localizedCaseInsensitiveContains("Type a command")
    }

    private static func pickerInputPlaceholder(for element: AXUIElement) -> String? {
        let role = copyStringAttribute(element, kAXRoleAttribute as CFString) ?? ""
        guard role == "AXComboBox"
            || role == (kAXTextFieldRole as String)
            || role == "AXSearchField" else {
            return nil
        }
        let candidates = [
            copyStringAttribute(element, kAXPlaceholderValueAttribute as CFString),
            copyStringAttribute(element, kAXDescriptionAttribute as CFString),
            copyStringAttribute(element, kAXTitleAttribute as CFString)
        ]
        for candidate in candidates {
            if let text = candidate, !text.isEmpty, isConvoPickerPlaceholder(text) {
                return text
            }
        }
        return nil
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

                if pickerInputPlaceholder(for: cur) != nil {
                    out.isConvoPickerOpen = true
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
            guard d <= 24 else { return }
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

                // 3. Expand the specific project folder, or if not pinned under "Projects"
                //    (e.g. ungrouped workspace folder), also ensure "Conversations" is expanded.
                if let projBtn = buttons.first(where: { $0.title.caseInsensitiveCompare(projectName) == .orderedSame }) {
                    if isElementExpanded(projBtn.element) == false {
                        _ = AXUIElementPerformAction(projBtn.element, "AXScrollToVisible" as CFString)
                        if AXUIElementPerformAction(projBtn.element, kAXPressAction as CFString) == .success {
                            didExpandAny = true
                            try? await Task.sleep(nanoseconds: 150_000_000)
                        }
                    }
                } else if let convSec = buttons.first(where: { $0.title == "Conversations" }),
                          isElementExpanded(convSec.element) == false {
                    _ = AXUIElementPerformAction(convSec.element, "AXScrollToVisible" as CFString)
                    if AXUIElementPerformAction(convSec.element, kAXPressAction as CFString) == .success {
                        didExpandAny = true
                        try? await Task.sleep(nanoseconds: 140_000_000)
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

    // MARK: - Conversation Picker (`Search Conversations` / `Cmd + K`) Fallback

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

        let wasPickerAlreadyOpen = isConversationPickerOpen(axApp: axApp, includeGenericDialog: false)
        var didTriggerOpen = wasPickerAlreadyOpen
        if !wasPickerAlreadyOpen {
            // Prefer clicking the toolbar "Search Conversations" button if present, else Cmd+K
            if pressButtonInHub(axApp: axApp, matchingDescription: "Search Conversations") {
                didTriggerOpen = true
            } else {
                if !runningApp.isActive {
                    _ = await activateOrLaunchAntigravity()
                }
                postKeyShortcut(virtualKey: CGKeyCode(kVK_ANSI_K), flags: .maskCommand)
                didTriggerOpen = true
            }
        }

        let selected = await performPickerSelection(
            axApp: axApp,
            runningApp: runningApp,
            conversation: conversation,
            targetPathFragment: targetPathFragment
        )

        if selected && isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
            if isConversationPickerOpen(axApp: axApp, includeGenericDialog: false) {
                await dismissConversationPickerIfOpen(
                    axApp: axApp,
                    runningApp: runningApp,
                    didTriggerOpen: false
                )
            }
            return true
        }

        // Mandatory Cleanup Guarantee: NEVER leave an empty or half-open Conversations modal stuck on screen
        await dismissConversationPickerIfOpen(
            axApp: axApp,
            runningApp: runningApp,
            didTriggerOpen: didTriggerOpen
        )

        return isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment)
    }

    @MainActor
    private static func performPickerSelection(
        axApp: AXUIElement,
        runningApp: NSRunningApplication,
        conversation: ConversationSnapshot,
        targetPathFragment: String
    ) async -> Bool {
        // 1. Poll up to ~840ms in 60ms steps to locate the picker search input inside hubWebArea (or focused element)
        var picker: (hubWebArea: AXUIElement, dialog: AXUIElement?, input: AXUIElement, placeholder: String)? = nil
        for _ in 0..<14 {
            if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
                return true
            }
            if let found = locateConversationPicker(axApp: axApp) {
                picker = found
                break
            }
            try? await Task.sleep(nanoseconds: 60_000_000)
        }

        guard var activePicker = picker else {
            return false
        }

        let initialScope = activePicker.dialog ?? activePicker.hubWebArea

        // 2. Pre-typing check: if target conversation is already present in the open picker, press it directly
        if let directLink = findNonSidebarLinkInSubtree(initialScope, targetPathFragment: targetPathFragment, maxDepth: 18),
           await pressAndVerify(directLink, axApp: axApp, targetPathFragment: targetPathFragment) {
            return true
        }

        let initialScan = scanPickerResults(in: initialScope)
        if let uniqueRow = uniquePreTypingMatch(in: initialScan.rows, conversation: conversation) {
            if await pressPickerRowAndVerify(
                uniqueRow,
                axApp: axApp,
                runningApp: runningApp,
                targetPathFragment: targetPathFragment,
                allowReturnFallback: false
            ) {
                return true
            }
            // If pressing that row navigated elsewhere and closed the picker, stop cleanly
            if !isConversationPickerOpen(axApp: axApp, includeGenericDialog: false) {
                return isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment)
            }
        }

        // 3. Build search queries and filter the picker input
        let queries = candidatePickerQueries(for: conversation)
        guard !queries.isEmpty else {
            return false
        }

        for query in queries {
            if let refreshed = locateConversationPicker(axApp: axApp) {
                activePicker = refreshed
            }

            await enterQueryIntoPickerInput(
                activePicker.input,
                query: query,
                placeholder: activePicker.placeholder,
                runningApp: runningApp
            )

            // Allow Base UI / React filter & full-text index to update AXList
            try? await Task.sleep(nanoseconds: 110_000_000)

            var sawNoResults = false
            var latestRows: [(element: AXUIElement, text: String, childTexts: [String])] = []

            for _ in 0..<8 {
                if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
                    return true
                }

                let currentPicker = locateConversationPicker(axApp: axApp) ?? activePicker
                let scope = currentPicker.dialog ?? currentPicker.hubWebArea

                if let directLink = findNonSidebarLinkInSubtree(scope, targetPathFragment: targetPathFragment, maxDepth: 18),
                   await pressAndVerify(directLink, axApp: axApp, targetPathFragment: targetPathFragment) {
                    return true
                }

                let scan = scanPickerResults(in: scope)
                latestRows = scan.rows

                if let matchedRow = bestMatchingPickerRow(
                    in: scan.rows,
                    conversation: conversation,
                    typedQuery: query
                ) {
                    if await pressPickerRowAndVerify(
                        matchedRow,
                        axApp: axApp,
                        runningApp: runningApp,
                        targetPathFragment: targetPathFragment,
                        allowReturnFallback: true
                    ) {
                        return true
                    }
                    return isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment)
                }

                if scan.hasNoResults {
                    sawNoResults = true
                    break
                }

                try? await Task.sleep(nanoseconds: 65_000_000)
            }

            // Fallback to Return keystroke if AX did not expose row labels and list is non-empty
            if !sawNoResults && latestRows.isEmpty && isConversationPickerOpen(axApp: axApp, includeGenericDialog: false) {
                if !runningApp.isActive {
                    _ = await activateOrLaunchAntigravity()
                }
                postKeyShortcut(virtualKey: CGKeyCode(kVK_Return), flags: [])
                for _ in 0..<8 {
                    try? await Task.sleep(nanoseconds: 65_000_000)
                    if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
                        return true
                    }
                }
            }
        }

        return false
    }

    private static func locateConversationPicker(
        axApp: AXUIElement
    ) -> (hubWebArea: AXUIElement, dialog: AXUIElement?, input: AXUIElement, placeholder: String)? {
        guard let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString) else {
            return nil
        }

        var firstHubWebArea: AXUIElement? = nil

        for win in windows {
            guard let hubWebArea = findHubWebArea(in: win, depth: 0) else { continue }
            if firstHubWebArea == nil {
                firstHubWebArea = hubWebArea
            }

            // BFS inside hubWebArea (skipping Sidebar) to locate the picker dialog & search input
            var queue: [(AXUIElement, Int, AXUIElement?)] = [(hubWebArea, 0, nil)]
            var head = 0
            while head < queue.count && head < 700 {
                let (cur, d, enclosingDialog) = queue[head]
                head += 1

                let role = copyStringAttribute(cur, kAXRoleAttribute as CFString) ?? ""
                let subrole = copyStringAttribute(cur, kAXSubroleAttribute as CFString) ?? ""
                let desc = copyStringAttribute(cur, kAXDescriptionAttribute as CFString) ?? ""
                if desc == "Sidebar" || role == "AXMenuBar" { continue }

                let nextDialog = (subrole == "AXApplicationDialog" || role == "AXDialog") ? cur : enclosingDialog

                if let ph = pickerInputPlaceholder(for: cur) {
                    return (hubWebArea, nextDialog, cur, ph)
                }

                if d < 20, let children = copyElementArrayAttribute(cur, kAXChildrenAttribute as CFString) {
                    for child in children {
                        queue.append((child, d + 1, nextDialog))
                    }
                }
            }
        }

        // Secondary fallback: inspect kAXFocusedUIElementAttribute on axApp
        if let hubWebArea = firstHubWebArea {
            var focusedValue: CFTypeRef?
            if AXUIElementCopyAttributeValue(axApp, kAXFocusedUIElementAttribute as CFString, &focusedValue) == .success,
               let focusedValue,
               CFGetTypeID(focusedValue) == AXUIElementGetTypeID() {
                let focused = unsafeBitCast(focusedValue, to: AXUIElement.self)
                if let ph = pickerInputPlaceholder(for: focused) {
                    return (hubWebArea, nil, focused, ph)
                }
            }
        }

        return nil
    }

    private static func isConversationPickerOpen(axApp: AXUIElement, includeGenericDialog: Bool) -> Bool {
        if locateConversationPicker(axApp: axApp) != nil {
            return true
        }
        guard let windows = copyElementArrayAttribute(axApp, kAXWindowsAttribute as CFString) else {
            return false
        }
        for win in windows {
            guard let hubWebArea = findHubWebArea(in: win, depth: 0) else { continue }
            var queue: [(AXUIElement, Int)] = [(hubWebArea, 0)]
            var head = 0
            while head < queue.count && head < 400 {
                let (cur, d) = queue[head]
                head += 1
                let role = copyStringAttribute(cur, kAXRoleAttribute as CFString) ?? ""
                let subrole = copyStringAttribute(cur, kAXSubroleAttribute as CFString) ?? ""
                let desc = copyStringAttribute(cur, kAXDescriptionAttribute as CFString) ?? ""
                if desc == "Sidebar" || role == "AXMenuBar" { continue }

                if subrole == "AXApplicationDialog" || role == "AXDialog" {
                    if includeGenericDialog {
                        // Exclude Delete confirmation modal if present
                        if findModalDeleteConfirmButton(in: cur) == nil {
                            return true
                        }
                    } else if dialogLooksLikeConversationPicker(cur) {
                        return true
                    }
                }

                if d < 14, let cs = copyElementArrayAttribute(cur, kAXChildrenAttribute as CFString) {
                    for c in cs { queue.append((c, d + 1)) }
                }
            }
        }
        return false
    }

    private static func dialogLooksLikeConversationPicker(_ dialog: AXUIElement) -> Bool {
        var queue: [(AXUIElement, Int)] = [(dialog, 0)]
        var head = 0
        while head < queue.count && head < 120 {
            let (cur, d) = queue[head]
            head += 1
            if pickerInputPlaceholder(for: cur) != nil {
                return true
            }
            let role = copyStringAttribute(cur, kAXRoleAttribute as CFString) ?? ""
            let desc = copyStringAttribute(cur, kAXDescriptionAttribute as CFString) ?? ""
            let val = copyStringAttribute(cur, kAXValueAttribute as CFString) ?? ""
            if desc.localizedCaseInsensitiveContains("Sort and filter conversations")
                || (role == "AXComboBox" && val == "Conversations") {
                return true
            }
            if d < 8, let cs = copyElementArrayAttribute(cur, kAXChildrenAttribute as CFString) {
                for c in cs { queue.append((c, d + 1)) }
            }
        }
        return false
    }

    @MainActor
    private static func dismissConversationPickerIfOpen(
        axApp: AXUIElement,
        runningApp: NSRunningApplication,
        didTriggerOpen: Bool
    ) async {
        if !isConversationPickerOpen(axApp: axApp, includeGenericDialog: didTriggerOpen) {
            guard didTriggerOpen else { return }
            // Give a brief settle window in case the dialog was mid-mount when selection aborted
            try? await Task.sleep(nanoseconds: 80_000_000)
            guard isConversationPickerOpen(axApp: axApp, includeGenericDialog: true) else { return }
        }

        for attempt in 0..<4 {
            guard isConversationPickerOpen(axApp: axApp, includeGenericDialog: didTriggerOpen) else {
                return
            }

            if !runningApp.isActive {
                _ = await activateOrLaunchAntigravity()
            }

            if let picker = locateConversationPicker(axApp: axApp) {
                if let dialog = picker.dialog {
                    _ = AXUIElementPerformAction(dialog, kAXCancelAction as CFString)
                    if attempt >= 2,
                       let cancelBtn = findButtonInSubtree(
                           dialog,
                           matchingDescriptions: ["Close", "Cancel", "Dismiss", "关闭", "取消"],
                           maxDepth: 8
                       ) {
                        _ = AXUIElementPerformAction(cancelBtn, kAXPressAction as CFString)
                    }
                }
                _ = AXUIElementSetAttributeValue(picker.input, kAXFocusedAttribute as CFString, kCFBooleanTrue)
            }

            postKeyShortcut(virtualKey: CGKeyCode(kVK_Escape), flags: [])
            try? await Task.sleep(nanoseconds: 110_000_000)
        }
    }

    @MainActor
    private static func enterQueryIntoPickerInput(
        _ input: AXUIElement,
        query: String,
        placeholder: String,
        runningApp: NSRunningApplication
    ) async {
        let fullQuery = placeholder.localizedCaseInsensitiveContains("commands")
            ? "conversations: \(query)"
            : query

        _ = AXUIElementSetAttributeValue(input, kAXFocusedAttribute as CFString, kCFBooleanTrue)

        // Primary method (verified live on Antigravity's Base-UI AXComboBox): direct AXValue update
        // avoids IME composition interference while immediately filtering the React result list.
        let setErr = AXUIElementSetAttributeValue(
            input,
            kAXValueAttribute as CFString,
            fullQuery as CFTypeRef
        )
        if setErr == .success {
            try? await Task.sleep(nanoseconds: 55_000_000)
            let readBack = copyStringAttribute(input, kAXValueAttribute as CFString) ?? ""
            if readBack == fullQuery {
                return
            }
        }

        // Fallback: focus input and type via CGEvent Unicode string
        if !runningApp.isActive {
            _ = await activateOrLaunchAntigravity()
        }
        _ = AXUIElementPerformAction(input, kAXPressAction as CFString)
        _ = AXUIElementSetAttributeValue(input, kAXFocusedAttribute as CFString, kCFBooleanTrue)
        try? await Task.sleep(nanoseconds: 40_000_000)
        postKeyShortcut(virtualKey: CGKeyCode(kVK_ANSI_A), flags: .maskCommand)
        sendUnicodeText(fullQuery)
    }

    @MainActor
    private static func pressPickerRowAndVerify(
        _ row: AXUIElement,
        axApp: AXUIElement,
        runningApp: NSRunningApplication,
        targetPathFragment: String,
        allowReturnFallback: Bool
    ) async -> Bool {
        _ = AXUIElementPerformAction(row, "AXScrollToVisible" as CFString)
        let pressOk = AXUIElementPerformAction(row, kAXPressAction as CFString) == .success
        if pressOk {
            for _ in 0..<10 {
                try? await Task.sleep(nanoseconds: 65_000_000)
                if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
                    return true
                }
            }
        }

        if allowReturnFallback && isConversationPickerOpen(axApp: axApp, includeGenericDialog: false) {
            if !runningApp.isActive {
                _ = await activateOrLaunchAntigravity()
            }
            postKeyShortcut(virtualKey: CGKeyCode(kVK_Return), flags: [])
            for _ in 0..<8 {
                try? await Task.sleep(nanoseconds: 65_000_000)
                if isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment) {
                    return true
                }
            }
        }

        return isViewingTargetConversation(axApp: axApp, targetPathFragment: targetPathFragment)
    }

    private static func findNonSidebarLinkInSubtree(
        _ element: AXUIElement,
        targetPathFragment: String,
        maxDepth: Int
    ) -> AXUIElement? {
        guard maxDepth >= 0 else { return nil }
        let desc = copyStringAttribute(element, kAXDescriptionAttribute as CFString) ?? ""
        if desc == "Sidebar" { return nil }

        let role = copyStringAttribute(element, kAXRoleAttribute as CFString) ?? ""
        if role == "AXLink",
           let u = copyURLStringAttribute(element),
           u.contains(targetPathFragment) {
            return element
        }
        for child in copyElementArrayAttribute(element, kAXChildrenAttribute as CFString) ?? [] {
            if let hit = findNonSidebarLinkInSubtree(child, targetPathFragment: targetPathFragment, maxDepth: maxDepth - 1) {
                return hit
            }
        }
        return nil
    }

    private static func scanPickerResults(
        in scope: AXUIElement
    ) -> (rows: [(element: AXUIElement, text: String, childTexts: [String])], hasNoResults: Bool) {
        var rows: [(element: AXUIElement, text: String, childTexts: [String])] = []
        var hasNoResults = false

        // Find the picker's AXList container (excluding Typeahead menu and Sidebar/Breadcrumb)
        var targetLists: [AXUIElement] = []
        func collectLists(_ el: AXUIElement, _ d: Int) {
            guard d <= 14 else { return }
            let role = copyStringAttribute(el, kAXRoleAttribute as CFString) ?? ""
            let desc = copyStringAttribute(el, kAXDescriptionAttribute as CFString) ?? ""
            if desc == "Sidebar" || desc == "Breadcrumb" || desc == "Typeahead menu" {
                return
            }
            if role == "AXList" {
                targetLists.append(el)
                return
            }
            for c in copyElementArrayAttribute(el, kAXChildrenAttribute as CFString) ?? [] {
                collectLists(c, d + 1)
            }
        }
        collectLists(scope, 0)

        func walkList(_ el: AXUIElement, _ d: Int) {
            guard d <= 8 else { return }
            let role = copyStringAttribute(el, kAXRoleAttribute as CFString) ?? ""
            let val = (copyStringAttribute(el, kAXValueAttribute as CFString) ?? "")
                .trimmingCharacters(in: .whitespacesAndNewlines)

            if role == "AXStaticText" && val.localizedCaseInsensitiveContains("No results found") {
                hasNoResults = true
                return
            }

            // Skip the list root itself (`AXList` also advertises AXPress in Chromium)
            if d > 0 && role != "AXList" && elementSupportsAction(el, kAXPressAction as CFString) {
                let childSegments = collectStaticTextSegments(in: el, maxDepth: 4)
                let combined = !val.isEmpty
                    ? val
                    : (copyStringAttribute(el, kAXTitleAttribute as CFString)
                        ?? copyStringAttribute(el, kAXDescriptionAttribute as CFString)
                        ?? childSegments.joined(separator: " "))
                        .trimmingCharacters(in: .whitespacesAndNewlines)

                let lower = combined.lowercased()
                let isShowMore = lower.hasPrefix("show ") && lower.contains("more")
                if !isShowMore && !combined.isEmpty {
                    rows.append((el, combined, childSegments))
                }
                return
            }

            for c in copyElementArrayAttribute(el, kAXChildrenAttribute as CFString) ?? [] {
                walkList(c, d + 1)
            }
        }

        for list in targetLists {
            walkList(list, 0)
        }

        return (rows, hasNoResults)
    }

    private static func collectStaticTextSegments(in element: AXUIElement, maxDepth: Int) -> [String] {
        var segments: [String] = []
        func walk(_ node: AXUIElement, _ d: Int) {
            guard d <= maxDepth else { return }
            if d > 0 && copyStringAttribute(node, kAXRoleAttribute as CFString) == "AXStaticText" {
                let v = (copyStringAttribute(node, kAXValueAttribute as CFString) ?? "")
                    .trimmingCharacters(in: .whitespacesAndNewlines)
                if !v.isEmpty {
                    segments.append(v)
                    return
                }
            }
            for c in copyElementArrayAttribute(node, kAXChildrenAttribute as CFString) ?? [] {
                walk(c, d + 1)
            }
        }
        walk(element, 0)
        return segments
    }

    private static func elementSupportsAction(_ element: AXUIElement, _ action: CFString) -> Bool {
        var actionsRef: CFArray?
        guard AXUIElementCopyActionNames(element, &actionsRef) == .success,
              let actions = actionsRef as? [String] else {
            return false
        }
        return actions.contains(action as String)
    }

    private static func normalizePickerText(_ raw: String) -> String {
        raw.replacingOccurrences(of: "…", with: "")
            .components(separatedBy: .whitespacesAndNewlines)
            .filter { !$0.isEmpty }
            .joined(separator: " ")
            .lowercased()
    }

    private static func cleanedTitleForMatching(conversation: ConversationSnapshot) -> String {
        let fallbackTitle = "会话 \(conversation.id.prefix(8))"
        if conversation.title == fallbackTitle {
            return ""
        }
        return normalizePickerText(conversation.title)
    }

    private static func uniquePreTypingMatch(
        in rows: [(element: AXUIElement, text: String, childTexts: [String])],
        conversation: ConversationSnapshot
    ) -> AXUIElement? {
        let titleNorm = cleanedTitleForMatching(conversation: conversation)
        guard !titleNorm.isEmpty else { return nil }
        let projNorm = (conversation.projectName == "default" ? "" : conversation.projectName)
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .lowercased()

        var strongMatches: [AXUIElement] = []
        for row in rows {
            let rowNorm = normalizePickerText(row.text)
            let firstChildNorm = row.childTexts.first.map(normalizePickerText) ?? ""
            let titleMatches = (firstChildNorm == titleNorm)
                || rowNorm == titleNorm
                || rowNorm.hasPrefix(titleNorm + " ")

            guard titleMatches else { continue }

            if !projNorm.isEmpty {
                let hasProj = row.childTexts.contains { $0.lowercased() == projNorm }
                    || rowNorm.contains(" " + projNorm + " ")
                    || rowNorm.hasSuffix(" " + projNorm)
                if hasProj {
                    strongMatches.append(row.element)
                }
            } else {
                strongMatches.append(row.element)
            }
        }

        return strongMatches.count == 1 ? strongMatches[0] : nil
    }

    private static func bestMatchingPickerRow(
        in rows: [(element: AXUIElement, text: String, childTexts: [String])],
        conversation: ConversationSnapshot,
        typedQuery: String
    ) -> AXUIElement? {
        guard !rows.isEmpty else { return nil }
        let titleNorm = cleanedTitleForMatching(conversation: conversation)
        let searchNorm = normalizePickerText(conversation.searchQuery)
        let queryNorm = normalizePickerText(typedQuery)
        let projNorm = (conversation.projectName == "default" ? "" : conversation.projectName)
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .lowercased()

        var bestElement: AXUIElement? = nil
        var bestScore = 0

        for (idx, row) in rows.enumerated() {
            let rowNorm = normalizePickerText(row.text)
            var score = 0

            if !titleNorm.isEmpty {
                if rowNorm == titleNorm || rowNorm.hasPrefix(titleNorm + " ") {
                    score += 300
                } else if rowNorm.hasPrefix(titleNorm) || rowNorm.contains(titleNorm) {
                    score += 220
                } else if titleNorm.count >= 12 && rowNorm.contains(String(titleNorm.prefix(16))) {
                    score += 150
                }
            }

            if score == 0 && !searchNorm.isEmpty && searchNorm.count >= 4 && rowNorm.contains(searchNorm) {
                score += 130
            }

            if score == 0 && !queryNorm.isEmpty && queryNorm.count >= 3 && rowNorm.contains(queryNorm) {
                score += 110
            }

            guard score > 0 else { continue }

            if !projNorm.isEmpty {
                let hasProj = row.childTexts.contains { $0.lowercased() == projNorm }
                    || rowNorm.contains(" " + projNorm + " ")
                    || rowNorm.contains(projNorm)
                if hasProj {
                    score += 90
                }
            }

            // Slight preference for top-ranked search hit on ties
            score += max(0, 15 - idx)

            if score > bestScore {
                bestScore = score
                bestElement = row.element
            }
        }

        return bestElement
    }

    private static func candidatePickerQueries(for conversation: ConversationSnapshot) -> [String] {
        var queries: [String] = []
        let fallbackTitle = "会话 \(conversation.id.prefix(8))"

        func clean(_ raw: String) -> String {
            let stripped = raw
                .replacingOccurrences(of: "…", with: "")
                .components(separatedBy: .whitespacesAndNewlines)
                .filter { !$0.isEmpty }
                .joined(separator: " ")
            return stripped.count > 34 ? String(stripped.prefix(34)) : stripped
        }

        if !conversation.title.isEmpty && conversation.title != fallbackTitle {
            let q1 = clean(conversation.title)
            if !q1.isEmpty {
                queries.append(q1)
            }
        }

        if !conversation.searchQuery.isEmpty {
            let q2 = clean(conversation.searchQuery)
            if !q2.isEmpty && !queries.contains(where: { $0.caseInsensitiveCompare(q2) == .orderedSame }) {
                queries.append(q2)
            }
        }

        return queries
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
