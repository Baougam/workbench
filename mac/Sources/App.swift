import SwiftUI
import AppKit
import ServiceManagement

enum AppInfo {
    static var version: String { (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String) ?? "0.1.0" }
}

/// Quietly checks once a day whether a newer version exists (VERSION file in the GitHub repo). Downloads nothing by itself.
enum Updates {
    static let downloadURL = URL(string: "https://github.com/Baougam/workbench/raw/main/download/Workbench-mac.zip")!
    static let versionURL = URL(string: "https://raw.githubusercontent.com/Baougam/workbench/main/VERSION")!

    static func newer(_ s: Settings) -> String? {
        let v = s.latestVersion.trimmingCharacters(in: .whitespacesAndNewlines)
        return v.compare(AppInfo.version, options: .numeric) == .orderedDescending ? v : nil
    }

    static func check() {
        let s = Store.shared
        guard Date().timeIntervalSince(s.settings.lastCheck) > 20 * 3600 else { return }
        URLSession.shared.dataTask(with: versionURL) { data, _, _ in
            DispatchQueue.main.async {
                s.settings.lastCheck = Date()
                if let d = data, let v = String(data: d, encoding: .utf8) { s.settings.latestVersion = v.trimmingCharacters(in: .whitespacesAndNewlines) }
                s.save()
            }
        }.resume()
    }
}

enum LoginItem {
    static func set(_ on: Bool) {
        do {
            if on { if SMAppService.mainApp.status != .enabled { try SMAppService.mainApp.register() } }
            else { try SMAppService.mainApp.unregister() }
        } catch { Log.info("login item: \(error)") }
    }
}

// MARK: - First-run tour

struct Tour: View {
    let step: Int
    @ObservedObject var m = MainModel.shared
    @ObservedObject var store = Store.shared

    struct S { let title: String; let text: String }
    static let steps: [S] = [
        S(title: "Welcome to Workbench", text: "Downloads that clean up after themselves, and a desktop that stays tidy."),
        S(title: "Sheets", text: "Each sheet is one place. Swipe up or down to switch. Drag a sheet on the left to reorder it."),
        S(title: "After every download", text: "A small card like this one asks how long you need the file. Pick a time with one click — or ignore it, and Workbench picks a sensible default."),
        S(title: "Expiring", text: "New downloads show up here. Each icon drains as time runs out. Then the file moves to the Trash."),
        S(title: "Your desktop", text: "Permanent is a desktop that stays tidy. Drag files in, double-click a folder to open it."),
        S(title: "In every Save dialog", text: "Drag the Workbench folder into Finder’s sidebar. Saving into “1 Week” from any app then sets the timer for you."),
        S(title: "Works across your Mac", text: "Right-click any file › Quick Actions or Services › Workbench to set a timer. The menu bar icon opens Workbench any time."),
    ]

