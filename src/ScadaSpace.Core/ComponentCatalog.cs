namespace ScadaSpace.Core;

public sealed record ComponentDescriptor(string Type, string DisplayName, string Category, double Width, double Height, IReadOnlyDictionary<string, Scalar> Defaults);

/// <summary>The catalog only advertises components with a concrete renderer in ScadaSpace.Controls.</summary>
public static class ComponentCatalog
{
    private static Dictionary<string, Scalar> Props(params (string Key, Scalar Value)[] values) => values.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
    private static Scalar N(double n) => Scalar.From(n);
    private static Scalar S(string s) => Scalar.From(s);
    public static IReadOnlyList<ComponentDescriptor> All { get; } = new ComponentDescriptor[]
    {
        new("Label", "Label", "Display", 160, 36, Props(("text", S("Label")), ("fontSize", N(16)), ("foreground", S("#273641")))),
        new("LEDDisplay", "LED Display", "Display", 170, 70, Props(("value", N(42.6)), ("unit", S("")), ("format", S("0.0")))),
        new("Tank", "Cylindrical Tank", "Display", 190, 235, Props(("value", N(60)), ("minimum", N(0)), ("maximum", N(100)), ("unit", S("%")), ("color", S("#48AFC0")), ("warning", N(85)))),
        new("Thermometer", "Thermometer", "Display", 95, 210, Props(("value", N(45)), ("minimum", N(0)), ("maximum", N(100)), ("unit", S("°C")), ("color", S("#E66C62")))),
        new("Progress", "Progress", "Display", 200, 24, Props(("value", N(65)), ("minimum", N(0)), ("maximum", N(100)))),
        new("Gauge", "Gauge", "Chart", 220, 150, Props(("value", N(58)), ("minimum", N(0)), ("maximum", N(100)), ("unit", S("%")), ("color", S("#47AFC5")))),
        new("TrendChart", "Time Series Chart", "Chart", 520, 230, Props(("title", S("Live trends")), ("series", S("[default]Plant/Tank01/Level,[default]Plant/Tank02/Level")), ("minimum", N(0)), ("maximum", N(100)), ("windowSeconds", N(120)))),
        new("BarChart", "Bar Chart", "Chart", 320, 220, Props(("title", S("Process values")), ("series", S("[default]Plant/Tank01/Level,[default]Plant/Tank02/Level")), ("minimum", N(0)), ("maximum", N(100)))),
        new("PieChart", "Pie Chart", "Chart", 300, 220, Props(("title", S("Distribution")), ("series", S("[default]Plant/Tank01/Level,[default]Plant/Tank02/Level")))),
        new("Button", "Button", "Input", 150, 38, Props(("text", S("Button")), ("enabled", Scalar.From(true)))),
        new("ToggleSwitch", "Toggle Switch", "Input", 170, 42, Props(("value", Scalar.From(false)), ("text", S("Enabled")))),
        new("CheckBox", "Checkbox", "Input", 150, 40, Props(("value", Scalar.From(false)), ("text", S("Selected")))),
        new("NumericEntry", "Numeric Entry", "Input", 150, 38, Props(("value", N(50)), ("minimum", N(0)), ("maximum", N(100)), ("step", N(1)))),
        new("TextField", "Text Field", "Input", 210, 38, Props(("text", S("")), ("placeholder", S("Enter text")))),
        new("Slider", "Slider", "Input", 220, 42, Props(("value", N(50)), ("minimum", N(0)), ("maximum", N(100)), ("step", N(1)))),
        new("Dropdown", "Dropdown", "Input", 170, 38, Props(("value", S("Auto")), ("options", S("Off,Hand,Auto")))),
        new("RadioGroup", "Radio Group", "Input", 190, 110, Props(("value", S("Auto")), ("options", S("Off,Hand,Auto")))),
        new("DateTimeInput", "Date Input", "Input", 210, 40, Props(("value", S("2026-01-01")))),
        new("Table", "Table", "Display", 480, 240, Props(("source", S("tags")), ("data", S("[]")), ("filter", S("")))),
        new("Tree", "Tree", "Display", 320, 300, Props(("items", Scalar.FromJson("[{\"label\":\"Equipment\",\"expanded\":true,\"items\":[{\"label\":\"Pump\",\"data\":\"Pump01\"},{\"label\":\"Tank\",\"data\":\"Tank01\"}]}]")), ("selection", S("")), ("selectedPaths", Scalar.Array([])), ("selectionData", Scalar.Array([])), ("filter", S("")), ("showFilter", Scalar.From(true)), ("interactable", Scalar.From(true)), ("multiSelect", Scalar.From(false)), ("rowHeight", N(28)))),
        new("TagBrowseTree", "Tag Browse Tree (local)", "Display", 340, 300, Props(("root", S("")), ("selection", S("")), ("selectedPaths", Scalar.Array([])), ("selectionData", Scalar.Array([])), ("filter", S("")), ("showFilter", Scalar.From(true)), ("interactable", Scalar.From(true)), ("multiSelect", Scalar.From(false)), ("rowHeight", N(28)))),
        new("AlarmStatusTable", "Alarm Status Table", "Alarming", 660, 240, Props(("showShelved", Scalar.From(true)))),
        new("AlarmJournalTable", "Alarm Journal Table", "Alarming", 660, 240, Props()),
        new("Rectangle", "Rectangle", "Drawing", 160, 80, Props(("color", S("#E7ECEF")), ("stroke", S("#9DAAB2")), ("strokeWidth", N(1)), ("radius", N(0)))),
        new("Ellipse", "Ellipse", "Drawing", 100, 100, Props(("color", S("#A7D4DF")), ("stroke", S("#5B8490")), ("strokeWidth", N(1)))),
        new("Line", "Line", "Drawing", 160, 10, Props(("color", S("#85959E")), ("strokeWidth", N(3)))),
        new("Pipe", "Pipe", "Drawing", 200, 70, Props(("color", S("#48AFC0")), ("route", S("elbow")), ("strokeWidth", N(12)), ("value", Scalar.From(true)))),
        new("Pump", "Pump", "Symbols", 100, 100, Props(("value", Scalar.From(true)), ("color", S("#56A999")), ("appearance", S("auto")), ("state", S("default")), ("animationSpeed", S("auto")), ("orientation", S("right")))),
        new("Valve", "Valve", "Symbols", 100, 80, Props(("value", Scalar.From(true)), ("color", S("#56A999")), ("appearance", S("auto")), ("state", S("default")), ("animationSpeed", S("auto")), ("orientation", S("right")))),
        new("Motor", "Motor", "Symbols", 130, 85, Props(("value", Scalar.From(true)), ("color", S("#56A999")), ("appearance", S("auto")), ("state", S("default")), ("animationSpeed", S("auto")), ("orientation", S("right")))),
        new("Sensor", "Sensor", "Symbols", 140, 150, Props(("appearance", S("auto")), ("state", S("default")), ("orientation", S("right")), ("animationSpeed", S("auto")), ("label", S("Sensor")), ("value", Scalar.Object(new Dictionary<string, Scalar> { ["text"] = S("0") })))),
        new("Vessel", "Vessel", "Symbols", 170, 245, Props(("appearance", S("auto")), ("state", S("default")), ("orientation", S("vertical")), ("animationSpeed", S("auto")), ("displayStand", Scalar.From(true)), ("displayAgitator", Scalar.From(true)), ("displayFillLevel", Scalar.From(true)), ("liquidOpacity", N(.7)), ("label", S("Vessel")), ("value", Scalar.Object(new Dictionary<string, Scalar> { ["value"] = N(60), ["capacity"] = N(100), ["displayValueAsPercent"] = Scalar.From(true) })))),
        new("Container", "Container", "Container", 320, 220, Props(("padding", N(8)), ("gap", N(8)), ("columns", N(2)), ("breakpoint", N(600)), ("activeTab", N(0)))),
        new("EmbeddedView", "Embedded View", "Container", 300, 220, Props(("view", S("EquipmentDetail")), ("param.equipment", S("Tank01")))),
        new("FlexRepeater", "Flex Repeater", "Container", 600, 220, Props(("view", S("EquipmentDetail")), ("count", N(2)), ("gap", N(8))))
    };
    public static ComponentNode Create(string type, double x = 0, double y = 0)
    {
        var descriptor = All.FirstOrDefault(x => x.Type == type) ?? throw new ArgumentException("Unsupported component type: " + type);
        return new() { Type = type, Name = type, X = x, Y = y, Width = descriptor.Width, Height = descriptor.Height, Props = new(descriptor.Defaults, StringComparer.Ordinal) };
    }
}
