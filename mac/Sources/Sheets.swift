import SwiftUI
import AppKit
import UniformTypeIdentifiers

// Sheets instead of fixed layers – same idea as on Windows. Expiring always exists; everything else is picked with +:
// desktop, any folder, a board (references, never moves files), screenshots, cloud. Bars (Recent, Shelf) dock to an edge.

struct Sheet: Codable, Identifiable, Equatable {
    var id = String(UUID().uuidString.prefix(8))
    var kind = "empty"          // temp | folder | board | cloud | empty
    var path = ""
    var name = ""
    var symbol = ""             // SF Symbol for folder sheets (desktop, screenshots)
    var bars: [String] = []     // "recent@top", "shelf@bottom" …
    var items: [String] = []    // board only

    init(kind: String, path: String = "", name: String = "", symbol: String = "") { self.kind = kind; self.path = path; self.name = name; self.symbol = symbol }
    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        id = (try? c.decode(String.self, forKey: .id)) ?? String(UUID().uuidString.prefix(8))
        kind = (try? c.decode(String.self, forKey: .kind)) ?? "empty"
        path = (try? c.decode(String.self, forKey: .path)) ?? ""
        name = (try? c.decode(String.self, forKey: .name)) ?? ""
        symbol = (try? c.decode(String.self, forKey: .symbol)) ?? ""
        bars = (try? c.decode([String].self, forKey: .bars)) ?? []
        items = (try? c.decode([String].self, forKey: .items)) ?? []
    }

    var isFolder: Bool { kind == "folder" || kind == "cloud" }
    var icon: String {
        switch kind {
        case "temp": return "hourglass"
        case "cloud": return "cloud"
        case "board": return "square.grid.2x2"
        case "empty": return "plus"
        default: return symbol.isEmpty ? "folder" : symbol
        }
    }
    var root: String? {
        if kind == "folder" { return path.isEmpty ? nil : path }
        if kind == "cloud" { return path.isEmpty ? Cloud.current(Store.shared.settings)?.path : path }
        return nil
    }
    var title: String {
        switch kind {
        case "temp": return "Expiring"
        case "empty": return "New sheet"
        case "board": return name.isEmpty ? "Board" : name
        case "cloud" where path.isEmpty: return Cloud.current(Store.shared.settings)?.name ?? "Cloud"
        default: return name.isEmpty ? (path as NSString).lastPathComponent : name
        }
    }
    func bar(at edge: String) -> String? { bars.first { $0.hasSuffix("@" + edge) }?.components(separatedBy: "@").first }
}

final class Sheets: ObservableObject {
    static let shared = Sheets()
    static let max = 6
    static let edges = ["top", "bottom", "right"]
    @Published var list: [Sheet] = []
    @Published var shelf: [String] = []
    var fresh = false

    private struct Saved: Codable { var list: [Sheet]; var shelf: [String] }
    private var url: URL { Paths.data.appendingPathComponent("sheets.json") }

    func load() {
        if let d = try? Data(contentsOf: url), let s = try? JSONDecoder().decode(Saved.self, from: d), !s.list.isEmpty {
            list = s.list; shelf = s.shelf
            if !list.contains(where: { $0.kind == "temp" }) { list.insert(Sheet(kind: "temp"), at: 0) }
            return
        }
        // first time: people who used the old fixed layers keep them; new people get Expiring and an empty sheet
        let had = ((try? Paths.fm.contentsOfDirectory(atPath: Paths.permanent.path)) ?? []).contains { !Paths.isHelperFile($0) }
        list = [Sheet(kind: "temp")]
        if had { list.append(Sheet(kind: "folder", path: Paths.permanent.path, name: "Permanent")); list.append(Sheet(kind: "cloud")) }
        else { list.append(Sheet(kind: "empty")); fresh = true }
        save()
    }

    func save() {
        try? Paths.fm.createDirectory(at: Paths.data, withIntermediateDirectories: true)
        try? JSONEncoder().encode(Saved(list: list, shelf: shelf)).write(to: url, options: .atomic)
    }

    var tempIndex: Int { list.firstIndex { $0.kind == "temp" } ?? 0 }
    var middle: Int { list.count / 2 - (list.count % 2 == 0 ? 1 : 0) }
    var startIndex: Int { list[middle].kind == "empty" && !fresh ? tempIndex : middle }

