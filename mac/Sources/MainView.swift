import SwiftUI
import AppKit
import UniformTypeIdentifiers

enum Layer: Int { case expiring = 0, permanent = 1, cloud = 2, settings = 4 }

final class MainModel: ObservableObject {
    static let shared = MainModel()
    @Published var layer: Layer = .expiring
    @Published var lastLayer: Layer = .expiring
    @Published var search = ""
    @Published var selected: String?
    @Published var folder: [Int: String] = [:]          // current subfolder per layer (1, 2)
    @Published var tour: Int? = nil
    @Published var slideFrom: CGFloat = 0
    var back: [Int: [String]] = [1: [], 2: []]
    var forward: [Int: [String]] = [1: [], 2: []]

    func show(_ l: Layer) {
        guard l != layer else { return }
        let dir: CGFloat = (l.rawValue < 3 && layer.rawValue < 3) ? (l.rawValue > layer.rawValue ? 1 : -1) : 0
        slideFrom = dir * 40
        withAnimation(.spring(response: 0.36, dampingFraction: 0.82)) {
            layer = l
            if l.rawValue < 3 { lastLayer = l }
            selected = nil
            search = ""
        }
    }

    func step(_ d: Int) {
        let from = layer.rawValue < 3 ? layer.rawValue : lastLayer.rawValue
        let to = max(0, min(2, from + d))
        if let l = Layer(rawValue: to), l != layer { show(l); if tour == 1 { tour = 2 } }     // trying it yourself counts in the tour
    }

    func root(_ l: Int) -> String? {
        if l == 1 { return Paths.permanent.path }
        if l == 2 { return Cloud.current(Store.shared.settings)?.path }
        return nil
    }

    func currentFolder(_ l: Int) -> String? {
        guard let r = root(l) else { return nil }
        if let f = folder[l], f.hasPrefix(r), Paths.fm.fileExists(atPath: f) { return f }
        return r
    }

    func enter(_ l: Int, _ path: String) {
        if let cur = currentFolder(l), cur != path { back[l, default: []].append(cur); forward[l] = [] }
        withAnimation(.easeOut(duration: 0.2)) { folder[l] = path; selected = nil }
    }

    func goBack() {
        let l = layer.rawValue
        guard l == 1 || l == 2, let p = back[l]?.popLast(), let cur = currentFolder(l) else { return }
        forward[l, default: []].append(cur)
        withAnimation(.easeOut(duration: 0.2)) { folder[l] = p }
    }

    func goForward() {
        let l = layer.rawValue
        guard l == 1 || l == 2, let p = forward[l]?.popLast(), let cur = currentFolder(l) else { return }
        back[l, default: []].append(cur)
        withAnimation(.easeOut(duration: 0.2)) { folder[l] = p }
    }
}

/// Your own desktop wallpaper – loaded tiny and scaled up softly (free blur), darkened.
struct Wallpaper: View {
    static let image: NSImage? = {
        guard let s = NSScreen.main, let u = NSWorkspace.shared.desktopImageURL(for: s), let img = NSImage(contentsOf: u) else { return nil }
        let small = NSImage(size: NSSize(width: 64, height: 40))
        small.lockFocus(); img.draw(in: NSRect(x: 0, y: 0, width: 64, height: 40)); small.unlockFocus()
        return small
    }()
    var body: some View {
        ZStack {
            Color.ink
            if let i = Wallpaper.image { Image(nsImage: i).resizable().interpolation(.high).aspectRatio(contentMode: .fill).scaleEffect(1.15) }
            else {
                RadialGradient(colors: [Color.amber.opacity(0.25), .clear], center: UnitPoint(x: 0.1, y: 1.05), startRadius: 0, endRadius: 700)
                RadialGradient(colors: [Color(hex: 0x3E8E9C, a: 0.22), .clear], center: UnitPoint(x: 0.95, y: -0.05), startRadius: 0, endRadius: 700)
            }
            Color.black.opacity(Wallpaper.image == nil ? 0.15 : 0.64)
        }.ignoresSafeArea()
    }
}

struct MainView: View {
    @ObservedObject var m = MainModel.shared
    @ObservedObject var store = Store.shared
    @State private var dragY: CGFloat = 0

