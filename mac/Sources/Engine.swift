import Foundation
import AppKit
import CoreServices

/// Watches folders with FSEvents (macOS reports changes itself — no polling).
final class Watcher {
    private var stream: FSEventStreamRef?
    private let paths: [String]
    private let handler: ([String]) -> Void

    init(paths: [String], handler: @escaping ([String]) -> Void) {
        self.paths = paths
        self.handler = handler
    }

    func start() {
        stop()
        var ctx = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(), retain: nil, release: nil, copyDescription: nil)
        let cb: FSEventStreamCallback = { _, info, _, eventPaths, _, _ in
            guard let info = info else { return }
            let me = Unmanaged<Watcher>.fromOpaque(info).takeUnretainedValue()
            let arr = unsafeBitCast(eventPaths, to: NSArray.self) as? [String] ?? []
            me.handler(arr)
        }
        let flags = FSEventStreamCreateFlags(kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagNoDefer)
        stream = FSEventStreamCreate(nil, cb, &ctx, paths as CFArray, FSEventStreamEventId(kFSEventStreamEventIdSinceNow), 0.25, flags)
        if let s = stream {
            FSEventStreamSetDispatchQueue(s, DispatchQueue.main)
            FSEventStreamStart(s)
        }
    }

    func stop() {
        if let s = stream { FSEventStreamStop(s); FSEventStreamInvalidate(s); FSEventStreamRelease(s); stream = nil }
    }
}

/// The background heart: notices finished downloads and saves into Workbench folders, and moves expired files to the Trash.
/// The expiry timer only wakes up at the next deadline.
final class Engine {
    static let shared = Engine()
    let store = Store.shared
    var onDownloads: (([Entry]) -> Void)?
    var onSaved: ((Entry) -> Void)?

    private var watcher: Watcher?
    private var pending: [String: (size: Int64, stable: Int, since: Date, origin: String)] = [:]
    private var gone: [String: Date] = [:]
    private var pendingTimer: Timer?
    private var expiryTimer: Timer?