    var body: some View {
        let s = Tour.steps[min(step, Tour.steps.count - 1)]
        ZStack {
            Color.black.opacity(0.55).ignoresSafeArea().onTapGesture {}
            VStack(alignment: .leading, spacing: 10) {
                HStack(alignment: .top) {
                    Text(s.title).font(.system(size: 16, weight: .semibold)).foregroundColor(.text1)
                    Spacer()
                    Button(action: { m.tour = nil }) { Image(systemName: "xmark").font(.system(size: 10, weight: .bold)).foregroundColor(.text3) }.buttonStyle(.plain).help("Later")
                }
                Text(s.text).font(.system(size: 13)).foregroundColor(.text2).fixedSize(horizontal: false, vertical: true)
                if step == 0 {
                    HStack { Text("Start at login").foregroundColor(.text1); Spacer()
                        Toggle("", isOn: Binding(get: { store.settings.startAtLogin }, set: { store.settings.startAtLogin = $0; store.save(); LoginItem.set($0) })).toggleStyle(.switch).labelsHidden()
                    }.font(.system(size: 13)).padding(.top, 4)
                }
                if step == 5 {
                    Button("Show Workbench in Finder") { NSWorkspace.shared.activateFileViewerSelecting([Paths.root]) }.padding(.top, 2)
                }
                HStack {
                    Button("Don’t show again") { store.settings.tourDone = true; store.save(); m.tour = nil }.buttonStyle(.plain).font(.system(size: 12)).foregroundColor(.text3)
                    Spacer()
                    HStack(spacing: 4) {
                        ForEach(0..<Tour.steps.count, id: \.self) { i in Capsule().fill(i == step ? Color.amber : Color.white.opacity(0.25)).frame(width: i == step ? 14 : 5, height: 5) }
                    }
                    Button(action: next) {
                        Text(step == Tour.steps.count - 1 ? "Get started" : "Next").font(.system(size: 13, weight: .semibold)).foregroundColor(Color(hex: 0x1C1405))
                            .padding(.horizontal, 16).padding(.vertical, 7).background(Capsule().fill(Color.amber))
                    }.buttonStyle(.plain)
                }.padding(.top, 8)
            }
            .padding(EdgeInsets(top: 18, leading: 18, bottom: 14, trailing: 16))
            .frame(width: 340)
            .background(RoundedRectangle(cornerRadius: 16).fill(Color.card))
            .overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.white.opacity(0.14)))
            .shadow(color: .black.opacity(0.5), radius: 20, y: 8)
        }
        .onAppear { prepare(step) }
        .onChange(of: step) { prepare($0) }
    }

    func next() {
        if step >= Tour.steps.count - 1 { store.settings.tourDone = true; store.save(); m.tour = nil; return }
        m.tour = step + 1
    }

    func prepare(_ s: Int) {
        if s == 1 || s == 3 { m.show(Sheets.shared.tempIndex) }
        if s == 2 { AppDelegate.shared?.exampleCard() }
    }
}