    var body: some View {
        ZStack(alignment: .topLeading) {
            Wallpaper()
            HStack(spacing: 0) {
                Rail().frame(width: 60)
                sheet.padding(.trailing, 18).padding(.bottom, 18)
            }.padding(.top, 44)
            topBar
            VStack { Spacer(); HStack { Spacer(); VersionLabel() } }.padding(.trailing, 26).padding(.bottom, 2)
            if let t = m.tour { Tour(step: t) }
        }
        .frame(minWidth: 720, minHeight: 460)
        .preferredColorScheme(.dark)
    }

    var title: String {
        switch m.layer {
        case .expiring: return "Expiring"
        case .permanent: return "Permanent"
        case .cloud: return Cloud.current(store.settings)?.name ?? "Cloud"
        case .settings: return "Settings"
        }
    }

    var topBar: some View {
        HStack(spacing: 10) {
            if (m.layer == .permanent || m.layer == .cloud), !(m.back[m.layer.rawValue] ?? []).isEmpty || !(m.forward[m.layer.rawValue] ?? []).isEmpty {
                HStack(spacing: 2) {
                    RoundIcon(system: "chevron.left", help: "Back", color: .text3) { m.goBack() }
                    RoundIcon(system: "chevron.right", help: "Forward", color: .text3) { m.goForward() }
                }
            }
            Text(breadcrumb).font(.system(size: 13, weight: .semibold)).foregroundColor(Color.white.opacity(0.6))
            Spacer()
            SearchField(text: $m.search)
        }
        .padding(.leading, 84).padding(.trailing, 16).frame(height: 44)
    }

    var breadcrumb: String {
        guard m.layer == .permanent || m.layer == .cloud, let r = m.root(m.layer.rawValue), let cur = m.currentFolder(m.layer.rawValue), cur != r else { return title }
        return title + "  ›  " + cur.dropFirst(r.count + 1).replacingOccurrences(of: "/", with: "  ›  ")
    }

    /// The current layer is a sheet; the edges of the layers below peek out on top.
    var sheet: some View {
        let below = m.layer.rawValue < 3 ? 2 - m.layer.rawValue : 0
        return ZStack(alignment: .top) {
            if below >= 2 { RoundedRectangle(cornerRadius: 16).fill(Color.white.opacity(0.03)).overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.white.opacity(0.08))).frame(height: 30).padding(.horizontal, 36) }
            if below >= 1 { RoundedRectangle(cornerRadius: 16).fill(Color.white.opacity(0.05)).overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.white.opacity(0.12))).frame(height: 30).padding(.horizontal, 18).padding(.top, 6) }
            ZStack {
                RoundedRectangle(cornerRadius: 18).fill(Color.white.opacity(0.06))
                RoundedRectangle(cornerRadius: 18).stroke(Color.white.opacity(0.09))
                content.padding(14)
            }
            .padding(.top, 12)
            .offset(y: dragY)
            .id(m.layer)
            .transition(.asymmetric(insertion: .offset(y: m.slideFrom).combined(with: .opacity), removal: .opacity))
            // press on empty space and drag up/down = switch layer
            .gesture(DragGesture(minimumDistance: 12).onChanged { v in
                if abs(v.translation.height) > abs(v.translation.width) * 1.3 { dragY = max(-46, min(46, v.translation.height * 0.3)) }
            }.onEnded { v in
                withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { dragY = 0 }
                if abs(v.translation.height) > 90 && abs(v.translation.height) > abs(v.translation.width) * 1.3 { m.step(v.translation.height < 0 ? 1 : -1) }
            })
        }
    }

    @ViewBuilder var content: some View {
        switch m.layer {
        case .expiring: ExpiringView()
        case .permanent: FolderView(layer: 1)
        case .cloud: FolderView(layer: 2)
        case .settings: SettingsView()
        }
    }
}

struct SearchField: View {
    @Binding var text: String
    @State private var open = false
    @FocusState private var focus: Bool
    var body: some View {
        HStack(spacing: 6) {
            Image(systemName: "magnifyingglass").font(.system(size: 13)).foregroundColor(.text3)
            if open || !text.isEmpty {
                TextField("", text: $text).textFieldStyle(.plain).font(.system(size: 13)).foregroundColor(.text1).focused($focus).frame(width: 170)
                    .onSubmit { focus = false }
            }
        }
        .padding(.horizontal, 10).frame(height: 30)
        .background(Capsule().fill(Color.white.opacity(open || !text.isEmpty ? 0.12 : 0)))
        .contentShape(Capsule())
        .onTapGesture { withAnimation(.easeOut(duration: 0.16)) { open = true }; DispatchQueue.main.async { focus = true } }
        .onChange(of: focus) { f in if !f && text.isEmpty { withAnimation(.easeOut(duration: 0.16)) { open = false } } }
        .help("Search")
    }
}

