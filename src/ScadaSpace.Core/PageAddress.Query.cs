using System.Globalization;

namespace ScadaSpace.Core;

public sealed partial record PageAddress
{
    /// <summary>Merge typed query fields into a canonical address. Null removes a field; structured values are rejected.</summary>
    public PageAddress WithQuery(IReadOnlyDictionary<string, Scalar> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count > 64) throw new ArgumentException("At most 64 query fields are allowed.");
        var original = Parse(Url);
        var fields = new Dictionary<string, Scalar>(original.Query, StringComparer.Ordinal);
        foreach (var (key, value) in changes)
        {
            if (key.Length is < 1 or > 128 || key.Any(char.IsControl) || !value.IsValid()) throw new ArgumentException("Invalid query field.");
            if (value.Kind == ScalarKind.Null) { fields.Remove(key); continue; }
            if (value.Kind is not (ScalarKind.String or ScalarKind.Boolean or ScalarKind.Number)) throw new ArgumentException("Query fields require strings, booleans, numbers or null.");
            var text = value.Kind == ScalarKind.Number ? value.Number.ToString("R", CultureInfo.InvariantCulture) : value.ToString();
            if (text.Length > 2048 || text.Any(char.IsControl)) throw new ArgumentException("Invalid query value.");
            fields[key] = Scalar.From(text);
        }
        var query = string.Join('&', fields.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value.Text!)));
        return Parse(original.Path + (query.Length == 0 ? "" : "?" + query));
    }
}
