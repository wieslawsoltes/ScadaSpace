namespace ScadaSpace.Core;

/// <summary>Session-local defaults for symbols using auto. Does not mutate project defaults.</summary>
public sealed record SymbolDefaults(string AutoAppearance = "mimic", double AutoAnimationSpeed = 50, bool ReducedMotion = false)
{
    public void Validate()
    {
        if (AutoAppearance is not ("mimic" or "simple" or "p&id") || !double.IsFinite(AutoAnimationSpeed) || AutoAnimationSpeed is < 0 or > 100)
            throw new ArgumentException("Symbol defaults require mimic, simple or p&id and animation speed 0–100.");
    }
}

public sealed record SymbolText(string Text = "", string Location = "bottom", string Justify = "center", double FontSize = 13, string Color = "#333333")
{
    public void Validate()
    {
        if (Text is null || Text.Length > 1024 || Location is not ("top" or "bottom" or "left" or "right" or "hidden")
            || Justify is not ("left" or "center" or "right" or "auto") || !double.IsFinite(FontSize) || FontSize is < 6 or > 96 || !StyleColor.TryParse(Color, out _))
            throw new ArgumentException("Invalid symbol text, location, justification, size or hexadecimal color.");
    }
}

/// <summary>UI-independent, immutable industrial symbol drawing options. The vocabulary is explicit, not arbitrary SVG/CSS.</summary>
public sealed record SymbolOptions
{
    public string Appearance { get; init; } = "mimic";
    public string State { get; init; } = "default";
    public string Orientation { get; init; } = "right";
    public string Variant { get; init; } = "centrifugal";
    public string Feet { get; init; } = "bottom";
    public double AnimationSpeed { get; init; } = 0;
    public bool ReverseFlow { get; init; }
    public bool DisplayStand { get; init; } = true;
    public bool DisplayAgitator { get; init; } = true;
    public bool DisplayFillLevel { get; init; } = true;
    public double LiquidOpacity { get; init; } = .7;
    public string LiquidColor { get; init; } = "#00ACAC";
    public string LiquidWarningColor { get; init; } = "#D88837";
    public SymbolText Label { get; init; } = new();
    public SymbolText Readout { get; init; } = new();
    public bool IsFaulted => State is "faulted" or "failedToOpen" or "failedToClose";
    public bool IsRunning => State is "running" or "open";
    public bool IsAnimated => IsRunning && AnimationSpeed > 0;
    public void Validate(string type)
    {
        if (Appearance is not ("mimic" or "simple" or "p&id") || Variant is not ("centrifugal" or "vacuum")
            || Feet is not ("top" or "bottom" or "left" or "right" or "hidden") || !double.IsFinite(AnimationSpeed) || AnimationSpeed is < 0 or > 100
            || !double.IsFinite(LiquidOpacity) || LiquidOpacity is < 0 or > 1 || !StyleColor.TryParse(LiquidColor, out _) || !StyleColor.TryParse(LiquidWarningColor, out _))
            throw new ArgumentException("Invalid symbol appearance, variant, feet, speed or liquid settings.");
        if (type == "Vessel" ? Orientation is not ("vertical" or "horizontal") : Orientation is not ("top" or "bottom" or "left" or "right"))
            throw new ArgumentException("Invalid " + type + " orientation.");
        if (type == "Valve" ? State is not ("default" or "open" or "closed" or "partiallyClosed" or "failedToOpen" or "failedToClose")
            : State is not ("default" or "running" or "stopped" or "faulted")) throw new ArgumentException("Unsupported " + type + " state: " + State);
        ArgumentNullException.ThrowIfNull(Label); ArgumentNullException.ThrowIfNull(Readout); Label.Validate(); Readout.Validate();
    }
    /// <summary>Bounded phase in turns; no animation state or writes are stored in the model.</summary>
    public double Phase(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (!double.IsFinite(AnimationSpeed) || AnimationSpeed is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(AnimationSpeed));
        if (!IsAnimated) return 0;
        var period = 100 / AnimationSpeed;
        // A subnormal positive speed can have an infinite period. Divide time first
        // rather than overflowing the period or underflowing speed / 100 to zero.
        var phase = double.IsFinite(period) ? elapsedSeconds % period / period : elapsedSeconds / 100 * AnimationSpeed;
        phase %= 1;
        return phase < 0 ? (phase + 1) % 1 : phase;
    }
}