struct VersionLabel: View {
    @ObservedObject var store = Store.shared
    @State private var over = false
    var body: some View {
        HStack(spacing: 8) {
            Text("v" + AppInfo.version).font(.system(size: 10.5)).foregroundColor(Color.white.opacity(0.25))
            if let n = Updates.newer(store.settings) {
                Text("update " + n).font(.system(size: 10.5)).foregroundColor(Color.amber.opacity(over ? 1 : 0.6)).underline(over)
                    .onHover { over = $0 }.onTapGesture { NSWorkspace.shared.open(Updates.downloadURL) }.help("Download the new version")
            }
        }
    }
}

// MARK: - Rail: the layer switcher as a little sheet stack

struct Rail: View {
    @ObservedObject var m = MainModel.shared
    @State private var hover: Int?
    @State private var dropOver: Int?
    var names: [String] { ["Expiring", "Permanent", Cloud.current(Store.shared.settings)?.name ?? "Cloud"] }

    var body: some View {
        VStack(spacing: 0) {
            Spacer()
            VStack(spacing: 5) {
                ForEach(0..<3, id: \.self) { i in
                    let active = (m.layer.rawValue < 3 ? m.layer.rawValue : m.lastLayer.rawValue) == i
                    RoundedRectangle(cornerRadius: 5)
                        .fill(active ? Color(hex: 0xF6EAD4) : Color(hex: 0x1C2028))
                        .overlay(RoundedRectangle(cornerRadius: 5).stroke(active || dropOver == i ? Color.amber : Color.white.opacity(0.24), lineWidth: 1))
                        .shadow(color: active && m.layer.rawValue < 3 ? Color.amber.opacity(0.55) : .clear, radius: 6)
                        .frame(width: 26, height: 18)
                        .opacity(active && m.layer.rawValue >= 3 ? 0.5 : 1)
                        .offset(x: active ? 9 : (hover == i || dropOver == i ? 4 : 0))
                        .animation(.spring(response: 0.32, dampingFraction: 0.62), value: m.layer)
                        .animation(.easeOut(duration: 0.12), value: hover)
                        .frame(width: 44, height: 23, alignment: .leading)
                        .contentShape(Rectangle())
                        .onTapGesture { if let l = Layer(rawValue: i) { m.show(l) } }
                        .onHover { hover = $0 ? i : (hover == i ? nil : hover) }
                        .help(names[i])
                        .onDrop(of: [UTType.fileURL], delegate: LayerDrop(index: i, over: $dropOver))
                }
            }
            .padding(.bottom, 60)
            Spacer()
            VStack(spacing: 6) {
                RoundIcon(system: "trash", help: "Open Trash") { NSWorkspace.shared.open(Paths.home.appendingPathComponent(".Trash")) }
                RoundIcon(system: "slider.horizontal.3", help: "Settings", color: m.layer == .settings ? .text1 : .text2) { m.show(m.layer == .settings ? m.lastLayer : .settings) }
            }.padding(.bottom, 16)
        }
    }
}

/// Dropping files onto a sheet moves them into that layer; holding a moment opens the layer (spring-loaded).
struct LayerDrop: DropDelegate {
    let index: Int
    @Binding var over: Int?

    func dropEntered(info: DropInfo) {
        over = index
        let i = index
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.65) { if over == i, let l = Layer(rawValue: i) { MainModel.shared.show(l) } }
    }
    func dropExited(info: DropInfo) { if over == index { over = nil } }
    func performDrop(info: DropInfo) -> Bool {
        over = nil
        let target: URL?
        switch index {
        case 0:
            let f = Paths.timeFolders.first(where: { $0.1 == Store.shared.settings.standard })?.0 ?? "1 Week"
            target = Paths.root.appendingPathComponent(f)
        case 1: target = Paths.permanent
        default: target = Cloud.current(Store.shared.settings).map { URL(fileURLWithPath: $0.path) }
        }
        guard let t = target else { return false }
        return dropFiles(info, into: t)
    }
}