    func index(for folder: String) -> Int? {
        var best: Int?, len = -1
        for (i, s) in list.enumerated() { if let r = s.root, (folder + "/").hasPrefix(r + "/") || folder == r, r.count > len { best = i; len = r.count } }
        return best
    }

    func setBar(_ i: Int, _ kind: String, _ edge: String?) {
        list[i].bars.removeAll { $0.hasPrefix(kind + "@") || (edge != nil && $0.hasSuffix("@" + edge!)) }
        if let e = edge { list[i].bars.append(kind + "@" + e) }
        save()
    }

    func park(_ paths: [String]) {
        for p in paths { shelf.removeAll { $0 == p }; shelf.insert(p, at: 0) }
        if shelf.count > 40 { shelf.removeLast(shelf.count - 40) }
        save()
    }

    static var screenshots: String {
        // macOS keeps screenshots where the user set it (default: Desktop)
        let d = UserDefaults(suiteName: "com.apple.screencapture")?.string(forKey: "location")
        return (d.map { ($0 as NSString).expandingTildeInPath }) ?? Paths.desktop.path
    }
}

/// Recently used files from Spotlight (last 14 days) plus Workbench's own downloads. Read once in a while, never in a loop.
final class RecentFiles: ObservableObject {
    static let shared = RecentFiles()
    @Published var paths: [String] = []
    private var query: NSMetadataQuery?
    private var last = Date.distantPast

    func refresh() {
        guard query == nil, Date().timeIntervalSince(last) > 30 else { return }
        last = Date()
        let q = NSMetadataQuery()
        q.predicate = NSPredicate(format: "kMDItemLastUsedDate >= %@ AND kMDItemContentTypeTree != 'public.folder'", Date().addingTimeInterval(-14 * 86400) as NSDate)
        q.searchScopes = [NSMetadataQueryUserHomeScope]
        q.sortDescriptors = [NSSortDescriptor(key: "kMDItemLastUsedDate", ascending: false)]
        NotificationCenter.default.addObserver(forName: .NSMetadataQueryDidFinishGathering, object: q, queue: .main) { [weak self] _ in
            guard let self = self else { return }
            q.stop()
            var found: [String] = []
            for i in 0..<min(q.resultCount, 60) {
                if let item = q.result(at: i) as? NSMetadataItem, let p = item.value(forAttribute: NSMetadataItemPathKey) as? String,
                   !p.contains("/Library/"), !Paths.isHelperFile(p) { found.append(p) }
            }
            let own = Store.shared.entries.sorted { $0.added > $1.added }.prefix(15).map { $0.path }
            var seen = Set<String>()
            self.paths = (Array(own) + found).filter { seen.insert($0).inserted && Paths.fm.fileExists(atPath: $0) }.prefix(30).map { $0 }
            self.query = nil
        }
        query = q
        q.start()
    }
}

// MARK: - Rail tile: a small landscape tile with a quiet icon; the active one is tinted amber

struct RailTile: View {
    let icon: String
    var active = false
    var target = false
    @State private var over = false
    var body: some View {
        Image(systemName: icon).font(.system(size: 13, weight: .regular))
            .foregroundColor(active ? .amber : Color.white.opacity(over || target ? 0.7 : 0.45))
            .frame(width: 40, height: 28)
            .background(RoundedRectangle(cornerRadius: 8).fill(active ? Color.amber.opacity(0.18) : Color.white.opacity(over ? 0.08 : 0)))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(active ? Color.amber.opacity(0.4) : (target ? Color.amber.opacity(0.6) : .clear), lineWidth: 1))
            .onHover { over = $0 }
            .animation(.easeOut(duration: 0.14), value: active)
    }
}

/// Dragging a tile up or down in the rail reorders the sheets.
struct SheetReorder: DropDelegate {
    let index: Int
    func validateDrop(info: DropInfo) -> Bool { info.hasItemsConforming(to: [UTType.plainText]) }
    func dropEntered(info: DropInfo) {
        guard let from = MainModel.shared.draggingSheet, from != index else { return }
        let s = Sheets.shared
        let cur = MainModel.shared.current?.id
        withAnimation(.spring(response: 0.3, dampingFraction: 0.8)) { s.list.move(fromOffsets: IndexSet(integer: from), toOffset: index > from ? index + 1 : index) }
        MainModel.shared.draggingSheet = index
        if let c = cur, let i = s.list.firstIndex(where: { $0.id == c }) { MainModel.shared.sheet = i }
    }
    func dropUpdated(info: DropInfo) -> DropProposal? { DropProposal(operation: .move) }
    func performDrop(info: DropInfo) -> Bool { MainModel.shared.draggingSheet = nil; Sheets.shared.save(); return true }
}

