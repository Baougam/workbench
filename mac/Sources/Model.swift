import Foundation
import AppKit

// Workbench for macOS — data model (timers, settings, paths). Same concepts and keys as the Windows version.

enum Dur {
    static let hour = "1 hour", day = "1 day", week = "1 week", month = "1 month", forever = "Forever"
    static let stations = [hour, day, week, month, forever]

    static func short(_ k: String) -> String {
        switch k {
        case hour: return "Hour"
        case day: return "Day"
        case week: return "Week"
        case month: return "Month"
        case forever: return "∞"
        default: return k
        }
    }

    static func expiry(_ k: String, from d: Date = Date()) -> Date? {
        switch k {
        case hour: return d.addingTimeInterval(3600)
        case day: return d.addingTimeInterval(86400)
        case week: return d.addingTimeInterval(7 * 86400)
        case month: return Calendar.current.date(byAdding: .month, value: 1, to: d)
        case forever: return nil
        default: return d.addingTimeInterval(7 * 86400)
        }
    }

    /// "42m", "5h", "3d", "2w", "15 Jan"
    static func left(_ until: Date, now: Date = Date()) -> String {
        let r = until.timeIntervalSince(now)
        if r < 3600 { return "\(max(1, Int(ceil(r / 60))))m" }
        if r < 86400 { return "\(Int((r / 3600).rounded()))h" }
        if r < 14 * 86400 { return "\(Int((r / 86400).rounded()))d" }
        if r < 60 * 86400 { return "\(Int((r / 604800).rounded()))w" }
        let f = DateFormatter(); f.locale = Locale(identifier: "en_GB"); f.dateFormat = "d MMM"
        return f.string(from: until)
    }

    /// "today at 15:12", "tomorrow at 09:00", "Tue 1 Dec at 15:12"
    static func when(_ d: Date) -> String {
        let c = Calendar.current
        let t = DateFormatter(); t.locale = Locale(identifier: "en_GB"); t.dateFormat = "HH:mm"
        if c.isDateInToday(d) { return "today at " + t.string(from: d) }
        if c.isDateInTomorrow(d) { return "tomorrow at " + t.string(from: d) }
        let f = DateFormatter(); f.locale = Locale(identifier: "en_GB")
        f.dateFormat = c.component(.year, from: d) == c.component(.year, from: Date()) ? "EEE d MMM" : "EEE d MMM yyyy"
        return f.string(from: d) + " at " + t.string(from: d)
    }

    /// 0 = today (< 24 h), 1 = this week, 2 = later
    static func column(_ until: Date, now: Date = Date()) -> Int {
        let r = until.timeIntervalSince(now)
        if r < 86400 { return 0 }
        if r <= 7.01 * 86400 { return 1 }
        return 2
    }

    static let installers: Set<String> = ["dmg", "pkg", "mpkg", "iso", "exe", "msi"]
}

struct Entry: Codable, Identifiable, Equatable {
    var id: String { path }
    var path: String
    var added: Date
    var expires: Date?
    var origin: String      // "download" | "desktop" | "folder" | "hand"
    var chosen: String
    var name: String { (path as NSString).lastPathComponent }
    var url: URL { URL(fileURLWithPath: path) }
}

struct Settings: Codable {
    var ask = true
    var standard = Dur.week
    var watchDesktop = true
    var confirmSaves = true
    var startAtLogin = true
    var tourDone = false
    var askedToMove = false
    var cloudPath = ""
    var latestVersion = ""
    var lastCheck = Date.distantPast
    var browser = false             // native messaging host registered
    var browsers: [String] = []     // browsers whose extension has said hello