func dropFiles(_ info: DropInfo, into dir: URL) -> Bool {
    let providers = info.itemProviders(for: [UTType.fileURL])
    for p in providers {
        _ = p.loadObject(ofClass: URL.self) { url, _ in
            guard let u = url else { return }
            DispatchQueue.main.async {
                if u.deletingLastPathComponent().path == dir.path { return }
                if let e = Store.shared.find(u.path) { Engine.shared.move(e, to: dir) }
                else { try? Paths.fm.moveItem(at: u, to: Paths.freeName(in: dir, u.lastPathComponent)) }
                Store.shared.version += 1
            }
        }
    }
    return !providers.isEmpty
}

// MARK: - Icons that drain as time runs out

/// The real icon stays untouched on top, but only below the fill level; a grey copy sits underneath. Works for every file type.
struct FileIcon: View {
    let path: String
    var fraction: Double? = nil
    var soon = false
    var size: CGFloat = 48
    var body: some View {
        let img = NSWorkspace.shared.icon(forFile: path)
        ZStack {
            if let f = fraction {
                Image(nsImage: img).resizable().interpolation(.high).frame(width: size, height: size)
                    .saturation(0).brightness(-0.25).opacity(0.6)
                    .colorMultiply(soon ? Color(hex: 0xF4D3A0) : .white)
                Image(nsImage: img).resizable().interpolation(.high).frame(width: size, height: size)
                    .mask(VStack(spacing: 0) { Spacer(minLength: 0); Rectangle().frame(height: size * CGFloat(max(0.05, min(1, f)))) })
            } else {
                Image(nsImage: img).resizable().interpolation(.high).frame(width: size, height: size)
            }
        }.frame(width: size, height: size)
    }
}

struct Tile: View {
    let path: String
    var fraction: Double? = nil
    var soon = false
    var help: String? = nil
    @ObservedObject var m = MainModel.shared
    @State private var over = false
    var name: String { (path as NSString).lastPathComponent }
    var body: some View {
        let sel = m.selected == path
        VStack(spacing: 6) {
            FileIcon(path: path, fraction: fraction, soon: soon)
            Text(name).font(.system(size: 12)).foregroundColor(.text1).multilineTextAlignment(.center).lineLimit(2).frame(width: 84, height: 32, alignment: .top)
        }
        .padding(.vertical, 8).frame(width: 92)
        .background(RoundedRectangle(cornerRadius: 7).fill(Color.white.opacity(sel ? 0.17 : (over ? 0.08 : 0))))
        .overlay(RoundedRectangle(cornerRadius: 7).stroke(Color.white.opacity(sel ? 0.3 : 0)))
        .onHover { over = $0 }
        .help(help ?? name)
        .onDrag { NSItemProvider(contentsOf: URL(fileURLWithPath: path)) ?? NSItemProvider() }
    }
}

// MARK: - Layer 1: Expiring

struct Gruppe: Identifiable { let id: String; let items: [Entry] }

struct ExpiringView: View {
    @ObservedObject var store = Store.shared
    @ObservedObject var m = MainModel.shared
    @State private var cardFor: String?

    var items: [Entry] {
        store.entries.filter { $0.expires != nil && Paths.fm.fileExists(atPath: $0.path) && (m.search.isEmpty || $0.name.localizedCaseInsensitiveContains(m.search)) }
            .sorted { $0.expires! < $1.expires! }
    }