// MARK: - App

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    static weak var shared: AppDelegate?
    var window: NSWindow?
    var status: NSStatusItem?
    var prompt: PromptPanel?
    var monitor: Any?
    let engine = Engine.shared
    let services = ServiceProvider()

    func applicationDidFinishLaunching(_ n: Notification) {
        AppDelegate.shared = self
        offerMoveToApplications()
        engine.onDownloads = { [weak self] l in
            guard Store.shared.settings.ask else { return }
            if let p = self?.prompt, p.isOpen, !p.isInfo { p.add(l); return }
            self?.showCard(l, info: false)
        }
        engine.onSaved = { [weak self] e in
            guard Store.shared.settings.confirmSaves else { return }
            if let p = self?.prompt, p.isOpen { return }
            self?.showCard([e], info: true)
        }
        engine.start()
        Sheets.shared.load()
        MainModel.shared.sheet = Sheets.shared.startIndex
        Browsers.listen()
        if !UserDefaults.standard.bool(forKey: "loginItemSet") {
            LoginItem.set(Store.shared.settings.startAtLogin)
            UserDefaults.standard.set(true, forKey: "loginItemSet")
        }
        NSApp.servicesProvider = services
        NSUpdateDynamicServices()
        setupStatusItem()
        Updates.check()
        Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { _ in Updates.check() }
        // started at login: stay in the background. Otherwise show the window (and the tour the first time).
        if ProcessInfo.processInfo.systemUptime > 180 || !Store.shared.settings.tourDone { showWindow() }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows: Bool) -> Bool { showWindow(); return true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }

    func showCard(_ l: [Entry], info: Bool) {
        prompt?.fadeOut()
        let p = PromptPanel(files: l, info: info)
        p.onClose = { [weak self, weak p] in if self?.prompt === p { self?.prompt = nil } }
        prompt = p
        p.show()
    }

    /// Tour: show the real download card once – with an example file Workbench doesn't know, so clicking changes nothing real.
    func exampleCard() {
        let dir = Paths.fm.temporaryDirectory.appendingPathComponent("Workbench tour")
        try? Paths.fm.createDirectory(at: dir, withIntermediateDirectories: true)
        let f = dir.appendingPathComponent("Example.pdf")
        if !Paths.fm.fileExists(atPath: f.path) { Paths.fm.createFile(atPath: f.path, contents: Data()) }
        showCard([Entry(path: f.path, added: Date(), expires: nil, origin: "download", chosen: Store.shared.settings.standard)], info: false)
    }

    func setupStatusItem() {
        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let b = status?.button {
            b.image = NSImage(systemSymbolName: "square.stack", accessibilityDescription: "Workbench")
            b.image?.isTemplate = true
        }
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "Open Workbench", action: #selector(openFromMenu), keyEquivalent: ""))
        menu.addItem(NSMenuItem(title: "Settings…", action: #selector(openSettings), keyEquivalent: ","))
        menu.addItem(.separator())
        menu.addItem(NSMenuItem(title: "Quit Workbench", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"))
        for i in menu.items { i.target = i.action == #selector(NSApplication.terminate(_:)) ? NSApp : self }
        status?.menu = menu
    }

    @objc func openFromMenu() { showWindow() }
    @objc func openSettings() { showWindow(); MainModel.shared.showSettings(true) }

    func showWindow() {
        if window == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1060, height: 680),
                             styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
            w.titlebarAppearsTransparent = true
            w.titleVisibility = .hidden
            w.title = "Workbench"
            w.isMovableByWindowBackground = false
            w.appearance = NSAppearance(named: .darkAqua)
            w.backgroundColor = NSColor(red: 0.055, green: 0.063, blue: 0.082, alpha: 1)
            w.isReleasedWhenClosed = false
            w.contentView = NSHostingView(rootView: MainView())
            w.center()
            w.delegate = self
            window = w
            installGestures()
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
        if !Store.shared.settings.tourDone && MainModel.shared.tour == nil { DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { MainModel.shared.tour = 0 } }
    }

    // MARK: gestures
    // Two fingers up/down (mouse wheel) = switch layer. Two fingers left/right = back/forward in folders.
    // Each swipe counts once. Arrow keys do the same.
    private var accX: CGFloat = 0, accY: CGFloat = 0, fired = false, lastWheel = Date.distantPast

    func installGestures() {
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.scrollWheel, .keyDown]) { [weak self] e in
            guard let self = self, let w = self.window, e.window === w else { return e }
            if e.type == .keyDown { return self.key(e) ? nil : e }
            return self.scroll(e) ? nil : e
        }
    }

    private func key(_ e: NSEvent) -> Bool {
        if window?.firstResponder is NSTextView { return false }
        let m = MainModel.shared
        if m.tour != nil { return false }
        let cmd = e.modifierFlags.contains(.command)
        if cmd && e.charactersIgnoringModifiers == "a" && !m.settings { m.selectAll(); return true }
        if (e.keyCode == 117 || (e.keyCode == 51 && cmd)) && !m.selection.isEmpty && !m.settings { if m.current?.kind == "board" { m.removeFromBoard(Array(m.selection)) } else { m.trash(Array(m.selection)) }; return true }   // ⌦ or ⌘⌫
        if e.keyCode == 53 && !m.selection.isEmpty { m.selection = []; return true }
        switch e.keyCode {
        case 125: m.step(1); return true            // ↓
        case 126: m.step(-1); return true           // ↑
        case 123: m.goBack(); return true           // ←
        case 124: m.goForward(); return true        // →
        default: return false
        }
    }

    private func scroll(_ e: NSEvent) -> Bool {
        let m = MainModel.shared
        if m.tour != nil || m.settings { return false }
        if !e.hasPreciseScrollingDeltas {
            // classic mouse wheel: one notch = one layer, with a short pause
            guard Date().timeIntervalSince(lastWheel) > 0.45, abs(e.scrollingDeltaY) > 0.5 else { return true }
            lastWheel = Date()
            m.step(e.scrollingDeltaY < 0 ? 1 : -1)
            return true
        }
        if !e.momentumPhase.isEmpty { return true }          // ignore the coasting after the fingers lift
        if e.phase.contains(.began) { accX = 0; accY = 0; fired = false }
        accX += e.scrollingDeltaX; accY += e.scrollingDeltaY
        if !fired {
            if abs(accY) > 40 && abs(accY) > abs(accX) * 1.2 { fired = true; m.step(accY < 0 ? 1 : -1) }
            else if abs(accX) > 50 && abs(accX) > abs(accY) * 1.2 { fired = true; if accX > 0 { m.goBack() } else { m.goForward() } }
        }
        if e.phase.contains(.ended) || e.phase.contains(.cancelled) { accX = 0; accY = 0; fired = false }
        return true
    }

    // MARK: first launch from Downloads: offer to move into Applications

    func offerMoveToApplications() {
        let path = Bundle.main.bundlePath
        guard !path.hasPrefix("/Applications"), !Store.shared.settings.askedToMove, path.hasSuffix(".app") else { return }
        Store.shared.load()
        Store.shared.settings.askedToMove = true
        Store.shared.save()
        let a = NSAlert()
        a.messageText = "Move Workbench to Applications?"
        a.informativeText = "So it starts at login and stays out of your Downloads folder."
        a.addButton(withTitle: "Move to Applications")
        a.addButton(withTitle: "Not now")
        NSApp.activate(ignoringOtherApps: true)
        guard a.runModal() == .alertFirstButtonReturn else { return }
        let dest = URL(fileURLWithPath: "/Applications/Workbench.app")
        do {
            if Paths.fm.fileExists(atPath: dest.path) { try Paths.fm.trashItem(at: dest, resultingItemURL: nil) }
            try Paths.fm.copyItem(at: URL(fileURLWithPath: path), to: dest)
            let cfg = NSWorkspace.OpenConfiguration()
            NSWorkspace.shared.openApplication(at: dest, configuration: cfg) { _, _ in
                DispatchQueue.main.async {
                    try? Paths.fm.trashItem(at: URL(fileURLWithPath: path), resultingItemURL: nil)
                    NSApp.terminate(nil)
                }
            }
        } catch {
            let f = NSAlert(); f.messageText = "Couldn’t move Workbench"; f.informativeText = "Drag it into Applications yourself – it works from anywhere, too."; f.runModal()
        }
    }

    static func main() {
        let app = NSApplication.shared
        let d = AppDelegate()
        app.delegate = d
        app.setActivationPolicy(.accessory)
        app.run()
    }
}

/// Right-click › Quick Actions / Services › Workbench: Keep for an hour/day/week/month – on any file in Finder.
final class ServiceProvider: NSObject {
    @objc func keepHour(_ pb: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString>) { keep(pb, Dur.hour) }
    @objc func keepDay(_ pb: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString>) { keep(pb, Dur.day) }
    @objc func keepWeek(_ pb: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString>) { keep(pb, Dur.week) }
    @objc func keepMonth(_ pb: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString>) { keep(pb, Dur.month) }

    // Safari & any app: on a link
    @objc func getDay(_ pb: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString>) { get(pb, Dur.day) }
    @objc func getWeek(_ pb: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString>) { get(pb, Dur.week) }
    @objc func getForever(_ pb: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString>) { get(pb, Dur.forever) }

    private func get(_ pb: NSPasteboard, _ d: String) {
        if let u = (pb.readObjects(forClasses: [NSURL.self], options: nil) as? [URL])?.first { Browsers.download(u, d); return }
        if let s = pb.string(forType: .string)?.trimmingCharacters(in: .whitespacesAndNewlines), let u = URL(string: s) { Browsers.download(u, d) }
    }

    private func keep(_ pb: NSPasteboard, _ d: String) {
        let urls = (pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
        var done: [Entry] = []
        for u in urls { if let e = Engine.shared.setByHand(u.path, d) { done.append(e) } }
        if !done.isEmpty { AppDelegate.shared?.showCard(done, info: true) }
    }
}

@main
enum Main {
    static func main() {
        if Browsers.isHost { Browsers.runHost(); return }
        AppDelegate.main()
    }
}
