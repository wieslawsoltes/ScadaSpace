using System.Collections.ObjectModel;
using System.Globalization;

namespace ScadaSpace.Core;

public sealed record PageAddress(string Path, string Url, IReadOnlyList<string> Segments, IReadOnlyDictionary<string, Scalar> Query)
{
    public const int MaximumLength = 4096;
    public static PageAddress Parse(string url)
    {
        if (string.IsNullOrEmpty(url) || url.Length > MaximumLength || url[0] != '/' || url.Contains('#') || url.Contains('\\') || url.Any(char.IsControl)) throw new ArgumentException("Use an internal page URL beginning with /, without a fragment.");
        var queryAt = url.IndexOf('?'); var rawPath = queryAt < 0 ? url : url[..queryAt];
        if (rawPath.Contains("//", StringComparison.Ordinal)) throw new ArgumentException("Empty page path segments are not allowed.");
        if (rawPath.Length > 1) rawPath = rawPath.TrimEnd('/');
        var segments = rawPath == "/" ? [] : rawPath[1..].Split('/').Select(p => Decode(p, false)).ToArray();
        if (segments.Length > 32 || segments.Any(p => p.Length > 512 || p is "." or ".." || p.IndexOfAny(['/', '?', '#', '%', '\\']) >= 0 || p.Any(char.IsControl))) throw new ArgumentException("Invalid or excessive page path segments.");
        var query = new Dictionary<string, Scalar>(StringComparer.Ordinal);
        if (queryAt >= 0 && queryAt + 1 < url.Length)
        {
            var pairs = url[(queryAt + 1)..].Split('&');
            if (pairs.Length > 64) throw new ArgumentException("At most 64 query fields are allowed.");
            foreach (var pair in pairs)
            {
                if (pair.Length == 0) continue;
                var equals = pair.IndexOf('=');
                var key = Decode(equals < 0 ? pair : pair[..equals], true);
                var value = Decode(equals < 0 ? "" : pair[(equals + 1)..], true);
                if (key.Length is < 1 or > 128 || value.Length > 2048 || key.Any(char.IsControl) || value.Any(char.IsControl)) throw new ArgumentException("Invalid query field.");
                query[key] = Scalar.From(value); // Explicit last-value rule for duplicate query keys.
            }
        }
        var path = "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
        var suffix = string.Join('&', query.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value.Text!)));
        return new(path, path + (suffix.Length == 0 ? "" : "?" + suffix), Array.AsReadOnly(segments), new ReadOnlyDictionary<string, Scalar>(query));
    }
    private static string Decode(string value, bool query)
    {
        for (var i = 0; i < value.Length; i++)
            if (value[i] == '%')
            {
                if (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])) throw new ArgumentException("Malformed URL escape.");
                i += 2;
            }
        return Uri.UnescapeDataString(query ? value.Replace('+', ' ') : value);
    }
}

/// <summary>Literal and :parameter route segments, with bounded matching and unambiguous expansion.</summary>
public sealed class PageRoutePattern
{
    private readonly string[] _segments;
    private readonly bool[] _parameters;
    public string Shape { get; }
    public string Specificity { get; }
    public PageRoutePattern(string pattern)
    {
        if (pattern is null || pattern.Contains('?') || pattern.Contains('%')) throw new ArgumentException("Route patterns cannot contain query strings or escapes.");
        var address = PageAddress.Parse(pattern); _segments = address.Segments.ToArray();
        _parameters = _segments.Select(s => s.StartsWith(':')).ToArray(); var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < _segments.Length; i++)
        {
            var name = _segments[i][(_parameters[i] ? 1 : 0)..];
            if (_parameters[i] && (name.Length == 0 || !(char.IsAsciiLetter(name[0]) || name[0] == '_') || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')) || !names.Add(name))) throw new ArgumentException("Route parameter names must be unique identifiers.");
            if (!_parameters[i] && name.Contains(':')) throw new ArgumentException("A route parameter must occupy an entire segment.");
        }
        Shape = "/" + string.Join('/', _segments.Select((s, i) => _parameters[i] ? ":" : s));
        Specificity = string.Concat(_parameters.Select(p => p ? '0' : '1'));
    }
    public bool TryMatch(PageAddress address, out IReadOnlyDictionary<string, Scalar> parameters)
    {
        var values = new Dictionary<string, Scalar>(StringComparer.Ordinal); parameters = new ReadOnlyDictionary<string, Scalar>(values);
        if (address.Segments.Count != _segments.Length) return false;
        for (var i = 0; i < _segments.Length; i++)
            if (_parameters[i]) values[_segments[i][1..]] = Scalar.From(address.Segments[i]);
            else if (_segments[i] != address.Segments[i]) return false;
        return true;
    }
    public string Expand(IReadOnlyDictionary<string, Scalar> parameters)
    {
        var parts = new string[_segments.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!_parameters[i]) { parts[i] = _segments[i]; continue; }
            var name = _segments[i][1..];
            if (!parameters.TryGetValue(name, out var value) || !value.IsValid() || value.Kind is not (ScalarKind.String or ScalarKind.Number or ScalarKind.Boolean)) throw new ArgumentException("Missing route parameter: " + name);
            parts[i] = value.Kind == ScalarKind.Number ? value.Number.ToString("R", CultureInfo.InvariantCulture) : value.ToString();
            if (parts[i].Length == 0 || parts[i] is "." or ".." || parts[i].IndexOfAny(['/', '?', '#', '%', '\\']) >= 0) throw new ArgumentException("Invalid route parameter: " + name);
        }
        return PageAddress.Parse("/" + string.Join('/', parts.Select(Uri.EscapeDataString))).Url;
    }
}