// MARK: - New sheet: a big quiet + that fans out into a few choices

struct NewSheetView: View {
    let index: Int
    @State private var open = false
    var choices: [(String, String, () -> Void)] {
        [
            ("desktopcomputer", "Desktop", { pick(Sheet(kind: "folder", path: Paths.desktop.path, name: "Desktop", symbol: "desktopcomputer")) }),
            ("folder", "Folder", { chooseFolder("folder") }),
            ("square.grid.2x2", "Board", { pick(Sheet(kind: "board", name: "Board")) }),
            ("viewfinder", "Screenshots", { pick(Sheet(kind: "folder", path: Sheets.screenshots, name: "Screenshots", symbol: "viewfinder")) }),
            ("cloud", Cloud.current(Store.shared.settings)?.name ?? "Cloud", { Cloud.current(Store.shared.settings) != nil ? pick(Sheet(kind: "cloud")) : chooseFolder("cloud") }),
        ]
    }

    var body: some View {
        ZStack {
            Image(systemName: "plus").font(.system(size: 44, weight: .ultraLight)).foregroundColor(Color.white.opacity(open ? 0.1 : 0.25))
            ForEach(Array(choices.enumerated()), id: \.offset) { i, c in
                let a = -Double.pi / 2 + Double(i) * 2 * Double.pi / Double(choices.count)
                Button(action: c.2) {
                    Image(systemName: c.0).font(.system(size: 16, weight: .light)).foregroundColor(Color.white.opacity(0.75))
                        .frame(width: 46, height: 46)
                        .background(Circle().fill(Color.white.opacity(0.06)))
                        .overlay(Circle().stroke(Color.white.opacity(0.1)))
                }
                .buttonStyle(.plain).help(c.1)
                .scaleEffect(open ? 1 : 0.6)
                .opacity(open ? 1 : 0)
                .offset(x: open ? cos(a) * 92 : 0, y: open ? sin(a) * 92 : 0)
                .animation(.spring(response: 0.34, dampingFraction: 0.72).delay(open ? Double(i) * 0.02 : 0), value: open)
                .allowsHitTesting(open)
            }
        }
        .frame(width: 300, height: 300)
        .contentShape(Rectangle())
        .onHover { open = $0 }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }

    func pick(_ s: Sheet) {
        var n = s
        n.id = Sheets.shared.list[index].id
        Sheets.shared.list[index] = n
        Sheets.shared.save()
        MainModel.shared.objectWillChange.send()
    }

    func chooseFolder(_ kind: String) {
        let p = NSOpenPanel()
        p.canChooseDirectories = true; p.canChooseFiles = false
        guard p.runModal() == .OK, let u = p.url else { return }
        pick(Sheet(kind: kind, path: u.path, name: u.lastPathComponent, symbol: u.path == Paths.desktop.path ? "desktopcomputer" : ""))
    }
}

// MARK: - Board: references from anywhere

struct BoardView: View {
    let index: Int
    @ObservedObject var sheets = Sheets.shared
    @ObservedObject var m = MainModel.shared
    @State private var over = false

    var items: [String] {
        guard index < sheets.list.count else { return [] }
        return sheets.list[index].items.filter { Paths.fm.fileExists(atPath: $0) && (m.search.isEmpty || ($0 as NSString).lastPathComponent.localizedCaseInsensitiveContains(m.search)) }
    }

    var body: some View {
        GeometryReader { geo in
            let rows = max(1, Int((geo.size.height - 10) / 104))
            Group {
                if items.isEmpty {
                    Image(systemName: "square.grid.2x2").font(.system(size: 34, weight: .ultraLight)).foregroundColor(Color.white.opacity(0.25)).frame(maxWidth: .infinity, maxHeight: .infinity)
                } else {
                    ScrollView(.horizontal, showsIndicators: false) {
                        LazyHGrid(rows: Array(repeating: GridItem(.fixed(100), spacing: 2), count: rows), alignment: .top, spacing: 2) {
                            ForEach(items, id: \.self) { p in
                                Tile(path: p, help: p)
                                    .onTapGesture(count: 2) { NSWorkspace.shared.open(URL(fileURLWithPath: p)) }
                                    .simultaneousGesture(TapGesture().onEnded { m.click(p) })
                                    .contextMenu {
                                        Button("Open") { NSWorkspace.shared.open(URL(fileURLWithPath: p)) }
                                        Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: p)]) }
                                        Divider()
                                        Button("Remove from board") { m.removeFromBoard(m.with(p)) }
                                    }
                            }
                        }
                    }
                }
            }.frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
        .contentShape(Rectangle())
        .onTapGesture { m.selected = nil }
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(over ? 0.3 : 0), lineWidth: 1.5))
        .onDrop(of: [UTType.fileURL], isTargeted: $over) { providers in addToBoard(index, providers); return true }
    }
}

