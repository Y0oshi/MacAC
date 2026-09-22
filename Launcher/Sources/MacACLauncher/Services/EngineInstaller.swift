import Foundation
import Combine

/// Downloads and installs the self-contained engine for this Mac's CPU.
@MainActor
final class EngineInstaller: ObservableObject {
    enum State: Equatable { case idle, checking, downloading, installing, done, failed(String) }

    @Published private(set) var state: State = .idle
    @Published private(set) var fraction = 0.0
    @Published private(set) var bytesDone: Int64 = 0
    @Published private(set) var bytesTotal: Int64 = 0
    @Published private(set) var available: String?      // latest version on the server
    private var task: Task<Void, Never>?

    static let installDirectory = Store.directory.appendingPathComponent("Engine", isDirectory: true)
    static let appPath = installDirectory.appendingPathComponent("MacAC Engine.app").path

    var isBusy: Bool { state == .checking || state == .downloading || state == .installing }

    func cancel() { task?.cancel(); state = .idle }

    /// Looks up the latest engine version without installing anything.
    func check() async {
        if let m = try? await Self.fetchManifest() { available = m.version }
    }

    /// Installs (or updates to) the latest engine into `store.engine`.
    func install(store: Store) {
        guard !isBusy else { return }
        state = .checking
        task = Task { [weak self] in
            guard let self else { return }
            do {
                let manifest = try await Self.fetchManifest()
                available = manifest.version
                let rid = Distribution.runtimeIdentifier
                guard let build = manifest.builds.first(where: { $0.rid == rid }) else {
                    throw NSError(domain: "MacAC", code: 1, userInfo: [NSLocalizedDescriptionKey:
                        "No engine build is published for this Mac (\(rid))."])
                }
                if store.engine.hasInstalledEngine, store.engine.engineVersion == manifest.version {
                    state = .done; return
                }

                state = .downloading; fraction = 0; bytesDone = 0; bytesTotal = build.assetSize
                let url = Distribution.latestAsset(repo: Distribution.appRepo, build.asset)
                let zip = try await Downloader().download(url, expectedSize: build.assetSize) { done, total in
                    Task { @MainActor in
                        self.bytesDone = done; self.bytesTotal = total
                        self.fraction = total > 0 ? Double(done) / Double(total) : 0
                    }
                }
                defer { try? FileManager.default.removeItem(at: zip) }
                guard try Downloader.sha256(of: zip) == build.assetSha256 else {
                    throw Downloader.DownloadError.checksum(build.asset)
                }

                state = .installing
                let dir = Self.installDirectory
                try? FileManager.default.removeItem(at: dir)
                try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
                // ditto preserves the bundle's signature and permissions. --noqtn drops the
                // quarantine flag the download carried: it is inherited by every file in the
                // archive, and Gatekeeper kills a quarantined engine on spawn with no message and
                // no log. The bytes have already been checked against the SHA-256 in the manifest
                // above, so this is the launcher vouching for what it just verified.
                try await Self.run("/usr/bin/ditto", ["--noqtn", "-x", "-k", zip.path, dir.path])
                // The zip holds "MacAC Engine-<rid>.app"; normalise the name.
                if let found = try FileManager.default.contentsOfDirectory(atPath: dir.path)
                    .first(where: { $0.hasSuffix(".app") }), found != "MacAC Engine.app" {
                    try FileManager.default.moveItem(atPath: dir.path + "/" + found, toPath: Self.appPath)
                }
                guard FileManager.default.fileExists(atPath: Self.appPath + "/Contents/MacOS/MacAC") else {
                    throw NSError(domain: "MacAC", code: 2, userInfo: [NSLocalizedDescriptionKey:
                        "The engine archive did not contain MacAC Engine.app."])
                }
                store.engine.engineApp = Self.appPath
                store.engine.engineVersion = manifest.version
                state = .done
            } catch is CancellationError {
                state = .idle
            } catch {
                state = .failed(error.localizedDescription)
            }
        }
    }

    static func fetchManifest() async throws -> EngineManifest {
        let url = Distribution.latestAsset(repo: Distribution.appRepo, "engine-manifest.json")
        let (data, _) = try await URLSession.shared.data(from: url)
        return try JSONDecoder().decode(EngineManifest.self, from: data)
    }

    static func run(_ exe: String, _ args: [String]) async throws {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
            let p = Process(); p.executableURL = URL(fileURLWithPath: exe); p.arguments = args
            p.standardOutput = Pipe(); p.standardError = Pipe()
            p.terminationHandler = { proc in
                proc.terminationStatus == 0 ? c.resume()
                    : c.resume(throwing: NSError(domain: "MacAC", code: Int(proc.terminationStatus),
                        userInfo: [NSLocalizedDescriptionKey: "\(exe) failed (\(proc.terminationStatus))."]))
            }
            do { try p.run() } catch { c.resume(throwing: error) }
        }
    }
}
