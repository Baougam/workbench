import SwiftUI
import AppKit

/// Browser extension "Download with Workbench" (Chrome, Brave, Edge, Vivaldi). Same extension as on Windows, bundled in Resources/browser.
/// The browser starts this binary with "chrome-extension://…" (native messaging); it hands the message to the running app.
enum Browsers {
    static let host = "com.workbench.host"
    static let extensionID = "mffnoocgaogljpgadglahpfhiaodlgab"
    static let note = Notification.Name("app.workbench.mac.browser")

    struct Target: Identifiable { let name: String, bundle: String, support: String, page: String; var id: String { name } }
    static let all = [
        Target(name: "Chrome", bundle: "com.google.Chrome", support: "Google/Chrome", page: "chrome://extensions"),
        Target(name: "Brave", bundle: "com.brave.Browser", support: "BraveSoftware/Brave-Browser", page: "brave://extensions"),
        Target(name: "Edge", bundle: "com.microsoft.edgemac", support: "Microsoft Edge", page: "edge://extensions"),
        Target(name: "Vivaldi", bundle: "com.vivaldi.Vivaldi", support: "Vivaldi", page: "vivaldi://extensions"),
    ]
    static var installed: [Target] { all.filter { NSWorkspace.shared.urlForApplication(withBundleIdentifier: $0.bundle) != nil } }
    static var folder: URL { Paths.data.appendingPathComponent("Browser", isDirectory: true) }
    private static var queue: URL { Paths.data.appendingPathComponent("BrowserQueue", isDirectory: true) }

    // MARK: setup

    /// Unpack the extension and register the native messaging host for every installed browser.
    static func register() {
        guard let src = Bundle.main.resourceURL?.appendingPathComponent("browser") else { return }
        do {
            try Paths.fm.createDirectory(at: folder, withIntermediateDirectories: true)
            for n in try Paths.fm.contentsOfDirectory(atPath: src.path) {
                var d = try Data(contentsOf: src.appendingPathComponent(n))
                if n == "manifest.json", let s = String(data: d, encoding: .utf8) { d = Data(s.replacingOccurrences(of: "__VERSION__", with: AppInfo.version).utf8) }
                let dest = folder.appendingPathComponent(n)
                if (try? Data(contentsOf: dest)) != d { try d.write(to: dest, options: .atomic) }
            }
            let json: [String: Any] = ["name": host, "description": "Workbench", "path": Bundle.main.executablePath ?? "", "type": "stdio",
                                       "allowed_origins": ["chrome-extension://\(extensionID)/"]]
            let data = try JSONSerialization.data(withJSONObject: json, options: [.prettyPrinted])
            let base = Paths.home.appendingPathComponent("Library/Application Support")
            for b in installed {
                let dir = base.appendingPathComponent(b.support).appendingPathComponent("NativeMessagingHosts")
                try Paths.fm.createDirectory(at: dir, withIntermediateDirectories: true)
                try data.write(to: dir.appendingPathComponent(host + ".json"), options: .atomic)
            }
        } catch { Log.info("browser register: \(error)") }
    }

    private static var panel: NSPanel?

