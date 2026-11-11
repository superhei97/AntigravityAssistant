import AppKit
import Carbon
import SwiftUI

// MARK: - Frosted Glass Vibrancy Bridge

struct VisualEffectBlur: NSViewRepresentable {
    var material: NSVisualEffectView.Material
    var blendingMode: NSVisualEffectView.BlendingMode
    var cornerRadius: CGFloat

    func makeNSView(context: Context) -> NSVisualEffectView {
        let view = NSVisualEffectView()
        view.material = material
        view.blendingMode = blendingMode
        view.state = .active
        view.wantsLayer = true
        view.layer?.cornerRadius = cornerRadius
        view.layer?.masksToBounds = true
        return view
    }

    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {
        nsView.material = material
        nsView.blendingMode = blendingMode
        nsView.layer?.cornerRadius = cornerRadius
    }
}

// MARK: - Non-Activating Floating NSPanel

final class FloatingAssistantPanel: NSPanel {
    var allowKeyFocus: Bool = false

    override var canBecomeKey: Bool { allowKeyFocus }
    override var canBecomeMain: Bool { false }

    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        // Allow unrestricted dragging across vertically/horizontally arranged multi-monitor setups
        // without getting blocked by a lower display's menu bar.
        frameRect
    }

    init(contentRect: NSRect) {
        super.init(
            contentRect: contentRect,
            styleMask: [.nonactivatingPanel, .borderless, .fullSizeContentView],
            backing: .buffered,
            defer: false
        )
        isFloatingPanel = true
        level = .floating
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
        appearance = NSAppearance(named: .aqua)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        hidesOnDeactivate = false
        isMovableByWindowBackground = false
        titleVisibility = .hidden
        titlebarAppearsTransparent = true
    }
}

// MARK: - Panel Controller & Global Hotkey (Option + A)

@MainActor
final class PanelWindowController: ObservableObject {
    static let compactWidth: CGFloat = 208
    static let expandedSize = NSSize(width: 408, height: 456)

    static func compactSize(hasPriorityCard: Bool, visibleRowCount: Int) -> NSSize {
        guard hasPriorityCard else {
            return NSSize(width: compactWidth, height: 124)
        }
        let clampedRows = max(0, min(visibleRowCount, 4))
        return NSSize(width: compactWidth, height: 164 + CGFloat(clampedRows) * 31)
    }

    @Published private(set) var isExpanded: Bool = false
    @Published private(set) var isVisible: Bool = true
    @Published private(set) var isCompactPriorityExpanded: Bool = false
    @Published private(set) var compactTotalPriorityCount: Int = 0
    @Published private(set) var compactRunningPriorityCount: Int = 0
    @Published var showSettings: Bool = false
    @Published var isQuotaExpanded: Bool = false
    @Published var confirmingDeleteConvId: String? = nil
    @Published var showCalibratePopover: Bool = false
    @Published var cal5hPercent: Double = 95
    @Published var cal5hHours: Int = 1
    @Published var cal5hMinutes: Int = 45
    @Published var calWeeklyPercent: Double = 98
    @Published var calWeeklyDays: Int = 6
    @Published var calWeeklyHours: Int = 20

    private(set) var panel: FloatingAssistantPanel?
    private var hotKeyRef: EventHotKeyRef?
    private var eventHandlerRef: EventHandlerRef?
    private var globalMouseMonitor: Any?
    private var localMouseMonitor: Any?
    private var hitMaskTimer: Timer?
    private var isDraggingWindow: Bool = false
    private weak var settingsRef: AppSettings?
    private var savedMascotMaxX: CGFloat?
    private var savedMascotMaxY: CGFloat?

    private static weak var sharedInstance: PanelWindowController?

    var currentCompactSize: NSSize {
        let hasCard = compactTotalPriorityCount > 0
        let visibleRows = isCompactPriorityExpanded ? compactTotalPriorityCount : compactRunningPriorityCount
        return Self.compactSize(hasPriorityCard: hasCard, visibleRowCount: visibleRows)
    }

