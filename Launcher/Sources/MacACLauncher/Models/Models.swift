import Foundation

/// A server published in the community directory.
struct PublicServer: Identifiable, Hashable {
    let id: String
    let name: String
    let description: String
    let emulator: String
    let host: String
    let port: Int
    let type: String       // "PvE" / "PvP" / ...
    let status: String     // "Stable" / "Development" / ...
    let websiteURL: URL?
    let discordURL: URL?

    var address: String { "\(host):\(port)" }
    var isPvP: Bool { type.localizedCaseInsensitiveContains("pvp") }
}

/// A server the user has added to their own list.
struct SavedServer: Identifiable, Codable, Hashable {
    var id: String = UUID().uuidString
    var name: String
    var host: String
    var port: Int
    var sourceID: String?   // PublicServer.id when added from the browser

    var address: String { "\(host):\(port)" }
}

/// A login the user has stored. Passwords live in the launcher's private config.
struct Account: Identifiable, Codable, Hashable {
    var id: String = UUID().uuidString
    var username: String
    var serverID: String    // SavedServer.id
}

/// Live population from TreeStats, keyed by server name.
struct PlayerCount: Decodable {
    let server: String
    let count: Int
    let date: String
    let age: String
}

/// Where the game engine and its data live on this Mac.
struct EnginePaths: Codable, Equatable {
    var datDirectory: String = ""
    var clientDLL: String = ""        // dev only: path to MacAC.Client.dll run via dotnet
    var engineApp: String = ""        // installed "MacAC Engine.app" (self-contained)
    var engineVersion: String = ""

    /// The engine executable to launch: installed bundle first, dev dll as fallback.
    var hasEngine: Bool { hasInstalledEngine || FileManager.default.fileExists(atPath: clientDLL) }
    var hasInstalledEngine: Bool {
        !engineApp.isEmpty && FileManager.default.fileExists(atPath: engineApp + "/Contents/MacOS/MacAC")
    }
}