    var body: some View {
        let now = Date()
        let all = items
        let groups = [("Today", 0), ("This week", 1), ("Later", 2)].map { g in Gruppe(id: g.0, items: all.filter { Dur.column($0.expires!, now: now) == g.1 }) }.filter { !$0.items.isEmpty }
        GeometryReader { geo in
            if groups.isEmpty {
                Image(systemName: m.search.isEmpty ? "checkmark" : "magnifyingglass").font(.system(size: 36, weight: .light)).foregroundColor(Color.white.opacity(0.25))
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                let rows = max(1, Int((geo.size.height - 30) / 104))
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(alignment: .top, spacing: 22) {
                        ForEach(groups) { g in
                            VStack(alignment: .leading, spacing: 6) {
                                Text(g.id).font(.system(size: 11)).foregroundColor(Color.white.opacity(0.6)).padding(.leading, 8)
                                LazyHGrid(rows: Array(repeating: GridItem(.fixed(100), spacing: 2), count: rows), alignment: .top, spacing: 2) {
                                    ForEach(g.items) { e in tile(e, now) }
                                }
                            }
                        }
                    }
                }
            }
        }
        .contentShape(Rectangle())
        .onTapGesture { m.selected = nil }
    }

    func tile(_ e: Entry, _ now: Date) -> some View {
        let total = max(1, e.expires!.timeIntervalSince(e.added))
        let rest = e.expires!.timeIntervalSince(now) / total
        return Tile(path: e.path, fraction: rest, soon: Dur.column(e.expires!, now: now) == 0, help: e.name + "\n" + Dur.left(e.expires!) + " left · " + Dur.when(e.expires!))
            .onTapGesture(count: 2) { NSWorkspace.shared.open(e.url) }
            .simultaneousGesture(TapGesture().onEnded { m.selected = e.path })
            .popover(isPresented: Binding(get: { cardFor == e.path }, set: { if !$0 { cardFor = nil } }), arrowEdge: .trailing) { TimeCard(entry: e) { cardFor = nil } }
            .contextMenu {
                Button("Change time…") { cardFor = e.path }
                Button("Open") { NSWorkspace.shared.open(e.url) }
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([e.url]) }
                Divider()
                Button("Keep permanently") { Engine.shared.move(e, to: Paths.permanent) }
                if let c = Cloud.current(store.settings) { Button("Move to " + c.name) { Engine.shared.move(e, to: URL(fileURLWithPath: c.path)) } }
                Button("Move to Trash") { Engine.shared.trashNow(e) }
            }
    }
}

/// Card with the time slider (right-click › Change time…): one click sets the new duration from now.
struct TimeCard: View {
    let entry: Entry
    var done: () -> Void
    @State private var calendar = false
    @State private var day = Date().addingTimeInterval(86400)
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(entry.name).font(.system(size: 13.5, weight: .semibold)).lineLimit(1)
                Spacer()
                if let x = entry.expires { Text(Dur.left(x) + " left").font(.system(size: 12)).foregroundColor(.text3) }
            }
            if calendar {
                DatePicker("", selection: $day, in: Date()..., displayedComponents: .date).datePickerStyle(.graphical).labelsHidden()
                Button("Keep until " + shortDate(day)) { Engine.shared.setTimer(entry, until: endOfDay(day), chosen: "until " + shortDate(day)); done() }
            } else {
                TimeSlider(current: Dur.stations.contains(entry.chosen) ? entry.chosen : nil, choose: { d in
                    if d == Dur.forever { Engine.shared.move(entry, to: Paths.permanent) } else { Engine.shared.setTimer(entry, until: Dur.expiry(d), chosen: d) }
                    done()
                }, calendar: { calendar = true }, cloud: Cloud.current(Store.shared.settings), toCloud: { c in Engine.shared.move(entry, to: URL(fileURLWithPath: c.path)); done() })
            }
        }
        .padding(16).frame(width: 380).preferredColorScheme(.dark)
    }
}

// MARK: - Layer 2 + 3: Permanent and Cloud as a desktop

struct FolderView: View {
    let layer: Int
    @ObservedObject var store = Store.shared
    @ObservedObject var m = MainModel.shared
    @State private var dropping = false
    @State private var renaming: String?
    @State private var newName = ""

    var items: [String] {
        _ = store.version
        guard let dir = m.currentFolder(layer), let names = try? Paths.fm.contentsOfDirectory(atPath: dir) else { return [] }
        let paths = names.filter { !Paths.isHelperFile($0) && (m.search.isEmpty || $0.localizedCaseInsensitiveContains(m.search)) }.map { (dir as NSString).appendingPathComponent($0) }
        func isDir(_ p: String) -> Bool { var d: ObjCBool = false; Paths.fm.fileExists(atPath: p, isDirectory: &d); return d.boolValue }
        return paths.sorted { a, b in
            let da = isDir(a), db = isDir(b)
            return da != db ? da : a.localizedStandardCompare(b) == .orderedAscending
        }
    }

