using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ScadaSpace.Core;

/// <summary>Immutable theme colors and independently enabled effects. Hex colors use explicit ARGB ordering.</summary>
public sealed record SymbolStateStyle(string PrimaryColor = "#F4F4F4", string SecondaryColor = "#D9E7DE",
    string TertiaryColor = "#00ACAC", string StrokeColor = "#606060", bool EnableAnimation = false, bool EnableFlashing = false)
{
    public void Validate()
    {
        if (!StyleColor.TryParse(PrimaryColor, out _) || !StyleColor.TryParse(SecondaryColor, out _)
            || !StyleColor.TryParse(TertiaryColor, out _) || !StyleColor.TryParse(StrokeColor, out _))
            throw new ArgumentException("Symbol state colors require #RRGGBB or #AARRGGBB.");
    }
}

/// <summary>Owned, bounded family assignments and original theme values for Reset Theme.</summary>
public sealed class SymbolStateDefinition
{
    public SymbolStateStyle Light { get; }
    public SymbolStateStyle Dark { get; }
    public IReadOnlyList<string> Symbols { get; }
    public SymbolStateStyle InitialLight { get; }
    public SymbolStateStyle InitialDark { get; }
    [JsonConstructor]
    public SymbolStateDefinition(SymbolStateStyle light, SymbolStateStyle dark, IReadOnlyList<string> symbols,
        SymbolStateStyle? initialLight = null, SymbolStateStyle? initialDark = null)
    {
        ArgumentNullException.ThrowIfNull(light); ArgumentNullException.ThrowIfNull(dark); ArgumentNullException.ThrowIfNull(symbols);
        light.Validate(); dark.Validate(); (initialLight ?? light).Validate(); (initialDark ?? dark).Validate();
        if (symbols.Count > SymbolStateCatalog.Families.Count || symbols.Any(s => !SymbolStateCatalog.Families.Contains(s, StringComparer.Ordinal))
            || symbols.Distinct(StringComparer.Ordinal).Count() != symbols.Count)
            throw new ArgumentException("Apply each supported symbol family at most once.");
        Light = light; Dark = dark; InitialLight = initialLight ?? light; InitialDark = initialDark ?? dark;
        Symbols = Array.AsReadOnly(symbols.Order(StringComparer.Ordinal).ToArray());
    }
    public SymbolStateDefinition WithTheme(string theme, SymbolStateStyle value)
    {
        SymbolStateCatalog.ValidateTheme(theme); ArgumentNullException.ThrowIfNull(value);
        return new(theme == "light" ? value : Light, theme == "dark" ? value : Dark, Symbols, InitialLight, InitialDark);
    }
    public SymbolStateDefinition WithSymbols(IReadOnlyList<string> symbols) => new(Light, Dark, symbols, InitialLight, InitialDark);
    public SymbolStateDefinition ResetTheme(string theme) => WithTheme(theme, theme == "light" ? InitialLight : InitialDark);
    public bool Equivalent(SymbolStateDefinition other) => other is not null && Light == other.Light && Dark == other.Dark
        && InitialLight == other.InitialLight && InitialDark == other.InitialDark && Symbols.SequenceEqual(other.Symbols);
}

