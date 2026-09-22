import SwiftUI

/// Every public server, live, with one-click add. No typing addresses.
struct ServerBrowserView: View {
    enum Filter: String, CaseIterable { case all = "All", pve = "PvE", pvp = "PvP" }

    @EnvironmentObject private var store: Store
    @State private var servers: [PublicServer] = []
    @State private var counts: [String: PlayerCount] = [:]
    @State private var query = ""
    @State private var filter: Filter = .all
    @State private var loading = false
    @State private var error: String?

    private var visible: [PublicServer] {
        servers.filter { s in
            (filter == .all || (filter == .pvp) == s.isPvP)
            && (query.isEmpty || s.name.localizedCaseInsensitiveContains(query)
                || s.host.localizedCaseInsensitiveContains(query)
                || s.description.localizedCaseInsensitiveContains(query))
        }
        .sorted { (ServerDirectory.population(for: $0, in: counts) ?? -1)
                > (ServerDirectory.population(for: $1, in: counts) ?? -1) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .top) {
                SectionHeader(title: "Server Browser",
                              subtitle: "\(servers.count) public servers · sorted by who's online")
                Spacer()
                Picker("", selection: $filter) {
                    ForEach(Filter.allCases, id: \.self) { Text($0.rawValue) }
                }.pickerStyle(.segmented).frame(width: 200)
                Button { Task { await load() } } label: { Image(systemName: "arrow.clockwise") }
                    .disabled(loading)
            }

            if let e = error {
                Label(e, systemImage: "exclamationmark.triangle").foregroundStyle(Theme.ember)
            }

            List(visible) { s in
                ServerRow(server: s, population: ServerDirectory.population(for: s, in: counts),
                          added: store.isAdded(s)) { store.add(s) }
            }
            .listStyle(.inset)
            .overlay {
                if loading && servers.isEmpty { ProgressView("Loading the directory…") }
            }
        }
        .padding(20)
        .searchable(text: $query, prompt: "Search servers")
        .task { await load() }
    }

    private func load() async {
        loading = true; error = nil
        async let list = ServerDirectory.fetchServers()
        async let pop = ServerDirectory.fetchPlayerCounts()
        do { servers = try await list } catch { self.error = "Couldn't reach the server directory. \(error.localizedDescription)" }
        counts = await pop
        loading = false
    }
}

struct ServerRow: View {
    let server: PublicServer
    let population: Int?
    let added: Bool
    let add: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 14) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    Text(server.name).font(.headline)
                    Pill(text: server.isPvP ? "PvP" : "PvE", tint: server.isPvP ? Theme.ember : Theme.sky)
                    if !server.status.isEmpty {
                        Pill(text: server.status,
                             tint: server.status.localizedCaseInsensitiveContains("stable") ? .green : .orange)
                    }
                }
                if !server.description.isEmpty {
                    Text(server.description).font(.callout).foregroundStyle(.secondary).lineLimit(2)
                }
                HStack(spacing: 12) {
                    Text(server.address).font(.caption.monospaced()).foregroundStyle(.tertiary)
                    PopulationView(count: population)
                    if let d = server.discordURL { Link("Discord", destination: d).font(.caption) }
                    if let w = server.websiteURL { Link("Website", destination: w).font(.caption) }
                }
            }
            Spacer()
            Button(action: add) {
                Label(added ? "Added" : "Add", systemImage: added ? "checkmark" : "plus")
            }
            .buttonStyle(.borderedProminent).tint(added ? .gray : Theme.gold)
            .disabled(added)
        }
        .padding(.vertical, 6)
    }
}
