namespace ScadaSpace.Core;

/// <summary>One UI-thread-owned immutable presentation snapshot. Every read still visits current inputs, so binding failures are never hidden by a cache hit.</summary>
public sealed class SymbolPresentationCache
{
    private static readonly string[] Keys = ["appearance", "animationSpeed", "state", "label", "value", "orientation", "variant", "feet", "reverseFlow", "displayStand", "displayAgitator", "displayFillLevel", "liquidOpacity", "liquidColor", "liquidWarningColor"];
    private static readonly Dictionary<string, int> Positions = Keys.Select((key, index) => (key, index)).ToDictionary(p => p.key, p => p.index, StringComparer.Ordinal);
    private readonly Scalar[] _committed = new Scalar[Keys.Length], _pending = new Scalar[Keys.Length];
    private readonly Func<string, Scalar> _readPending;
    private SymbolOptions? _result;
    private SymbolDefaults? _defaults;
    private string? _type;
    private double _legacyValue;
    private bool _reading;
    public long BuildCount { get; private set; }
    public SymbolPresentationCache() => _readPending = key => _pending[Positions[key]];

    public SymbolOptions Read(string type, Func<string, Scalar> read, SymbolDefaults defaults, double legacyValue = 0)
    {
        ArgumentNullException.ThrowIfNull(read); ArgumentNullException.ThrowIfNull(defaults);
        if (_reading) throw new InvalidOperationException("Symbol presentation reads cannot be reentered.");
        if (type is not ("Pump" or "Motor" or "Valve" or "Sensor" or "Vessel") || !double.IsFinite(legacyValue)) throw new ArgumentException("Invalid symbol family or legacy value.");
        _reading = true;
        try
        {
            var unchanged = _result is not null && _type == type && _defaults == defaults && _legacyValue == legacyValue;
            for (var i = 0; i < Keys.Length; i++)
            {
                _pending[i] = read(Keys[i]);
                if (_pending[i] != _committed[i]) unchanged = false;
            }
            if (unchanged) return _result!;
            var result = SymbolPresentation.Read(type, _readPending, defaults, legacyValue);
            Array.Copy(_pending, _committed, Keys.Length);
            _type = type; _defaults = defaults; _legacyValue = legacyValue; _result = result; BuildCount++;
            return result;
        }
        finally { _reading = false; }
    }
}
