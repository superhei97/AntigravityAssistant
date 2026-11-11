import AppKit
import SwiftUI

// MARK: - 16-Bit Pixel Art Antigravity Robot Mascot & AppIcon Generator

enum PixelRobotSprite {
    static let width = 31
    static let height = 37

    enum Expression: Equatable {
        case normal
        case blink
        case working
        case happy
        case error
    }

    struct OrbPalette {
        let main: SIMD4<UInt8>
        let glow: SIMD4<UInt8>
        let shade: SIMD4<UInt8>

        static func forPhase(_ phase: AgentPhase) -> OrbPalette {
            switch phase {
            case .thinking:
                return OrbPalette(
                    main: SIMD4(36, 198, 255, 255),
                    glow: SIMD4(148, 236, 255, 255),
                    shade: SIMD4(18, 128, 208, 255)
                )
            case .runningTool:
                return OrbPalette(
                    main: SIMD4(142, 98, 255, 255),
                    glow: SIMD4(198, 172, 255, 255),
                    shade: SIMD4(92, 54, 204, 255)
                )
            case .waitingUser:
                return OrbPalette(
                    main: SIMD4(255, 178, 36, 255),
                    glow: SIMD4(255, 226, 122, 255),
                    shade: SIMD4(206, 122, 14, 255)
                )
            case .error:
                return OrbPalette(
                    main: SIMD4(255, 76, 88, 255),
                    glow: SIMD4(255, 154, 162, 255),
                    shade: SIMD4(196, 36, 48, 255)
                )
            case .idle, .completed, .interrupted, .paused:
                return OrbPalette(
                    main: SIMD4(46, 138, 255, 255),
                    glow: SIMD4(122, 194, 255, 255),
                    shade: SIMD4(26, 92, 204, 255)
                )
            }
        }
    }

    // 31x37 hand-crafted pixel grid matching the retro CRT Antigravity robot mascot
    static let baseRows: [String] = [
        "....................KKKK.......", // 00: Antenna orb top
        "...................KWQOOK......", // 01: Orb highlight
        "...................KQQOOK......", // 02: Orb glow
        "..................KKOOooK......", // 03: Wire + orb lower
        ".................KK.KKKK.......", // 04: Wire + orb bottom
        "................KK.............", // 05: Bent antenna stem
        ".............KKKKK.............", // 06: Antenna dome top
        "............KGGgggK............", // 07: Antenna dome base
        ".......KKKKKKKKKKKKKKKKK.......", // 08: CRT head top border
        ".....KKWWWWWWWWWWWWWWWWWKK.....", // 09: CRT top white highlight
        "....KWWLLLLLLLLLLLLLLLLLWWK....", // 10: CRT upper bevel
        "....KWLMKKKKKKKKKKKKKKKMLWK....", // 11: Screen top frame
        "....KLMKbbBABBBBBBBCCCCKLWK....", // 12: Screen + Antigravity 'A' tip
        "....KLMKbBABABBBBBBCCCCKLWK....", // 13: Screen + 'A' mid
        "....KLMKbAAAAABBBBBBCCCKLWK....", // 14: Screen + 'A' base
        "....KLMKbBBBBBBBBBBBBCCKMWK....", // 15: Screen upper blue glow
        "....KLMKbBBEEBBBBBEEBBCKMWK....", // 16: Eyes row 1
        "....KLSKbBBEEBBBBBEEBBCKMSK....", // 17: Eyes row 2
        "....KLSKbBBEEBBBBBEEBBBKMSK....", // 18: Eyes row 3
        "....KLSKbBBBBBmmmBBBBBBKMSK....", // 19: Cute smile (1px gap below eyes)
        "....KMSKbBpPpBBcBBpPpBBKSMK....", // 20: Rosy pink blush cheeks + lower lip glint
        "....KMSKbbbbbbbbbbbbbbbKSMK....", // 21: Screen bottom shadow
        "....KMSSKKKKKKKKKKKKKKKSSMK....", // 22: Screen lower frame (rounded corners)
        ".....KKSSSSSSSSSSSSSSSSSKK.....", // 23: CRT chin bevel
        ".......KKKKKKKKKKKKKKKKK.......", // 24: CRT bottom border
        ".........KDDDDDDDDDDDK.........", // 25: Neck shadow
        "........KKMLLLLLLLLLSKK........", // 26: Shoulders
        "......KWLKLLLnnnnnLLSKLWK......", // 27: Arms + chest badge top
        ".....KWLLKWLLnzzznLLSKLLWK.....", // 28: Arms + chest badge Antigravity arch top
        ".....KLMSKWLLnznznLLSKSMlK.....", // 29: Arms + chest badge Antigravity arch legs
        "......KKKKMLLnnnnnLMSKKKK......", // 30: Wrists + chest badge bottom
        ".........KMLLLLLLLLMSK.........", // 31: Lower torso
        ".........KSSSSSSSSSSSK.........", // 32: Hips bevel
        ".........KMMSKKKKKMSSK.........", // 33: Upper legs split
        ".........KMMSK...KMSSK.........", // 34: Stubby legs
        ".........KSSDK...KSSDK.........", // 35: Leg ankles
        "..........KKKK...KKKK.........."  // 36: Robot feet
    ]

