import Foundation
import Combine

/// Starts and supervises the MacAC game engine for one session.
@MainActor
final class EngineLauncher: ObservableObject {
    enum State: Equatable { case idle, starting, running(pid: Int32), exited(code: Int32), failed(String) }

    @Published private(set) var state: State = .idle
    @Published private(set) var log: [String] = []
    private var process: Process?
    private var logFile: FileHandle?

    /// Full engine output, including noisy input events, for diagnostics.
    static let logFileURL = Store.directory.appendingPathComponent("engine.log")

    var isRunning: Bool { if case .running = state { return true }; return false }

    /// Locate the `dotnet` host.
    static func findDotnet() -> String? {
        for p in ["/usr/local/share/dotnet/dotnet", "/opt/homebrew/bin/dotnet", "/usr/local/bin/dotnet"]
        where FileManager.default.isExecutableFile(atPath: p) { return p }
        return nil
    }

    /// Homebrew prefix, for the Vulkan loader + MoltenVK when running an unpackaged engine.
    static func brewPrefix() -> String {
        for p in ["/opt/homebrew", "/usr/local"]
        where FileManager.default.fileExists(atPath: p + "/lib/libvulkan.dylib") { return p }
        return "/opt/homebrew"
    }

    func launch(server: SavedServer, account: Account, password: String, engine: EnginePaths) {
        guard !isRunning else { return }
        guard engine.hasEngine else {
            state = .failed("The game engine is not installed. Open Setup and download it."); return
        }
        guard !password.isEmpty else {
            state = .failed("No password saved for \(account.username). Re-enter it in Accounts, then Play."); return
        }
        guard FileManager.default.fileExists(atPath: engine.datDirectory + "/client_portal.dat") else {
            state = .failed("Game data not found in \(engine.datDirectory). Finish Setup first."); return
        }

        var env = ProcessInfo.processInfo.environment
        env["MACAC_DAT_DIR"] = engine.datDirectory
        env["MACAC_LIVE"] = "1"
        env["MACAC_TEST_HOST"] = server.host
        env["MACAC_TEST_PORT"] = String(server.port)
        env["MACAC_TEST_USER"] = account.username
        env["MACAC_TEST_PASS"] = password

        let p = Process()
        if engine.hasInstalledEngine {
            // Self-contained bundle: brings its own runtime and Vulkan.
            p.executableURL = URL(fileURLWithPath: engine.engineApp + "/Contents/MacOS/MacAC")
            p.arguments = [engine.datDirectory]
            p.currentDirectoryURL = URL(fileURLWithPath: engine.engineApp + "/Contents/Resources/engine")
        } else {
            // Developer build: run the assembly with the system dotnet and Homebrew Vulkan.
            guard let dotnet = Self.findDotnet() else {
                state = .failed("The .NET runtime was not found. Install .NET 10 and try again."); return
            }
            let brew = Self.brewPrefix()
            env["DYLD_LIBRARY_PATH"] = brew + "/lib"
            env["VK_DRIVER_FILES"] = brew + "/etc/vulkan/icd.d/MoltenVK_icd.json"
            p.executableURL = URL(fileURLWithPath: dotnet)
            p.arguments = [engine.clientDLL, engine.datDirectory]
            p.currentDirectoryURL = URL(fileURLWithPath: engine.clientDLL).deletingLastPathComponent()
        }
        p.environment = env

        FileManager.default.createFile(atPath: Self.logFileURL.path, contents: nil)
        let file = try? FileHandle(forWritingTo: Self.logFileURL)
        logFile = file

        let pipe = Pipe()
        p.standardOutput = pipe; p.standardError = pipe
        pipe.fileHandleForReading.readabilityHandler = { [weak self] h in
            let d = h.availableData
            guard !d.isEmpty, let s = String(data: d, encoding: .utf8) else { return }
            try? file?.write(contentsOf: d)
            // Keep the on-screen log readable: raw input events go to the file only.
            let lines = s.split(separator: "\n").map(String.init).filter { !$0.hasPrefix("[input]") }
            guard !lines.isEmpty else { return }
            Task { @MainActor in
                self?.log.append(contentsOf: lines)
                if let n = self?.log.count, n > 2000 { self?.log.removeFirst(n - 2000) }
            }
        }
        p.terminationHandler = { [weak self] proc in
            pipe.fileHandleForReading.readabilityHandler = nil
            try? file?.close()
            Task { @MainActor in self?.state = .exited(code: proc.terminationStatus); self?.process = nil }
        }

        log = ["Launching \(server.name) (\(server.address)) as \(account.username)…"]
        state = .starting
        do { try p.run(); process = p; state = .running(pid: p.processIdentifier) }
        catch { state = .failed("Could not start the engine: \(error.localizedDescription)") }
    }

    func stop() { process?.terminate() }
}