    static func setUp(_ t: Target) {
        Store.shared.settings.browser = true
        Store.shared.save()
        register()
        copy(t.page)
        if let app = NSWorkspace.shared.urlForApplication(withBundleIdentifier: t.bundle) {
            NSWorkspace.shared.openApplication(at: app, configuration: NSWorkspace.OpenConfiguration()) { _, _ in }
        }
        panel?.close()
        let p = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 320, height: 210), styleMask: [.titled, .closable, .nonactivatingPanel, .utilityWindow], backing: .buffered, defer: false)
        p.title = "Add to \(t.name)"
        p.level = .floating
        p.hidesOnDeactivate = false
        p.isReleasedWhenClosed = false
        p.appearance = NSAppearance(named: .darkAqua)
        p.contentView = NSHostingView(rootView: BrowserSteps(target: t))
        if let s = NSScreen.main?.visibleFrame { p.setFrameOrigin(NSPoint(x: s.maxX - 340, y: s.minY + 20)) }
        p.orderFrontRegardless()
        panel = p
    }

    static func copy(_ s: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(s, forType: .string)
    }

    // MARK: native messaging host (short-lived process started by the browser)

    static var isHost: Bool { CommandLine.arguments.contains { $0.hasPrefix("chrome-extension://") } }

    static func runHost() {
        let input = FileHandle.standardInput
        func read(_ n: Int) -> Data? {
            var d = Data()
            while d.count < n { let part = input.readData(ofLength: n - d.count); if part.isEmpty { return nil }; d.append(part) }
            return d
        }
        guard let l = read(4) else { return }
        let n = l.enumerated().reduce(0) { $0 + (Int($1.element) << (8 * $1.offset)) }
        guard n > 0, n < 1 << 20, let body = read(n) else { return }
        if (try? JSONSerialization.jsonObject(with: body)) is [String: Any] {
            try? Paths.fm.createDirectory(at: queue, withIntermediateDirectories: true)
            let name = String(format: "%.6f-%@.json", Date().timeIntervalSince1970, UUID().uuidString)
            try? body.write(to: queue.appendingPathComponent(name), options: .atomic)
            DistributedNotificationCenter.default().postNotificationName(note, object: nil, userInfo: nil, deliverImmediately: true)
        }
        let reply = Data("{\"ok\":true}".utf8)
        var out = Data([UInt8(reply.count), 0, 0, 0])
        out.append(reply)
        FileHandle.standardOutput.write(out)
    }

    // MARK: in the running app

    static func listen() {
        DistributedNotificationCenter.default().addObserver(forName: note, object: nil, queue: .main) { _ in drain() }
        if Store.shared.settings.browser { register() }
        drain()
    }

    private static func drain() {
        let names = ((try? Paths.fm.contentsOfDirectory(atPath: queue.path)) ?? []).filter { $0.hasSuffix(".json") }.sorted()
        for n in names {
            let f = queue.appendingPathComponent(n)
            let data = try? Data(contentsOf: f)
            try? Paths.fm.removeItem(at: f)
            guard let d = data, let m = (try? JSONSerialization.jsonObject(with: d)) as? [String: Any], let op = m["op"] as? String else { continue }
            if op == "hello" {
                let s = Store.shared
                if let b = m["browser"] as? String, all.contains(where: { $0.name == b }), !s.settings.browsers.contains(b) { s.settings.browsers.append(b); s.save() }
                panel?.close(); panel = nil
            } else if op == "plan" || op == "done", let t = m["time"] as? String, Dur.stations.contains(t), let p = m["path"] as? String, p.hasPrefix("/") {
                Engine.shared.planned(p, t, done: op == "done")
            }
        }
    }

    // MARK: Safari (Services menu) – Safari only loads signed extensions, so Workbench downloads the link itself

    static func download(_ url: URL, _ time: String) {
        guard url.scheme == "https" || url.scheme == "http" else { return }
        URLSession.shared.downloadTask(with: url) { tmp, resp, err in
            guard let tmp = tmp, let resp = resp, (resp as? HTTPURLResponse).map({ (200..<300).contains($0.statusCode) }) ?? true else {
                Log.info("download failed: \(String(describing: err))")
                DispatchQueue.main.async { NSSound.beep() }
                return
            }
            let raw = resp.suggestedFilename ?? url.lastPathComponent
            let name = (raw as NSString).lastPathComponent
            // the temporary file disappears when this block returns: move it aside first
            let hold = Paths.fm.temporaryDirectory.appendingPathComponent(UUID().uuidString)
            do { try Paths.fm.moveItem(at: tmp, to: hold) } catch { Log.info("download: \(error)"); return }
            DispatchQueue.main.async {
                let dest = Paths.freeName(in: Paths.downloads, name.isEmpty ? "Download" : name)
                Engine.shared.planned(dest.path, time, done: false)
                do { try Paths.fm.moveItem(at: hold, to: dest) } catch { Log.info("download: \(error)") }
            }
        }.resume()
    }
}

struct BrowserSteps: View {
    let target: Browsers.Target
    var body: some View {
        VStack(alignment: .leading, spacing: 11) {
            step(1, "Paste into the address bar", copy: target.page)
            step(2, "Turn on Developer mode", copy: nil)
            step(3, "Click Load unpacked", copy: nil)
            step(4, "Press ⇧⌘G, paste the folder", copy: Browsers.folder.path)
        }
        .padding(18)
        .frame(width: 320, alignment: .leading)
    }

    func step(_ n: Int, _ t: String, copy: String?) -> some View {
        HStack(spacing: 10) {
            Text("\(n)").font(.system(size: 11, weight: .semibold)).foregroundColor(.amber).frame(width: 20, height: 20).overlay(Circle().stroke(Color.amber, lineWidth: 1.2))
            Text(t).font(.system(size: 13)).foregroundColor(.text1)
            Spacer()
            if let c = copy { Button("Copy") { Browsers.copy(c) }.controlSize(.small) }
        }
    }
}