    static func rows(for expression: Expression, waveTick: Bool = false) -> [[Character]] {
        var grid = baseRows.map { Array($0) }

        switch expression {
        case .normal:
            break
        case .blink:
            // Clear upper 2 eye rows, draw cute closed horizontal lashes on row 18
            for y in [16, 17] {
                grid[y][11] = "B"; grid[y][12] = "B"
                grid[y][18] = "B"; grid[y][19] = "B"
            }
            grid[18][10] = "E"; grid[18][11] = "E"; grid[18][12] = "E"
            grid[18][18] = "E"; grid[18][19] = "E"; grid[18][20] = "E"
        case .working:
            // Focused bright pixel glint in top-left of each eye + subtle antenna sparkle
            grid[16][11] = "W"
            grid[16][18] = "W"
            if waveTick {
                // Cute animated signal sparkles around antenna orb
                grid[0][17] = "Q"
                grid[1][26] = "Q"
                grid[3][27] = "W"
            } else {
                grid[1][17] = "W"
                grid[0][25] = "Q"
            }
        case .happy:
            // Cheerful ^ ^ pixel eyes
            for y in [16, 17, 18] {
                grid[y][11] = "B"; grid[y][12] = "B"
                grid[y][18] = "B"; grid[y][19] = "B"
            }
            grid[16][11] = "E"; grid[17][10] = "E"; grid[17][12] = "E"
            grid[16][19] = "E"; grid[17][18] = "E"; grid[17][20] = "E"
        case .error:
            // > < pixel eyes
            for y in [16, 17, 18] {
                grid[y][11] = "B"; grid[y][12] = "B"
                grid[y][18] = "B"; grid[y][19] = "B"
            }
            grid[16][10] = "E"; grid[17][11] = "E"; grid[18][10] = "E"
            grid[16][20] = "E"; grid[17][19] = "E"; grid[18][20] = "E"
            // Invert mouth to tiny 'o'
            grid[19][14] = "B"; grid[19][15] = "m"; grid[19][16] = "B"
        }

        return grid
    }