    var body: some View {
        if layer == 2 && Cloud.current(store.settings) == nil {
            VStack(spacing: 14) {
                Image(systemName: "cloud").font(.system(size: 40, weight: .light)).foregroundColor(Color.white.opacity(0.35))
                Button("Choose folder") { chooseCloud() }
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            GeometryReader { geo in
                let rows = max(1, Int((geo.size.height - 10) / 104))
                Group {
                    if items.isEmpty {
                        Image(systemName: layer == 2 ? "cloud" : "folder").font(.system(size: 36, weight: .light)).foregroundColor(Color.white.opacity(0.25)).frame(maxWidth: .infinity, maxHeight: .infinity)
                    } else {
                        ScrollView(.horizontal, showsIndicators: false) {
                            LazyHGrid(rows: Array(repeating: GridItem(.fixed(100), spacing: 2), count: rows), alignment: .top, spacing: 2) {
                                ForEach(items, id: \.self) { p in tile(p) }
                            }
                        }
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            }
            .contentShape(Rectangle())
            .onTapGesture { m.selected = nil }
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.amber.opacity(dropping ? 0.7 : 0), lineWidth: 1.5))
            .onDrop(of: [UTType.fileURL], delegate: FolderDrop(dir: { m.currentFolder(layer) }))
            .contextMenu {
                Button("New folder") { newFolder() }
                Button("Open in Finder") { if let d = m.currentFolder(layer) { NSWorkspace.shared.open(URL(fileURLWithPath: d)) } }
            }
            .sheet(isPresented: Binding(get: { renaming != nil }, set: { if !$0 { renaming = nil } })) {
                VStack(alignment: .leading, spacing: 12) {
                    TextField("Name", text: $newName).textFieldStyle(.roundedBorder).frame(width: 260).onSubmit { rename() }
                    HStack { Spacer(); Button("Cancel") { renaming = nil }; Button("Rename") { rename() }.keyboardShortcut(.defaultAction) }
                }.padding(18)
            }
        }
    }

    func tile(_ p: String) -> some View {
        var d: ObjCBool = false
        Paths.fm.fileExists(atPath: p, isDirectory: &d)
        let folder = d.boolValue
        return Tile(path: p)
            .onTapGesture(count: 2) { if folder { m.enter(layer, p) } else { NSWorkspace.shared.open(URL(fileURLWithPath: p)) } }
            .simultaneousGesture(TapGesture().onEnded { m.selected = p })
            .onDrop(of: [UTType.fileURL], delegate: FolderDrop(dir: { folder ? p : nil }, springOpen: folder ? { m.enter(layer, p) } : nil))
            .contextMenu {
                Button("Open") { if folder { m.enter(layer, p) } else { NSWorkspace.shared.open(URL(fileURLWithPath: p)) } }
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: p)]) }
                Button("Rename") { newName = (p as NSString).lastPathComponent; renaming = p }
                if layer == 1, let c = Cloud.current(store.settings) { Button("Move to " + c.name) { moveTo(p, URL(fileURLWithPath: c.path)) } }
                if layer == 2 { Button("Move to Permanent") { moveTo(p, Paths.permanent) } }
                Divider()
                Button("Move to Trash") { try? Paths.fm.trashItem(at: URL(fileURLWithPath: p), resultingItemURL: nil); store.version += 1 }
            }
    }

    func moveTo(_ p: String, _ dir: URL) {
        try? Paths.fm.moveItem(at: URL(fileURLWithPath: p), to: Paths.freeName(in: dir, (p as NSString).lastPathComponent))
        store.version += 1
    }

    func newFolder() {
        guard let d = m.currentFolder(layer) else { return }
        let u = Paths.freeName(in: URL(fileURLWithPath: d), "New folder")
        try? Paths.fm.createDirectory(at: u, withIntermediateDirectories: false)
        store.version += 1
        newName = u.lastPathComponent; renaming = u.path
    }

    func rename() {
        guard let p = renaming, !newName.isEmpty, !newName.contains("/") else { renaming = nil; return }
        let to = ((p as NSString).deletingLastPathComponent as NSString).appendingPathComponent(newName)
        try? Paths.fm.moveItem(atPath: p, toPath: to)
        renaming = nil
        store.version += 1
    }
}

