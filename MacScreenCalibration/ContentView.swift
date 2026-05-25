import SwiftUI

struct ContentView: View {
    @ObservedObject var calibration: CalibrationModel
    @State private var selectedPoint: CalibrationPoint = .middle

    var body: some View {
        VStack(spacing: 0) {

            // ── Header ──────────────────────────────────────────────────
            HStack {
                Image(systemName: "slider.horizontal.3")
                    .foregroundColor(.accentColor)
                    .imageScale(.medium)
                Text("ScreenTune")
                    .font(.system(size: 13, weight: .semibold))
                Spacer()
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)

            Divider()

            // ── TONE section ─────────────────────────────────────────────
            sectionHeader("Tone")

            Picker("", selection: $selectedPoint) {
                ForEach(CalibrationPoint.allCases) { p in Text(p.label).tag(p) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .padding(.horizontal, 14)
            .padding(.bottom, 8)

            VStack(spacing: 5) {
                row("Red",   .red,   calibrationBinding(.red),   rgbRange)
                row("Green", .green, calibrationBinding(.green), rgbRange)
                row("Blue",  .blue,  calibrationBinding(.blue),  rgbRange)
            }
            .padding(.horizontal, 14)
            .padding(.bottom, 10)

            Divider()

            // ── GLOBAL section ───────────────────────────────────────────
            sectionHeader("Global")

            VStack(spacing: 5) {
                row("Gamma",      .primary, $calibration.gamma,      0.5...3.5)
                row("Contrast",   .orange,  $calibration.contrast,   0.0...2.0)
                row("Saturation", .purple,  $calibration.saturation, 0.0...2.0)
            }
            .padding(.horizontal, 14)
            .padding(.bottom, 10)

            Divider()

            // ── PREVIEW section ──────────────────────────────────────────
            sectionHeader("Preview")

            rampView
                .padding(.horizontal, 14)
                .padding(.bottom, 5)

            swatchRow
                .padding(.horizontal, 14)
                .padding(.bottom, 10)

            Divider()

            // ── Buttons ──────────────────────────────────────────────────
            HStack(spacing: 6) {
                Button("Reset") { calibration.reset() }
                    .buttonStyle(.bordered)
                    .controlSize(.small)

                Spacer()

                Button { calibration.applyAll() } label: {
                    Image(systemName: "arrow.clockwise")
                }
                .buttonStyle(.bordered)
                .controlSize(.small)
                .help("Force re-apply calibration")

                Button { SettingsWindowManager.shared.show(calibration: calibration) } label: {
                    Image(systemName: "gearshape")
                }
                .buttonStyle(.bordered)
                .controlSize(.small)

                Button("Quit") { NSApplication.shared.terminate(nil) }
                    .buttonStyle(.bordered)
                    .controlSize(.small)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
        }
        .frame(width: 320)
        .background(.regularMaterial)
    }

    // MARK: - Section header

    @ViewBuilder
    func sectionHeader(_ title: String) -> some View {
        HStack {
            Text(title.uppercased())
                .font(.system(size: 10, weight: .semibold))
                .foregroundColor(.secondary)
            Spacer()
        }
        .padding(.horizontal, 14)
        .padding(.top, 8)
        .padding(.bottom, 3)
    }

    // MARK: - Slider row

    @ViewBuilder
    func row(_ label: String, _ color: Color, _ value: Binding<Double>, _ range: ClosedRange<Double>) -> some View {
        HStack(spacing: 8) {
            Text(label)
                .font(.system(size: 12, weight: .medium))
                .foregroundColor(color)
                .frame(width: 70, alignment: .leading)
            Slider(value: value, in: range)
            Text(String(format: "%.2f", value.wrappedValue))
                .font(.system(size: 12).monospacedDigit())
                .foregroundColor(.secondary)
                .frame(width: 38, alignment: .trailing)
        }
    }

    // MARK: - Grayscale ramp

    @ViewBuilder
    var rampView: some View {
        Canvas { ctx, size in
            let steps = Int(size.width)
            guard steps > 0 else { return }
            for x in 0..<steps {
                let t = Double(x) / Double(steps - 1)
                let v = calibration.applyGray(t)
                ctx.fill(
                    Path(CGRect(x: CGFloat(x), y: 0, width: 1, height: size.height)),
                    with: .color(Color(white: v))
                )
            }
        }
        .frame(height: 18)
        .clipShape(RoundedRectangle(cornerRadius: 4))
        .overlay(RoundedRectangle(cornerRadius: 4).strokeBorder(Color.primary.opacity(0.1), lineWidth: 0.5))
    }

    // MARK: - Swatch row

    @ViewBuilder
    var swatchRow: some View {
        let swatches: [(Color, String)] = [
            (.white, "W"), (.red, "R"), (.green, "G"), (.blue, "B"), (.black, "Bk")
        ]
        HStack(spacing: 4) {
            ForEach(0..<swatches.count, id: \.self) { i in
                ZStack {
                    RoundedRectangle(cornerRadius: 4)
                        .fill(calibration.apply(to: swatches[i].0))
                    RoundedRectangle(cornerRadius: 4)
                        .strokeBorder(Color.primary.opacity(0.12), lineWidth: 0.5)
                }
                .frame(height: 30)
            }
        }
    }

    // MARK: - Helpers

    var rgbRange: ClosedRange<Double> {
        switch selectedPoint {
        case .black:  return 0.0...0.5
        case .middle: return 0.5...1.5
        case .white:  return 0.5...1.5
        }
    }

    func calibrationBinding(_ channel: RGBChannel) -> Binding<Double> {
        switch selectedPoint {
        case .black:
            switch channel {
            case .red: return $calibration.blackRed
            case .green: return $calibration.blackGreen
            case .blue: return $calibration.blackBlue
            }
        case .middle:
            switch channel {
            case .red: return $calibration.midRed
            case .green: return $calibration.midGreen
            case .blue: return $calibration.midBlue
            }
        case .white:
            switch channel {
            case .red: return $calibration.whiteRed
            case .green: return $calibration.whiteGreen
            case .blue: return $calibration.whiteBlue
            }
        }
    }
}

// MARK: - Enums

enum CalibrationPoint: String, CaseIterable, Identifiable {
    case black, middle, white
    var id: String { rawValue }
    var label: String {
        switch self {
        case .black:  return "Black"
        case .middle: return "Middle"
        case .white:  return "White"
        }
    }
}

enum RGBChannel { case red, green, blue }

#Preview {
    ContentView(calibration: CalibrationModel())
}
