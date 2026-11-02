import SwiftUI

struct ContentView: View {
    @ObservedObject var calibration: CalibrationModel
    var dismiss: () -> Void = {}
    @State private var point: TonePoint = .middle

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            header

            if calibration.screens.count > 1 {
                screenList
            }

            VStack(alignment: .leading, spacing: 14) {
                Card("Tone") {
                    Picker("Tone point", selection: $point) {
                        ForEach(TonePoint.allCases) { Text($0.label).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()

                    SliderRow("Red", value: binding(point.keyPath(.red)), range: point.range, tint: .red)
                    SliderRow("Green", value: binding(point.keyPath(.green)), range: point.range, tint: .green)
                    SliderRow("Blue", value: binding(point.keyPath(.blue)), range: point.range, tint: .blue)
                }

                Card("Adjust") {
                    SliderRow("Warmth", value: warmthBinding, range: DisplaySettings.temperatureRange,
                              step: Nudge.warmth, tint: .orange, valueWidth: 46) { value in
                        "\(Self.kelvin(fromSlider: value))K"
                    }
                    SliderRow("Gamma", value: binding(\.gamma), range: DisplaySettings.gammaRange)
                    SliderRow("Contrast", value: binding(\.contrast), range: DisplaySettings.contrastRange)
                    SliderRow("Saturation", value: binding(\.saturation), range: DisplaySettings.saturationRange)
                        .disabled(!calibration.saturationSupported)
                    if !calibration.saturationSupported {
                        Text("Saturation isn't available on this version of macOS.")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                Card("Preview") {
                    PreviewPatterns()
                }
            }
            .disabled(!calibration.isEnabled)
            .opacity(calibration.isEnabled ? 1 : 0.45)
            .animation(.easeOut(duration: 0.2), value: calibration.isEnabled)

            Divider()

            actions
        }
        .padding(14)
        .frame(width: 340)
    }

    // MARK: - Sections

    private var header: some View {
        HStack(spacing: 8) {
            Text("ScreenTune")
                .font(.system(size: 13, weight: .bold))
            Spacer()
            CompareButton(isComparing: $calibration.isComparing)
                .disabled(!calibration.isEnabled)
            Toggle("Tuning", isOn: $calibration.isEnabled)
                .toggleStyle(.switch)
                .controlSize(.small)
                .labelsHidden()
                .help(calibration.isEnabled ? "Turn tuning off" : "Turn tuning on")
        }
    }

    private var screenList: some View {
        VStack(alignment: .leading, spacing: 6) {
            SectionTitle("Screen")
            VStack(spacing: 0) {
                ForEach(calibration.screens) { screen in
                    MenuRow {
                        calibration.selectedScreenID = screen.id
                    } label: {
                        Image(systemName: screen.isBuiltIn ? "laptopcomputer" : "display")
                            .foregroundStyle(.secondary)
                            .frame(width: 20)
                        Text(screen.name)
                            .lineLimit(1)
                        Spacer()
                        if screen.id == calibration.selectedScreenID {
                            Image(systemName: "checkmark")
                                .font(.system(size: 11, weight: .semibold))
                        }
                    }
                    .accessibilityAddTraits(screen.id == calibration.selectedScreenID ? .isSelected : [])
                }
            }
            .padding(.horizontal, -6)
        }
    }

    private var actions: some View {
        VStack(spacing: 0) {
            MenuRow(action: calibration.resetCurrent) {
                Text(resetTitle)
            }
            .disabled(calibration.current == DisplaySettings())

            MenuRow {
                dismiss()
                SettingsWindowManager.shared.show(calibration: calibration)
            } label: {
                Text("Settings…")
                Spacer()
                Text("⌘,").foregroundStyle(.secondary)
            }
            .keyboardShortcut(",")

            MenuRow {
                NSApplication.shared.terminate(nil)
            } label: {
                Text("Quit ScreenTune")
                Spacer()
                Text("⌘Q").foregroundStyle(.secondary)
            }
            .keyboardShortcut("q")
        }
        .padding(.horizontal, -6)
        .padding(.top, -4)
    }

    private var resetTitle: String {
        guard calibration.screens.count > 1, let name = calibration.selectedScreen?.name else {
            return "Reset to Defaults"
        }
        return "Reset \(name)"
    }

    // MARK: - Bindings

    private func binding(_ keyPath: WritableKeyPath<DisplaySettings, Double>) -> Binding<Double> {
        Binding(
            get: { calibration.current[keyPath: keyPath] },
            set: { calibration.current[keyPath: keyPath] = $0 }
        )
    }

    // The slider runs left to right from neutral to warm, so it is the Kelvin value mirrored.
    private var warmthBinding: Binding<Double> {
        let range = DisplaySettings.temperatureRange
        return Binding(
            get: { range.lowerBound + range.upperBound - calibration.current.temperature },
            set: { calibration.current.temperature = range.lowerBound + range.upperBound - $0 }
        )
    }

    // Exact Kelvin: warmth moves in 1K steps, like the Windows app.
    private static func kelvin(fromSlider value: Double) -> Int {
        let range = DisplaySettings.temperatureRange
        return Int((range.lowerBound + range.upperBound - value).rounded())
    }
}

// MARK: - Building blocks

private struct SectionTitle: View {
    let title: String
    init(_ title: String) { self.title = title }

    var body: some View {
        Text(title)
            .font(.subheadline.weight(.semibold))
            .foregroundStyle(.secondary)
    }
}

private struct Card<Content: View>: View {
    let title: String
    @ViewBuilder let content: Content

    init(_ title: String, @ViewBuilder content: () -> Content) {
        self.title = title
        self.content = content()
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            SectionTitle(title)
            VStack(alignment: .leading, spacing: 10) {
                content
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(.fill.quaternary, in: .rect(cornerRadius: 12, style: .continuous))
        }
    }
}

/// Label, - / slider / +, value. Same fine-tune behavior as the Windows app: the
/// buttons move one step (0.001, warmth 1K) and repeat while held, and drags land
/// on the same grid, so the number shown is the exact value.
private struct SliderRow: View {
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    let step: Double
    let tint: Color?
    let valueWidth: CGFloat
    let valueText: (Double) -> String

    init(
        _ title: String,
        value: Binding<Double>,
        range: ClosedRange<Double>,
        step: Double = Nudge.fine,
        tint: Color? = nil,
        valueWidth: CGFloat = 40,
        valueText: ((Double) -> String)? = nil
    ) {
        self.title = title
        self._value = value
        self.range = range
        self.step = step
        self.tint = tint
        self.valueWidth = valueWidth
        let decimals = Nudge.decimals(step)
        self.valueText = valueText ?? { String(format: "%.\(decimals)f", $0) }
    }

    var body: some View {
        HStack(spacing: 6) {
            Text(title)
                .frame(width: 70, alignment: .leading)
            NudgeButton(systemImage: "minus", label: "Decrease \(title)") { nudge(-1) }
                .disabled(value <= range.lowerBound)
            Slider(value: snapped, in: range)
                .tint(tint)
                .accessibilityLabel(title)
                .accessibilityValue(valueText(value))
            NudgeButton(systemImage: "plus", label: "Increase \(title)") { nudge(1) }
                .disabled(value >= range.upperBound)
            Text(valueText(value))
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .frame(width: valueWidth, alignment: .trailing)
        }
    }

    // Snapping in the binding (not Slider's `step:`) keeps the track free of tick marks.
    private var snapped: Binding<Double> {
        Binding(
            get: { value },
            set: { value = Nudge.apply($0, step: step, steps: 0, range: range) }
        )
    }

    private func nudge(_ steps: Int) {
        value = Nudge.apply(value, step: step, steps: steps, range: range)
    }
}

/// The small - / + beside each slider. Holding it repeats, like the Windows app.
private struct NudgeButton: View {
    let systemImage: String
    let label: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: systemImage)
                .font(.system(size: 9, weight: .bold))
                .frame(width: 18, height: 20)
                .contentShape(.rect)
        }
        .buttonStyle(NudgeButtonStyle())
        .buttonRepeatBehavior(.enabled)
        .accessibilityLabel(label)
    }

    private struct NudgeButtonStyle: ButtonStyle {
        func makeBody(configuration: Configuration) -> some View {
            NudgeButtonBody(configuration: configuration)
        }
    }

    private struct NudgeButtonBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hovering = false
        @Environment(\.isEnabled) private var isEnabled

        var body: some View {
            configuration.label
                .foregroundStyle(!isEnabled ? AnyShapeStyle(.tertiary)
                                 : configuration.isPressed ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                .background(highlight, in: .rect(cornerRadius: 5, style: .continuous))
                .onHover { hovering = $0 }
        }

        private var highlight: AnyShapeStyle {
            guard isEnabled else { return AnyShapeStyle(.clear) }
            if configuration.isPressed { return AnyShapeStyle(.fill.secondary) }
            if hovering { return AnyShapeStyle(.fill.tertiary) }
            return AnyShapeStyle(.clear)
        }
    }
}