struct FolderDrop: DropDelegate {
    let dir: () -> String?
    var springOpen: (() -> Void)? = nil
    func validateDrop(info: DropInfo) -> Bool { dir() != nil }
    func dropEntered(info: DropInfo) {
        guard let open = springOpen else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { if NSEvent.pressedMouseButtons != 0 { open() } }   // hold over a folder = open it
    }
    func performDrop(info: DropInfo) -> Bool {
        guard let d = dir() else { return false }
        return dropFiles(info, into: URL(fileURLWithPath: d))
    }
}

func chooseCloud() {
    let p = NSOpenPanel()
    p.canChooseDirectories = true; p.canChooseFiles = false; p.message = "Choose a folder for layer 3"
    if p.runModal() == .OK, let u = p.url { Store.shared.settings.cloudPath = u.path; Store.shared.save() }
}

// MARK: - Settings

struct SettingsView: View {
    @ObservedObject var store = Store.shared
    var body: some View {
        VStack(spacing: 0) {
            row("Ask after downloads") { Toggle("", isOn: bind(\.ask)).toggleStyle(.switch).labelsHidden() }
            row("Default timer") {
                HStack(spacing: 4) {
                    ForEach([Dur.hour, Dur.day, Dur.week, Dur.month], id: \.self) { d in
                        Button(Dur.short(d)) { store.settings.standard = d; store.save() }
                            .buttonStyle(.plain).font(.system(size: 12)).padding(.horizontal, 10).padding(.vertical, 5)
                            .background(Capsule().fill(store.settings.standard == d ? Color.amber.opacity(0.18) : .clear))
                            .overlay(Capsule().stroke(store.settings.standard == d ? Color.amber.opacity(0.6) : Color.white.opacity(0.15)))
                    }
                }
            }
            row("Cloud") {
                HStack(spacing: 4) {
                    ForEach(Cloud.find(), id: \.path) { c in
                        Button(c.name) { store.settings.cloudPath = c.path; store.save() }.buttonStyle(.plain).font(.system(size: 12))
                            .padding(.horizontal, 10).padding(.vertical, 5)
                            .overlay(Capsule().stroke(Cloud.current(store.settings)?.path == c.path ? Color.amber.opacity(0.6) : Color.white.opacity(0.15)))
                    }
                    Button("…") { chooseCloud() }.buttonStyle(.plain).padding(.horizontal, 8)
                }
            }
            row("Ask for new desktop files") { Toggle("", isOn: bind(\.watchDesktop)).toggleStyle(.switch).labelsHidden() }
            row("Confirm saves to Workbench") { Toggle("", isOn: bind(\.confirmSaves)).toggleStyle(.switch).labelsHidden() }
            row("Start at login") { Toggle("", isOn: bind(\.startAtLogin)).toggleStyle(.switch).labelsHidden() }
            row("Workbench in Finder") { Button("Show") { NSWorkspace.shared.activateFileViewerSelecting([Paths.root]) }.help("Drag the Workbench folder into Finder’s sidebar – then it shows up in every Save dialog") }
            row("Tour") { Button("Show") { MainModel.shared.tour = 0 } }
            HStack {
                Text("v" + AppInfo.version).font(.system(size: 11.5)).foregroundColor(Color.white.opacity(0.35))
                Spacer()
                Button("Quit Workbench") { NSApp.terminate(nil) }.buttonStyle(.plain).foregroundColor(.danger)
            }.frame(height: 44)
        }
        .padding(.horizontal, 20).padding(.vertical, 6)
        .frame(width: 480)
        .background(RoundedRectangle(cornerRadius: 16).fill(Color(hex: 0x1A1D24, a: 0.85)))
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.white.opacity(0.1)))
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    func bind(_ k: WritableKeyPath<Settings, Bool>) -> Binding<Bool> {
        Binding(get: { store.settings[keyPath: k] }, set: { v in
            store.settings[keyPath: k] = v; store.save()
            if k == \Settings.watchDesktop { Engine.shared.restartWatcher() }
            if k == \Settings.startAtLogin { LoginItem.set(v) }
        })
    }

    func row<C: View>(_ t: String, @ViewBuilder _ c: () -> C) -> some View {
        VStack(spacing: 0) {
            HStack { Text(t).font(.system(size: 13.5)).foregroundColor(.text1); Spacer(); c() }.frame(minHeight: 44)
            Rectangle().fill(Color.white.opacity(0.07)).frame(height: 1)
        }
    }
}
