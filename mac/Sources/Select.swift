import SwiftUI
import AppKit
import UniformTypeIdentifiers

// Selecting several files (drag a frame, ⌘-click, ⌘A) and switching layers by holding a dragged file at the top or bottom edge.

/// Where each visible tile is, in the sheet's coordinates.
struct TileFrames: PreferenceKey {
    static var defaultValue: [String: CGRect] = [:]
    static func reduce(value: inout [String: CGRect], nextValue: () -> [String: CGRect]) { value.merge(nextValue()) { $1 } }
}

extension View {
    func reportsFrame(_ path: String) -> some View {
        background(GeometryReader { g in Color.clear.preference(key: TileFrames.self, value: [path: g.frame(in: .named("sheet"))]) })
    }
}

extension MainModel {
    /// Everything selected together with this one (otherwise just the one).
    func with(_ p: String) -> [String] { selection.count > 1 && selection.contains(p) ? Array(selection) : [p] }

    func click(_ p: String) {
        if NSEvent.modifierFlags.contains(.command) {
            if selection.contains(p) { selection.remove(p) } else { selection.insert(p) }
        } else { selection = [p] }
    }

    func selectAll() { selection = Set(frames.keys) }

    /// Frame from a to b: selects everything it touches.
    func frame(_ a: CGPoint, _ b: CGPoint, keep: Set<String>) {
        let r = CGRect(x: min(a.x, b.x), y: min(a.y, b.y), width: abs(a.x - b.x), height: abs(a.y - b.y))
        marquee = r
        let hit = keep.union(frames.filter { $0.value.insetBy(dx: 10, dy: 8).intersects(r) }.keys)
        if hit != selection { selection = hit }
    }

    func move(_ paths: [String], to dir: URL) {
        for p in paths {
            let u = URL(fileURLWithPath: p)
            if u.deletingLastPathComponent().path == dir.path || dir.path == p || dir.path.hasPrefix(p + "/") { continue }
            if let e = Store.shared.find(p) { Engine.shared.move(e, to: dir) }
            else {
                do { try Paths.fm.moveItem(at: u, to: Paths.freeName(in: dir, u.lastPathComponent)) } catch { Log.info("move: \(error)") }
            }
        }
        selection = []
        Store.shared.version += 1
    }

    func trash(_ paths: [String]) {
        for p in paths {
            if let e = Store.shared.find(p) { Engine.shared.trashNow(e) }
            else {
                do { try Paths.fm.trashItem(at: URL(fileURLWithPath: p), resultingItemURL: nil) } catch { Log.info("trash: \(error)") }
            }
        }
        selection = []
        Store.shared.version += 1
    }

    static var standardFolder: URL {
        let f = Paths.timeFolders.first(where: { $0.1 == Store.shared.settings.standard })?.0 ?? "1 Week"
        return Paths.root.appendingPathComponent(f)
    }
}

/// Small bar while several files are selected.
struct SelectionBar: View {
    @ObservedObject var m = MainModel.shared
    var body: some View {
        if m.selection.count > 1 && m.layer != .settings {
            HStack(spacing: 4) {
                Text("\(m.selection.count)").font(.system(size: 13, weight: .semibold)).foregroundColor(.amber).padding(.horizontal, 10)
                if m.layer != .permanent { RoundIcon(system: "infinity", help: "Move to Permanent") { m.move(Array(m.selection), to: Paths.permanent) } }
                if m.layer != .cloud, let c = Cloud.current(Store.shared.settings) {
                    RoundIcon(system: "cloud", help: "Move to " + c.name) { m.move(Array(m.selection), to: URL(fileURLWithPath: c.path)) }
                }
                RoundIcon(system: "trash", help: "Move to Trash") { m.trash(Array(m.selection)) }
            }
            .padding(6)
            .background(Capsule().fill(Color.card))
            .overlay(Capsule().stroke(Color.white.opacity(0.12)))
            .shadow(color: .black.opacity(0.4), radius: 12, y: 4)
        }
    }
}

/// The soft white glow that grows while a dragged file rests at the edge.
struct EdgeGlow: View {
    @ObservedObject var edge = EdgeHold.shared
    var body: some View {
        let top = edge.zone < 0
        let w = 1 - (1 - edge.progress) * (1 - edge.progress)
        VStack(spacing: 0) {
            if !top { Spacer(minLength: 0) }
            LinearGradient(stops: [.init(color: Color.white.opacity(0.36), location: 0), .init(color: Color.white.opacity(0.15), location: 0.35), .init(color: .clear, location: 1)],
                           startPoint: top ? .top : .bottom, endPoint: top ? .bottom : .top)
                .frame(height: 110)
                .scaleEffect(x: 1, y: 0.35 + 0.65 * w, anchor: top ? .top : .bottom)
            if top { Spacer(minLength: 0) }
        }
        .opacity(edge.zone == 0 || edge.waiting ? 0 : 0.15 + 0.85 * w)
        .animation(.easeOut(duration: 0.2), value: edge.zone == 0 || edge.waiting)
        .clipShape(RoundedRectangle(cornerRadius: 18))
        .allowsHitTesting(false)
    }
}

/// Runs only while a file is being dragged.
final class EdgeHold: ObservableObject {
    static let shared = EdgeHold()
    @Published var zone = 0
    @Published var progress: Double = 0
    @Published var waiting = false
    /// Files dragged together from inside Workbench (SwiftUI hands over only the one under the pointer).
    var dragged: [String] = []
    private var timer: Timer?
    private var since = Date()

    func begin() {
        if timer != nil { return }
        let t = Timer(timeInterval: 0.03, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    private func tick() {
        guard NSEvent.pressedMouseButtons & 1 != 0 else {
            timer?.invalidate(); timer = nil
            zone = 0; progress = 0; waiting = false
            // dropped elsewhere: forget the group (the drop itself arrives just after the button is released)
            DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in if let s = self, s.timer == nil { s.dragged = [] } }
            return
        }
        let z = currentZone()
        if z != zone { zone = z; since = Date(); progress = 0; waiting = false; return }
        guard z != 0 else { return }
        let dt = Date().timeIntervalSince(since)
        if waiting {
            if dt > 0.45 { waiting = false; since = Date(); progress = 0 }       // still at the edge: on to the next layer
            return
        }
        progress = min(1, dt / 0.7)
        if progress >= 1 {
            waiting = true
            since = Date()
            MainModel.shared.step(z)
        }
    }

    private func currentZone() -> Int {
        guard let w = AppDelegate.shared?.window, w.isVisible, let v = w.contentView else { return 0 }
        let p = w.convertPoint(fromScreen: NSEvent.mouseLocation)
        let size = v.bounds.size
        guard p.x >= 60, p.x <= size.width, p.y >= 0, p.y <= size.height else { return 0 }
        let layer = MainModel.shared.layer.rawValue
        guard layer < 3 else { return 0 }
        if p.y > size.height - 76 { return layer > 0 ? -1 : 0 }
        if p.y < 64 { return layer < 2 ? 1 : 0 }
        return 0
    }
}

/// Dropping on the Expiring surface: gets the default time. Files that already have a timer stay as they are.
struct ExpiringDrop: DropDelegate {
    func dropEntered(info: DropInfo) { EdgeHold.shared.begin() }
    func performDrop(info: DropInfo) -> Bool { dropFiles(info, into: MainModel.standardFolder, skipTimed: true) }
}
