using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace ScadaSpace.Core;

public sealed record StyleProperty(string Name, string Group, ScalarKind Kind, string Hint, double Minimum = 0, double Maximum = 0);

/// <summary>Typed, bounded local style vocabulary. Unknown properties are preserved, not interpreted as CSS.</summary>
public static class StyleRules
{
    public const int MaximumClasses = 1024, MaximumProperties = 128, MaximumAssignments = 64;
    // Class-local limits must not reject the union of individually valid class maps.
    public const int MaximumResolvedProperties = (MaximumAssignments + 1) * MaximumProperties;
    public static IReadOnlyList<StyleProperty> Properties { get; } = Array.AsReadOnly(new StyleProperty[]
    {
        new("foreground", "Text", ScalarKind.String, "#RRGGBB or #AARRGGBB"),
        new("fontSize", "Text", ScalarKind.Number, "6–200 logical pixels", 6, 200),
        new("fontWeight", "Text", ScalarKind.String, "Normal, SemiBold or Bold"),
        new("italic", "Text", ScalarKind.Boolean, "true or false"),
        new("textAlignment", "Text", ScalarKind.String, "Left, Center or Right (labels)"),
        new("background", "Background", ScalarKind.String, "#RRGGBB or #AARRGGBB"),
        new("opacity", "Background", ScalarKind.Number, "0–1", 0, 1),
        new("borderColor", "Border", ScalarKind.String, "#RRGGBB or #AARRGGBB"),
        new("borderWidth", "Border", ScalarKind.Number, "0–128 logical pixels", 0, 128),
        new("cornerRadius", "Border", ScalarKind.Number, "0–1024 logical pixels", 0, 1024),
        new("padding", "Spacing", ScalarKind.Number, "0–256 logical pixels", 0, 256)
    });
    private static readonly Dictionary<string, StyleProperty> Known = Properties.ToDictionary(p => p.Name, StringComparer.Ordinal);
    public static bool IsSupported(string name) => Known.ContainsKey(name);
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name != name.Trim() || name.Any(char.IsControl)
            || name.Split('/').Any(s => s.Length == 0 || s is "." or "..") || name.Contains(',') || name.Contains('\\'))
            throw new ArgumentException("Use a unique style name of 1–256 visible characters; folder segments cannot be empty, . or ...");
    }
    public static void ValidateValues(IReadOnlyDictionary<string, Scalar> values) => ValidateValues(values, MaximumProperties);
    public static void ValidateResolvedValues(IReadOnlyDictionary<string, Scalar> values) => ValidateValues(values, MaximumResolvedProperties);
    private static void ValidateValues(IReadOnlyDictionary<string, Scalar> values, int limit)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > limit) throw new ArgumentException($"Style property count exceeds its {limit}-property budget.");
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl) || !value.IsValid()) throw new ArgumentException("Invalid style property.");
            if (!Known.TryGetValue(key, out var spec)) continue;
            if (value.Kind != spec.Kind) throw new ArgumentException(key + " requires " + spec.Kind + ".");
            if (value.Kind == ScalarKind.Number && (value.Number < spec.Minimum || value.Number > spec.Maximum)) throw new ArgumentException(key + " must be within " + spec.Hint + ".");
            if (key is "foreground" or "background" or "borderColor")
            {
                if (!StyleColor.TryParse(value.Text, out _))
                    throw new ArgumentException(key + " requires #RRGGBB or #AARRGGBB.");
            }
            if (key == "fontWeight" && value.Text is not ("Normal" or "SemiBold" or "Bold")) throw new ArgumentException("fontWeight must be Normal, SemiBold or Bold.");
            if (key == "textAlignment" && value.Text is not ("Left" or "Center" or "Right")) throw new ArgumentException("textAlignment must be Left, Center or Right.");
        }
    }
    public static void ValidateAssignments(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count > MaximumAssignments) throw new ArgumentException("At most 64 style classes may be assigned to a component.");
        foreach (var name in names) ValidateName(name);
    }
    public static void ValidateClasses(IReadOnlyDictionary<string, Dictionary<string, Scalar>> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);
        if (classes.Count > MaximumClasses) throw new ArgumentException("At most 1,024 style classes are allowed.");
        foreach (var (name, values) in classes) { ValidateName(name); ValidateValues(values); }
    }
}

