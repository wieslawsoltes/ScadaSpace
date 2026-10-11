using Microsoft.UI.Xaml;

namespace ScadaSpace.Controls;

public sealed partial class HmiNumericEntry
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(HmiNumericEntry), new PropertyMetadata(0d, (o,e) => ((HmiNumericEntry)o).ApplyProperty(ValueProperty, (double)e.NewValue)));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(HmiNumericEntry), new PropertyMetadata(double.MinValue, (o,e) => ((HmiNumericEntry)o).ApplyProperty(MinimumProperty, (double)e.NewValue)));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(HmiNumericEntry), new PropertyMetadata(double.MaxValue, (o,e) => ((HmiNumericEntry)o).ApplyProperty(MaximumProperty, (double)e.NewValue)));
    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(nameof(SmallChange), typeof(double), typeof(HmiNumericEntry), new PropertyMetadata(1d, (o,e) => ((HmiNumericEntry)o).ApplyProperty(SmallChangeProperty, (double)e.NewValue)));
    private bool _propertySync;
    private void ApplyProperty(DependencyProperty property, double value)
    {
        if (_propertySync) return;
        try
        {
            if (property == ValueProperty) Value = value;
            else if (property == MinimumProperty) Minimum = value;
            else if (property == MaximumProperty) Maximum = value;
            else SmallChange = value;
        }
        finally { SynchronizeProperties(); }
    }
    private void SynchronizeProperties()
    {
        if (_propertySync) return;
        _propertySync = true;
        try
        {
            if ((double)GetValue(ValueProperty) != _value) SetValue(ValueProperty, _value);
            if ((double)GetValue(MinimumProperty) != _minimum) SetValue(MinimumProperty, _minimum);
            if ((double)GetValue(MaximumProperty) != _maximum) SetValue(MaximumProperty, _maximum);
            if ((double)GetValue(SmallChangeProperty) != _smallChange) SetValue(SmallChangeProperty, _smallChange);
        }
        finally { _propertySync = false; }
    }
}
