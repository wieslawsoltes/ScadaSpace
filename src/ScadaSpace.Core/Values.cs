using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScadaSpace.Core;

public enum ScalarKind { Null, Number, Boolean, String, Object, Array }
public enum Quality { Good, Uncertain, Stale, BadNotFound, BadType, BadExpression, BadDisabled }

/// <summary>A closed, immutable value type. It never carries executable code or mutable JSON objects.</summary>
[JsonConverter(typeof(ScalarJsonConverter))]
public readonly partial record struct Scalar(ScalarKind Kind, double Number = 0, bool Boolean = false, string? Text = null, StructuredValue? Structured = null)
{
    public static Scalar Null => default;
    public static Scalar From(double value) => double.IsFinite(value) ? new(ScalarKind.Number, value) : throw new ArgumentOutOfRangeException(nameof(value), "Values must be finite.");
    public static Scalar From(bool value) => new(ScalarKind.Boolean, Boolean: value);
    public static Scalar From(string? value) => value is null ? Null : new(ScalarKind.String, Text: value);
    public double AsNumber() => Kind switch
    {
        ScalarKind.Number => Number,
        ScalarKind.Boolean => Boolean ? 1 : 0,
        ScalarKind.String when double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) => n,
        _ => throw new InvalidOperationException("A numeric value is required.")
    };
    public bool AsBoolean() => Kind switch
    {
        ScalarKind.Boolean => Boolean,
        ScalarKind.Number => Number != 0,
        ScalarKind.Null => false,
        ScalarKind.String => bool.TryParse(Text, out var value) ? value : !string.IsNullOrEmpty(Text),
        _ => false
    };
    public override string ToString() => Kind switch
    {
        ScalarKind.Number => Number.ToString("0.###", CultureInfo.InvariantCulture),
        ScalarKind.Boolean => Boolean ? "true" : "false",
        ScalarKind.String => Text ?? "",
        ScalarKind.Object or ScalarKind.Array => ToJson(),
        _ => ""
    };
    public static Scalar Parse(string text, ScalarKind kind) => kind switch
    {
        ScalarKind.Number => From(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
        ScalarKind.Boolean => From(bool.Parse(text)),
        ScalarKind.String => From(text),
        ScalarKind.Null => Null,
        ScalarKind.Object or ScalarKind.Array => ParseStructured(text, kind),
        _ => throw new FormatException("Unknown scalar type.")
    };
    private static Scalar ParseStructured(string text, ScalarKind kind)
    {
        var value = FromJson(text);
        return value.Kind == kind ? value : throw new FormatException("JSON does not match the property type.");
    }
    public bool IsValid() => Enum.IsDefined(Kind) && double.IsFinite(Number) && (Text?.Length ?? 0) <= 65536 &&
        (Kind is ScalarKind.Object or ScalarKind.Array
            ? Structured is not null && Structured.IsArray == (Kind == ScalarKind.Array)
            : Structured is null);
}

/// <summary>Explicit, reflection-free tagged-value wire format, shared by desktop and WebAssembly.</summary>
public sealed class ScalarJsonConverter : JsonConverter<Scalar>
{
    public override Scalar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("A scalar must be a tagged object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
            if (!fields.TryAdd(property.Name, property.Value)) throw new JsonException("Duplicate scalar property: " + property.Name);
        if (!fields.TryGetValue("kind", out var token)) throw new JsonException("A scalar kind is required.");
        ScalarKind kind;
        if (token.ValueKind == JsonValueKind.String)
        {
            if (!Enum.TryParse(token.GetString(), true, out kind) || !Enum.IsDefined(kind)) throw new JsonException("Unknown scalar kind.");
        }
        else if (token.ValueKind == JsonValueKind.Number && token.TryGetInt32(out var number) && Enum.IsDefined((ScalarKind)number)) kind = (ScalarKind)number;
        else throw new JsonException("Invalid scalar kind.");
        try
        {
            var result = kind switch
            {
                ScalarKind.Null => Scalar.Null,
                ScalarKind.Number when fields.TryGetValue("number", out var n) => Scalar.From(n.GetDouble()),
                ScalarKind.Boolean when fields.TryGetValue("boolean", out var b) => Scalar.From(b.GetBoolean()),
                ScalarKind.String when fields.TryGetValue("text", out var s) && s.ValueKind == JsonValueKind.String => Scalar.From(s.GetString()),
                ScalarKind.Array when fields.TryGetValue("items", out var items) && items.ValueKind == JsonValueKind.Array => Scalar.Array(items.EnumerateArray().Select(x => x.Deserialize<Scalar>(options))),
                ScalarKind.Object when fields.TryGetValue("fields", out var values) && values.ValueKind == JsonValueKind.Object => Scalar.Object(values.EnumerateObject().Select(x => new KeyValuePair<string, Scalar>(x.Name, x.Value.Deserialize<Scalar>(options)))),
                _ => throw new JsonException("The scalar payload does not match its kind.")
            };
            if (!result.IsValid()) throw new JsonException("Invalid scalar payload.");
            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        { throw new JsonException("Invalid scalar payload: " + ex.Message, ex); }
    }
    public override void Write(Utf8JsonWriter writer, Scalar value, JsonSerializerOptions options)
    {
        if (!value.IsValid()) throw new JsonException("Cannot serialize an invalid scalar.");
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind.ToString());
        writer.WriteNumber("number", value.Number);
        writer.WriteBoolean("boolean", value.Boolean);
        if (value.Text is null) writer.WriteNull("text"); else writer.WriteString("text", value.Text);
        if (value.Kind == ScalarKind.Array)
        {
            writer.WriteStartArray("items");
            foreach (var item in value.Items) JsonSerializer.Serialize(writer, item, options);
            writer.WriteEndArray();
        }
        if (value.Kind == ScalarKind.Object)
        {
            writer.WriteStartObject("fields");
            foreach (var (key, item) in value.Fields.OrderBy(p => p.Key, StringComparer.Ordinal))
            { writer.WritePropertyName(key); JsonSerializer.Serialize(writer, item, options); }
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
}

public readonly record struct QualifiedValue(Scalar Value, Quality Quality, DateTimeOffset Timestamp, long Revision = 0)
{
    [JsonIgnore] public bool IsGood => Quality == Quality.Good;
    public static QualifiedValue Good(Scalar value, DateTimeOffset now, long revision = 0) => new(value, Quality.Good, now, revision);
    public static QualifiedValue Bad(Quality quality, DateTimeOffset now) => new(Scalar.Null, quality, now);
}

public sealed record Diagnostic(string Code, string Message, string? Path = null);
public sealed record AuditEntry(long Sequence, DateTimeOffset Timestamp, string Actor, string Action, string Target, string Detail);

public sealed class AuditLog(int capacity = 4096)
{
    private readonly object _gate = new();
    private readonly Queue<AuditEntry> _entries = new();
    private long _sequence;
    public void Append(DateTimeOffset now, string actor, string action, string target, string detail)
    {
        lock (_gate)
        {
            _entries.Enqueue(new(++_sequence, now, actor, action, target, detail));
            while (_entries.Count > Math.Max(1, capacity)) _entries.Dequeue();
        }
    }
    public AuditEntry[] Snapshot() { lock (_gate) return _entries.ToArray(); }
}
