using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScreenTune.Core;

namespace ScreenTune.Views;

/// <summary>
/// Label, slider and value, like the macOS SliderRow, plus − / + buttons for fine
/// steps. The buttons, arrow keys, mouse wheel and drag all move on the same grid.
/// </summary>
public sealed partial class SliderRow : UserControl
{
    private const int WheelNotch = 120;

    private bool _updating;
    private double _step = 0.01;
    private int _decimals = 2;
    private int _wheelDelta;
    private Func<double, string>? _format;

    public SliderRow()
    {
        InitializeComponent();
    }

    /// <summary>Raised only for user changes, never for <see cref="SetValue(double)"/>.</summary>
    public event Action<double>? ValueChanged;

    public string Title
    {
        get => TitleText.Text;
        set
        {
            TitleText.Text = value;
            AutomationProperties.SetName(Slider, value);
            AutomationProperties.SetName(DecreaseButton, $"Decrease {value}");
            AutomationProperties.SetName(IncreaseButton, $"Increase {value}");
        }
    }

    /// <summary>Colors the filled part of the track and the thumb. Set from XAML, before the slider loads.</summary>
    public Brush? Tint
    {
        get => null;
        set
        {
            if (value is null) return;
            foreach (var key in new[]
            {
                "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
                "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
            })
            {
                Slider.Resources[key] = value;
            }
        }
    }

    public double ValueWidth
    {
        get => ValueColumn.Width.Value;
        set => ValueColumn.Width = new GridLength(value);
    }

    /// <summary>Custom value text. Without one, the value shows as many decimals as one step needs.</summary>
    public Func<double, string>? Format
    {
        get => _format;
        set
        {
            _format = value;
            UpdateText();
        }
    }

    public double Value => Slider.Value;

    /// <summary>Range and fine step. Drag, keys, wheel and the − / + buttons all move by <paramref name="step"/>.</summary>
    public void SetRange(double minimum, double maximum, double step)
    {
        _updating = true;
        Slider.Minimum = minimum;
        Slider.Maximum = maximum;
        Slider.StepFrequency = step;
        Slider.SmallChange = step;
        Slider.LargeChange = (maximum - minimum) / 10;
        _updating = false;
        _step = step;
        _decimals = Nudge.Decimals(step);
        UpdateText();
    }

    public void SetValue(double value)
    {
        _updating = true;
        Slider.Value = value;
        _updating = false;
        UpdateText();
    }

    private void NudgeBy(int steps)
    {
        if (!Slider.IsEnabled) return;
        var target = Nudge.Apply(Slider.Value, _step, steps, new ValueRange(Slider.Minimum, Slider.Maximum));
        if (target != Slider.Value) Slider.Value = target;
    }

    private void OnDecreaseClick(object sender, RoutedEventArgs e) => NudgeBy(-1);

    private void OnIncreaseClick(object sender, RoutedEventArgs e) => NudgeBy(1);

    private void OnSliderWheel(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(Slider).Properties;
        if (properties.IsHorizontalMouseWheel || !Slider.IsEnabled) return;
        // Over a slider the wheel tunes it instead of scrolling the flyout. Touchpads send
        // small deltas, so they add up to whole notches.
        e.Handled = true;
        _wheelDelta += properties.MouseWheelDelta;
        var steps = _wheelDelta / WheelNotch;
        if (steps == 0) return;
        _wheelDelta -= steps * WheelNotch;
        NudgeBy(steps);
    }

    private void OnSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateText();
        // Rounded so float noise from the slider's own snapping never reaches saved settings.
        if (!_updating) ValueChanged?.Invoke(Math.Round(e.NewValue, _decimals));
    }

    private void UpdateText()
    {
        var value = Slider.Value;
        var text = _format?.Invoke(value) ?? value.ToString($"F{_decimals}");
        ValueText.Text = text;
        AutomationProperties.SetItemStatus(Slider, text);
        // Dimmed at the ends, so the buttons show when there is no further to go.
        DecreaseButton.IsEnabled = value > Slider.Minimum;
        IncreaseButton.IsEnabled = value < Slider.Maximum;
    }
}