    init() {}
    // missing keys fall back to defaults (older files keep working)
    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        ask = (try? c.decode(Bool.self, forKey: .ask)) ?? true
        standard = (try? c.decode(String.self, forKey: .standard)) ?? Dur.week
        watchDesktop = (try? c.decode(Bool.self, forKey: .watchDesktop)) ?? true
        confirmSaves = (try? c.decode(Bool.self, forKey: .confirmSaves)) ?? true
        startAtLogin = (try? c.decode(Bool.self, forKey: .startAtLogin)) ?? true
        tourDone = (try? c.decode(Bool.self, forKey: .tourDone)) ?? false
        askedToMove = (try? c.decode(Bool.self, forKey: .askedToMove)) ?? false
        cloudPath = (try? c.decode(String.self, forKey: .cloudPath)) ?? ""
        latestVersion = (try? c.decode(String.self, forKey: .latestVersion)) ?? ""
        browser = (try? c.decode(Bool.self, forKey: .browser)) ?? false
        browsers = (try? c.decode([String].self, forKey: .browsers)) ?? []
        lastCheck = (try? c.decode(Date.self, forKey: .lastCheck)) ?? Date.distantPast
    }
}

enum Paths {
    static let fm = FileManager.default
    static let home = fm.homeDirectoryForCurrentUser
    static let root = home.appendingPathComponent("Workbench", isDirectory: true)
    static let permanent = root.appendingPathComponent("Permanent", isDirectory: true)
    static let downloads = fm.urls(for: .downloadsDirectory, in: .userDomainMask).first ?? home.appendingPathComponent("Downloads")
    static let desktop = fm.urls(for: .desktopDirectory, in: .userDomainMask).first ?? home.appendingPathComponent("Desktop")
    static let data: URL = {
        let base = fm.urls(for: .applicationSupportDirectory, in: .userDomainMask).first ?? home.appendingPathComponent("Library/Application Support")
        return base.appendingPathComponent("Workbench", isDirectory: true)
    }()
    /// folder name → duration key: saving into one of these sets the timer
    static let timeFolders: [(String, String)] = [("1 Hour", Dur.hour), ("1 Day", Dur.day), ("1 Week", Dur.week), ("1 Month", Dur.month)]

    static func create() {
        for u in [root, permanent, data] + timeFolders.map({ root.appendingPathComponent($0.0, isDirectory: true) }) {
            try? fm.createDirectory(at: u, withIntermediateDirectories: true)
        }
    }

    /// duration if the item sits directly in a time folder, "Permanent" for the permanent folder, else nil
    static func durationFor(_ path: String) -> String? {
        let parent = (path as NSString).deletingLastPathComponent
        if parent == permanent.path { return "Permanent" }
        for (n, d) in timeFolders where parent == root.appendingPathComponent(n).path { return d }
        return nil
    }

    static func freeName(in dir: URL, _ name: String) -> URL {
        var u = dir.appendingPathComponent(name)
        if !fm.fileExists(atPath: u.path) { return u }
        let base = (name as NSString).deletingPathExtension, ext = (name as NSString).pathExtension
        var i = 2
        repeat {
            u = dir.appendingPathComponent(ext.isEmpty ? "\(base) (\(i))" : "\(base) (\(i)).\(ext)")
            i += 1
        } while fm.fileExists(atPath: u.path)
        return u
    }

    static func isHelperFile(_ path: String) -> Bool {
        let n = (path as NSString).lastPathComponent.lowercased()
        if n.isEmpty || n.hasPrefix(".") || n.hasPrefix("~$") || n == "desktop.ini" || n == "icon\r" { return true }
        for e in [".crdownload", ".part", ".partial", ".download", ".opdownload", ".tmp", ".temp", ".aria2", ".!qb", ".crswap", ".bc!", ".dlpart", ".!ut"] where n.hasSuffix(e) { return true }
        return false
    }
}

/// Cloud folders macOS already knows about: Google Drive, OneDrive, Dropbox (File Provider), iCloud Drive.
struct CloudPlace: Equatable { var name: String; var path: String }