/// A full-width row that looks and behaves like a menu item: hover and press
/// highlights, dimmed when disabled.
private struct MenuRow<Label: View>: View {
    let action: () -> Void
    @ViewBuilder let label: Label

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) { label }
                .padding(.horizontal, 8)
                .frame(maxWidth: .infinity, minHeight: 26, alignment: .leading)
                .contentShape(.rect)
        }
        .buttonStyle(MenuRowStyle())
    }
}

private struct MenuRowStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        MenuRowBody(configuration: configuration)
    }

    private struct MenuRowBody: View {
        let configuration: ButtonStyleConfiguration
        @State private var hovering = false
        @Environment(\.isEnabled) private var isEnabled

        var body: some View {
            configuration.label
                .foregroundStyle(isEnabled ? .primary : .tertiary)
                .background(highlight, in: .rect(cornerRadius: 7, style: .continuous))
                .onHover { hovering = $0 }
        }

        private var highlight: AnyShapeStyle {
            guard isEnabled else { return AnyShapeStyle(.clear) }
            if configuration.isPressed { return AnyShapeStyle(.fill.secondary) }
            if hovering { return AnyShapeStyle(.fill.tertiary) }
            return AnyShapeStyle(.clear)
        }
    }
}

/// Shows the original colors only while held down.
private struct CompareButton: View {
    @Binding var isComparing: Bool

