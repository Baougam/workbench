import SwiftUI
import AppKit

// Colors shared by every view (same palette as Windows)
extension Color {
    init(hex: UInt32, a: Double = 1) {
        self.init(.sRGB, red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255, blue: Double(hex & 0xFF) / 255, opacity: a)
    }
    static let amber = Color(hex: 0xF4B860)
    static let ink = Color(hex: 0x0E1015)
    static let card = Color(hex: 0x191C23)
    static let text1 = Color(hex: 0xECEEF2)
    static let text2 = Color(hex: 0xA3AAB6)
    static let text3 = Color(hex: 0x8A92A0)
    static let danger = Color(hex: 0xFF9C8F)
}

func typeColor(_ ext: String) -> Color {
    switch ext.lowercased() {
    case "pdf": return Color(hex: 0xF08A7E)
    case "zip", "rar", "7z", "gz", "tar", "dmg": return Color(hex: 0xE9C46A)
    case "exe", "msi", "pkg", "app": return Color(hex: 0x9DB4FF)
    case "png", "jpg", "jpeg", "gif", "webp", "heic", "bmp", "svg", "tiff": return Color(hex: 0x6CD3A6)
    case "mp4", "mov", "mkv", "avi", "webm", "mp3", "wav", "m4a": return Color(hex: 0xC3A6F5)
    case "doc", "docx", "pages", "odt", "rtf": return Color(hex: 0x9DB4FF)
    case "xls", "xlsx", "numbers", "csv": return Color(hex: 0x7ED39B)
    case "ppt", "pptx", "key": return Color(hex: 0xF4A261)
    default: return Color(hex: 0xC9CED8)
    }
}

/// Workbench's own file glyph: a calm document with a folded corner in the type's color – not the opening app's icon.
/// Images show their real thumbnail.
struct Glyph: View {
    let path: String
    var w: CGFloat = 38, h: CGFloat = 46
    var body: some View {
        let ext = (path as NSString).pathExtension
        if ["png", "jpg", "jpeg", "gif", "webp", "heic", "bmp", "tiff"].contains(ext.lowercased()), let img = NSImage(contentsOfFile: path) {
            Image(nsImage: img).resizable().aspectRatio(contentMode: .fill).frame(width: w, height: w).clipShape(RoundedRectangle(cornerRadius: 9))
                .overlay(RoundedRectangle(cornerRadius: 9).stroke(Color.white.opacity(0.15)))
                .frame(width: w, height: h)
        } else {
            ZStack(alignment: .bottom) {
                DocShape(fold: w * 0.34).fill(typeColor(ext))
                DocShape(fold: w * 0.34).fill(LinearGradient(colors: [Color.white.opacity(0.16), .clear], startPoint: .top, endPoint: .bottom))
                FoldShape(fold: w * 0.34).fill(Color.white.opacity(0.45))
                Text(ext.isEmpty ? "FILE" : String(ext.uppercased().prefix(4)))
                    .font(.system(size: max(8, w * 0.21), weight: .bold)).foregroundColor(Color(hex: 0x1C1405, a: 0.75)).padding(.bottom, h * 0.12)
            }.frame(width: w, height: h)
        }
    }
}

struct DocShape: Shape {
    var fold: CGFloat
    func path(in r: CGRect) -> Path {
        var p = Path(); let c: CGFloat = 6
        p.move(to: CGPoint(x: c, y: 0)); p.addLine(to: CGPoint(x: r.width - fold, y: 0)); p.addLine(to: CGPoint(x: r.width, y: fold))
        p.addLine(to: CGPoint(x: r.width, y: r.height - c)); p.addQuadCurve(to: CGPoint(x: r.width - c, y: r.height), control: CGPoint(x: r.width, y: r.height))
        p.addLine(to: CGPoint(x: c, y: r.height)); p.addQuadCurve(to: CGPoint(x: 0, y: r.height - c), control: CGPoint(x: 0, y: r.height))
        p.addLine(to: CGPoint(x: 0, y: c)); p.addQuadCurve(to: CGPoint(x: c, y: 0), control: .zero); p.closeSubpath()
        return p
    }
}

