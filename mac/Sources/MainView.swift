import SwiftUI
import AppKit
import UniformTypeIdentifiers

final class MainModel: ObservableObject {
    static let shared = MainModel()
    @Published var sheet = 0                          // index into Sheets.shared.list
    @Published var settings = false
    @Published var search = ""
    @Published var selection: Set<String> = []
    var selected: String? {
        get { selection.first }
        set { selection = newValue.map { [$0] } ?? [] }
    }
    @Published var marquee: CGRect?
    var frames: [String: CGRect] = [:]                  // visible tiles in sheet coordinates
    var sheetSize: CGSize = .zero
    @Published var folder: [String: String] = [:]       // sheet id → current subfolder
    @Published var tour: Int? = nil
    @Published var slideFrom: CGFloat = 0
    @Published var draggingSheet: Int?
    var back: [String: [String]] = [:]
    var forward: [String: [String]] = [:]

    var sheets: [Sheet] { Sheets.shared.list }
    var current: Sheet? { settings || sheet >= sheets.count ? nil : sheets[sheet] }
    var key: String { settings ? "settings" : (current?.id ?? "") }

    func show(_ i: Int) {
        guard i >= 0, i < sheets.count, i != sheet || settings else { return }
        slideFrom = settings ? 0 : (i > sheet ? 40 : -40)
        withAnimation(.spring(response: 0.36, dampingFraction: 0.82)) {
            settings = false
            sheet = i
            selected = nil
            search = ""
        }
    }

    func showSettings(_ on: Bool) {
        slideFrom = 0
        withAnimation(.spring(response: 0.36, dampingFraction: 0.82)) { settings = on; selected = nil }
    }

    func step(_ d: Int) {
        let to = max(0, min(sheets.count - 1, sheet + d))
        if to != sheet || settings { show(to); if tour == 1 { tour = 2 } }     // trying it yourself counts in the tour
    }

    func addSheet() {
        let s = Sheets.shared
        if let i = s.list.firstIndex(where: { $0.kind == "empty" }) { show(i); return }
        guard s.list.count < Sheets.max else { return }
        s.list.append(Sheet(kind: "empty"))
        s.save()
        show(s.list.count - 1)
    }

    func removeSheet(_ i: Int) {
        let s = Sheets.shared
        guard i < s.list.count, s.list[i].kind != "temp" else { return }
        let cur = current?.id
        s.list.remove(at: i)
        s.save()
        sheet = s.list.firstIndex { $0.id == cur } ?? max(0, min(i, s.list.count - 1))
        objectWillChange.send()
    }

    func currentFolder(_ s: Sheet) -> String? {
        guard let r = s.root else { return nil }
        if let f = folder[s.id], f.hasPrefix(r), Paths.fm.fileExists(atPath: f) { return f }
        return r
    }

    func enter(_ s: Sheet, _ path: String) {
        if let cur = currentFolder(s), cur != path { back[s.id, default: []].append(cur); forward[s.id] = [] }
        withAnimation(.easeOut(duration: 0.2)) { folder[s.id] = path; selected = nil }
    }

    func goBack() {
        guard let s = current, s.isFolder, let p = back[s.id]?.popLast(), let cur = currentFolder(s) else { return }
        forward[s.id, default: []].append(cur)
        withAnimation(.easeOut(duration: 0.2)) { folder[s.id] = p; selection = [] }
    }

    func goForward() {
        guard let s = current, s.isFolder, let p = forward[s.id]?.popLast(), let cur = currentFolder(s) else { return }
        back[s.id, default: []].append(cur)
        withAnimation(.easeOut(duration: 0.2)) { folder[s.id] = p; selection = [] }
    }

    func removeFromBoard(_ paths: [String]) {
        let s = Sheets.shared
        guard sheet < s.list.count, s.list[sheet].kind == "board" else { return }
        s.list[sheet].items.removeAll { paths.contains($0) }
        s.save()
        selection = []
    }

    /// Other folder sheets to move things to (at most three).
    func otherFolders(_ s: Sheet?) -> [Sheet] { Array(sheets.filter { $0.id != s?.id && $0.isFolder && $0.root != nil }.prefix(3)) }
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
    @State private var press: Press?

