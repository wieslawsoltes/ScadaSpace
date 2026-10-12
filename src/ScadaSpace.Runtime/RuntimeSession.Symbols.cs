using ScadaSpace.Core;

namespace ScadaSpace.Runtime;

public sealed partial class RuntimeSession
{
    private SymbolDefaults _symbols = new();
    /// <summary>Local automatic appearance/speed and reduced-motion preferences. Shared by this session's view hosts.</summary>
    public SymbolDefaults Symbols
    {
        get => _symbols;
        set { ArgumentNullException.ThrowIfNull(value); value.Validate(); _symbols = value; }
    }
}