struct FoldShape: Shape {
    var fold: CGFloat
    func path(in r: CGRect) -> Path {
        var p = Path()
        p.move(to: CGPoint(x: r.width - fold, y: 0)); p.addLine(to: CGPoint(x: r.width - fold, y: fold - 3))
        p.addQuadCurve(to: CGPoint(x: r.width - fold + 3, y: fold), control: CGPoint(x: r.width - fold, y: fold)); p.addLine(to: CGPoint(x: r.width, y: fold)); p.closeSubpath()
        return p
    }
}

/// The time slider – Hour · Day · Week · Month · ∞. Same in the download card and in the file card.
struct TimeSlider: View {
    var current: String?
    var choose: (String) -> Void
    var calendar: () -> Void
    var cloud: CloudPlace?
    var toCloud: ((CloudPlace) -> Void)?
    @State private var hover: String?

    var body: some View {
        HStack(alignment: .top, spacing: 6) {
            ZStack(alignment: .top) {
                GeometryReader { g in
                    let half = g.size.width / CGFloat(Dur.stations.count) / 2
                    Rectangle().fill(Color.white.opacity(0.15)).frame(height: 2).padding(.horizontal, half).padding(.top, 12)
                }.frame(height: 26)
                HStack(spacing: 0) {
                    ForEach(Dur.stations, id: \.self) { s in
                        let on = s == current
                        Button(action: { choose(s) }) {
                            VStack(spacing: 3) {
                                ZStack {
                                    if on { Circle().fill(Color.amber.opacity(0.2)).frame(width: 22, height: 22) }
                                    Circle().fill(on ? Color.amber : (hover == s ? Color.white : Color.white.opacity(0.55)))
                                        .frame(width: on ? 14 : (hover == s ? 12 : 8), height: on ? 14 : (hover == s ? 12 : 8))
                                }.frame(width: 26, height: 26)
                                Text(Dur.short(s)).font(.system(size: s == Dur.forever ? 15 : 12, weight: on ? .semibold : .regular))
                                    .foregroundColor(on || hover == s ? .text1 : .text2)
                            }.frame(maxWidth: .infinity).contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        .help(s == Dur.forever ? "Forever" : "")
                        .onHover { h in hover = h ? s : (hover == s ? nil : hover) }
                    }
                }
            }
            Rectangle().fill(Color.white.opacity(0.12)).frame(width: 1, height: 24).padding(.top, 3)
            RoundIcon(system: "calendar", help: "Until…", action: calendar)
            if let c = cloud, let t = toCloud { RoundIcon(system: "cloud", help: c.name) { t(c) } }
        }
    }
}

struct RoundIcon: View {
    var system: String
    var help: String
    var color: Color = .text2
    var action: () -> Void
    @State private var over = false
    var body: some View {
        Button(action: action) {
            Image(systemName: system).font(.system(size: 14, weight: .regular)).foregroundColor(color)
                .frame(width: 32, height: 32).background(Circle().fill(Color.white.opacity(over ? 0.12 : 0)))
        }.buttonStyle(.plain).help(help).onHover { over = $0 }
    }
}

// MARK: - The card after a download

final class PromptModel: ObservableObject {
    @Published var files: [Entry]
    @Published var suggestion: String
    @Published var showCalendar = false
    @Published var confirmation: String?
    @Published var progress: Double = 1
    var info: Bool
    var paused = false
    init(files: [Entry], info: Bool) {
        self.files = files
        self.info = info
        suggestion = files.first?.chosen ?? Store.shared.settings.standard
    }
}

struct PromptView: View {
    @ObservedObject var m: PromptModel
    var apply: (String, Date?, Bool) -> Void
    var toCloud: (CloudPlace) -> Void
    var close: () -> Void
    @State private var day = Date().addingTimeInterval(86400)