    struct Press { var since = Date(); var mode = 0; var keep: Set<String> = [] }      // mode 0 = undecided, 1 = frame, 2 = ignore (began on a tile)

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

    @ObservedObject var sheets = Sheets.shared

    var title: String { m.settings ? "Settings" : (m.current?.kind == "empty" ? "" : (m.current?.title ?? "")) }

    var topBar: some View {
        HStack(spacing: 10) {
            if let s = m.current, s.isFolder, !(m.back[s.id] ?? []).isEmpty || !(m.forward[s.id] ?? []).isEmpty {
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
        guard let s = m.current, s.isFolder, let r = s.root, let cur = m.currentFolder(s), cur != r else { return title }
        return title + "  ›  " + cur.dropFirst(r.count + 1).replacingOccurrences(of: "/", with: "  ›  ")
    }

    /// The current layer is a sheet; the edges of the layers below peek out on top.
    var sheet: some View {
        let below = m.settings ? 0 : min(2, m.sheets.count - 1 - m.sheet)
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
            .id(m.key)
            .transition(.asymmetric(insertion: .offset(y: m.slideFrom).combined(with: .opacity), removal: .opacity))
            .gesture(pressDrag)
            if let r = m.marquee {
                ZStack(alignment: .topLeading) {
                    Color.clear
                    RoundedRectangle(cornerRadius: 6).fill(Color.amber.opacity(0.12)).overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.amber.opacity(0.5)))
                        .frame(width: r.width, height: r.height).offset(x: r.minX, y: r.minY)
                }.clipped().allowsHitTesting(false)
            }
            EdgeGlow().padding(.top, 12)
            VStack { Spacer(); SelectionBar() }.padding(.bottom, 14)
        }
        .coordinateSpace(name: "sheet")
        .background(GeometryReader { g in Color.clear.onAppear { MainModel.shared.sheetSize = g.size }.onChange(of: g.size) { MainModel.shared.sheetSize = $0 } })
        .onPreferenceChange(TileFrames.self) { f in MainModel.shared.frames = f }
    }

    /// On empty space: a quick stroke up/down switches layers, any other drag pulls up a selection frame.
    var pressDrag: some Gesture {
        DragGesture(minimumDistance: 6, coordinateSpace: .named("sheet"))
            .onChanged { v in
                if press == nil {
                    var p = Press()
                    if m.settings || m.current?.kind == "empty" || m.frames.values.contains(where: { $0.contains(v.startLocation) }) { p.mode = 2 }
                    if NSEvent.modifierFlags.contains(.command) { p.keep = m.selection }
                    press = p
                }
                guard var p = press, p.mode != 2 else { return }
                let t = v.translation
                if p.mode == 0 {
                    let stroke = abs(t.height) > abs(t.width) * 1.6 && Date().timeIntervalSince(p.since) < 0.28
                    if stroke {
                        dragY = max(-46, min(46, t.height * 0.3))
                        if abs(t.height) > 90 {
                            p.mode = 2; press = p
                            withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { dragY = 0 }
                            m.step(t.height < 0 ? 1 : -1)
                        }
                        return
                    }
                    p.mode = 1; press = p
                    if dragY != 0 { withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { dragY = 0 } }
                }
                m.frame(v.startLocation, v.location, keep: p.keep)
            }
            .onEnded { _ in
                press = nil
                m.marquee = nil
                if dragY != 0 { withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { dragY = 0 } }
            }
    }

    @ViewBuilder var content: some View {
        if m.settings { SettingsView() }
        else if let s = m.current {
            switch s.kind {
            case "temp": WithBars(index: m.sheet) { ExpiringView() }
            case "board": WithBars(index: m.sheet) { BoardView(index: m.sheet) }
            case "empty": NewSheetView(index: m.sheet)
            default: WithBars(index: m.sheet) { FolderView(sheet: s) }
            }
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

// MARK: - Rail: one landscape tile per sheet, + appears when you point at them

struct Rail: View {
    @ObservedObject var m = MainModel.shared
    @ObservedObject var sheets = Sheets.shared
    @State private var dropOver: Int?
    @State private var hover = false

    var body: some View {
        VStack(spacing: 0) {
            Spacer()
            VStack(spacing: 6) {
                ForEach(Array(sheets.list.enumerated()), id: \.element.id) { i, s in
                    RailTile(icon: s.icon, active: !m.settings && m.sheet == i, target: dropOver == i)
                        .opacity(m.settings && m.sheet == i ? 0.5 : 1)
                        .contentShape(Rectangle())
                        .onTapGesture { m.show(i) }
                        .help(s.title)
                        .onDrag { m.draggingSheet = i; return NSItemProvider(object: ("sheet:" + s.id) as NSString) }
                        .onDrop(of: [UTType.plainText], delegate: SheetReorder(index: i))
                        .onDrop(of: [UTType.fileURL], delegate: LayerDrop(index: i, over: $dropOver))
                        .contextMenu {
                            if s.kind == "board" || s.isFolder { Button("Show in Finder") { if let r = s.root { NSWorkspace.shared.open(URL(fileURLWithPath: r)) } }.disabled(s.root == nil) }
                            if s.kind != "temp" { Button("Remove sheet") { m.removeSheet(i) } }
                        }
                }
                if sheets.list.count < Sheets.max && !sheets.list.contains(where: { $0.kind == "empty" }) {
                    RailTile(icon: "plus").opacity(hover || sheets.list.count <= 1 ? 0.7 : 0)
                        .contentShape(Rectangle())
                        .onTapGesture { m.addSheet() }
                        .help("New sheet")
                        .animation(.easeOut(duration: 0.15), value: hover)
                }
            }
            .onHover { hover = $0 }
            .padding(.bottom, 60)
            Spacer()
            VStack(spacing: 6) {
                RoundIcon(system: "trash", help: "Open Trash") { NSWorkspace.shared.open(Paths.home.appendingPathComponent(".Trash")) }
                RoundIcon(system: "slider.horizontal.3", help: "Settings", color: m.settings ? .text1 : .text2) { m.showSettings(!m.settings) }
            }.padding(.bottom, 16)
        }
    }
}

/// Dropping files onto a tile moves them into that sheet (board: adds references); holding a moment opens the sheet.
struct LayerDrop: DropDelegate {
    let index: Int
    @Binding var over: Int?

    func dropEntered(info: DropInfo) {
        EdgeHold.shared.begin()
        over = index
        let i = index
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.65) { if over == i { MainModel.shared.show(i) } }
    }
    func dropExited(info: DropInfo) { if over == index { over = nil } }
    func performDrop(info: DropInfo) -> Bool {
        over = nil
        guard index < Sheets.shared.list.count else { return false }
        let s = Sheets.shared.list[index]
        switch s.kind {
        case "temp": return dropFiles(info, into: MainModel.standardFolder, skipTimed: true)
        case "board": addToBoard(index, info.itemProviders(for: [UTType.fileURL])); return true
        default:
            guard let r = s.root else { return false }
            return dropFiles(info, into: URL(fileURLWithPath: r))
        }
    }
}

func dropFiles(_ info: DropInfo, into dir: URL, skipTimed: Bool = false) -> Bool {
    let providers = info.itemProviders(for: [UTType.fileURL])
    let group = EdgeHold.shared.dragged      // several files dragged together inside Workbench
    EdgeHold.shared.dragged = []
    for p in providers {
        _ = p.loadObject(ofClass: URL.self) { url, _ in
            guard let u = url else { return }
            DispatchQueue.main.async {
                var paths = group.contains(u.path) ? group : [u.path]
                if skipTimed { paths = paths.filter { Store.shared.find($0) == nil } }
                MainModel.shared.move(paths, to: dir)
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
        let sel = m.selection.contains(path)
        VStack(spacing: 6) {
            FileIcon(path: path, fraction: fraction, soon: soon)
            Text(name).font(.system(size: 12)).foregroundColor(.text1).multilineTextAlignment(.center).lineLimit(2).frame(width: 84, height: 32, alignment: .top)
        }
        .padding(.vertical, 8).frame(width: 92)
        .background(RoundedRectangle(cornerRadius: 7).fill(Color.white.opacity(sel ? 0.17 : (over ? 0.08 : 0))))
        .overlay(RoundedRectangle(cornerRadius: 7).stroke(Color.white.opacity(sel ? 0.3 : 0)))
        .onHover { over = $0 }
        .help(help ?? name)
        .onDrag {
            EdgeHold.shared.dragged = m.with(path)
            EdgeHold.shared.begin()
            return NSItemProvider(contentsOf: URL(fileURLWithPath: path)) ?? NSItemProvider()
        }
        .reportsFrame(path)
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
        .onDrop(of: [UTType.fileURL], delegate: ExpiringDrop())
    }

    func tile(_ e: Entry, _ now: Date) -> some View {
        let total = max(1, e.expires!.timeIntervalSince(e.added))
        let rest = e.expires!.timeIntervalSince(now) / total
        return Tile(path: e.path, fraction: rest, soon: Dur.column(e.expires!, now: now) == 0, help: e.name + "\n" + Dur.left(e.expires!) + " left · " + Dur.when(e.expires!))
            .onTapGesture(count: 2) { NSWorkspace.shared.open(e.url) }
            .simultaneousGesture(TapGesture().onEnded { m.click(e.path) })
            .popover(isPresented: Binding(get: { cardFor == e.path }, set: { if !$0 { cardFor = nil } }), arrowEdge: .trailing) { TimeCard(entry: e, more: m.with(e.path).compactMap { store.find($0) }) { cardFor = nil } }
            .contextMenu {
                Button("Change time…") { cardFor = e.path }
                Button("Open") { NSWorkspace.shared.open(e.url) }
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([e.url]) }
                Divider()
                Button("Keep permanently") { m.move(m.with(e.path), to: Paths.permanent) }
                if let c = Cloud.current(store.settings) { Button("Move to " + c.name) { m.move(m.with(e.path), to: URL(fileURLWithPath: c.path)) } }
                Button("Move to Trash") { m.trash(m.with(e.path)) }
            }
    }
}

/// Card with the time slider (right-click › Change time…): one click sets the new duration from now.
struct TimeCard: View {
    let entry: Entry
    var more: [Entry] = []
    var done: () -> Void
    @State private var calendar = false
    @State private var day = Date().addingTimeInterval(86400)
    var all: [Entry] { more.count > 1 ? more : [entry] }
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text(all.count > 1 ? "\(all.count) files" : entry.name).font(.system(size: 13.5, weight: .semibold)).lineLimit(1)
                Spacer()
                if all.count == 1, let x = entry.expires { Text(Dur.left(x) + " left").font(.system(size: 12)).foregroundColor(.text3) }
            }
            if calendar {
                DatePicker("", selection: $day, in: Date()..., displayedComponents: .date).datePickerStyle(.graphical).labelsHidden()
                Button("Keep until " + shortDate(day)) { for e in all { Engine.shared.setTimer(e, until: endOfDay(day), chosen: "until " + shortDate(day)) }; done() }
            } else {
                TimeSlider(current: Dur.stations.contains(entry.chosen) ? entry.chosen : nil, choose: { d in
                    for e in all { if d == Dur.forever { Engine.shared.move(e, to: Paths.permanent) } else { Engine.shared.setTimer(e, until: Dur.expiry(d), chosen: d) } }
                    done()
                }, calendar: { calendar = true }, cloud: Cloud.current(Store.shared.settings), toCloud: { c in for e in all { Engine.shared.move(e, to: URL(fileURLWithPath: c.path)) }; done() })
            }
        }
        .padding(16).frame(width: 380).preferredColorScheme(.dark)
    }
}