    static func rgba(for token: Character, orb: OrbPalette) -> SIMD4<UInt8>? {
        switch token {
        case ".": return nil
        case "K": return SIMD4(22, 25, 36, 255)      // Crisp pixel outline
        case "W": return SIMD4(255, 255, 255, 255)   // Pure white specular
        case "L", "l": return SIMD4(234, 237, 244, 255) // Light silver-white casing
        case "M": return SIMD4(194, 200, 212, 255)   // Mid bevel grey
        case "S": return SIMD4(142, 149, 165, 255)   // Lower shadow bevel
        case "D": return SIMD4(96, 103, 120, 255)    // Deep neck shadow
        case "G": return SIMD4(136, 144, 164, 255)   // Antenna base light
        case "g": return SIMD4(90, 97, 115, 255)     // Antenna base dark
        case "O": return orb.main                    // Dynamic orb primary
        case "Q": return orb.glow                    // Dynamic orb glow
        case "o": return orb.shade                   // Dynamic orb shadow
        case "b": return SIMD4(26, 88, 190, 255)     // Screen inner deep blue
        case "B": return SIMD4(48, 136, 255, 255)    // Screen vibrant sky blue
        case "C": return SIMD4(76, 160, 255, 255)    // Screen upper-right glow
        case "c": return SIMD4(126, 204, 255, 255)   // Cute lower-lip light blue
        case "A": return SIMD4(248, 252, 255, 255)   // Antigravity 'A' white badge
        case "E": return SIMD4(26, 30, 43, 255)      // Pixel eyes
        case "m": return SIMD4(26, 30, 43, 255)      // Cute smile
        case "p": return SIMD4(246, 142, 168, 255)   // Pink cheek blush
        case "P": return SIMD4(255, 184, 202, 255)   // Pink cheek center highlight
        case "n": return SIMD4(23, 78, 174, 255)     // Chest badge deep blue
        case "z": return SIMD4(138, 220, 255, 255)   // Chest badge cyan glyph
        default:  return nil
        }
    }

    // MARK: - Nearest-Neighbor CGImage / NSImage Renderers

    static func makeCGImage(
        phase: AgentPhase = .idle,
        expression: Expression = .normal,
        waveTick: Bool = false,
        cropRows: Range<Int>? = nil
    ) -> CGImage? {
        let grid = rows(for: expression, waveTick: waveTick)
        let rowRange = cropRows ?? (0..<height)
        let outH = rowRange.count
        let outW = width
        let orb = OrbPalette.forPhase(phase)

        var pixels = [UInt8](repeating: 0, count: outW * outH * 4)

        for (outY, srcY) in rowRange.enumerated() {
            guard srcY >= 0 && srcY < grid.count else { continue }
            let row = grid[srcY]
            for x in 0..<min(outW, row.count) {
                guard let c = rgba(for: row[x], orb: orb) else { continue }
                let offset = (outY * outW + x) * 4
                pixels[offset + 0] = c.x
                pixels[offset + 1] = c.y
                pixels[offset + 2] = c.z
                pixels[offset + 3] = c.w
            }
        }

        guard let provider = CGDataProvider(data: Data(pixels) as CFData) else { return nil }
        return CGImage(
            width: outW,
            height: outH,
            bitsPerComponent: 8,
            bitsPerPixel: 32,
            bytesPerRow: outW * 4,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
            provider: provider,
            decode: nil,
            shouldInterpolate: false,
            intent: .defaultIntent
        )
    }

    /// Crisp Retina icon for the macOS Menu Bar status item (crops to head + antenna `0..<25`)
    static func makeMenuBarIcon(phase: AgentPhase) -> NSImage? {
        let expr: Expression = {
            switch phase {
            case .thinking, .runningTool: return .working
            case .error: return .error
            default: return .normal
            }
        }()
        guard let rawCG = makeCGImage(phase: phase, expression: expr, cropRows: 0..<25) else {
            return nil
        }

        let targetPt = NSSize(width: 18, height: 18)
        let pxW = 36
        let pxH = 36

        guard let ctx = CGContext(
            data: nil,
            width: pxW,
            height: pxH,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return nil }

        ctx.interpolationQuality = .none
        // Draw centered with crisp nearest-neighbor scaling
        let drawW: CGFloat = 34
        let drawH: CGFloat = 28
        let rect = CGRect(x: (CGFloat(pxW) - drawW) * 0.5, y: (CGFloat(pxH) - drawH) * 0.5, width: drawW, height: drawH)
        ctx.draw(rawCG, in: rect)

        guard let scaled = ctx.makeImage() else { return nil }
        let image = NSImage(cgImage: scaled, size: targetPt)
        image.isTemplate = false
        return image
    }

