import SwiftUI

@main
struct ScreenTuneApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        // The menu bar panel is AppKit (StatusBarController); SwiftUI needs a scene.
        Settings { EmptyView() }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var calibration: CalibrationModel?
    private var statusBar: StatusBarController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let calibration = CalibrationModel()
        self.calibration = calibration
        statusBar = StatusBarController(calibration: calibration)
    }
}
