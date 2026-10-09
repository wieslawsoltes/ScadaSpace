using System.Globalization;
using System.Text.Json.Serialization;

namespace ScadaSpace.Core;

public enum ScalarKind { Null, Number, Boolean, String }
public enum Quality { Good, Uncertain, Stale, BadNotFound, BadType, BadExpression, BadDisabled }

/// <summary>A closed, immutable value type. It never carries executable code or mutable JSON objects.</summary>
public readonly record struct Scalar(ScalarKind Kind, double Number = 0, bool Boolean = false, string? Text = null)
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
        _ => ""
    };
    public static Scalar Parse(string text, ScalarKind kind) => kind switch
    {
        ScalarKind.Number => From(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
        ScalarKind.Boolean => From(bool.Parse(text)),
        ScalarKind.String => From(text),
        _ => Null
    };
    public bool IsValid() => Enum.IsDefined(Kind) && (Kind != ScalarKind.Number || double.IsFinite(Number)) && (Text?.Length ?? 0) <= 65536;
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
