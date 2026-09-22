import SwiftUI

/// MacAC's look: deep night sky with a portal-gold accent.
enum Theme {
    static let gold = Color(red: 0.93, green: 0.72, blue: 0.29)
    static let ember = Color(red: 0.86, green: 0.36, blue: 0.22)
    static let sky = Color(red: 0.36, green: 0.62, blue: 0.93)
    static let mist = Color.white.opacity(0.06)
}

/// A small capsule label used for server type / status.
struct Pill: View {
    let text: String
    var tint: Color = .secondary
    var body: some View {
        Text(text)
            .font(.caption2.weight(.semibold))
            .padding(.horizontal, 7).padding(.vertical, 3)
            .background(tint.opacity(0.18), in: Capsule())
            .foregroundStyle(tint)
    }
}

/// A live-population indicator.
struct PopulationView: View {
    let count: Int?
    var body: some View {
        HStack(spacing: 4) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(count.map { "\($0) online" } ?? "—")
                .font(.caption).foregroundStyle(.secondary).monospacedDigit()
        }
    }
    private var color: Color {
        guard let c = count else { return .gray }
        return c > 0 ? .green : .gray
    }
}

struct SectionHeader: View {
    let title: String
    var subtitle: String? = nil
    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(title).font(.title2.weight(.semibold))
            if let s = subtitle { Text(s).font(.callout).foregroundStyle(.secondary) }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.bottom, 6)
    }
}
