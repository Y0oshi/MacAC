import Foundation

/// Fetches the community server directory and live populations.
enum ServerDirectory {
    static let listURL = URL(string:
        "https://raw.githubusercontent.com/acresources/serverslist/master/Servers.xml")!
    static let countsURL = URL(string:
        "https://treestats.net/player_counts-latest.json")!

    static func fetchServers() async throws -> [PublicServer] {
        let (data, _) = try await URLSession.shared.data(from: listURL)
        return ServerListParser.parse(data)
    }

    static func fetchPlayerCounts() async -> [String: PlayerCount] {
        guard let (data, _) = try? await URLSession.shared.data(from: countsURL),
              let counts = try? JSONDecoder().decode([PlayerCount].self, from: data)
        else { return [:] }
        return Dictionary(counts.map { ($0.server.lowercased(), $0) }, uniquingKeysWith: { a, _ in a })
    }

    /// Match a directory entry to a TreeStats row by name or by a hostname label
    /// (e.g. "coldeve" in play.coldeve.ac), the same heuristic players expect.
    static func population(for server: PublicServer, in counts: [String: PlayerCount]) -> Int? {
        if let c = counts[server.name.lowercased()] { return c.count }
        let labels = server.host.lowercased().split(separator: ".").map(String.init)
        for (name, c) in counts {
            let n = name.replacingOccurrences(of: " ", with: "")
            if labels.contains(where: { $0.count >= 4 && n.contains($0) }) { return c.count }
        }
        return nil
    }
}

/// Streaming parser for the `<ArrayOfServerItem>` document.
final class ServerListParser: NSObject, XMLParserDelegate {
    private var servers: [PublicServer] = []
    private var fields: [String: String] = [:]
    private var current = ""
    private var text = ""

    static func parse(_ data: Data) -> [PublicServer] {
        let p = ServerListParser()
        let parser = XMLParser(data: data)
        parser.delegate = p
        parser.parse()
        return p.servers
    }

    func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?,
                qualifiedName: String?, attributes: [String: String] = [:]) {
        if name == "ServerItem" { fields = [:] }
        current = name; text = ""
    }

    func parser(_ parser: XMLParser, foundCharacters string: String) { text += string }

    func parser(_ parser: XMLParser, didEndElement name: String, namespaceURI: String?,
                qualifiedName: String?) {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if name == "ServerItem" {
            let host = fields["server_host"] ?? ""
            guard !host.isEmpty else { return }
            servers.append(PublicServer(
                id: fields["id"] ?? UUID().uuidString,
                name: fields["name"] ?? host,
                description: fields["description"] ?? "",
                emulator: fields["emu"] ?? "",
                host: host,
                port: Int(fields["server_port"] ?? "") ?? 9000,
                type: fields["type"] ?? "",
                status: fields["status"] ?? "",
                websiteURL: URL(string: fields["website_url"] ?? ""),
                discordURL: URL(string: fields["discord_url"] ?? "")))
        } else {
            fields[name] = value
        }
    }
}