public readonly record struct StyleValueOrigin(string? ClassName, bool IsInline);
public sealed record ResolvedStyle(IReadOnlyDictionary<string, Scalar> Values, IReadOnlyList<string> MissingClasses, IReadOnlyList<string> UnsupportedProperties)
{
    private static readonly IReadOnlyDictionary<string, StyleValueOrigin> EmptyOrigins =
        new ReadOnlyDictionary<string, StyleValueOrigin>(new Dictionary<string, StyleValueOrigin>(StringComparer.Ordinal));
    public IReadOnlyDictionary<string, StyleValueOrigin> Origins { get; init; } = EmptyOrigins;
}

/// <summary>Owned class catalog with weak per-component caching. Classes sort ordinally; inline values win.</summary>
public sealed class StyleResolver
{
    private sealed record Entry(string[] Names, Dictionary<string, Scalar> Inline, ResolvedStyle Result);
    private readonly Dictionary<string, IReadOnlyDictionary<string, Scalar>> _classes;
    private readonly ConditionalWeakTable<ComponentNode, Entry> _cache = new();
    private readonly object _gate = new();
    public long ResolveCount { get; private set; }
    public StyleResolver(IReadOnlyDictionary<string, Dictionary<string, Scalar>> classes)
    {
        StyleRules.ValidateClasses(classes);
        _classes = classes.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, Scalar>)new ReadOnlyDictionary<string, Scalar>(new Dictionary<string, Scalar>(p.Value, StringComparer.Ordinal)), StringComparer.Ordinal);
    }
    /// <summary>Compares incoming contents against the owned ordinal catalog, ignoring the caller's comparers.</summary>
    public bool MatchesCatalog(IReadOnlyDictionary<string, Dictionary<string, Scalar>> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);
        if (classes.Count != _classes.Count) return false;
        foreach (var (name, properties) in classes)
        {
            if (properties is null || !_classes.TryGetValue(name, out var own) || properties.Count != own.Count) return false;
            foreach (var (key, value) in properties)
                if (!own.TryGetValue(key, out var previous) || previous != value) return false;
        }
        return true;
    }
    public ResolvedStyle Resolve(ComponentNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        lock (_gate)
        {
            if (_cache.TryGetValue(node, out var entry) && Same(entry, node)) return entry.Result;
            return Build(node);
        }
    }
    private static bool Same(Entry entry, ComponentNode node)
    {
        if (node.StyleClasses is null || node.Style is null || entry.Names.Length != node.StyleClasses.Count || entry.Inline.Count != node.Style.Count) return false;
        for (var i = 0; i < entry.Names.Length; i++) if (entry.Names[i] != node.StyleClasses[i]) return false;
        // Query our ordinal snapshot, never a caller's potentially case-insensitive map.
        foreach (var pair in node.Style) if (!entry.Inline.TryGetValue(pair.Key, out var value) || value != pair.Value) return false;
        return true;
    }
    private ResolvedStyle Build(ComponentNode node)
    {
        StyleRules.ValidateAssignments(node.StyleClasses); StyleRules.ValidateValues(node.Style);
        var values = new Dictionary<string, Scalar>(StringComparer.Ordinal); var missing = new List<string>();
        var origins = new Dictionary<string, StyleValueOrigin>(StringComparer.Ordinal);
        foreach (var name in node.StyleClasses.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            if (_classes.TryGetValue(name, out var style))
            {
                foreach (var pair in style) { values[pair.Key] = pair.Value; origins[pair.Key] = new(name, false); }
            }
            else missing.Add(name);
        foreach (var pair in node.Style) { values[pair.Key] = pair.Value; origins[pair.Key] = new(null, true); }
        StyleRules.ValidateResolvedValues(values);
        var result = new ResolvedStyle(new ReadOnlyDictionary<string, Scalar>(values), missing.AsReadOnly(), Array.AsReadOnly(values.Keys.Where(k => !StyleRules.IsSupported(k)).ToArray()))
        { Origins = new ReadOnlyDictionary<string, StyleValueOrigin>(origins) };
        _cache.Remove(node); _cache.Add(node, new(node.StyleClasses.ToArray(), new Dictionary<string, Scalar>(node.Style, StringComparer.Ordinal), result)); ResolveCount++; return result;
    }
}
