// Draws the Workbench icon (two stacked amber sheets on ink) as a 1024 px PNG. Usage: swift makeicon.swift out.png
import AppKit

let size: CGFloat = 1024
let img = NSImage(size: NSSize(width: size, height: size))
img.lockFocus()
let k = size / 24
func rounded(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat, _ r: CGFloat, _ c: NSColor) {
    c.setFill()
    // AppKit's origin is bottom-left: flip y so the drawing matches the Windows icon
    NSBezierPath(roundedRect: NSRect(x: x * k, y: size - (y + h) * k, width: w * k, height: h * k), xRadius: r * k, yRadius: r * k).fill()
}
rounded(1, 1, 22, 22, 5.2, NSColor(red: 0.078, green: 0.086, blue: 0.11, alpha: 1))
rounded(8, 4.5, 12, 9.5, 2.6, NSColor(red: 0.957, green: 0.722, blue: 0.376, alpha: 0.47))
rounded(4, 10, 12, 9.5, 2.6, NSColor(red: 0.957, green: 0.722, blue: 0.376, alpha: 1))
img.unlockFocus()
let rep = NSBitmapImageRep(data: img.tiffRepresentation!)!
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: CommandLine.arguments[1]))
