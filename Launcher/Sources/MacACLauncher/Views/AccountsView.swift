import SwiftUI

/// Logins per server. Servers create your account on first login.
struct AccountsView: View {
    @EnvironmentObject private var store: Store
    @State private var serverID: String = ""
    @State private var username = ""
    @State private var password = ""

    private var server: SavedServer? { store.servers.first { $0.id == serverID } ?? store.servers.first }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            SectionHeader(title: "Accounts",
                          subtitle: "Pick a username and password. Most servers create the account the first time you log in.")
            if store.servers.isEmpty {
                ContentUnavailableView("Add a server first", systemImage: "person.crop.circle.badge.questionmark",
                    description: Text("Accounts belong to a server. Add one from the Server Browser."))
            } else {
                List {
                    ForEach(store.servers) { s in
                        Section {
                            if store.accounts(for: s).isEmpty { Text("No accounts").foregroundStyle(.tertiary) }
                            ForEach(store.accounts(for: s)) { a in
                                HStack {
                                    Label(a.username, systemImage: "person")
                                    if !store.hasPassword(a) {
                                        Pill(text: "password needed", tint: Theme.ember)
                                    }
                                    Spacer()
                                    Button(role: .destructive) { store.remove(account: a) } label: {
                                        Image(systemName: "trash")
                                    }.buttonStyle(.borderless)
                                }
                            }
                        } header: { Text(s.name) }
                    }
                }.listStyle(.inset)
                GroupBox("Add or update an account") {
                    HStack {
                        Picker("Server", selection: $serverID) {
                            ForEach(store.servers) { Text($0.name).tag($0.id) }
                        }.frame(width: 200)
                        TextField("Username", text: $username)
                        SecureField("Password", text: $password)
                        Button("Save") {
                            if let s = server {
                                store.addAccount(username: username, password: password, server: s)
                                username = ""; password = ""
                            }
                        }
                        .disabled(username.isEmpty || password.isEmpty || server == nil)
                        .buttonStyle(.borderedProminent).tint(Theme.gold)
                    }.textFieldStyle(.roundedBorder).padding(4)
                }
                .onAppear { if serverID.isEmpty { serverID = store.servers.first?.id ?? "" } }
            }
        }.padding(20)
    }
}
