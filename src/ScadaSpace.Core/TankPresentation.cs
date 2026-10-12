namespace ScadaSpace.Core;

/// <summary>Cylindrical-tank appearance, separate from rotating industrial vessel symbols.</summary>
public sealed record TankOptions(string TankColor = "#D6D6D6", string TankWarningColor = "#F1D6B7", double LiquidOpacity = .5,
    string LiquidWarningColor = "#D88837", double? WarningThreshold = null, bool ShowValue = true, string ValueFormat = "None",
    string DisplayUnit = "", bool UnitPrefix = false, bool ShowScale = true)
{
    public void Validate()
    {
        if (!StyleColor.TryParse(TankColor, out _) || !StyleColor.TryParse(TankWarningColor, out _) || !StyleColor.TryParse(LiquidWarningColor, out _)
            || !double.IsFinite(LiquidOpacity) || LiquidOpacity is < 0 or > 1 || WarningThreshold is { } warning && (!double.IsFinite(warning) || warning is < 0 or > 100)
            || ValueFormat is not ("None" or "Integer" or "Percent" or "Currency") || DisplayUnit is null || DisplayUnit.Length > 128)
            throw new ArgumentException("Invalid tank colors, opacity, threshold or value display.");
    }
    public static TankOptions Read(Func<string, Scalar> read)
    {
        string Text(string key, string fallback) { var v = read(key); return v.Kind == ScalarKind.Null ? fallback : v.Kind == ScalarKind.String ? v.Text! : throw new ArgumentException(key + " must be text."); }
        var opacity = read("liquidOpacity"); var warning = read("warningThreshold");
        if (opacity.Kind is not (ScalarKind.Number or ScalarKind.Null) || warning.Kind is not (ScalarKind.Number or ScalarKind.Null)) throw new ArgumentException("Tank opacity and threshold must be numeric.");
        var display = read("valueDisplay"); var show = true; var format = "None"; var unit = ""; var prefix = false;
        if (display.Kind != ScalarKind.Null)
        {
            if (display.Kind != ScalarKind.Object) throw new ArgumentException("valueDisplay must be an object.");
            var f = display.Fields; show = f.GetValueOrDefault("enabled", Scalar.From(true)).AsBoolean();
            format = f.GetValueOrDefault("format", Scalar.From("None")).ToString();
            if (f.TryGetValue("style", out var style) && style.Kind != ScalarKind.Null && (style.Kind != ScalarKind.Object || style.Fields.Count > 0)) throw new ArgumentException("Tank valueDisplay CSS styling is not supported.");
            if (f.TryGetValue("unit", out var u))
            {
                if (u.Kind != ScalarKind.Object) throw new ArgumentException("valueDisplay.unit must be an object.");
                if (u.Fields.GetValueOrDefault("enabled", Scalar.From(false)).AsBoolean())
                {
                    unit = u.Fields.GetValueOrDefault("value", Scalar.From("")).ToString();
                    var fix = u.Fields.GetValueOrDefault("fix", Scalar.From("postfix")).ToString();
                    if (fix is not ("prefix" or "postfix")) throw new ArgumentException("Tank unit fix must be prefix or postfix."); prefix = fix == "prefix";
                }
            }
        }
        var scale = read("showScale"); if (scale.Kind is not (ScalarKind.Boolean or ScalarKind.Null)) throw new ArgumentException("showScale must be Boolean.");
        var result = new TankOptions(Text("tankColor", "#D6D6D6"), Text("tankWarningColor", "#F1D6B7"), opacity.Kind == ScalarKind.Null ? .5 : opacity.Number,
            Text("liquidWarningColor", "#D88837"), warning.Kind == ScalarKind.Null ? null : warning.Number, show, format, unit, prefix, scale.Kind == ScalarKind.Null || scale.Boolean);
        result.Validate(); return result;
    }
}