/// <summary>Reads typed component properties and nested annotation objects using the host's qualified resolver.</summary>
public static class SymbolPresentation
{
    public static SymbolOptions Read(string type, Func<string, Scalar> read, SymbolDefaults defaults, double legacyValue = 0)
    {
        ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(defaults); defaults.Validate();
        string Text(string key, string fallback)
        {
            var value = read(key); if (value.Kind == ScalarKind.Null) return fallback;
            if (value.Kind != ScalarKind.String) throw new ArgumentException(key + " requires a string."); return value.Text!;
        }
        double Number(string key, double fallback)
        {
            var value = read(key); if (value.Kind == ScalarKind.Null) return fallback;
            if (value.Kind != ScalarKind.Number) throw new ArgumentException(key + " requires a number."); return value.Number;
        }
        bool Flag(string key, bool fallback)
        {
            var value = read(key); if (value.Kind == ScalarKind.Null) return fallback;
            if (value.Kind != ScalarKind.Boolean) throw new ArgumentException(key + " requires a Boolean."); return value.Boolean;
        }
        var appearance = Text("appearance", "auto"); if (appearance == "auto") appearance = defaults.AutoAppearance;
        var speed = read("animationSpeed");
        var animation = speed.Kind == ScalarKind.Null || speed.Kind == ScalarKind.String && speed.Text == "auto" ? defaults.AutoAnimationSpeed
            : speed.Kind == ScalarKind.Number ? speed.Number : throw new ArgumentException("animationSpeed requires auto or a percentage.");
        if (!double.IsFinite(animation) || animation is < 0 or > 100) throw new ArgumentException("animationSpeed must be within 0–100.");
        var state = Text("state", type == "Valve" ? legacyValue != 0 ? "open" : "closed" : legacyValue != 0 ? "running" : "stopped");
        var label = Annotation(read("label")); var value = read("value");
        var readout = value.Kind == ScalarKind.Object ? Annotation(value) : new SymbolText();
        if (type == "Vessel" && value.Kind == ScalarKind.Object && value.Fields.ContainsKey("value"))
        {
            var numberValue = value.Fields["value"]; var capacityValue = value.Fields.GetValueOrDefault("capacity", Scalar.From(100));
            var displayPercent = value.Fields.GetValueOrDefault("displayValueAsPercent", Scalar.From(true));
            if (numberValue.Kind != ScalarKind.Number || capacityValue.Kind != ScalarKind.Number || displayPercent.Kind != ScalarKind.Boolean)
                throw new ArgumentException("Vessel value/capacity require numbers and displayValueAsPercent requires a Boolean.");
            var n = numberValue.Number; var capacity = capacityValue.Number;
            if (!double.IsFinite(capacity) || capacity <= 0) throw new ArgumentException("Vessel capacity must be positive.");
            var percent = displayPercent.Boolean;
            readout = readout with { Text = NumericInputText.FormatDouble(percent ? Fraction(n, 0, capacity) * 100 : n) + (percent ? "%" : "") };
        }
        var result = new SymbolOptions
        {
            Appearance = appearance, State = state, AnimationSpeed = defaults.ReducedMotion ? 0 : animation,
            Orientation = Text("orientation", type == "Vessel" ? "vertical" : "right"), Variant = Text("variant", "centrifugal"), Feet = Text("feet", "bottom"),
            ReverseFlow = Flag("reverseFlow", false), DisplayStand = Flag("displayStand", true), DisplayAgitator = Flag("displayAgitator", true), DisplayFillLevel = Flag("displayFillLevel", true),
            LiquidOpacity = Number("liquidOpacity", .7), LiquidColor = Text("liquidColor", "#00ACAC"), LiquidWarningColor = Text("liquidWarningColor", "#D88837"), Label = label, Readout = readout
        };
        result.Validate(type); return result;
    }
    public static SymbolText Annotation(Scalar value)
    {
        if (value.Kind == ScalarKind.Null) return new();
        if (value.Kind == ScalarKind.String) { var plain = new SymbolText(value.Text!); plain.Validate(); return plain; }
        if (value.Kind != ScalarKind.Object) throw new ArgumentException("A symbol annotation requires an object or text.");
        var f = value.Fields;
        string Text(string key, string fallback) => f.TryGetValue(key, out var v) ? v.Kind == ScalarKind.String ? v.Text! : throw new ArgumentException("Annotation " + key + " must be a string.") : fallback;
        var style = f.GetValueOrDefault("style", Scalar.Object([]));
        if (style.Kind != ScalarKind.Object) throw new ArgumentException("Annotation style must be an object.");
        // Unsupported CSS is diagnosed rather than presented as implemented.
        if (style.Fields.Keys.Any(k => k is not ("color" or "fontSize"))) throw new ArgumentException("Symbol text style supports color and fontSize only.");
        var size = style.Fields.GetValueOrDefault("fontSize", Scalar.From(13));
        var color = style.Fields.GetValueOrDefault("color", Scalar.From("#333333"));
        if (size.Kind != ScalarKind.Number || color.Kind != ScalarKind.String) throw new ArgumentException("Invalid annotation style types.");
        var result = new SymbolText(Text("text", ""), Text("location", "bottom"), Text("justify", "center"), size.Number, color.Text!); result.Validate(); return result;
    }
    public static double Fraction(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || !double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum) throw new ArgumentException("Symbol range requires finite values and increasing bounds.");
        if (value <= minimum) return 0; if (value >= maximum) return 1;
        // Direct subtraction is accurate for close values; scale only when subtraction overflows.
        var span = maximum - minimum;
        return double.IsFinite(span) ? (value - minimum) / span : (value / 2 - minimum / 2) / (maximum / 2 - minimum / 2);
    }
}