func addToBoard(_ index: Int, _ providers: [NSItemProvider]) {
    let group = EdgeHold.shared.dragged
    EdgeHold.shared.dragged = []
    for p in providers {
        _ = p.loadObject(ofClass: URL.self) { url, _ in
            guard let u = url else { return }
            DispatchQueue.main.async {
                let s = Sheets.shared
                guard index < s.list.count else { return }
                for path in group.contains(u.path) ? group : [u.path] where !s.list[index].items.contains(path) { s.list[index].items.append(path) }
                s.save()
            }
        }
    }
}

// MARK: - Bars: Recent and Shelf. No text – icons only, fading out towards the end until you hover.

struct Bar: View {
    let index: Int
    let kind: String
    let edge: String
    var moveTo: (String?) -> Void            // nil = remove
    @ObservedObject var recent = RecentFiles.shared
    @ObservedObject var sheets = Sheets.shared
    @State private var over = false
    @State private var dropOver = false
    @State private var drag: CGSize = .zero

    var across: Bool { edge != "right" }
    var paths: [String] { kind == "recent" ? recent.paths : sheets.shelf.filter { Paths.fm.fileExists(atPath: $0) } }

    var body: some View {
        let layout = across ? AnyLayout(HStackLayout(spacing: 4)) : AnyLayout(VStackLayout(spacing: 4))
        layout {
            Image(systemName: kind == "recent" ? "clock" : "tray")
                .font(.system(size: 11)).foregroundColor(Color.white.opacity(0.3))
                .frame(width: across ? 28 : 46, height: across ? 46 : 28)
                .contentShape(Rectangle())
                .help(kind == "recent" ? "Recent" : "Shelf")
                .gesture(DragGesture(coordinateSpace: .named("sheet")).onChanged { drag = $0.translation }.onEnded { v in
                    drag = .zero
                    moveTo(edgeFor(v.location))
                })
            ScrollView(across ? .horizontal : .vertical, showsIndicators: false) {
                let inner = across ? AnyLayout(HStackLayout(spacing: 2)) : AnyLayout(VStackLayout(spacing: 2))
                inner {
                    ForEach(paths, id: \.self) { p in BarItem(path: p, shelf: kind == "shelf") }
                    if kind == "shelf" && paths.isEmpty {
                        RoundedRectangle(cornerRadius: 8).strokeBorder(Color.white.opacity(0.15), style: StrokeStyle(lineWidth: 1, dash: [3, 3]))
                            .frame(width: across ? 70 : 36, height: across ? 36 : 70).help("Drop files here to park them")
                    }
                }.padding(across ? .trailing : .bottom, 30)
            }
            .mask(LinearGradient(stops: [.init(color: .black, location: 0), .init(color: .black, location: over ? 0.95 : 0.5), .init(color: .clear, location: 1)],
                                 startPoint: across ? .leading : .top, endPoint: across ? .trailing : .bottom))
        }
        .padding(across ? .horizontal : .vertical, 4)
        .frame(height: across ? 50 : nil).frame(width: across ? nil : 50)
        .background(RoundedRectangle(cornerRadius: 12).fill(Color.white.opacity(dropOver ? 0.08 : 0.03)))
        .fixedSize(horizontal: across, vertical: !across)
        .opacity(drag == .zero ? 1 : 0.6)
        .offset(drag)
        .onHover { h in withAnimation(.easeOut(duration: 0.25)) { over = h } }
        .onAppear { if kind == "recent" { recent.refresh() } }
        .onDrop(of: [UTType.fileURL], isTargeted: $dropOver) { providers in
            guard kind == "shelf" else { return false }
            for p in providers { _ = p.loadObject(ofClass: URL.self) { u, _ in if let u = u { DispatchQueue.main.async { Sheets.shared.park([u.path]) } } } }
            return true
        }
    }

