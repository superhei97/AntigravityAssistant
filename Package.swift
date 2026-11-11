// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "AntigravityAssistant",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .executable(
            name: "AntigravityAssistant",
            targets: ["AntigravityAssistant"]
        )
    ],
    targets: [
        .executableTarget(
            name: "AntigravityAssistant",
            path: "Sources/AntigravityAssistant",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("ApplicationServices"),
                .linkedFramework("SwiftUI"),
                .linkedFramework("Carbon"),
                .linkedFramework("UserNotifications"),
                .linkedLibrary("sqlite3")
            ]
        )
    ]
)
