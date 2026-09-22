// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "MacACLauncher",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "MacACLauncher",
            path: "Sources/MacACLauncher",
            swiftSettings: [.unsafeFlags(["-parse-as-library"])]
        )
    ],
    swiftLanguageVersions: [.v5]
)
