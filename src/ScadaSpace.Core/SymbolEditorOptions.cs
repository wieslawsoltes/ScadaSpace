namespace ScadaSpace.Core;

/// <summary>Read-only, UI-independent choices for the symbol/display properties actually supported by the renderer.</summary>
public static class SymbolEditorOptions
{
    private static readonly IReadOnlyList<string> Appearances = Array.AsReadOnly(new[] { "auto", "mimic", "simple", "p&id" });
    private static readonly IReadOnlyList<string> Directions = Array.AsReadOnly(new[] { "right", "bottom", "left", "top" });
    private static readonly IReadOnlyList<string> VesselDirections = Array.AsReadOnly(new[] { "vertical", "horizontal" });
    private static readonly IReadOnlyList<string> PumpVariants = Array.AsReadOnly(new[] { "centrifugal", "vacuum" });
    private static readonly IReadOnlyList<string> PumpFeet = Array.AsReadOnly(new[] { "bottom", "right", "top", "left", "hidden" });
    private static readonly IReadOnlyList<string> PipeRoutes = Array.AsReadOnly(new[] { "straight", "elbow" });

    /// <summary>Returns no choices for free-form or unsupported properties. Dynamic states are an owned snapshot.</summary>
    public static IReadOnlyList<string> Choices(string type, string property, SymbolStateCatalog? states = null)
    {
        ArgumentNullException.ThrowIfNull(type); ArgumentNullException.ThrowIfNull(property);
        if (type == "LEDDisplay") return property switch
        { "segmentFormat" => LedDisplayOptions.SegmentFormats, "numberFormat" => LedDisplayOptions.NumberFormats, _ => Array.Empty<string>() };
        if (type == "Pipe" && property == "route") return PipeRoutes;
        if (!SymbolStateCatalog.IsFamily(type)) return Array.Empty<string>();
        return property switch
        {
            "appearance" => Appearances,
            "orientation" => type == "Vessel" ? VesselDirections : Directions,
            "variant" when type == "Pump" => PumpVariants,
            "feet" when type == "Pump" => PumpFeet,
            "state" => StatesFor(type, states ?? SymbolStateCatalog.Empty),
            _ => Array.Empty<string>()
        };
    }
    private static IReadOnlyList<string> StatesFor(string type, SymbolStateCatalog states) =>
        Array.AsReadOnly(states.Names.Where(name => states.Get(name).AppliesTo(type)).ToArray());

    /// <summary>Keep an imported custom or invalid current value visible; never silently select another option.</summary>
    public static IReadOnlyList<string> IncludeCurrent(IReadOnlyList<string> choices, string current)
    {
        ArgumentNullException.ThrowIfNull(choices); ArgumentNullException.ThrowIfNull(current);
        for (var i = 0; i < choices.Count; i++) if (choices[i] == current) return choices;
        var result = new string[choices.Count + 1];
        for (var i = 0; i < choices.Count; i++) result[i] = choices[i];
        result[^1] = current; return Array.AsReadOnly(result);
    }
}