    // MARK: - macOS Squircle `.iconset` Exporter (`--export-iconset <dir>`)

    static func exportAppIconset(to directoryURL: URL) throws {
        let fm = FileManager.default
        try fm.createDirectory(at: directoryURL, withIntermediateDirectories: true)

        let specs: [(name: String, pixels: Int)] = [
            ("icon_16x16.png", 16),
            ("icon_16x16@2x.png", 32),
            ("icon_32x32.png", 32),
            ("icon_32x32@2x.png", 64),
            ("icon_128x128.png", 128),
            ("icon_128x128@2x.png", 256),
            ("icon_256x256.png", 256),
            ("icon_256x256@2x.png", 512),
            ("icon_512x512.png", 512),
            ("icon_512x512@2x.png", 1024)
        ]

        guard let robotCG = makeCGImage(phase: .idle, expression: .normal, waveTick: true) else {
            return
        }

        for spec in specs {
            guard let pngData = renderSquircleIconPNG(pixelSize: spec.pixels, robotCG: robotCG) else {
                continue
            }
            try pngData.write(to: directoryURL.appendingPathComponent(spec.name))
        }
    }

    private static func renderSquircleIconPNG(pixelSize: Int, robotCG: CGImage) -> Data? {
        let s = CGFloat(pixelSize)
        guard let rep = NSBitmapImageRep(
            bitmapDataPlanes: nil,
            pixelsWide: pixelSize,
            pixelsHigh: pixelSize,
            bitsPerSample: 8,
            samplesPerPixel: 4,
            hasAlpha: true,
            isPlanar: false,
            colorSpaceName: .deviceRGB,
            bytesPerRow: 0,
            bitsPerPixel: 0
        ), let nsCtx = NSGraphicsContext(bitmapImageRep: rep) else {
            return nil
        }

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = nsCtx
        let cg = nsCtx.cgContext

        // Standard Apple macOS Big Sur / Sequoia icon inset (~9.6% margin -> 80.8% squircle body)
        let pad = s * 0.095
        let cardRect = CGRect(x: pad, y: pad, width: s - pad * 2, height: s - pad * 2)
        let radius = cardRect.width * 0.225
        let path = NSBezierPath(roundedRect: cardRect, xRadius: radius, yRadius: radius)

        // Subtle outer drop shadow for larger sizes
        if pixelSize >= 64 {
            cg.saveGState()
            cg.setShadow(
                offset: CGSize(width: 0, height: -s * 0.020),
                blur: s * 0.042,
                color: NSColor(calibratedWhite: 0.0, alpha: 0.22).cgColor
            )
            NSColor(calibratedWhite: 1.0, alpha: 1.0).setFill()
            path.fill()
            cg.restoreGState()
        }

        // Clip inside squircle & paint clean pure-white light surface (#FFFFFF -> #F9FBFE)
        NSGraphicsContext.saveGraphicsState()
        path.addClip()

        let bgGradient = NSGradient(colors: [
            NSColor(calibratedRed: 1.0, green: 1.0, blue: 1.0, alpha: 1.0),
            NSColor(calibratedRed: 0.975, green: 0.982, blue: 0.995, alpha: 1.0)
        ])
        bgGradient?.draw(in: cardRect, angle: -90)

        // Draw crisp pixel-art robot centered inside light squircle
        cg.saveGState()
        cg.interpolationQuality = .none
        if pixelSize >= 64 {
            cg.setShadow(
                offset: CGSize(width: 0, height: -s * 0.015),
                blur: s * 0.026,
                color: NSColor(calibratedRed: 0.08, green: 0.14, blue: 0.26, alpha: 0.20).cgColor
            )
        }
        let robotAspect = CGFloat(width) / CGFloat(height) // 31 / 37
        let robotH = round(cardRect.height * 0.78)
        let robotW = round(robotH * robotAspect)
        let robotRect = CGRect(
            x: round((s - robotW) * 0.5),
            y: round((s - robotH) * 0.5),
            width: robotW,
            height: robotH
        )
        cg.draw(robotCG, in: robotRect)
        cg.restoreGState()

        NSGraphicsContext.restoreGraphicsState()

        // Delicate light-mode squircle border so white icon remains crisp on white backgrounds
        NSColor(calibratedWhite: 0.0, alpha: 0.09).setStroke()
        path.lineWidth = max(1.0, s * 0.010)
        path.stroke()

        NSGraphicsContext.restoreGraphicsState()
        return rep.representation(using: .png, properties: [:])
    }
}

