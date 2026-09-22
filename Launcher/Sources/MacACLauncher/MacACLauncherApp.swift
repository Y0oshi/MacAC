import SwiftUI

enum Page: String, CaseIterable, Identifiable {
    case play = "Play", browse = "Server Browser", servers = "My Servers",
         accounts = "Accounts", setup = "Setup"
    var id: String { rawValue }
    var icon: String {
        switch self {
        case .play: return "play.circle.fill"
        case .browse: return "globe"
        case .servers: return "server.rack"
        case .accounts: return "person.crop.circle"
        case .setup: return "wrench.and.screwdriver"
        }
    }
}

@main
struct MacACLauncherApp: App {
    @StateObject private var store = Store()
    @StateObject private var engine = EngineLauncher()

    var body: some Scene {
        WindowGroup("MacAC") {
            RootView()
                .environmentObject(store)
                .environmentObject(engine)
                .frame(minWidth: 900, minHeight: 560)
                .preferredColorScheme(.dark)
        }
        .windowStyle(.hiddenTitleBar)
    }
}

struct RootView: View {
    @State private var page: Page? = .play
    @EnvironmentObject private var engine: EngineLauncher

    var body: some View {
        NavigationSplitView {
            List(Page.allCases, selection: $page) { s in
                Label(s.rawValue, systemImage: s.icon).tag(s)
            }
            .listStyle(.sidebar)
            .navigationSplitViewColumnWidth(min: 180, ideal: 200)
            .safeAreaInset(edge: .top) {
                VStack(alignment: .leading, spacing: 0) {
                    Text("MacAC").font(.system(size: 22, weight: .bold, design: .serif))
                        .foregroundStyle(Theme.gold)
                    Text("Asheron's Call, natively.").font(.caption).foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 16).padding(.top, 28).padding(.bottom, 8)
            }
            .safeAreaInset(edge: .bottom) {
                if engine.isRunning {
                    Label("Game running", systemImage: "circle.fill")
                        .font(.caption).foregroundStyle(.green)
                        .frame(maxWidth: .infinity, alignment: .leading).padding(12)
                }
            }
        } detail: {
            switch page ?? .play {
            case .play: PlayView()
            case .browse: ServerBrowserView()
            case .servers: MyServersView()
            case .accounts: AccountsView()
            case .setup: SetupView()
            }
        }
    }
}
