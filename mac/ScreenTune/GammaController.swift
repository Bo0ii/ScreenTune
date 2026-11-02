import CoreGraphics

/// Writes per-display transfer tables and re-writes them when macOS resets them.
@MainActor
final class GammaController {
    private struct Table {
        let red: [CGGammaValue]
        let green: [CGGammaValue]
        let blue: [CGGammaValue]
    }

    private var applied: [CGDirectDisplayID: Table] = [:]
    private static let driftTolerance: CGGammaValue = 0.02

    var hasAppliedTables: Bool { !applied.isEmpty }

    /// Identity settings leave the display untouched so Night Shift, True Tone and
    /// the display's own ColorSync calibration keep working on untuned screens.
    func apply(_ settings: DisplaySettings, to display: CGDirectDisplayID) {
        guard !settings.isToneIdentity else {
            if applied.removeValue(forKey: display) != nil {
                restoreSystemTables()
            }
            return
        }
        let t = settings.transferTable(size: 256)
        let table = Table(red: t.red, green: t.green, blue: t.blue)
        applied[display] = table
        write(table, to: display)
    }

    func removeAll() {
        guard !applied.isEmpty else { return }
        applied.removeAll()
        CGDisplayRestoreColorSyncSettings()
    }

    func retain(only displays: Set<CGDirectDisplayID>) {
        applied = applied.filter { displays.contains($0.key) }
    }

    func reapplyAll() {
        for (display, table) in applied {
            write(table, to: display)
        }
    }

    func reapplyDrifted() {
        for (display, table) in applied where hasDrifted(table, on: display) {
            write(table, to: display)
        }
    }

    // There is no per-display restore call, so restore everything and put back
    // the tables that are still wanted.
    private func restoreSystemTables() {
        CGDisplayRestoreColorSyncSettings()
        reapplyAll()
    }

    private func write(_ table: Table, to display: CGDirectDisplayID) {
        CGSetDisplayTransferByTable(display, UInt32(table.red.count), table.red, table.green, table.blue)
    }

    private func hasDrifted(_ table: Table, on display: CGDirectDisplayID) -> Bool {
        let n = table.red.count
        var r = [CGGammaValue](repeating: 0, count: n)
        var g = [CGGammaValue](repeating: 0, count: n)
        var b = [CGGammaValue](repeating: 0, count: n)
        var count: UInt32 = 0
        guard CGGetDisplayTransferByTable(display, UInt32(n), &r, &g, &b, &count) == .success,
              count == UInt32(n) else { return false }
        for i in 0..<n where abs(r[i] - table.red[i]) > Self.driftTolerance
            || abs(g[i] - table.green[i]) > Self.driftTolerance
            || abs(b[i] - table.blue[i]) > Self.driftTolerance {
            return true
        }
        return false
    }
}
