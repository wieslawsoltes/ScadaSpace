using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace ScadaSpace.Core;

/// <summary>Immutable, structurally comparable storage for objects and arrays.</summary>
public sealed class StructuredValue : IEquatable<StructuredValue>
{
    public const int MaximumNodes = 10000;
    public const int MaximumDepth = 24;
    private readonly IReadOnlyDictionary<string, Scalar>? _fields;
    private readonly IReadOnlyList<Scalar>? _items;
    public bool IsArray => _items is not null;
    public int Count => _items?.Count ?? _fields!.Count;
    public int NodeCount { get; }
    public int Depth { get; }
    public IReadOnlyDictionary<string, Scalar> Fields => _fields ?? throw new InvalidOperationException("Not an object.");
    public IReadOnlyList<Scalar> Items => _items ?? throw new InvalidOperationException("Not an array.");
    private readonly int _hash;
    internal StructuredValue(IEnumerable<KeyValuePair<string, Scalar>>? fields, IEnumerable<Scalar>? items)
    {
        var count = 1; var depth = 1;
        void Check(Scalar value)
        {
            if (!value.IsValid()) throw new ArgumentException("Invalid structured property value.");
            count = checked(count + (value.Structured?.NodeCount ?? 1));
            depth = Math.Max(depth, 1 + (value.Structured?.Depth ?? 0));
            if (count > MaximumNodes || depth > MaximumDepth) throw new ArgumentException("Structured property exceeds its node or depth limit.");
        }
        var hash = new HashCode();
        if (items is not null)
        {
            var copy = new List<Scalar>(); hash.Add(ScalarKind.Array);
            foreach (var value in items) { Check(value); copy.Add(value); hash.Add(value); }
            _items = copy.AsReadOnly();
        }
        else
        {
            ArgumentNullException.ThrowIfNull(fields);
            var copy = new Dictionary<string, Scalar>(StringComparer.Ordinal);
            foreach (var (key, value) in fields)
            {
                if (key is null || key.Length > 512) throw new ArgumentException("Object keys must contain at most 512 characters.");
                Check(value);
                if (!copy.TryAdd(key, value)) throw new ArgumentException("Duplicate object key: " + key);
            }
            hash.Add(ScalarKind.Object);
            foreach (var (key, value) in copy.OrderBy(x => x.Key, StringComparer.Ordinal)) { hash.Add(key, StringComparer.Ordinal); hash.Add(value); }
            _fields = new ReadOnlyDictionary<string, Scalar>(copy);
        }
        NodeCount = count; Depth = depth; _hash = hash.ToHashCode();
    }
    public bool Equals(StructuredValue? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || IsArray != other.IsArray || Count != other.Count || _hash != other._hash) return false;
        return IsArray ? Items.SequenceEqual(other.Items) : Fields.All(p => other.Fields.TryGetValue(p.Key, out var value) && value == p.Value);
    }
    public override bool Equals(object? obj) => obj is StructuredValue other && Equals(other);
    public override int GetHashCode() => _hash;
}

