import SwiftUI

/// Choose a server and account, press Play.
struct PlayView: View {
    @EnvironmentObject private var store: Store
    @EnvironmentObject private var engine: EngineLauncher
    @State private var serverID = ""
    @State private var accountID = ""

    private var server: SavedServer? { store.servers.first { $0.id == serverID } }
    private var accounts: [Account] { server.map(store.accounts(for:)) ?? [] }
    private var account: Account? { accounts.first { $0.id == accountID } }
    private var ready: Bool { server != nil && account != nil && !engine.isRunning }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            SectionHeader(title: "Play")

            if store.servers.isEmpty {
                ContentUnavailableView("Nothing to play yet", systemImage: "play.slash",
                    description: Text("Add a server from the Server Browser, then create an account."))
            } else {
                GroupBox {
                    Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 12) {
                        GridRow {
                            Text("Server").foregroundStyle(.secondary)
                            Picker("", selection: $serverID) {
                                ForEach(store.servers) { Text("\($0.name)  ·  \($0.address)").tag($0.id) }
                            }.labelsHidden()
                        }
                        GridRow {
                            Text("Account").foregroundStyle(.secondary)
                            Picker("", selection: $accountID) {
                                if accounts.isEmpty { Text("No accounts for this server").tag("") }
                                ForEach(accounts) { Text($0.username).tag($0.id) }
                            }.labelsHidden()
                        }
                    }.padding(6)
                }

                HStack(spacing: 12) {
                    Button {
                        if let s = server, let a = account {
                            engine.launch(server: s, account: a, password: store.password(for: a), engine: store.engine)
                        }
                    } label: {
                        Label("Play", systemImage: "play.fill").frame(minWidth: 120)
                    }
                    .controlSize(.large).buttonStyle(.borderedProminent).tint(Theme.gold)
                    .disabled(!ready)

                    if engine.isRunning {
                        Button("Stop", role: .destructive) { engine.stop() }.controlSize(.large)
                    }
                    Spacer()
                    stateLabel
                }

                GroupBox("Engine log") {
                    ScrollViewReader { proxy in
                        ScrollView {
                            LazyVStack(alignment: .leading, spacing: 1) {
                                ForEach(Array(engine.log.enumerated()), id: \.offset) { i, line in
                                    Text(line).font(.caption.monospaced()).textSelection(.enabled).id(i)
                                }
                            }.frame(maxWidth: .infinity, alignment: .leading)
                        }
                        .onChange(of: engine.log.count) { _, n in if n > 0 { proxy.scrollTo(n - 1) } }
                    }.frame(maxHeight: .infinity)
                }
            }
        }
        .padding(20)
        .onAppear {
            if serverID.isEmpty { serverID = store.servers.first?.id ?? "" }
        }
        .onChange(of: serverID) { _, _ in accountID = accounts.first?.id ?? "" }
        .onChange(of: store.accounts.count) { _, _ in if account == nil { accountID = accounts.first?.id ?? "" } }
    }

    @ViewBuilder private var stateLabel: some View {
        switch engine.state {
        case .idle: EmptyView()
        case .starting: Label("Starting…", systemImage: "hourglass").foregroundStyle(.secondary)
        case .running(let pid): Label("Running (pid \(pid))", systemImage: "circle.fill").foregroundStyle(.green)
        case .exited(let code): Label("Exited (\(code))", systemImage: "stop.circle").foregroundStyle(.secondary)
        case .failed(let msg): Label(msg, systemImage: "exclamationmark.triangle").foregroundStyle(Theme.ember)
        }
    }
}
