namespace ScadaSpace.Designer;

/// <summary>Bounded, literal, case-insensitive filtering for a flat property inspector.</summary>
/// <remarks>Query state is local UI state. Matching and equivalent updates allocate nothing.</remarks>
public sealed class PropertyFilter
{
    public const int MaximumQueryLength = 256;
    public string Query { get; private set; } = "";
    public bool BoundOnly { get; private set; }
    public long Revision { get; private set; }

    public bool Set(string query, bool boundOnly = false)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > MaximumQueryLength) throw new ArgumentOutOfRangeException(nameof(query), "Property filters are limited to 256 characters.");
        var normalized = query.AsSpan().Trim();
        if (BoundOnly == boundOnly && normalized.Equals(Query.AsSpan(), StringComparison.OrdinalIgnoreCase)) return false;
        Query = normalized.ToString(); BoundOnly = boundOnly; Revision++;
        return true;
    }

    /// <param name="path">A precomputed qualified name, for example props.liquidColor.</param>
    public bool Matches(string path, bool bound = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        return (!BoundOnly || bound) && (Query.Length == 0 || path.Contains(Query, StringComparison.OrdinalIgnoreCase));
    }
}