    /// Where the handle was let go: near an edge = dock there, in the middle = remove the bar.
    func edgeFor(_ p: CGPoint) -> String? {
        let size = MainModel.shared.sheetSize
        let d = [("top", p.y), ("bottom", size.height - p.y), ("right", size.width - p.x)].min { $0.1 < $1.1 }!
        return d.1 > min(size.width, size.height) * 0.22 ? nil : d.0
    }
}

struct BarItem: View {
    let path: String
    var shelf = false
    @State private var over = false
    var body: some View {
        Image(nsImage: NSWorkspace.shared.icon(forFile: path)).resizable().interpolation(.high).frame(width: 30, height: 30)
            .frame(width: 40, height: 40)
            .background(RoundedRectangle(cornerRadius: 9).fill(Color.white.opacity(over ? 0.1 : 0)))
            .onHover { over = $0 }
            .help((path as NSString).lastPathComponent)
            .onTapGesture { NSWorkspace.shared.open(URL(fileURLWithPath: path)) }
            .onDrag { NSItemProvider(contentsOf: URL(fileURLWithPath: path)) ?? NSItemProvider() }
            .contextMenu {
                Button("Open") { NSWorkspace.shared.open(URL(fileURLWithPath: path)) }
                Button("Show in Finder") { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)]) }
                if shelf { Divider(); Button("Remove") { Sheets.shared.shelf.removeAll { $0 == path }; Sheets.shared.save() } }
            }
    }
}

/// Wraps a sheet's content with its bars; free edges show a small + when the pointer comes close.
struct WithBars<Content: View>: View {
    let index: Int
    @ViewBuilder var content: Content
    @ObservedObject var sheets = Sheets.shared
    @State private var near: String?
    @State private var size: CGSize = .zero

    var sheet: Sheet { sheets.list[min(index, sheets.list.count - 1)] }
    var free: [String] { ["recent", "shelf"].filter { k in !sheet.bars.contains { $0.hasPrefix(k + "@") } } }

    var body: some View {
        VStack(spacing: 8) {
            if let b = sheet.bar(at: "top") { HStack { bar(b, "top"); Spacer(minLength: 0) } }
            HStack(spacing: 8) {
                content
                if let b = sheet.bar(at: "right") { VStack { bar(b, "right"); Spacer(minLength: 0) } }
            }
            if let b = sheet.bar(at: "bottom") { HStack { bar(b, "bottom"); Spacer(minLength: 0) } }
        }
        .background(GeometryReader { g in Color.clear.onAppear { size = g.size }.onChange(of: g.size) { size = $0 } })
        .onContinuousHover { phase in
            switch phase {
            case .active(let p):
                let w = size.width, h = size.height
                var n: String? = nil
                if p.y < 30 && abs(p.x - w / 2) < 220 { n = "top" }
                else if h - p.y < 30 && abs(p.x - w / 2) < 220 { n = "bottom" }
                else if w - p.x < 30 && abs(p.y - h / 2) < 160 { n = "right" }
                if n != near { near = n }
            case .ended: near = nil
            }
        }
        .overlay(ZStack {
            ForEach(Sheets.edges.filter { sheet.bar(at: $0) == nil && !free.isEmpty }, id: \.self) { e in
                Menu {
                    ForEach(free, id: \.self) { k in Button(k == "recent" ? "Recent" : "Shelf") { sheets.setBar(index, k, e) } }
                } label: { Image(systemName: "plus").font(.system(size: 10, weight: .semibold)).foregroundColor(Color.white.opacity(0.6)) }
                .menuStyle(.borderlessButton).menuIndicator(.hidden).frame(width: 24, height: 24)
                .background(Circle().fill(Color.white.opacity(0.08)))
                .opacity(near == e ? 1 : 0)
                .allowsHitTesting(near == e)
                .position(e == "top" ? CGPoint(x: size.width / 2, y: 2) : e == "bottom" ? CGPoint(x: size.width / 2, y: size.height - 2) : CGPoint(x: size.width - 2, y: size.height / 2))
                .animation(.easeOut(duration: 0.15), value: near)
            }
        })
    }

    func bar(_ kind: String, _ edge: String) -> some View {
        Bar(index: index, kind: kind, edge: edge) { target in
            guard let t = target else { sheets.setBar(index, kind, nil); return }
            if t == edge { return }
            let other = sheet.bar(at: t)
            sheets.setBar(index, kind, t)
            if let o = other { sheets.setBar(index, o, edge) }     // swap
        }
    }
}
