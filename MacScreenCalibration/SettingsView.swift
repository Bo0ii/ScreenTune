import SwiftUI
import AppKit
import ServiceManagement

// MARK: - Window manager

@MainActor
final class SettingsWindowManager {
    static let shared = SettingsWindowManager()
    private var window: NSWindow?

    func show(calibration: CalibrationModel) {
        if let w = window, w.isVisible {
            w.makeKeyAndOrderFront(nil)
            NSApp.activate(ignoringOtherApps: true)
            return
        }
        let win = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 380, height: 0),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        win.title = "Display Filter — Settings"
        win.isReleasedWhenClosed = false
        win.contentView = NSHostingView(rootView: SettingsView(calibration: calibration))
        win.center()
        win.setFrameAutosaveName("DisplayFilterSettings")
        window = win
        win.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }
}

// MARK: - Settings view

struct SettingsView: View {
    @ObservedObject var calibration: CalibrationModel
    @State private var statusMessage: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {

            // ── PROFILES ─────────────────────────────────────────────────
            sectionHeader("Profiles")

            VStack(alignment: .leading, spacing: 6) {
                Button {
                    exportProfile()
                } label: {
                    Label("Export Profile…", systemImage: "arrow.up.doc")
                }
                .buttonStyle(.bordered)

                Button {
                    importProfile()
                } label: {
                    Label("Import Profile…", systemImage: "arrow.down.doc")
                }
                .buttonStyle(.bordered)

                if let msg = statusMessage {
                    Text(msg)
                        .font(.system(size: 11))
                        .foregroundColor(.secondary)
                        .transition(.opacity)
                }
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 12)

            Divider()

            // ── STARTUP ──────────────────────────────────────────────────
            sectionHeader("Startup")

            VStack(alignment: .leading, spacing: 4) {
                Toggle("Launch at Login", isOn: loginBinding)
                Text("Filter is applied automatically when the app launches.")
                    .font(.system(size: 11))
                    .foregroundColor(.secondary)
            }
            .padding(.horizontal, 16)
            .padding(.bottom, 12)

            Divider()

            // ── DISPLAY ──────────────────────────────────────────────────
            sectionHeader("Display")

            Text("Settings re-apply automatically after a KVM switch, display configuration change, or system wake from sleep.")
                .font(.system(size: 11))
                .foregroundColor(.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.horizontal, 16)
                .padding(.bottom, 12)

            Divider()

            // ── ABOUT ────────────────────────────────────────────────────
            sectionHeader("About")

            Text("Display Filter 1.0")
                .font(.system(size: 12))
                .padding(.horizontal, 16)
                .padding(.bottom, 16)
        }
        .frame(width: 380)
        .padding(.top, 4)
    }

    // MARK: - Section header

    @ViewBuilder
    private func sectionHeader(_ title: String) -> some View {
        Text(title.uppercased())
            .font(.system(size: 10, weight: .semibold))
            .foregroundColor(.secondary)
            .padding(.horizontal, 16)
            .padding(.top, 12)
            .padding(.bottom, 6)
    }

    // MARK: - Launch at Login binding

    private var loginBinding: Binding<Bool> {
        Binding<Bool>(
            get: { SMAppService.mainApp.status == .enabled },
            set: { on in
                do {
                    if on {
                        try SMAppService.mainApp.register()
                    } else {
                        try SMAppService.mainApp.unregister()
                    }
                } catch {
                    // Toggle snaps back on failure — computed binding handles this automatically
                }
            }
        )
    }

    // MARK: - Export

    private func exportProfile() {
        let profile = calibration.exportProfile()
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(profile) else { return }

        let panel = NSSavePanel()
        panel.nameFieldStringValue = "DisplayFilter.json"
        panel.allowedContentTypes = [.json]
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                try data.write(to: url)
                withAnimation {
                    statusMessage = "Exported to \(url.lastPathComponent)"
                }
                DispatchQueue.main.asyncAfter(deadline: .now() + 3) {
                    withAnimation { statusMessage = nil }
                }
            } catch {
                withAnimation { statusMessage = "Export failed: \(error.localizedDescription)" }
            }
        }
    }

    // MARK: - Import

    private func importProfile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        panel.allowsMultipleSelection = false
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                let data = try Data(contentsOf: url)
                let profile = try JSONDecoder().decode(CalibrationProfile.self, from: data)
                calibration.importProfile(profile)
                withAnimation {
                    statusMessage = "Imported \(url.lastPathComponent)"
                }
                DispatchQueue.main.asyncAfter(deadline: .now() + 3) {
                    withAnimation { statusMessage = nil }
                }
            } catch {
                withAnimation { statusMessage = "Import failed: \(error.localizedDescription)" }
            }
        }
    }
}