public readonly partial record struct Scalar
{
    public static Scalar Object(IEnumerable<KeyValuePair<string, Scalar>> fields) => new(ScalarKind.Object, Structured: new(fields, null));
    public static Scalar Array(IEnumerable<Scalar> items) => new(ScalarKind.Array, Structured: new(null, items));
    public IReadOnlyDictionary<string, Scalar> Fields => Kind == ScalarKind.Object ? Structured!.Fields : throw new InvalidOperationException("Not an object.");
    public IReadOnlyList<Scalar> Items => Kind == ScalarKind.Array ? Structured!.Items : throw new InvalidOperationException("Not an array.");
    public static Scalar FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > ProjectCodec.MaximumBytes) throw new FormatException("JSON exceeds 8 MiB.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = StructuredValue.MaximumDepth });
        var budget = StructuredValue.MaximumNodes;
        Scalar Read(JsonElement element)
        {
            if (--budget < 0) throw new FormatException("JSON exceeds the property node limit.");
            return element.ValueKind switch
            {
                JsonValueKind.Null => Null,
                JsonValueKind.True => From(true), JsonValueKind.False => From(false),
                JsonValueKind.Number => From(element.GetDouble()),
                JsonValueKind.String => From(element.GetString()),
                JsonValueKind.Array => Array(element.EnumerateArray().Select(Read)),
                JsonValueKind.Object => Object(element.EnumerateObject().Select(p => new KeyValuePair<string, Scalar>(p.Name, Read(p.Value)))),
                _ => throw new FormatException("Unsupported JSON value.")
            };
        }
        var value = Read(document.RootElement);
        if (!value.IsValid()) throw new FormatException("Invalid JSON value.");
        return value;
    }
    public string ToJson()
    {
        if (!IsValid()) throw new InvalidOperationException("Invalid property value.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteNative(writer, this);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void WriteNative(Utf8JsonWriter writer, Scalar value)
    {
        switch (value.Kind)
        {
            case ScalarKind.Null: writer.WriteNullValue(); break;
            case ScalarKind.Number: writer.WriteNumberValue(value.Number); break;
            case ScalarKind.Boolean: writer.WriteBooleanValue(value.Boolean); break;
            case ScalarKind.String: writer.WriteStringValue(value.Text); break;
            case ScalarKind.Array:
                writer.WriteStartArray(); foreach (var item in value.Items) WriteNative(writer, item); writer.WriteEndArray(); break;
            case ScalarKind.Object:
                writer.WriteStartObject();
                foreach (var (key, item) in value.Fields.OrderBy(p => p.Key, StringComparer.Ordinal)) { writer.WritePropertyName(key); WriteNative(writer, item); }
                writer.WriteEndObject(); break;
            default: throw new InvalidOperationException("Unknown value kind.");
        }
    }
}

/// <summary>Bounded, non-executable property paths: field.child[0]["quoted.key"]. Writes copy only the modified branch.</summary>
public sealed class PropertyPath
{
    public readonly record struct Segment(string? Key, int Index = -1);
    public IReadOnlyList<Segment> Segments { get; }
    private PropertyPath(List<Segment> segments) => Segments = segments.AsReadOnly();
    public static PropertyPath Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length is < 1 or > 2048) throw new FormatException("A property path must contain 1–2048 characters.");
        var result = new List<Segment>(); var i = 0;
        void Field()
        {
            var start = i;
            while (i < path.Length && path[i] is not ('.' or '[' or ']')) i++;
            if (i == start || path[start..i].Any(char.IsControl)) throw new FormatException("Invalid property name.");
            result.Add(new(path[start..i]));
        }
        if (path[0] != '[') Field();
        while (i < path.Length)
        {
            if (result.Count >= 32) throw new FormatException("Property path exceeds 32 segments.");
            if (path[i] == '.') { i++; Field(); continue; }
            if (path[i++] != '[' || i >= path.Length) throw new FormatException("Invalid property path.");
            if (path[i] == '"')
            {
                var start = i++; var closed = false;
                while (i < path.Length)
                {
                    if (path[i] == '\\') { i += 2; continue; }
                    if (path[i++] == '"') { closed = true; break; }
                }
                if (!closed) throw new FormatException("Unclosed property key.");
                result.Add(new(JsonSerializer.Deserialize<string>(path[start..i])!));
            }
            else
            {
                var start = i; while (i < path.Length && char.IsAsciiDigit(path[i])) i++;
                if (!int.TryParse(path[start..i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)) throw new FormatException("Invalid array index.");
                result.Add(new(null, index));
            }
            if (i >= path.Length || path[i++] != ']') throw new FormatException("Unclosed property index.");
        }
        if (result.Count == 0 || result.Count > 32) throw new FormatException("Invalid property path.");
        return new(result);
    }
    public bool TryRead(Scalar root, out Scalar value)
    {
        value = root;
        foreach (var part in Segments)
        {
            if (part.Key is { } key)
            {
                if (value.Kind != ScalarKind.Object || !value.Fields.TryGetValue(key, out var next)) { value = Scalar.Null; return false; }
                value = next;
            }
            else
            {
                if (value.Kind != ScalarKind.Array || part.Index >= value.Items.Count) { value = Scalar.Null; return false; }
                value = value.Items[part.Index];
            }
        }
        return true;
    }
    public Scalar Set(Scalar root, Scalar value)
    {
        if (!value.IsValid()) throw new ArgumentException("Invalid property value.");
        Scalar Visit(Scalar parent, int offset)
        {
            if (offset == Segments.Count) return value;
            var part = Segments[offset];
            if (part.Key is { } key)
            {
                if (parent.Kind != ScalarKind.Object) throw new InvalidOperationException("Property parent is not an object.");
                if (!parent.Fields.TryGetValue(key, out var old) && offset < Segments.Count - 1) throw new KeyNotFoundException("Missing intermediate property: " + key);
                var updated = Visit(old, offset + 1);
                if (parent.Fields.ContainsKey(key) && old == updated) return parent;
                var fields = parent.Fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal); fields[key] = updated;
                return Scalar.Object(fields);
            }
            if (parent.Kind != ScalarKind.Array || part.Index >= parent.Items.Count) throw new IndexOutOfRangeException("Array index outside the property value.");
            var item = Visit(parent.Items[part.Index], offset + 1);
            if (item == parent.Items[part.Index]) return parent;
            var items = parent.Items.ToArray(); items[part.Index] = item; return Scalar.Array(items);
        }
        return Visit(root, 0);
    }
}
