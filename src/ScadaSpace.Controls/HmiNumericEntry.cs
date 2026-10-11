using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using ScadaSpace.Core;
using Windows.System;

namespace ScadaSpace.Controls;

public sealed record NumericValueChangedEventArgs(double OldValue, double NewValue);

/// <summary>Finite numeric editor using native text/step controls and round-trip invariant formatting.</summary>
public sealed partial class HmiNumericEntry : UserControl
{
    private readonly TextBox _input = new() { MinWidth = 0, MinHeight = 0, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), MaxLength = NumericInputText.MaximumLength };
    private readonly RepeatButton _up = new() { Content = "▴", MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), FontSize = 10 };
    private readonly RepeatButton _down = new() { Content = "▾", MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), FontSize = 10 };
    private double _value, _minimum = double.MinValue, _maximum = double.MaxValue, _smallChange = 1;
    private bool _sync;
    private string _formattedText = "0";
    public event EventHandler<NumericValueChangedEventArgs>? ValueChanged;
    public bool HasEditFocus => _input.FocusState != FocusState.Unfocused;
    public string? InputError { get; private set; }
    public double Value
    {
        get => _value;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            SetValue(Math.Clamp(value, _minimum, _maximum));
        }
    }
    public double Minimum
    {
        get => _minimum;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _minimum = value; if (_maximum < value) _maximum = value;
            SetValue(Math.Clamp(_value, _minimum, _maximum)); UpdateButtons();
        }
    }
    public double Maximum
    {
        get => _maximum;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _maximum = value; if (_minimum > value) _minimum = value;
            SetValue(Math.Clamp(_value, _minimum, _maximum)); UpdateButtons();
        }
    }
    public double SmallChange
    {
        get => _smallChange;
        set { if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value)); _smallChange = value; }
    }
    public HmiNumericEntry()
    {
        IsTabStop = false;
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(20) });
        var buttons = new Grid(); buttons.RowDefinitions.Add(new()); buttons.RowDefinitions.Add(new());
        buttons.Children.Add(_up); Grid.SetRow(_down, 1); buttons.Children.Add(_down);
        grid.Children.Add(_input); Grid.SetColumn(buttons, 1); grid.Children.Add(buttons); Content = grid;
        AutomationProperties.SetName(_input, "Numeric value"); AutomationProperties.SetName(_up, "Increase value"); AutomationProperties.SetName(_down, "Decrease value");
        _input.Text = NumericInputText.FormatDouble(_value);
        _input.TextChanged += (_, _) => { if (!_sync && _input.Text != _formattedText) SetError(null); };
        _input.LostFocus += (_, _) => Commit();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { RestoreText(); SetError(null); e.Handled = true; }
            else if (e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.PageUp or VirtualKey.PageDown)
            { Step(e.Key is VirtualKey.Up or VirtualKey.PageUp ? 1 : -1, e.Key is VirtualKey.PageUp or VirtualKey.PageDown ? 10 : 1); e.Handled = true; }
        };
        _input.PointerWheelChanged += (_, e) =>
        {
            if (!HasEditFocus) return;
            var delta = e.GetCurrentPoint(_input).Properties.MouseWheelDelta;
            if (delta != 0) Step(delta > 0 ? 1 : -1, 1);
            e.Handled = true;
        };
        _up.Click += (_, _) => Step(1, 1); _down.Click += (_, _) => Step(-1, 1);
        Loaded += (_, _) =>
        {
            var name = AutomationProperties.GetName(this);
            if (!string.IsNullOrEmpty(name)) AutomationProperties.SetName(_input, name);
        };
        UpdateButtons();
    }
    private void SetValue(double value)
    {
        var old = _value;
        _value = value; UpdateButtons();
        if (old == value) return;
        RestoreText(); SetError(null); ValueChanged?.Invoke(this, new(old, value));
    }
    public bool Commit()
    {
        var parsed = NumericInputText.ParseDouble(_input.Text);
        if (parsed is null) { RestoreText(); SetError("Enter a finite number using a decimal point."); return false; }
        SetValue(Math.Clamp(parsed.Value, _minimum, _maximum)); RestoreText(); SetError(null); return true;
    }
    private void Step(int direction, int multiplier)
    {
        if (!IsEnabled || !Commit()) return;
        var change = _smallChange * multiplier;
        var candidate = _value + direction * change;
        if (!double.IsFinite(candidate)) candidate = direction > 0 ? _maximum : _minimum;
        SetValue(Math.Clamp(candidate, _minimum, _maximum)); RestoreText(); _input.Select(_input.Text.Length, 0);
    }
    private void RestoreText()
    {
        _sync = true;
        try { _formattedText = NumericInputText.FormatDouble(_value); _input.Text = _formattedText; }
        finally { _sync = false; }
    }
    private void SetError(string? message)
    {
        InputError = message;
        AutomationProperties.SetHelpText(_input, message ?? "Invariant numeric input");
        ToolTipService.SetToolTip(_input, message);
    }
    private void UpdateButtons() { _up.IsEnabled = _value < _maximum; _down.IsEnabled = _value > _minimum; }
}