    var body: some View {
        VStack(spacing: 0) {
            // stacked sheets – same motif as the main window
            ZStack(alignment: .top) {
                RoundedRectangle(cornerRadius: 14).fill(Color(hex: 0x2A2E38, a: 0.75)).frame(height: 14).padding(.horizontal, 36)
                RoundedRectangle(cornerRadius: 16).fill(Color(hex: 0x242833, a: 0.9)).frame(height: 14).padding(.horizontal, 18).padding(.top, 5)
            }.frame(height: 10, alignment: .top).clipped()
            VStack(alignment: .leading, spacing: 12) {
                if let c = m.confirmation {
                    HStack(spacing: 10) {
                        Image(systemName: "checkmark").font(.system(size: 13, weight: .bold)).foregroundColor(.amber)
                            .frame(width: 30, height: 30).background(Circle().fill(Color.amber.opacity(0.15)))
                        Text(c).font(.system(size: 14, weight: .semibold)).foregroundColor(.text1)
                        Text(m.files.first?.name ?? "").font(.system(size: 12.5)).foregroundColor(.text3).lineLimit(1)
                    }.padding(.vertical, 4)
                } else {
                    HStack(spacing: 14) {
                        ZStack(alignment: .bottomLeading) {
                            ForEach(Array(m.files.prefix(3).enumerated().reversed()), id: \.offset) { item in
                                Glyph(path: item.element.path).opacity(item.offset == 0 ? 1 : 0.45)
                                    .offset(x: CGFloat(item.offset) * 4, y: CGFloat(-item.offset) * 3)
                            }
                        }.frame(width: 46, height: 50, alignment: .bottomLeading)
                        VStack(alignment: .leading, spacing: 3) {
                            HStack(spacing: 8) {
                                Text(m.files.first?.name ?? "").font(.system(size: 14, weight: .semibold)).foregroundColor(.text1).lineLimit(1)
                                if m.files.count > 1 {
                                    Text("+\(m.files.count - 1)").font(.system(size: 11, weight: .semibold)).foregroundColor(.text2)
                                        .padding(.horizontal, 6).padding(.vertical, 1).background(Capsule().fill(Color.white.opacity(0.12)))
                                }
                            }
                            Text(meta).font(.system(size: 12)).foregroundColor(.text3)
                        }
                        Spacer(minLength: 0)
                        Button(action: { m.info ? close() : apply(m.suggestion, nil, false) }) {
                            Image(systemName: "xmark").font(.system(size: 10, weight: .bold)).foregroundColor(.text3).frame(width: 26, height: 26)
                        }.buttonStyle(.plain).help("Close")
                    }
                    if m.showCalendar {
                        DatePicker("", selection: $day, in: Date()..., displayedComponents: .date)
                            .datePickerStyle(.graphical).labelsHidden().colorScheme(.dark)
                        HStack {
                            Button("Back") { m.showCalendar = false; m.paused = false }.buttonStyle(.plain).foregroundColor(.text2)
                            Spacer()
                            Button(action: { apply("Until…", endOfDay(day), true) }) {
                                Text("Keep until " + shortDate(day)).font(.system(size: 13, weight: .semibold)).foregroundColor(Color(hex: 0x1C1405))
                                    .padding(.horizontal, 14).padding(.vertical, 7).background(Capsule().fill(Color.amber))
                            }.buttonStyle(.plain)
                        }
                    } else {
                        TimeSlider(current: m.suggestion, choose: { apply($0, nil, true) }, calendar: { m.showCalendar = true; m.paused = true },
                                   cloud: Cloud.current(Store.shared.settings), toCloud: toCloud)
                    }
                    // time runs out quietly: a fine amber line at the bottom
                    GeometryReader { g in
                        ZStack(alignment: .leading) {
                            Capsule().fill(Color.white.opacity(0.05))
                            Capsule().fill(Color.amber.opacity(0.7)).frame(width: g.size.width * CGFloat(m.progress))
                        }
                    }.frame(height: 2).padding(.horizontal, 4)
                }
            }
            .padding(EdgeInsets(top: 16, leading: 16, bottom: 12, trailing: 14))
            .background(RoundedRectangle(cornerRadius: 18).fill(Color.card.opacity(0.97)))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(Color.white.opacity(0.12)))
        }
        .frame(width: 370)
        .padding(18)
        .shadow(color: .black.opacity(0.5), radius: 18, y: 8)
        .onHover { m.paused = $0 || m.showCalendar }
    }

    var meta: String {
        let ext = (m.files.first?.path as NSString?)?.pathExtension.uppercased() ?? ""
        var size: Int64 = 0
        for f in m.files { size += ((try? Paths.fm.attributesOfItem(atPath: f.path)[.size]) as? Int64) ?? 0 }
        let kind = m.files.count > 1 ? "\(m.files.count) files" : (ext.isEmpty ? "File" : ext)
        return size > 0 ? kind + " · " + ByteCountFormatter.string(fromByteCount: size, countStyle: .file) : kind
    }
}

