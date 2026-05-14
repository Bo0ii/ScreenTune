import SwiftUI

@main
struct MacScreenCalibrationApp: App {
    @StateObject private var calibration = CalibrationModel()

    var body: some Scene {
        MenuBarExtra("Display Filter", systemImage: "slider.horizontal.3") {
            ContentView(calibration: calibration)
        }
        .menuBarExtraStyle(.window)
    }
}
