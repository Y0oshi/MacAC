import Foundation
import Combine

/// Fetches the game data manifest and installs any missing or changed file.
@MainActor
final class DataInstaller: ObservableObject {
    enum State: Equatable { case idle, checking, installing, done, failed(String) }

    @Published private(set) var state: State = .idle
    @Published private(set) var currentFile = ""
    @Published private(set) var fileIndex = 0
    @Published private(set) var fileCount = 0
    @Published private(set) var fraction = 0.0      // within the current file
    @Published private(set) var bytesDone: Int64 = 0
    @Published private(set) var bytesTotal: Int64 = 0
    private var task: Task<Void, Never>?

    var isBusy: Bool { state == .checking || state == .installing }

    func cancel() { task?.cancel(); state = .idle }

    /// Installs into `directory`, skipping files whose checksum already matches.
    func install(into directory: String) {
        guard !isBusy else { return }
        state = .checking
        task = Task { [weak self] in
            guard let self else { return }
            do {
                let manifest = try await Self.fetchManifest()
                let dir = URL(fileURLWithPath: directory, isDirectory: true)
                try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

                // Decide what needs doing.
                var pending: [DataFile] = []
                for f in manifest.files {
                    let dest = dir.appendingPathComponent(f.name)
                    if FileManager.default.fileExists(atPath: dest.path),
                       (try? FileManager.default.attributesOfItem(atPath: dest.path)[.size] as? Int64) == f.size,
                       (try? Downloader.sha256(of: dest)) == f.sha256 { continue }
                    pending.append(f)
                }
                fileCount = pending.count
                if pending.isEmpty { state = .done; return }
                state = .installing

                for (i, f) in pending.enumerated() {
                    try Task.checkCancellation()
                    fileIndex = i + 1; currentFile = f.name; fraction = 0
                    bytesDone = 0; bytesTotal = f.assetSize

                    let url = Distribution.latestAsset(repo: Distribution.dataRepo, f.asset)
                    let zip = try await Downloader().download(url, expectedSize: f.assetSize) { done, total in
                        Task { @MainActor in
                            self.bytesDone = done; self.bytesTotal = total
                            self.fraction = total > 0 ? Double(done) / Double(total) : 0
                        }
                    }
                    defer { try? FileManager.default.removeItem(at: zip) }

                    guard try Downloader.sha256(of: zip) == f.assetSha256 else {
                        throw Downloader.DownloadError.checksum(f.asset)
                    }
                    currentFile = "Unpacking \(f.name)"
                    try await Self.unzip(zip, member: f.name, into: dir)
                    let dest = dir.appendingPathComponent(f.name)
                    guard try Downloader.sha256(of: dest) == f.sha256 else {
                        try? FileManager.default.removeItem(at: dest)
                        throw Downloader.DownloadError.checksum(f.name)
                    }
                }
                state = .done
            } catch is CancellationError {
                state = .idle
            } catch {
                state = .failed(error.localizedDescription)
            }
        }
    }

    static func fetchManifest() async throws -> DataManifest {
        let url = Distribution.latestAsset(repo: Distribution.dataRepo, "manifest.json")
        let (data, _) = try await URLSession.shared.data(from: url)
        return try JSONDecoder().decode(DataManifest.self, from: data)
    }

    /// Uses the system unzip so multi-hundred-MB archives stream to disk.
    static func unzip(_ zip: URL, member: String, into dir: URL) async throws {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
            p.arguments = ["-o", "-q", zip.path, member, "-d", dir.path]
            p.standardOutput = Pipe(); p.standardError = Pipe()
            p.terminationHandler = { proc in
                proc.terminationStatus == 0
                    ? c.resume()
                    : c.resume(throwing: NSError(domain: "MacAC", code: Int(proc.terminationStatus),
                        userInfo: [NSLocalizedDescriptionKey: "Could not unpack \(member)."]))
            }
            do { try p.run() } catch { c.resume(throwing: error) }
        }
    }
}
