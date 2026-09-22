import SwiftUI

/// The servers you've chosen, plus manual entry for private ones.
struct MyServersView: View {
    @EnvironmentObject private var store: Store
    @State private var name = ""
    @State private var host = ""
    @State private var port = "9000"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionHeader(title: "My Servers",
                          subtitle: "Add from the Server Browser, or enter a private server below.")
            if store.servers.isEmpty {
                ContentUnavailableView("No servers yet", systemImage: "server.rack",
                    description: Text("Open the Server Browser and click Add on any server."))
            } else {
                List {
                    ForEach(store.servers) { s in
                        HStack {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(s.name).font(.headline)
                                Text(s.address).font(.caption.monospaced()).foregroundStyle(.secondary)
                            }
                            Spacer()
                            Text("\(store.accounts(for: s).count) account(s)")
                                .font(.caption).foregroundStyle(.tertiary)
                            Button(role: .destructive) { store.remove(server: s) } label: {
                                Image(systemName: "trash")
                            }.buttonStyle(.borderless)
                        }.padding(.vertical, 4)
                    }
                }.listStyle(.inset)
            }
            GroupBox("Add a private server") {
                HStack {
                    TextField("Name", text: $name)
                    TextField("Host", text: $host)
                    TextField("Port", text: $port).frame(width: 70)
                    Button("Add") {
                        store.addManual(name: name.isEmpty ? host : name, host: host, port: Int(port) ?? 9000)
                        name = ""; host = ""; port = "9000"
                    }
                    .disabled(host.trimmingCharacters(in: .whitespaces).isEmpty)
                    .buttonStyle(.borderedProminent).tint(Theme.gold)
                }.textFieldStyle(.roundedBorder).padding(4)
            }
        }.padding(20)
    }
}