enum Cloud {
    static func find() -> [CloudPlace] {
        var l: [CloudPlace] = []
        let cs = Paths.home.appendingPathComponent("Library/CloudStorage")
        if let items = try? Paths.fm.contentsOfDirectory(atPath: cs.path) {
            for i in items.sorted() {
                let p = cs.appendingPathComponent(i)
                if i.hasPrefix("GoogleDrive") {
                    for sub in ["My Drive", "Meine Ablage"] where Paths.fm.fileExists(atPath: p.appendingPathComponent(sub).path) {
                        l.append(CloudPlace(name: "Google Drive", path: p.appendingPathComponent(sub).path)); break
                    }
                } else if i.hasPrefix("OneDrive") {
                    l.append(CloudPlace(name: "OneDrive", path: p.path))
                } else if i.hasPrefix("Dropbox") {
                    l.append(CloudPlace(name: "Dropbox", path: p.path))
                }
            }
        }
        let icloud = Paths.home.appendingPathComponent("Library/Mobile Documents/com~apple~CloudDocs")
        if Paths.fm.fileExists(atPath: icloud.path) { l.append(CloudPlace(name: "iCloud Drive", path: icloud.path)) }
        return l
    }

    static func current(_ s: Settings) -> CloudPlace? {
        if !s.cloudPath.isEmpty, Paths.fm.fileExists(atPath: s.cloudPath) {
            return CloudPlace(name: find().first(where: { $0.path == s.cloudPath })?.name ?? (s.cloudPath as NSString).lastPathComponent, path: s.cloudPath)
        }
        return find().first
    }
}

/// The list of timers + settings. Small JSON files in ~/Library/Application Support/Workbench.
final class Store: ObservableObject {
    static let shared = Store()
    @Published var entries: [Entry] = []
    @Published var settings = Settings()
    @Published var version = 0                     // bumps on every change (views refresh)
    var lastChoice: [String: String] = [:]

    private var entriesURL: URL { Paths.data.appendingPathComponent("timers.json") }
    private var settingsURL: URL { Paths.data.appendingPathComponent("settings.json") }
    private var choiceURL: URL { Paths.data.appendingPathComponent("choices.json") }

    func load() {
        let dec = JSONDecoder()
        if let d = try? Data(contentsOf: entriesURL), let e = try? dec.decode([Entry].self, from: d) { entries = e }
        if let d = try? Data(contentsOf: settingsURL), let s = try? dec.decode(Settings.self, from: d) { settings = s }
        if let d = try? Data(contentsOf: choiceURL), let c = try? dec.decode([String: String].self, from: d) { lastChoice = c }
    }

    func save() {
        let enc = JSONEncoder()
        try? Paths.fm.createDirectory(at: Paths.data, withIntermediateDirectories: true)
        try? enc.encode(entries).write(to: entriesURL, options: .atomic)
        try? enc.encode(settings).write(to: settingsURL, options: .atomic)
        try? enc.encode(lastChoice).write(to: choiceURL, options: .atomic)
        version += 1
    }

    func find(_ path: String) -> Entry? { entries.first { $0.path == path } }
    func remove(_ path: String) { entries.removeAll { $0.path == path } }
    func update(_ e: Entry) {
        if let i = entries.firstIndex(where: { $0.path == e.path }) { entries[i] = e } else { entries.append(e) }
    }

    /// suggestion: what was picked last for this file type, 1 day for installers, otherwise the default
    func suggestion(for path: String) -> String {
        let ext = (path as NSString).pathExtension.lowercased()
        if !ext.isEmpty, let v = lastChoice[ext] { return v }
        if Dur.installers.contains(ext) { return Dur.day }
        return settings.standard
    }

    func remember(_ path: String, _ choice: String) {
        let ext = (path as NSString).pathExtension.lowercased()
        if !ext.isEmpty { lastChoice[ext] = choice }
    }
}

enum Log {
    static func info(_ s: String) {
        let url = Paths.data.appendingPathComponent("log.txt")
        let line = "\(Date())  \(s)\n"
        if let h = try? FileHandle(forWritingTo: url) { h.seekToEndOfFile(); h.write(line.data(using: .utf8)!); try? h.close() }
        else { try? line.data(using: .utf8)?.write(to: url) }
    }
}
