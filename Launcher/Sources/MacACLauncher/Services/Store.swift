import Foundation
import Combine

/// The launcher's persisted state: your servers, your accounts, engine paths.
@MainActor
final class Store: ObservableObject {
    @Published var servers: [SavedServer] = [] { didSet { save() } }
    @Published var accounts: [Account] = [] { didSet { save() } }
    @Published var engine = EnginePaths() { didSet { save() } }
    /// Passwords keyed by Account.id. Kept in the launcher's private config file
    /// (owner-only permissions) so they survive rebuilds and re-signing.
    private var passwords: [String: String] = [:] { didSet { save() } }

    private struct Document: Codable {
        var servers: [SavedServer]
        var accounts: [Account]
        var engine: EnginePaths
        var passwords: [String: String]? = nil
    }

    static let directory: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let dir = base.appendingPathComponent("MacAC", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }()
    private static let file = directory.appendingPathComponent("launcher.json")
    private var loading = true

    init() {
        if let data = try? Data(contentsOf: Self.file),
           let doc = try? JSONDecoder().decode(Document.self, from: data) {
            servers = doc.servers; accounts = doc.accounts; engine = doc.engine
            passwords = doc.passwords ?? [:]
        }
        loading = false
    }

    private func save() {
        guard !loading else { return }
        let doc = Document(servers: servers, accounts: accounts, engine: engine, passwords: passwords)
        let enc = JSONEncoder(); enc.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? enc.encode(doc) {
            try? data.write(to: Self.file, options: .atomic)
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: Self.file.path)
        }
    }

    // MARK: servers

    func isAdded(_ server: PublicServer) -> Bool {
        servers.contains { $0.sourceID == server.id || ($0.host == server.host && $0.port == server.port) }
    }

    func add(_ server: PublicServer) {
        guard !isAdded(server) else { return }
        servers.append(SavedServer(name: server.name, host: server.host, port: server.port, sourceID: server.id))
    }

    func addManual(name: String, host: String, port: Int) {
        servers.append(SavedServer(name: name, host: host, port: port))
    }

    func remove(server: SavedServer) {
        for a in accounts where a.serverID == server.id { passwords[a.id] = nil }
        accounts.removeAll { $0.serverID == server.id }
        servers.removeAll { $0.id == server.id }
    }

    // MARK: accounts

    func accounts(for server: SavedServer) -> [Account] { accounts.filter { $0.serverID == server.id } }

    /// Adds an account, or updates the password if that username already exists on the server.
    func addAccount(username: String, password: String, server: SavedServer) {
        if let existing = accounts.first(where: { $0.serverID == server.id && $0.username == username }) {
            passwords[existing.id] = password
            return
        }
        let a = Account(username: username, serverID: server.id)
        passwords[a.id] = password
        accounts.append(a)
    }

    func remove(account: Account) {
        passwords[account.id] = nil
        accounts.removeAll { $0.id == account.id }
    }

    func password(for account: Account) -> String { passwords[account.id] ?? "" }

    func hasPassword(_ account: Account) -> Bool { !password(for: account).isEmpty }
}