    func start() {
        Paths.create()
        store.load()
        reconcile()
        restartWatcher()
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            self?.sweep(); self?.plan()
        }
        sweep()
        plan()
    }

    func restartWatcher() {
        var p = [Paths.downloads.path, Paths.root.path]
        if store.settings.watchDesktop { p.append(Paths.desktop.path) }
        watcher = Watcher(paths: p) { [weak self] changed in self?.changed(changed) }
        watcher?.start()
    }

    // MARK: events

    private func originFor(_ path: String) -> String? {
        let parent = (path as NSString).deletingLastPathComponent
        if parent == Paths.downloads.path { return "download" }
        if store.settings.watchDesktop && parent == Paths.desktop.path { return "desktop" }
        if let d = Paths.durationFor(path), d != "Permanent" { return "folder" }
        return nil
    }

    private func changed(_ paths: [String]) {
        var refresh = false
        for p in Set(paths) {
            let exists = Paths.fm.fileExists(atPath: p)
            if !exists {
                if store.find(p) != nil { gone[p] = Date() }
                pending.removeValue(forKey: p)
                refresh = true
                continue
            }
            if gone[p] != nil { gone.removeValue(forKey: p); continue }      // saved again (Word & co.): timer stays
            guard let origin = originFor(p), !Paths.isHelperFile(p), store.find(p) == nil, pending[p] == nil else { refresh = true; continue }
            var isDir: ObjCBool = false
            Paths.fm.fileExists(atPath: p, isDirectory: &isDir)
            let ext = (p as NSString).pathExtension.lowercased()
            if (origin == "download" || origin == "desktop") && isDir.boolValue && ext != "app" { continue }  // unzipped/new folders: don't ask
            if origin == "desktop" && ["webloc", "alias", "inetloc", "fileloc"].contains(ext) { continue }
            pending[p] = (-1, 0, Date(), origin)
        }
        if !pending.isEmpty || !gone.isEmpty { startPendingTimer() }
        if refresh { store.version += 1 }
    }

    private func startPendingTimer() {
        if pendingTimer != nil { return }
        pendingTimer = Timer.scheduledTimer(withTimeInterval: 0.7, repeats: true) { [weak self] _ in self?.tick() }
    }

    private func stillRunning(_ p: String) -> Bool {
        for e in [".crdownload", ".part", ".download", ".partial"] where Paths.fm.fileExists(atPath: p + e) { return true }
        return false
    }

    private func tick() {
        let now = Date()
        var downloads: [Entry] = []
        for (p, w) in pending {
            guard Paths.fm.fileExists(atPath: p) else { pending.removeValue(forKey: p); continue }
            if now.timeIntervalSince(w.since) > 6 * 3600 { pending.removeValue(forKey: p); continue }
            if stillRunning(p) { pending[p] = (w.size, 0, w.since, w.origin); continue }
            let size = (try? Paths.fm.attributesOfItem(atPath: p)[.size] as? Int64) ?? 0
            var stable = w.stable
            if size == w.size { stable += 1 } else { stable = 0 }
            pending[p] = (size, stable, w.since, w.origin)
            guard stable >= 2, size > 0 || now.timeIntervalSince(w.since) > 6 else { continue }
            pending.removeValue(forKey: p)
            if store.find(p) != nil { continue }
            if w.origin == "folder" {
                guard let d = Paths.durationFor(p), d != "Permanent" else { continue }
                let e = Entry(path: p, added: now, expires: Dur.expiry(d, from: now), origin: "folder", chosen: d)
                store.update(e); store.save(); plan()
                onSaved?(e)
            } else {
                let s = store.suggestion(for: p)
                let e = Entry(path: p, added: now, expires: Dur.expiry(s, from: now), origin: w.origin, chosen: s)
                store.update(e)
                downloads.append(e)
            }
        }
        for (p, t) in gone where now.timeIntervalSince(t) >= 4 {
            gone.removeValue(forKey: p)
            if !Paths.fm.fileExists(atPath: p) { store.remove(p); store.save() }
        }
        if !downloads.isEmpty { store.save(); plan(); onDownloads?(downloads) }
        if pending.isEmpty && gone.isEmpty { pendingTimer?.invalidate(); pendingTimer = nil }
    }

    // MARK: timers

    /// Set a new timer. nil = forever: the file moves to Permanent (desktop/hand-picked files just stay where they are).
    @discardableResult
    func setTimer(_ e: Entry, until: Date?, chosen: String) -> String {
        var e = e
        if until == nil {
            if e.origin == "desktop" || e.origin == "hand" { store.remove(e.path); store.save(); plan(); return e.path }
            return move(e, to: Paths.permanent)
        }
        e.expires = until
        e.chosen = chosen
        store.update(e); store.save(); plan()
        return e.path
    }

    /// Move a timed file somewhere else (Permanent, cloud …) – it counts as kept.
    @discardableResult
    func move(_ e: Entry, to dir: URL) -> String {
        store.remove(e.path)
        var target = e.path
        if Paths.fm.fileExists(atPath: e.path) {
            let dest = Paths.freeName(in: dir, e.name)
            do { try Paths.fm.moveItem(at: e.url, to: dest); target = dest.path }
            catch { Log.info("move failed: \(error)"); store.update(e) }
        }
        store.save(); plan()
        return target
    }

    /// Right-click › Services › Workbench: set a timer on any file.
    func setByHand(_ path: String, _ d: String) -> Entry? {
        guard Paths.fm.fileExists(atPath: path) else { return nil }
        if d == Dur.forever { store.remove(path); store.save(); plan(); return nil }
        let now = Date()
        let e = Entry(path: path, added: now, expires: Dur.expiry(d, from: now), origin: "hand", chosen: d)
        store.update(e); store.save(); plan()
        return e
    }

    func trashNow(_ e: Entry) {
        do { try Paths.fm.trashItem(at: e.url, resultingItemURL: nil) } catch { Log.info("trash failed: \(error)") }
        store.remove(e.path); store.save(); plan()
    }

    /// Everything expired goes to the Trash. If a file is in use, try again in 10 minutes.
    func sweep() {
        let now = Date()
        var changed = false
        for e in store.entries {
            guard let x = e.expires, x <= now else { continue }
            changed = true
            if !Paths.fm.fileExists(atPath: e.path) { store.remove(e.path); continue }
            do {
                try Paths.fm.trashItem(at: e.url, resultingItemURL: nil)
                store.remove(e.path)
                Log.info("expired → Trash: \(e.path)")
            } catch {
                var later = e; later.expires = now.addingTimeInterval(600); store.update(later)
            }
        }
        if changed { store.save() }
    }

    func plan() {
        expiryTimer?.invalidate()
        guard let next = store.entries.compactMap({ $0.expires }).min() else { return }
        let wait = min(max(next.timeIntervalSinceNow, 1), 6 * 3600)
        expiryTimer = Timer.scheduledTimer(withTimeInterval: wait, repeats: false) { [weak self] _ in self?.sweep(); self?.plan() }
    }

    /// At start: drop entries whose file is gone, pick up files sitting in time folders without a timer.
    func reconcile() {
        store.entries.removeAll { !Paths.fm.fileExists(atPath: $0.path) }
        for (folder, d) in Paths.timeFolders {
            let dir = Paths.root.appendingPathComponent(folder)
            for n in (try? Paths.fm.contentsOfDirectory(atPath: dir.path)) ?? [] {
                let p = dir.appendingPathComponent(n).path
                if Paths.isHelperFile(p) || store.find(p) != nil { continue }
                let since = ((try? Paths.fm.attributesOfItem(atPath: p)[.creationDate]) as? Date) ?? Date()
                store.update(Entry(path: p, added: since, expires: Dur.expiry(d, from: since), origin: "folder", chosen: d))
            }
        }
        store.save()
    }
}
