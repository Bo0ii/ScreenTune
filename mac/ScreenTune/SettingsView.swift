import SwiftUI
import AppKit
import ServiceManagement
import UniformTypeIdentifiers

// MARK: - Window manager

@MainActor
final class SettingsWindowManager {
    static let shared = SettingsWindowManager()
    private var window: NSWindow?

    func show(calibration: CalibrationModel) {
        if window == nil {
            let controller = NSHostingController(rootView: SettingsView(calibration: calibration))
            let win = NSWindow(contentViewController: controller)
            win.title = "ScreenTune Settings"
            win.styleMask = [.titled, .closable]
            win.isReleasedWhenClosed = false
            win.center()
            win.setFrameAutosaveName("ScreenTuneSettings")
            window = win
        }
        NSApp.activate()
        window?.makeKeyAndOrderFront(nil)
    }
}

// MARK: - Settings view

struct SettingsView: View {
    @ObservedObject var calibration: CalibrationModel
    @State private var profileMessage: String?
    @State private var loginStatus = SMAppService.mainApp.status
    @State private var loginError: String?

    var body: some View {
        Form {
            Section {
                LabeledContent("Screen", value: calibration.selectedScreen?.name ?? "None")
                LabeledContent("Profile") {
                    HStack(spacing: 8) {
                        Button("Import…", action: importProfile)
                        Button("Export…", action: exportProfile)
                    }
                }
                if let profileMessage {
                    Text(profileMessage)
                        .foregroundStyle(.secondary)
                }
            } header: {
                Text("Profiles")
            } footer: {
                Text("Import and export apply to the screen picked in the menu bar.")
                    .foregroundStyle(.secondary)
            }

            Section {
                Toggle("Launch at Login", isOn: loginBinding)
                if loginStatus == .requiresApproval {
                    LabeledContent("Needs approval in Login Items") {
                        Button("Open Login Items") { SMAppService.openSystemSettingsLoginItems() }
                    }
                }
                if let loginError {
                    Text(loginError)
                        .foregroundStyle(.red)
                }
            } header: {
                Text("General")
            } footer: {
                Text("Settings re-apply automatically after display changes, wake from sleep, and KVM switches.")
                    .foregroundStyle(.secondary)
            }

            Section("About") {
                LabeledContent("Version", value: Self.version)
            }
        }
        .formStyle(.grouped)
        .scrollDisabled(true)
        .frame(width: 460, height: 400)
        .onAppear { loginStatus = SMAppService.mainApp.status }
    }

    private static var version: String {
        let info = Bundle.main.infoDictionary
        let short = info?["CFBundleShortVersionString"] as? String ?? "?"
        let build = info?["CFBundleVersion"] as? String ?? "?"
        return "\(short) (\(build))"
    }

    // MARK: - Launch at Login

    private var loginBinding: Binding<Bool> {
        Binding(
            get: { loginStatus == .enabled || loginStatus == .requiresApproval },
            set: { on in
                do {
                    if on {
                        try SMAppService.mainApp.register()
                    } else {
                        try SMAppService.mainApp.unregister()
                    }
                    loginError = nil
                } catch {
                    loginError = "Couldn't change Launch at Login: \(error.localizedDescription)"
                }
                loginStatus = SMAppService.mainApp.status
            }
        )
    }

    // MARK: - Profiles

    private func exportProfile() {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        guard let data = try? encoder.encode(calibration.current) else { return }

        let panel = NSSavePanel()
        panel.nameFieldStringValue = "ScreenTune - \(calibration.selectedScreen?.name ?? "Screen").json"
        panel.allowedContentTypes = [.json]
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                try data.write(to: url)
                show("Exported \(url.lastPathComponent)")
            } catch {
                show("Export failed: \(error.localizedDescription)")
            }
        }
    }

    private func importProfile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.json]
        panel.allowsMultipleSelection = false
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                let data = try Data(contentsOf: url)
                let profile = try JSONDecoder().decode(DisplaySettings.self, from: data)
                calibration.importProfile(profile)
                show("Imported \(url.lastPathComponent)")
            } catch {
                show("Import failed: \(error.localizedDescription)")
            }
        }
    }

    private func show(_ message: String) {
        withAnimation { profileMessage = message }
        DispatchQueue.main.asyncAfter(deadline: .now() + 4) {
            withAnimation { profileMessage = nil }
        }
    }
}