/// <summary>Immutable project catalog. Stored states are overrides; built-in names are always available for editing.</summary>
public sealed class SymbolStateCatalog
{
    public const int MaximumOverrides = 128;
    public static IReadOnlyList<string> Families { get; } = Array.AsReadOnly(new[] { "Pump", "Motor", "Valve", "Sensor", "Vessel" });
    private static readonly IReadOnlyDictionary<string, SymbolStateDefinition> BuiltIns = CreateBuiltIns();
    public static SymbolStateCatalog Empty { get; } = new(new Dictionary<string, SymbolStateDefinition>());
    public IReadOnlyDictionary<string, SymbolStateDefinition> States { get; }
    [JsonIgnore] public IReadOnlyList<string> Names { get; }
    [JsonConstructor]
    public SymbolStateCatalog(IReadOnlyDictionary<string, SymbolStateDefinition> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (states.Count > MaximumOverrides) throw new ArgumentException("At most 128 symbol state overrides are allowed.");
        var owned = new Dictionary<string, SymbolStateDefinition>(StringComparer.Ordinal);
        foreach (var (name, state) in states)
        { ValidateName(name); ArgumentNullException.ThrowIfNull(state); owned.Add(name, state); }
        States = new ReadOnlyDictionary<string, SymbolStateDefinition>(owned);
        Names = Array.AsReadOnly(BuiltIns.Keys.Concat(owned.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name != name.Trim() || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Use a symbol state name of 1–64 ASCII letters, digits, hyphens or underscores.");
    }
    public static void ValidateTheme(string theme)
    { if (theme is not ("light" or "dark")) throw new ArgumentException("Symbol theme must be light or dark."); }
    public static bool IsBuiltIn(string name) => name is not null && BuiltIns.ContainsKey(name);
    public SymbolStateDefinition Get(string name) => States.TryGetValue(name, out var custom) ? custom
        : BuiltIns.TryGetValue(name, out var builtin) ? builtin : throw new ArgumentException("Unknown symbol state: " + name);
    public SymbolStateStyle Resolve(string family, string name, string theme)
    {
        ValidateTheme(theme);
        if (!Families.Contains(family, StringComparer.Ordinal)) throw new ArgumentException("Unknown symbol family: " + family);
        var state = Get(name);
        if (!state.Symbols.Contains(family, StringComparer.Ordinal)) throw new ArgumentException("State " + name + " is not applied to " + family + ".");
        return theme == "light" ? state.Light : state.Dark;
    }
    public SymbolStateCatalog With(string name, SymbolStateDefinition value)
    {
        ValidateName(name); ArgumentNullException.ThrowIfNull(value);
        if (States.TryGetValue(name, out var old) && old.Equivalent(value)) return this;
        var next = new Dictionary<string, SymbolStateDefinition>(States, StringComparer.Ordinal) { [name] = value };
        return new(next);
    }
    public SymbolStateCatalog WithoutCustom(string name)
    {
        if (IsBuiltIn(name)) throw new ArgumentException("Built-in states cannot be deleted or renamed.");
        if (!States.ContainsKey(name)) throw new ArgumentException("Unknown custom state: " + name);
        var next = new Dictionary<string, SymbolStateDefinition>(States, StringComparer.Ordinal); next.Remove(name); return new(next);
    }
    public bool Equivalent(SymbolStateCatalog other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || States.Count != other.States.Count) return false;
        foreach (var (name, state) in States)
            if (!other.States.TryGetValue(name, out var value) || !state.Equivalent(value)) return false;
        return true;
    }
    private static IReadOnlyDictionary<string, SymbolStateDefinition> CreateBuiltIns()
    {
        var result = new Dictionary<string, SymbolStateDefinition>(StringComparer.Ordinal);
        foreach (var name in new[] { "default", "running", "stopped", "faulted", "open", "closed", "partiallyClosed", "failedToOpen", "failedToClose" })
        {
            var running = name is "running" or "open";
            var stroke = name is "faulted" or "failedToOpen" or "failedToClose" ? "#C43D35" : name == "partiallyClosed" ? "#C28A25" : running ? "#258748" : "#606060";
            var light = new SymbolStateStyle(StrokeColor: stroke, EnableAnimation: running);
            var dark = new SymbolStateStyle("#343B41", "#536358", "#00ACAC", stroke == "#606060" ? "#BBC4CB" : stroke, running);
            var families = name == "default" ? Families : name is "running" or "stopped" or "faulted" ? Families.Where(f => f != "Valve").ToArray() : new[] { "Valve" };
            result.Add(name, new(light, dark, families));
        }
        return new ReadOnlyDictionary<string, SymbolStateDefinition>(result);
    }
}

public sealed partial class ProjectDefinition
{
    private SymbolStateCatalog _symbolStates = SymbolStateCatalog.Empty;
    /// <summary>Validated immutable state configuration; absent in older projects means built-in defaults.</summary>
    public SymbolStateCatalog SymbolStates
    {
        get => _symbolStates;
        set { ArgumentNullException.ThrowIfNull(value); _symbolStates = value; }
    }
}