    func setupPanel<Content: View>(
        rootView: Content,
        settings: AppSettings,
        initialTotalPriorityCount: Int = 0,
        initialRunningPriorityCount: Int = 0
    ) {
        self.settingsRef = settings
        self.compactTotalPriorityCount = initialTotalPriorityCount
        self.compactRunningPriorityCount = initialRunningPriorityCount
        Self.sharedInstance = self

        let initSize = currentCompactSize
        let initialOrigin = loadSavedTopRightOrigin(for: initSize)
        self.savedMascotMaxX = initialOrigin.x + initSize.width
        self.savedMascotMaxY = initialOrigin.y + initSize.height
        let rect = NSRect(origin: initialOrigin, size: initSize)

        let panel = FloatingAssistantPanel(contentRect: rect)
        panel.level = settings.alwaysOnTop ? .floating : .normal
        panel.hasShadow = false

        let hosting = NSHostingView(rootView: rootView)
        hosting.frame = NSRect(origin: .zero, size: initSize)
        hosting.autoresizingMask = [.width, .height]
        panel.contentView = hosting

        self.panel = panel
        panel.orderFrontRegardless()
        registerGlobalHotKey()
        startCompactHitMaskMonitoring()
        updateCompactMousePassthrough()
    }

    private func startCompactHitMaskMonitoring() {
        if let globalMouseMonitor {
            NSEvent.removeMonitor(globalMouseMonitor)
        }
        if let localMouseMonitor {
            NSEvent.removeMonitor(localMouseMonitor)
        }
        hitMaskTimer?.invalidate()

        globalMouseMonitor = NSEvent.addGlobalMonitorForEvents(
            matching: [.mouseMoved, .leftMouseDragged, .leftMouseUp, .rightMouseDown]
        ) { [weak self] _ in
            Task { @MainActor in
                self?.updateCompactMousePassthrough()
            }
        }

        localMouseMonitor = NSEvent.addLocalMonitorForEvents(
            matching: [.mouseMoved, .leftMouseDragged, .leftMouseUp]
        ) { [weak self] event in
            self?.updateCompactMousePassthrough()
            return event
        }

        hitMaskTimer = Timer.scheduledTimer(withTimeInterval: 0.06, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.updateCompactMousePassthrough()
            }
        }
    }

    /// Ensures empty regions around the Pixel Robot & 5h/7d capsule never intercept clicks meant for underlying windows (e.g. Chrome).
    func updateCompactMousePassthrough() {
        guard let panel else { return }
        if isExpanded || isDraggingWindow {
            if panel.ignoresMouseEvents {
                panel.ignoresMouseEvents = false
            }
            return
        }

        let mouse = NSEvent.mouseLocation
        let shouldAcceptMouse = isPointInsideCompactInteractiveControls(mouse)
        let shouldIgnore = !shouldAcceptMouse
        if panel.ignoresMouseEvents != shouldIgnore {
            panel.ignoresMouseEvents = shouldIgnore
        }
    }

    private func isPointInsideCompactInteractiveControls(_ point: NSPoint) -> Bool {
        guard let panel else { return true }
        let frame = panel.frame
        guard frame.contains(point) else { return false }

        // 1. Pixel Robot Mascot (62x74 centered inside top 80pt slot after 4pt top padding)
        let robotRect = NSRect(
            x: frame.midX - 32,
            y: frame.maxY - 84,
            width: 64,
            height: 80
        )
        if robotRect.contains(point) {
            return true
        }

        // 2. 5h | 7d Quota Capsule (136x29 centered below 5pt spacing)
        let capsuleRect = NSRect(
            x: frame.midX - 68,
            y: frame.maxY - 118,
            width: 136,
            height: 30
        )
        if capsuleRect.contains(point) {
            return true
        }

        // 3. Priority Conversations Card (`优先事项`, 196pt wide, shown only when compactTotalPriorityCount > 0)
        if compactTotalPriorityCount > 0 {
            let cardHeight = max(32, frame.height - 132)
            let cardRect = NSRect(
                x: frame.midX - 98,
                y: frame.maxY - 124 - cardHeight,
                width: 196,
                height: cardHeight
            )
            if cardRect.contains(point) {
                return true
            }
        }

        return false
    }

    func toggleCompactPriorityExpanded() {
        isCompactPriorityExpanded.toggle()
        resizeCompactPanelKeepingTopAnchor(animated: true)
    }

    func updateCompactPriorityState(totalCount: Int, runningCount: Int) {
        guard compactTotalPriorityCount != totalCount || compactRunningPriorityCount != runningCount else {
            return
        }
        compactTotalPriorityCount = totalCount
        compactRunningPriorityCount = runningCount
        resizeCompactPanelKeepingTopAnchor(animated: false)
    }

    private func resizeCompactPanelKeepingTopAnchor(animated: Bool) {
        guard let panel, !isExpanded else { return }

        let oldFrame = panel.frame
        let newSize = currentCompactSize
        let anchorMaxX = savedMascotMaxX ?? oldFrame.maxX
        let anchorMaxY = savedMascotMaxY ?? oldFrame.maxY
        let screenFrame = visibleFrameForAnchor(x: anchorMaxX, y: anchorMaxY, fallbackScreen: panel.screen)

        let newX = max(screenFrame.minX + 8, min(anchorMaxX - newSize.width, screenFrame.maxX - newSize.width - 8))
        let newY = max(screenFrame.minY + 8, min(anchorMaxY - newSize.height, screenFrame.maxY - newSize.height - 8))
        let targetFrame = NSRect(origin: NSPoint(x: newX, y: newY), size: newSize)

        if animated {
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = 0.16
                panel.animator().setFrame(targetFrame, display: true)
            } completionHandler: { [weak panel] in
                Task { @MainActor in
                    panel?.invalidateShadow()
                }
            }
        } else {
            panel.setFrame(targetFrame, display: true)
            panel.invalidateShadow()
        }
    }

    func updateAlwaysOnTop(_ enabled: Bool) {
        panel?.level = enabled ? .floating : .normal
    }

    func requestDeleteConfirmation(for convId: String) {
        confirmingDeleteConvId = convId
        DispatchQueue.main.asyncAfter(deadline: .now() + 3.0) { [weak self] in
            if self?.confirmingDeleteConvId == convId {
                self?.confirmingDeleteConvId = nil
            }
        }
    }

    func clearDeleteConfirmation() {
        confirmingDeleteConvId = nil
    }

    func toggleVisibility() {
        guard let panel else { return }
        if panel.isVisible {
            panel.orderOut(nil)
            isVisible = false
        } else {
            panel.orderFrontRegardless()
            isVisible = true
        }
    }

    func showAndExpand() {
        guard let panel else { return }
        if !panel.isVisible {
            panel.orderFrontRegardless()
            isVisible = true
        }
        if !isExpanded {
            setExpanded(true)
        }
    }

    func toggleExpanded() {
        setExpanded(!isExpanded)
    }

    func setExpanded(_ expand: Bool) {
        guard let panel, isExpanded != expand else { return }
        let oldFrame = panel.frame
        if expand {
            savedMascotMaxX = oldFrame.maxX
            savedMascotMaxY = oldFrame.maxY
        }

        isExpanded = expand
        panel.allowKeyFocus = expand
        panel.hasShadow = expand
        if expand {
            panel.ignoresMouseEvents = false
        }

        let targetSize = expand ? Self.expandedSize : currentCompactSize
        let anchorMaxX = expand ? oldFrame.maxX : (savedMascotMaxX ?? oldFrame.maxX)
        let anchorMaxY = expand ? oldFrame.maxY : (savedMascotMaxY ?? oldFrame.maxY)
        let screenFrame = visibleFrameForAnchor(x: anchorMaxX, y: anchorMaxY, fallbackScreen: panel.screen)

        var newX = anchorMaxX - targetSize.width
        var newY = anchorMaxY - targetSize.height

        if expand && newY < screenFrame.minY + 12 {
            newY = oldFrame.minY
        }
        newX = max(screenFrame.minX + 10, min(newX, screenFrame.maxX - targetSize.width - 10))
        newY = max(screenFrame.minY + 10, min(newY, screenFrame.maxY - targetSize.height - 10))

        let targetFrame = NSRect(origin: NSPoint(x: newX, y: newY), size: targetSize)

        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.22
            ctx.timingFunction = CAMediaTimingFunction(controlPoints: 0.2, 0.9, 0.25, 1.0)
            panel.animator().setFrame(targetFrame, display: true)
        } completionHandler: { [weak self] in
            Task { @MainActor in
                guard let self, let p = self.panel else { return }
                p.invalidateShadow()
                if !self.isExpanded {
                    self.saveAnchorFromCurrentFrame()
                }
                self.updateCompactMousePassthrough()
            }
        }
    }

    func moveBy(deltaX: CGFloat, deltaY: CGFloat) {
        guard let panel else { return }
        isDraggingWindow = true
        if panel.ignoresMouseEvents {
            panel.ignoresMouseEvents = false
        }
        var frame = panel.frame
        frame.origin.x += deltaX
        frame.origin.y += deltaY
        panel.setFrameOrigin(frame.origin)
    }

    func finishDrag() {
        isDraggingWindow = false
        guard let panel else { return }
        let mouse = NSEvent.mouseLocation
        let targetScreen = Self.bestScreen(forPoint: mouse)
            ?? Self.bestScreen(forPoint: NSPoint(x: panel.frame.midX, y: panel.frame.midY))
            ?? panel.screen
            ?? NSScreen.main
        let screenFrame = targetScreen?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)

        var frame = panel.frame
        frame.origin.x = max(screenFrame.minX + 8, min(frame.origin.x, screenFrame.maxX - frame.width - 8))
        frame.origin.y = max(screenFrame.minY + 8, min(frame.origin.y, screenFrame.maxY - frame.height - 8))
        panel.setFrameOrigin(frame.origin)
        savedMascotMaxX = frame.maxX
        savedMascotMaxY = frame.maxY
        saveAnchorFromCurrentFrame()
        updateCompactMousePassthrough()
    }

    func resetPositionToDefault() {
        guard let panel else { return }
        let currentScreen = panel.screen ?? NSScreen.main
        let screenFrame = currentScreen?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let size = panel.frame.size
        let origin = NSPoint(
            x: screenFrame.maxX - size.width - 24,
            y: screenFrame.maxY - size.height - 36
        )
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.2
            panel.animator().setFrameOrigin(origin)
        } completionHandler: { [weak self] in
            Task { @MainActor in
                guard let self, let p = self.panel else { return }
                self.savedMascotMaxX = p.frame.maxX
                self.savedMascotMaxY = p.frame.maxY
                self.saveAnchorFromCurrentFrame()
            }
        }
    }

    private func visibleFrameForAnchor(x: CGFloat, y: CGFloat, fallbackScreen: NSScreen?) -> NSRect {
        let screen = Self.bestScreen(forPoint: NSPoint(x: x - 20, y: y - 20))
            ?? fallbackScreen
            ?? NSScreen.main
        return screen?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
    }

    private static func bestScreen(forPoint point: NSPoint) -> NSScreen? {
        let screens = NSScreen.screens
        guard !screens.isEmpty else { return nil }
        if let exact = screens.first(where: { $0.frame.insetBy(dx: -2, dy: -2).contains(point) }) {
            return exact
        }
        return screens.min { lhs, rhs in
            squaredDistance(from: point, to: lhs.frame) < squaredDistance(from: point, to: rhs.frame)
        }
    }

    private static func squaredDistance(from p: NSPoint, to rect: NSRect) -> CGFloat {
        let dx = max(rect.minX - p.x, 0, p.x - rect.maxX)
        let dy = max(rect.minY - p.y, 0, p.y - rect.maxY)
        return dx * dx + dy * dy
    }

    private func saveAnchorFromCurrentFrame() {
        guard let panel else { return }
        let frame = panel.frame
        UserDefaults.standard.set(Double(frame.maxX), forKey: "panelAnchorMaxX")
        UserDefaults.standard.set(Double(frame.maxY), forKey: "panelAnchorMaxY")
    }

    private func loadSavedTopRightOrigin(for size: NSSize) -> NSPoint {
        let defaults = UserDefaults.standard
        if defaults.object(forKey: "panelAnchorMaxX") != nil,
           defaults.object(forKey: "panelAnchorMaxY") != nil {
            let maxX = CGFloat(defaults.double(forKey: "panelAnchorMaxX"))
            let maxY = CGFloat(defaults.double(forKey: "panelAnchorMaxY"))
            let screenFrame = visibleFrameForAnchor(x: maxX, y: maxY, fallbackScreen: NSScreen.main)
            let x = max(screenFrame.minX + 8, min(maxX - size.width, screenFrame.maxX - size.width - 8))
            let y = max(screenFrame.minY + 8, min(maxY - size.height, screenFrame.maxY - size.height - 8))
            return NSPoint(x: x, y: y)
        }
        let screenFrame = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        return NSPoint(
            x: screenFrame.maxX - size.width - 24,
            y: screenFrame.maxY - size.height - 36
        )
    }

    // MARK: - Carbon Global Shortcut: Option + A (kVK_ANSI_A = 0x00)

    private func registerGlobalHotKey() {
        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )

        let callback: EventHandlerUPP = { _, _, _ in
            DispatchQueue.main.async {
                PanelWindowController.sharedInstance?.toggleVisibility()
            }
            return noErr
        }

        InstallEventHandler(
            GetApplicationEventTarget(),
            callback,
            1,
            &eventType,
            nil,
            &eventHandlerRef
        )

        let hotKeyID = EventHotKeyID(signature: OSType(0x41475941), id: 1) // 'AGYA'
        RegisterEventHotKey(
            UInt32(kVK_ANSI_A),
            UInt32(optionKey),
            hotKeyID,
            GetApplicationEventTarget(),
            0,
            &hotKeyRef
        )
    }
}

