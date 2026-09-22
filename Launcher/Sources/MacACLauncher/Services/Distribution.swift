import Foundation

/// Where MacAC downloads its pieces from. Only the launcher knows these.
enum Distribution {
    /// Game data (retail DAT files), published as release assets.
    static let dataRepo = "Ac1container/ac_container"
    /// The MacAC launcher and engine builds.
    static let appRepo = "y0oshi/MacAC"

    static func latestAsset(repo: String, _ name: String) -> URL {
        // MACAC_RELEASE_BASE lets a developer test against a local server.
        if let base = ProcessInfo.processInfo.environment["MACAC_RELEASE_BASE"], !base.isEmpty {
            return URL(string: "\(base)/\(name)")!
        }
        return URL(string: "https://github.com/\(repo)/releases/latest/download/\(name)")!
    }

    /// The .NET runtime identifier for this Mac's CPU, so we fetch the right engine.
    static var runtimeIdentifier: String {
        var u = utsname(); uname(&u)
        let machine = withUnsafePointer(to: &u.machine) {
            $0.withMemoryRebound(to: CChar.self, capacity: 256) { String(cString: $0) }
        }
        return machine == "arm64" ? "osx-arm64" : "osx-x64"
    }
}

/// One engine build the engine manifest describes.
struct EngineBuild: Decodable {
    let rid: String
    let asset: String
    let assetSize: Int64
    let assetSha256: String
}

struct EngineManifest: Decodable {
    let version: String
    let builds: [EngineBuild]
}

/// One file the data manifest describes.
struct DataFile: Decodable {
    let name: String        // extracted file name
    let asset: String       // release asset (zip)
    let assetSize: Int64
    let assetSha256: String
    let size: Int64
    let sha256: String
}

struct DataManifest: Decodable {
    let version: Int
    let kind: String
    let files: [DataFile]
}