// MARK: - Animated SwiftUI Pixel Robot Mascot View

struct PixelRobotMascotView: View {
    let phase: AgentPhase
    var height: CGFloat = 74
    var animateFloat: Bool = true

    private var aspectRatio: CGFloat {
        CGFloat(PixelRobotSprite.width) / CGFloat(PixelRobotSprite.height)
    }

    var body: some View {
        TimelineView(.animation(minimumInterval: phase.isWorking ? 0.14 : 0.18)) { context in
            let t = context.date.timeIntervalSinceReferenceDate
            let expression = currentExpression(at: t)
            let waveTick = Int(t * 3.2) % 2 == 0
            let bobOffset: CGFloat = animateFloat
                ? CGFloat(sin(t * (phase.isWorking ? 4.2 : 2.1))) * (phase.isWorking ? 2.2 : 1.4)
                : 0

            Canvas { ctx, size in
                let cols = PixelRobotSprite.width
                let rows = PixelRobotSprite.height
                let grid = PixelRobotSprite.rows(for: expression, waveTick: waveTick)
                let orb = PixelRobotSprite.OrbPalette.forPhase(phase)

                let cellW = size.width / CGFloat(cols)
                let cellH = size.height / CGFloat(rows)

                for y in 0..<rows {
                    let row = grid[y]
                    let y0 = (CGFloat(y) * cellH).rounded(.down)
                    let y1 = (CGFloat(y + 1) * cellH).rounded(.down)
                    let h = max(1, y1 - y0)

                    for x in 0..<cols {
                        guard let c = PixelRobotSprite.rgba(for: row[x], orb: orb) else { continue }
                        let x0 = (CGFloat(x) * cellW).rounded(.down)
                        let x1 = (CGFloat(x + 1) * cellW).rounded(.down)
                        let w = max(1, x1 - x0)

                        let rect = CGRect(x: x0, y: y0, width: w, height: h)
                        let color = Color(
                            .sRGB,
                            red: Double(c.x) / 255.0,
                            green: Double(c.y) / 255.0,
                            blue: Double(c.z) / 255.0,
                            opacity: Double(c.w) / 255.0
                        )
                        ctx.fill(Path(rect), with: .color(color))
                    }
                }
            }
            .frame(width: (height * aspectRatio).rounded(), height: height.rounded())
            .offset(y: bobOffset)
        }
    }

    private func currentExpression(at t: TimeInterval) -> PixelRobotSprite.Expression {
        switch phase {
        case .error:
            return .error
        case .thinking, .runningTool:
            let cycle = t.truncatingRemainder(dividingBy: 3.6)
            return cycle < 0.16 ? .blink : .working
        case .waitingUser:
            let cycle = t.truncatingRemainder(dividingBy: 3.0)
            return cycle < 0.16 ? .blink : .happy
        default:
            let cycle = t.truncatingRemainder(dividingBy: 4.4)
            return cycle < 0.15 ? .blink : .normal
        }
    }
}