func endOfDay(_ d: Date) -> Date { Calendar.current.date(bySettingHour: 23, minute: 59, second: 0, of: d) ?? d }
func shortDate(_ d: Date) -> String { let f = DateFormatter(); f.locale = Locale(identifier: "en_GB"); f.dateFormat = "d MMM"; return f.string(from: d) }

/// Floating, non-activating panel bottom right. Never steals focus; one click is enough; ignore it and the suggestion applies.
final class PromptPanel: NSPanel {
    let model: PromptModel
    private var timer: Timer?
    private var done = false
    var onClose: (() -> Void)?

    init(files: [Entry], info: Bool) {
        model = PromptModel(files: files, info: info)
        super.init(contentRect: NSRect(x: 0, y: 0, width: 406, height: 220), styleMask: [.nonactivatingPanel, .borderless], backing: .buffered, defer: false)
        isFloatingPanel = true
        level = .floating
        backgroundColor = .clear
        isOpaque = false
        hasShadow = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        hidesOnDeactivate = false
        let view = PromptView(m: model,
                              apply: { [weak self] d, until, active in self?.apply(d, until, active) },
                              toCloud: { [weak self] c in self?.toCloud(c) },
                              close: { [weak self] in self?.fadeOut() })
        let host = NSHostingView(rootView: view)
        host.wantsLayer = true
        host.layer?.backgroundColor = .clear
        contentView = host
    }

    override var canBecomeKey: Bool { true }

    func show() {
        guard let v = contentView else { return }
        let size = v.fittingSize
        let screen = NSScreen.screens.first(where: { NSMouseInRect(NSEvent.mouseLocation, $0.frame, false) }) ?? NSScreen.main
        let f = screen?.visibleFrame ?? .zero
        setFrame(NSRect(x: f.maxX - size.width + 4, y: f.minY - 4, width: size.width, height: size.height), display: true)
        alphaValue = 0
        orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { c in c.duration = 0.22; animator().alphaValue = 1 }
        let total: Double = model.info ? 6 : 8
        timer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in
            guard let self = self, !self.model.paused, !self.done else { return }
            self.model.progress -= (1.0 / 30) / total
            if self.model.progress <= 0 {
                if self.model.info { self.fadeOut() } else { self.apply(self.model.suggestion, nil, false) }
            }
        }
    }

    func add(_ more: [Entry]) {
        guard !done, !model.info else { return }
        model.files.append(contentsOf: more)
        model.progress = 1
    }

    var isOpen: Bool { !done }
    var isInfo: Bool { model.info }

    private func apply(_ d: String, _ until: Date?, _ active: Bool) {
        guard !done else { return }
        done = true
        if model.info && !active { fadeOut(); return }
        let e = Engine.shared, s = Store.shared
        let x: Date? = until ?? Dur.expiry(d, from: Date())
        for f in model.files {
            guard let cur = s.find(f.path) else { continue }
            if active && d != "Until…" { s.remember(f.path, d) }
            e.setTimer(cur, until: x, chosen: d == "Until…" ? "until " + shortDate(until ?? Date()) : d)
        }
        if active { s.save() }
        confirm(x == nil ? "Kept forever" : "Stays until " + Dur.when(x!))
    }

    private func toCloud(_ c: CloudPlace) {
        guard !done else { return }
        done = true
        for f in model.files { if let cur = Store.shared.find(f.path) { Engine.shared.move(cur, to: URL(fileURLWithPath: c.path)) } }
        confirm("Moved to " + c.name)
    }

    private func confirm(_ text: String) {
        model.confirmation = text
        DispatchQueue.main.async { [weak self] in
            guard let self = self, let v = self.contentView else { return }
            let s = v.fittingSize; var fr = self.frame; fr.size = s; self.setFrame(fr, display: true)
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.1) { [weak self] in self?.fadeOut() }
    }

    func fadeOut() {
        done = true
        timer?.invalidate()
        NSAnimationContext.runAnimationGroup({ c in c.duration = 0.2; animator().alphaValue = 0 }, completionHandler: { [weak self] in
            self?.orderOut(nil); self?.onClose?()
        })
    }
}
