// Draws ISLe8's MSIX logos into src/ISLe8.App/Assets, in the look of the Mac app's icon
// (dark tile, warm Ember glow, the island with artwork and bars). Runs on a Mac:
//   swift scripts/make-assets.swift
import AppKit
import SwiftUI

func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(
        srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
        green: CGFloat((hex >> 8) & 0xFF) / 255,
        blue: CGFloat(hex & 0xFF) / 255,
        alpha: alpha
    )
}

func linear(_ colors: [CGColor]) -> CGGradient {
    CGGradient(colorsSpace: CGColorSpace(name: CGColorSpace.sRGB), colors: colors as CFArray, locations: nil)!
}

/// The island: a black capsule with artwork on the left and bars on the right.
func drawIsland(in context: CGContext, center: CGPoint, width: CGFloat) {
    let height = width * 0.28
    let island = CGRect(x: center.x - width / 2, y: center.y - height / 2, width: width, height: height)
    let capsule = CGPath(roundedRect: island, cornerWidth: height / 2, cornerHeight: height / 2, transform: nil)
    context.saveGState()
    context.setShadow(offset: CGSize(width: 0, height: -height * 0.06), blur: height * 0.24, color: color(0x000000, 0.6))
    context.addPath(capsule)
    context.setFillColor(color(0x000000))
    context.fillPath()
    context.restoreGState()
    context.addPath(capsule)
    context.setStrokeColor(color(0xFF8A4C, 0.22))
    context.setLineWidth(max(1, height * 0.012))
    context.strokePath()

    let artSize = height * 0.6
    let art = CGRect(x: island.minX + height * 0.2, y: island.midY - artSize / 2, width: artSize, height: artSize)
    context.saveGState()
    context.addPath(CGPath(roundedRect: art, cornerWidth: artSize * 0.26, cornerHeight: artSize * 0.26, transform: nil))
    context.clip()
    context.drawLinearGradient(
        linear([color(0xFFB340), color(0xFF375F)]),
        start: CGPoint(x: art.minX, y: art.maxY), end: CGPoint(x: art.maxX, y: art.minY), options: [])
    context.restoreGState()

    let heights: [CGFloat] = [0.31, 0.55, 0.42, 0.24].map { $0 * height }
    let barWidth = height * 0.12
    let gap = height * 0.083
    var x = island.maxX - height * 0.27 - CGFloat(heights.count) * barWidth - CGFloat(heights.count - 1) * gap
    for barHeight in heights {
        let bar = CGRect(x: x, y: island.midY - barHeight / 2, width: barWidth, height: barHeight)
        context.addPath(CGPath(roundedRect: bar, cornerWidth: barWidth / 2, cornerHeight: barWidth / 2, transform: nil))
        x += barWidth + gap
    }
    context.setFillColor(color(0xFF8A4C, 0.95))
    context.fillPath()
}

/// The square tile: Ember's dark gradient, a glow under the island.
func drawTile(in context: CGContext, size: CGFloat) {
    let body = CGRect(x: 0, y: 0, width: size, height: size).insetBy(dx: size * 0.04, dy: size * 0.04)
    let shape = RoundedRectangle(cornerRadius: size * 0.2, style: .continuous).path(in: body).cgPath
    context.saveGState()
    context.addPath(shape)
    context.clip()
    context.drawLinearGradient(
        linear([color(0x2E140B), color(0x110A08)]),
        start: CGPoint(x: 0, y: 0), end: CGPoint(x: 0, y: size), options: [])
    context.drawRadialGradient(
        linear([color(0xFF7A3D, 0.40), color(0xFF375F, 0.0)]),
        startCenter: CGPoint(x: size / 2, y: size * 0.5), startRadius: 0,
        endCenter: CGPoint(x: size / 2, y: size * 0.46), endRadius: size * 0.46, options: [])
    context.restoreGState()
    context.addPath(shape)
    context.setStrokeColor(color(0xFFFFFF, 0.10))
    context.setLineWidth(max(1, size * 0.004))
    context.strokePath()
    drawIsland(in: context, center: CGPoint(x: size / 2, y: size * 0.5), width: size * 0.66)
}

/// Wide tile and splash: the island and the wordmark on Ember's gradient.
func drawBanner(in context: CGContext, width: CGFloat, height: CGFloat) {
    context.drawLinearGradient(
        linear([color(0x2E140B), color(0x110A08)]),
        start: CGPoint(x: 0, y: 0), end: CGPoint(x: 0, y: height), options: [])
    context.drawRadialGradient(
        linear([color(0xFF7A3D, 0.30), color(0xFF375F, 0.0)]),
        startCenter: CGPoint(x: width / 2, y: height / 2), startRadius: 0,
        endCenter: CGPoint(x: width / 2, y: height / 2), endRadius: width * 0.5, options: [])
    let islandWidth = height * 0.95
    drawIsland(in: context, center: CGPoint(x: width / 2 - height * 0.42, y: height / 2), width: islandWidth)
    let fontSize = height * 0.2
    let font = NSFont.systemFont(ofSize: fontSize, weight: .bold)
    let text = NSAttributedString(string: "ISLe8", attributes: [
        .font: font,
        .foregroundColor: NSColor(white: 1, alpha: 0.92),
        .kern: -fontSize * 0.02,
    ])
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: false)
    text.draw(at: CGPoint(x: width / 2 + height * 0.14, y: height / 2 - text.size().height / 2))
    NSGraphicsContext.restoreGraphicsState()
}

func png(width: Int, height: Int, draw: (CGContext) -> Void) -> Data {
    let context = CGContext(
        data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
        space: CGColorSpace(name: CGColorSpace.sRGB)!,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    )!
    context.interpolationQuality = .high
    draw(context)
    return NSBitmapImageRep(cgImage: context.makeImage()!).representation(using: .png, properties: [:])!
}

let folder = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "src/ISLe8.App/Assets")
try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)

// Each logo at 100 % and 200 %; MRT picks the right one for the display's scale.
for (name, size) in [("Square44x44Logo", 44), ("Square150x150Logo", 150), ("StoreLogo", 50)] {
    for scale in [1, 2] {
        let pixels = size * scale
        let file = scale == 1 ? "\(name).png" : "\(name).scale-200.png"
        try png(width: pixels, height: pixels) { drawTile(in: $0, size: CGFloat(pixels)) }.write(to: folder.appending(path: file))
    }
}
for (name, width, height) in [("Wide310x150Logo", 310, 150), ("SplashScreen", 620, 300)] {
    for scale in [1, 2] {
        let file = scale == 1 ? "\(name).png" : "\(name).scale-200.png"
        try png(width: width * scale, height: height * scale) {
            drawBanner(in: $0, width: CGFloat(width * scale), height: CGFloat(height * scale))
        }.write(to: folder.appending(path: file))
    }
}
print(folder.path)
