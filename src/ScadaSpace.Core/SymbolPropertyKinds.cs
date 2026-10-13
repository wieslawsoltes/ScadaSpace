namespace ScadaSpace.Core;

/// <summary>Typed authoring for properties actually consumed by ScadaSpace's vector renderers.</summary>
public static class SymbolPropertyKinds
{
    public static bool IsColor(string type, string property) => type switch
    {
        "LEDDisplay" => property is "backgroundColor" or "diodeOnColor" or "diodeOffColor",
        "Tank" => property is "color" or "liquidColor" or "tankColor" or "tankWarningColor" or "liquidWarningColor" or "stroke",
        "Vessel" => property is "liquidColor" or "liquidWarningColor",
        "Rectangle" or "Ellipse" => property is "color" or "stroke",
        "Gauge" or "Thermometer" or "Pipe" or "Line" => property == "color",
        _ => false
    };

    public static bool IsSwitch(string type, string property) => type switch
    {
        "Vessel" => property is "displayStand" or "displayAgitator" or "displayFillLevel",
        "Valve" => property == "reverseFlow",
        "Tank" => property == "showScale",
        _ => false
    };
}