    var body: some View {
        Button {} label: {
            Image(systemName: isComparing ? "eye.fill" : "eye")
                .font(.system(size: 13, weight: .medium))
                .frame(width: 28, height: 22)
                .contentShape(.rect)
        }
        .buttonStyle(HoldButtonStyle { isComparing = $0 })
        .help("Hold to see the original colors")
        .accessibilityLabel("Compare with original colors")
    }

    private struct HoldButtonStyle: ButtonStyle {
        let onPressChange: (Bool) -> Void

        func makeBody(configuration: Configuration) -> some View {
            HoldButtonBody(configuration: configuration, onPressChange: onPressChange)
        }
    }

    private struct HoldButtonBody: View {
        let configuration: ButtonStyleConfiguration
        let onPressChange: (Bool) -> Void
        @State private var hovering = false
        @Environment(\.isEnabled) private var isEnabled

        var body: some View {
            configuration.label
                .foregroundStyle(!isEnabled ? AnyShapeStyle(.tertiary)
                                 : configuration.isPressed ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                .background(highlight, in: .rect(cornerRadius: 6, style: .continuous))
                .onHover { hovering = $0 }
                .onChange(of: configuration.isPressed) { _, pressed in onPressChange(pressed) }
        }

        private var highlight: AnyShapeStyle {
            guard isEnabled else { return AnyShapeStyle(.clear) }
            if configuration.isPressed { return AnyShapeStyle(.fill.secondary) }
            if hovering { return AnyShapeStyle(.fill.tertiary) }
            return AnyShapeStyle(.clear)
        }
    }
}

/// Plain reference patterns. They are drawn without any processing: the screen's own
/// correction is what you judge them by. Drawing them pre-corrected (as older versions
/// did) applied the correction twice.
private struct PreviewPatterns: View {
    private let swatches: [Color] = [.white, .red, .green, .blue, .black]

    var body: some View {
        VStack(spacing: 6) {
            Canvas { context, size in
                let steps = max(Int(size.width), 2)
                for x in 0..<steps {
                    let level = Double(x) / Double(steps - 1)
                    context.fill(
                        Path(CGRect(x: CGFloat(x), y: 0, width: 1, height: size.height)),
                        with: .color(Color(.sRGB, white: level))
                    )
                }
            }
            .frame(height: 18)
            .clipShape(.rect(cornerRadius: 6, style: .continuous))
            .accessibilityHidden(true)

            HStack(spacing: 6) {
                ForEach(swatches.indices, id: \.self) { i in
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(swatches[i])
                        .frame(height: 26)
                }
            }
            .accessibilityHidden(true)
        }
    }
}

// MARK: - Tone points

enum TonePoint: String, CaseIterable, Identifiable {
    case black, middle, white

    var id: String { rawValue }

    var label: String {
        switch self {
        case .black: "Black"
        case .middle: "Middle"
        case .white: "White"
        }
    }

    var range: ClosedRange<Double> {
        switch self {
        case .black: DisplaySettings.blackRange
        case .middle: DisplaySettings.midRange
        case .white: DisplaySettings.whiteRange
        }
    }

    func keyPath(_ channel: DisplaySettings.Channel) -> WritableKeyPath<DisplaySettings, Double> {
        switch (self, channel) {
        case (.black, .red): \.blackRed
        case (.black, .green): \.blackGreen
        case (.black, .blue): \.blackBlue
        case (.middle, .red): \.midRed
        case (.middle, .green): \.midGreen
        case (.middle, .blue): \.midBlue
        case (.white, .red): \.whiteRed
        case (.white, .green): \.whiteGreen
        case (.white, .blue): \.whiteBlue
        }
    }
}
