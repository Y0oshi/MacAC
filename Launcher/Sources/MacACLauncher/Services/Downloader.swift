import Foundation
import CryptoKit

/// Streams a URL to disk, reporting progress, then verifies its SHA-256.
final class Downloader: NSObject, URLSessionDownloadDelegate, @unchecked Sendable {
    typealias Progress = @Sendable (Int64, Int64) -> Void

    private var continuation: CheckedContinuation<URL, Error>?
    private var onProgress: Progress?
    private var session: URLSession!
    private var expectedTotal: Int64 = 0

    static func sha256(of url: URL) throws -> String {
        let h = try FileHandle(forReadingFrom: url)
        defer { try? h.close() }
        var hasher = SHA256()
        while let chunk = try h.read(upToCount: 8 * 1024 * 1024), !chunk.isEmpty { hasher.update(data: chunk) }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    /// Downloads `url` to a temporary file and returns it. The caller moves it.
    func download(_ url: URL, expectedSize: Int64, progress: @escaping Progress) async throws -> URL {
        expectedTotal = expectedSize
        onProgress = progress
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForRequest = 60
        cfg.timeoutIntervalForResource = 6 * 60 * 60
        session = URLSession(configuration: cfg, delegate: self, delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }
        return try await withCheckedThrowingContinuation { c in
            continuation = c
            session.downloadTask(with: url).resume()
        }
    }

    func urlSession(_ s: URLSession, downloadTask: URLSessionDownloadTask, didWriteData: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        let total = totalBytesExpectedToWrite > 0 ? totalBytesExpectedToWrite : expectedTotal
        onProgress?(totalBytesWritten, total)
    }

    func urlSession(_ s: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        // The file is deleted when this returns, so move it somewhere stable first.
        let keep = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        do {
            try FileManager.default.moveItem(at: location, to: keep)
            if let http = downloadTask.response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                continuation?.resume(throwing: DownloadError.http(http.statusCode))
            } else {
                continuation?.resume(returning: keep)
            }
        } catch { continuation?.resume(throwing: error) }
        continuation = nil
    }

    func urlSession(_ s: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if let error { continuation?.resume(throwing: error); continuation = nil }
    }

    enum DownloadError: LocalizedError {
        case http(Int), checksum(String)
        var errorDescription: String? {
            switch self {
            case .http(let c): return "The server answered HTTP \(c)."
            case .checksum(let f): return "\(f) failed verification. The download may be corrupt."
            }
        }
    }
}
