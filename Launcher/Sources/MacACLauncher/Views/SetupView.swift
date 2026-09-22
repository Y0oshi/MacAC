import SwiftUI
import AppKit

/// Where the engine and game data live. Validates the four retail DAT files.
struct SetupView: View {
    @EnvironmentObject private var store: Store
    @StateObject private var data = DataInstaller()
    @StateObject private var engineInstaller = EngineInstaller()
    @State private var showDeveloper = false
    /// Where game data goes unless the player chooses otherwise.
    static let defaultDataDirectory = Store.directory.appendingPathComponent("GameData", isDirectory: true).path
    private static let required = ["client_portal.dat", "client_cell_1.dat", "client_highres.dat", "client_local_English.dat"]

    private var missing: [String] {
        Self.required.filter { !FileManager.default.fileExists(atPath: store.engine.datDirectory + "/" + $0) }
    }
    private var engineOK: Bool { store.engine.hasEngine }
    private var dotnetOK: Bool { EngineLauncher.findDotnet() != nil }
    private var vulkanOK: Bool { FileManager.default.fileExists(atPath: EngineLauncher.brewPrefix() + "/lib/libMoltenVK.dylib") }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                SectionHeader(title: "Setup", subtitle: "One-time. MacAC downloads what it needs.")

                GroupBox("Game data") {
                    VStack(alignment: .leading, spacing: 8) {
                        HStack {
                            TextField("Game data folder", text: $store.engine.datDirectory)
                                .textFieldStyle(.roundedBorder)
                            Button("Choose…") { choose(directory: true) { store.engine.datDirectory = $0 } }
                        }
                        if missing.isEmpty && !store.engine.datDirectory.isEmpty {
                            Label("All game data files present.", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                        } else {
                            Label(missing.count == Self.required.count ? "Game data not installed yet."
                                  : "Missing: \(missing.joined(separator: ", "))",
                                  systemImage: "arrow.down.circle").foregroundStyle(.secondary)
                        }
                        HStack {
                            Button(data.isBusy ? "Downloading…" : (missing.isEmpty ? "Verify / repair" : "Download game data")) {
                                data.install(into: store.engine.datDirectory)
                            }
                            .buttonStyle(.borderedProminent).tint(Theme.gold)
                            .disabled(data.isBusy || store.engine.datDirectory.isEmpty)
                            if data.isBusy { Button("Cancel") { data.cancel() } }
                        }
                        if data.state == .checking {
                            ProgressView().controlSize(.small)
                            Text("Checking what you already have…").font(.caption).foregroundStyle(.secondary)
                        }
                        if data.state == .installing {
                            ProgressView(value: data.fraction) {
                                HStack {
                                    Text("\(data.currentFile)  (\(data.fileIndex) of \(data.fileCount))")
                                    Spacer()
                                    Text("\(ByteCountFormatter.string(fromByteCount: data.bytesDone, countStyle: .file)) / \(ByteCountFormatter.string(fromByteCount: data.bytesTotal, countStyle: .file))")
                                        .foregroundStyle(.secondary).monospacedDigit()
                                }.font(.caption)
                            }
                        }
                        if case .failed(let m) = data.state {
                            Label(m, systemImage: "exclamationmark.triangle").foregroundStyle(Theme.ember)
                        }
                        if data.state == .done {
                            Text("Game data is up to date.").font(.caption).foregroundStyle(.secondary)
                        }
                    }.padding(4)
                }

                GroupBox("Game engine") {
                    VStack(alignment: .leading, spacing: 8) {
                        if store.engine.hasInstalledEngine {
                            HStack {
                                Label("Engine \(store.engine.engineVersion) installed.", systemImage: "checkmark.circle.fill")
                                    .foregroundStyle(.green)
                                if let a = engineInstaller.available, a != store.engine.engineVersion {
                                    Pill(text: "update \(a) available", tint: Theme.gold)
                                }
                            }
                        } else if !store.engine.clientDLL.isEmpty && engineOK {
                            Label("Using developer build.", systemImage: "hammer").foregroundStyle(.secondary)
                        } else {
                            Label("Engine not installed yet.", systemImage: "arrow.down.circle").foregroundStyle(.secondary)
                        }
                        HStack {
                            Button(engineInstaller.isBusy ? "Installing…"
                                   : (store.engine.hasInstalledEngine ? "Check for update" : "Download engine")) {
                                engineInstaller.install(store: store)
                            }
                            .buttonStyle(.borderedProminent).tint(Theme.gold)
                            .disabled(engineInstaller.isBusy)
                            if engineInstaller.isBusy { Button("Cancel") { engineInstaller.cancel() } }
                            Spacer()
                            Text("\(Distribution.runtimeIdentifier == "osx-arm64" ? "Apple Silicon" : "Intel") Mac")
                                .font(.caption).foregroundStyle(.tertiary)
                        }
                        if engineInstaller.state == .downloading {
                            ProgressView(value: engineInstaller.fraction) {
                                HStack {
                                    Text("Downloading engine")
                                    Spacer()
                                    Text("\(ByteCountFormatter.string(fromByteCount: engineInstaller.bytesDone, countStyle: .file)) / \(ByteCountFormatter.string(fromByteCount: engineInstaller.bytesTotal, countStyle: .file))")
                                        .foregroundStyle(.secondary).monospacedDigit()
                                }.font(.caption)
                            }
                        }
                        if engineInstaller.state == .installing {
                            ProgressView().controlSize(.small)
                            Text("Installing…").font(.caption).foregroundStyle(.secondary)
                        }
                        if case .failed(let m) = engineInstaller.state {
                            Label(m, systemImage: "exclamationmark.triangle").foregroundStyle(Theme.ember)
                        }
                        if engineInstaller.state == .done && store.engine.hasInstalledEngine {
                            Text("Engine is up to date.").font(.caption).foregroundStyle(.secondary)
                        }

                        DisclosureGroup("Developer", isExpanded: $showDeveloper) {
                            VStack(alignment: .leading, spacing: 6) {
                                HStack {
                                    TextField("Path to MacAC.Client.dll (run with system dotnet)", text: $store.engine.clientDLL)
                                        .textFieldStyle(.roundedBorder)
                                    Button("Choose…") { choose(directory: false) { store.engine.clientDLL = $0 } }
                                    Button("Auto-detect") { autodetect() }
                                }
                                check(dotnetOK, ok: ".NET runtime found.", bad: ".NET 10 runtime not found (brew install dotnet).")
                                check(vulkanOK, ok: "Vulkan / MoltenVK found.", bad: "MoltenVK not found (brew install molten-vk vulkan-loader).")
                                Text("Only needed when running the engine from a source build. Players use the downloaded engine.")
                                    .font(.caption).foregroundStyle(.tertiary)
                            }.padding(.top, 4)
                        }.font(.caption)
                    }.padding(4)
                }

            }.padding(20)
        }
        .onAppear {
            if store.engine.datDirectory.isEmpty { store.engine.datDirectory = Self.defaultDataDirectory }
            // Adopt an engine installed by a previous run even if config was lost.
            if store.engine.engineApp.isEmpty,
               FileManager.default.fileExists(atPath: EngineInstaller.appPath + "/Contents/MacOS/MacAC") {
                store.engine.engineApp = EngineInstaller.appPath
            }
        }
        .task { await engineInstaller.check() }
    }

    @ViewBuilder private func check(_ ok: Bool, ok okText: String, bad: String) -> some View {
        Label(ok ? okText : bad, systemImage: ok ? "checkmark.circle.fill" : "xmark.circle")
            .foregroundStyle(ok ? .green : Theme.ember)
    }

    private func choose(directory: Bool, _ done: @escaping (String) -> Void) {
        let p = NSOpenPanel()
        p.canChooseDirectories = directory; p.canChooseFiles = !directory; p.allowsMultipleSelection = false
        if p.runModal() == .OK, let u = p.url { done(u.path) }
    }

    /// Look for a built engine near this app or in a sibling source checkout.
    private func autodetect() {
        let exe = Bundle.main.executableURL ?? URL(fileURLWithPath: CommandLine.arguments[0])
        var dir = exe.deletingLastPathComponent()
        for _ in 0..<8 {
            let c = dir.appendingPathComponent("src/MacAC.Client/bin/Release/net10.0/MacAC.Client.dll").path
            if FileManager.default.fileExists(atPath: c) { store.engine.clientDLL = c; return }
            dir.deleteLastPathComponent()
        }
    }
}