// MARK: - Layer 2 + 3: Permanent and Cloud as a desktop

struct FolderView: View {
    let sheet: Sheet
    @ObservedObject var store = Store.shared
    @ObservedObject var m = MainModel.shared
    @State private var dropping = false
    @State private var renaming: String?
    @State private var newName = ""

    var items: [String] {
        _ = store.version
        guard let dir = m.currentFolder(sheet), let names = try? Paths.fm.contentsOfDirectory(atPath: dir) else { return [] }
        let paths = names.filter { !Paths.isHelperFile($0) && (m.search.isEmpty || $0.localizedCaseInsensitiveContains(m.search)) }.map { (dir as NSString).appendingPathComponent($0) }
        func isDir(_ p: String) -> Bool { var d: ObjCBool = false; Paths.fm.fileExists(atPath: p, isDirectory: &d); return d.boolValue }
        return paths.sorted { a, b in
            let da = isDir(a), db = isDir(b)
            return da != db ? da : a.localizedStandardCompare(b) == .orderedAscending
        }
    }

    var body: some View {
        if sheet.root == nil || !Paths.fm.fileExists(atPath: sheet.root!) {
            VStack(spacing: 14) {
                Image(systemName: sheet.icon).font(.system(size: 40, weight: .light)).foregroundColor(Color.white.opacity(0.35))
                Button("Choose folder") { chooseSheetFolder(sheet) }
            }.frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            GeometryReader { geo in
                let rows = max(1, Int((geo.size.height - 10) / 104))
                Group {
                    if items.isEmpty {
                        Image(systemName: sheet.icon).font(.system(size: 34, weight: .ultraLight)).foregroundColor(Color.white.opacity(0.25)).frame(maxWidth: .infinity, maxHeight: .infinity)
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
            .onDrop(of: [UTType.fileURL], delegate: FolderDrop(dir: { m.currentFolder(sheet) }))
            .contextMenu {
                Button("New folder") { newFolder() }
                Button("Open in Finder") { if let d = m.currentFolder(sheet) { NSWorkspace.shared.open(URL(fileURLWithPath: d)) } }
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
            .onTapGesture(count: 2) { if folder { m.enter(sheet, p) } else { NSWorkspace.shared.open(URL(fileURLWithPath: p)) } }
            .simultaneousGesture(TapGesture().onEnded { m.click(p) })
            .onDrop(of: [UTType.fileURL], delegate: FolderDrop(dir: { folder ? p : nil }, springOpen: folder ? { m.enter(sheet, p) } : nil))
            .contextMenu {
                Button("Open") { if folder { m.enter(sheet, p) } else { NSWorkspace.shared.open(URL(fileURLWithPath: p)) } }
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: p)]) }
                Button("Rename") { newName = (p as NSString).lastPathComponent; renaming = p }
                ForEach(m.otherFolders(sheet)) { o in Button("Move to " + o.title) { m.move(m.with(p), to: URL(fileURLWithPath: o.root!)) } }
                Divider()
                Button("Move to Trash") { m.trash(m.with(p)) }
            }
    }

    func newFolder() {
        guard let d = m.currentFolder(sheet) else { return }
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
        EdgeHold.shared.begin()
        guard let open = springOpen else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { if NSEvent.pressedMouseButtons != 0 { open() } }   // hold over a folder = open it
    }
    func performDrop(info: DropInfo) -> Bool {
        guard let d = dir() else { return false }
        return dropFiles(info, into: URL(fileURLWithPath: d))
    }
}

/// The folder behind a sheet is missing (cloud not found, folder deleted): pick another.
func chooseSheetFolder(_ s: Sheet) {
    let p = NSOpenPanel()
    p.canChooseDirectories = true; p.canChooseFiles = false
    guard p.runModal() == .OK, let u = p.url, let i = Sheets.shared.list.firstIndex(where: { $0.id == s.id }) else { return }
    Sheets.shared.list[i].path = u.path
    if Sheets.shared.list[i].kind == "folder" { Sheets.shared.list[i].name = u.lastPathComponent }
    Sheets.shared.save()
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
            if !Browsers.installed.isEmpty {
                row("Browser") {
                    HStack(spacing: 6) {
                        ForEach(Browsers.installed) { b in
                            Button((store.settings.browsers.contains(b.name) ? "✓ " : "") + b.name) { Browsers.setUp(b) }.help("Right-click a link › Download with Workbench")
                        }
                    }
                }
            }
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