// MARK: - AppKit Drag + Click Gesture Overlay for Smooth 120Hz Dragging

struct WindowDragArea: NSViewRepresentable {
    var onTap: (() -> Void)?
    var onMove: (CGFloat, CGFloat) -> Void
    var onEndDrag: () -> Void

    func makeNSView(context: Context) -> DragTrackingView {
        let v = DragTrackingView()
        v.onTap = onTap
        v.onMove = onMove
        v.onEndDrag = onEndDrag
        return v
    }

    func updateNSView(_ nsView: DragTrackingView, context: Context) {
        nsView.onTap = onTap
        nsView.onMove = onMove
        nsView.onEndDrag = onEndDrag
    }

    final class DragTrackingView: NSView {
        var onTap: (() -> Void)?
        var onMove: ((CGFloat, CGFloat) -> Void)?
        var onEndDrag: (() -> Void)?

        private var initialMouseLocation: NSPoint = .zero
        private var lastMouseLocation: NSPoint = .zero
        private var didDrag = false

        override func acceptsFirstMouse(for event: NSEvent?) -> Bool {
            true
        }

        override func mouseDown(with event: NSEvent) {
            initialMouseLocation = NSEvent.mouseLocation
            lastMouseLocation = initialMouseLocation
            didDrag = false
        }

        override func mouseDragged(with event: NSEvent) {
            let current = NSEvent.mouseLocation
            let totalDist = hypot(current.x - initialMouseLocation.x, current.y - initialMouseLocation.y)
            if totalDist > 3.5 {
                didDrag = true
            }
            if didDrag {
                let dx = current.x - lastMouseLocation.x
                let dy = current.y - lastMouseLocation.y
                lastMouseLocation = current
                onMove?(dx, dy)
            }
        }

        override func mouseUp(with event: NSEvent) {
            if didDrag {
                onEndDrag?()
            } else {
                onTap?()
            }
        }
    }
}
