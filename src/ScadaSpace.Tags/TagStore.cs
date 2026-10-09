using ScadaSpace.Core;

namespace ScadaSpace.Tags;

public enum WriteStatus { Good, NotFound, ReadOnly, BadType, OutOfRange, RevisionConflict, Disabled }
public sealed record WriteResult(WriteStatus Status, QualifiedValue Current, string Message = "")
{
    public bool Success => Status == WriteStatus.Good;
}
public sealed record TagChange(string Path, QualifiedValue Previous, QualifiedValue Current);

public interface ITagProvider
{
    QualifiedValue Read(string path);
    IReadOnlyList<TagDefinition> Browse(string prefix = "");
    WriteResult Write(string path, Scalar value, string actor, long? expectedRevision = null);
    IDisposable Subscribe(Action<TagChange> listener);
}

/// <summary>Thread-safe in-process provider. It is a simulator/memory provider, not an OPC-UA client.</summary>
public sealed class TagStore : ITagProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TagDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualifiedValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<long, Action<TagChange>> _listeners = [];
    private readonly TimeProvider _clock;
    private long _sequence;
    private long _subscription;
    public AuditLog Audit { get; }
    public bool WritesEnabled { get; set; } = true;
    public Action<Exception>? SubscriberError { get; set; }

    public TagStore(IEnumerable<TagDefinition> definitions, AuditLog? audit = null, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        Audit = audit ?? new();
        foreach (var definition in definitions)
        {
            if (!TagPaths.IsValid(definition.Path) || definition.DataType != definition.InitialValue.Kind || !definition.InitialValue.IsValid()) throw new ArgumentException("Invalid tag definition.");
            if (_definitions.ContainsKey(definition.Path)) throw new ArgumentException("Duplicate tag path.");
            var copy = Copy(definition);
            if (copy.Minimum > copy.Maximum || (copy.Minimum.HasValue && !double.IsFinite(copy.Minimum.Value)) || (copy.Maximum.HasValue && !double.IsFinite(copy.Maximum.Value))) throw new ArgumentException("Invalid tag range.");
            _definitions.Add(copy.Path, copy);
            _values.Add(copy.Path, QualifiedValue.Good(copy.InitialValue, _clock.GetUtcNow(), ++_sequence));
        }
    }
    private static TagDefinition Copy(TagDefinition d) => new()
    {
        Path = d.Path, DataType = d.DataType, InitialValue = d.InitialValue, Writable = d.Writable,
        Unit = d.Unit, Minimum = d.Minimum, Maximum = d.Maximum, HistoryEnabled = d.HistoryEnabled, HistoryDeadband = d.HistoryDeadband
    };
    public QualifiedValue Read(string path)
    {
        lock (_gate) return _values.TryGetValue(path, out var value) ? value : QualifiedValue.Bad(Quality.BadNotFound, _clock.GetUtcNow());
    }
    public IReadOnlyList<TagDefinition> Browse(string prefix = "")
    {
        lock (_gate) return _definitions.Values.Where(x => x.Path.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(x => x.Path, StringComparer.Ordinal).Select(Copy).ToArray();
    }
    public Dictionary<string, QualifiedValue> Snapshot() { lock (_gate) return new(_values, StringComparer.Ordinal); }
    public WriteResult Write(string path, Scalar value, string actor, long? expectedRevision = null) => Update(path, value, Quality.Good, actor, expectedRevision, false);
    public WriteResult Publish(string path, Scalar value, Quality quality = Quality.Good) => Update(path, value, quality, "simulator", null, true);
    private WriteResult Update(string path, Scalar value, Quality quality, string actor, long? expected, bool publish)
    {
        TagChange change;
        Action<TagChange>[] listeners;
        lock (_gate)
        {
            if (!_definitions.TryGetValue(path, out var definition)) return new(WriteStatus.NotFound, QualifiedValue.Bad(Quality.BadNotFound, _clock.GetUtcNow()));
            var old = _values[path];
            WriteResult Reject(WriteStatus status, string message)
            {
                Audit.Append(_clock.GetUtcNow(), actor, "write-rejected", path, message);
                return new(status, old, message);
            }
            if (!publish && !WritesEnabled) return Reject(WriteStatus.Disabled, "Provider writes are disabled.");
            if (!publish && !definition.Writable) return Reject(WriteStatus.ReadOnly, "Tag is read-only.");
            if (expected.HasValue && expected.Value != old.Revision) return Reject(WriteStatus.RevisionConflict, "Tag changed since it was read.");
            if (value.Kind != definition.DataType || !value.IsValid()) return Reject(WriteStatus.BadType, "Value does not match tag type.");
            if (value.Kind == ScalarKind.Number && (value.Number < definition.Minimum || value.Number > definition.Maximum)) return Reject(WriteStatus.OutOfRange, "Value is outside engineering limits.");
            var current = new QualifiedValue(value, quality, _clock.GetUtcNow(), ++_sequence);
            _values[path] = current;
            change = new(path, old, current);
            listeners = _listeners.Values.ToArray();
            if (!publish) Audit.Append(current.Timestamp, actor, "write-tag", path, $"{old.Value} -> {value}");
        }
        // A bad subscriber must never roll back an already committed write or starve other subscribers.
        foreach (var listener in listeners)
        {
            try { listener(change); }
            catch (Exception ex) { try { SubscriberError?.Invoke(ex); } catch { /* Error reporting cannot abort publication. */ } }
        }
        return new(WriteStatus.Good, change.Current);
    }
    public IDisposable Subscribe(Action<TagChange> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        long id;
        lock (_gate) { id = ++_subscription; _listeners.Add(id, listener); }
        return new Subscription(() => { lock (_gate) _listeners.Remove(id); });
    }
    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
