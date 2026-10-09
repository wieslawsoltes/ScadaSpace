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

/// <summary>Thread-safe in-process provider with ordered notifications. It is not an OPC-UA client.</summary>
public sealed class TagStore : ITagProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TagDefinition> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualifiedValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<long, Action<TagChange>> _listeners = [];
    private readonly Queue<(TagChange Change, Action<TagChange>[] Listeners)> _pending = new();
    private readonly TimeProvider _clock;
    private long _sequence;
    private long _subscription;
    private bool _publishing;
    private bool _writesEnabled = true;
    public AuditLog Audit { get; }
    public bool WritesEnabled { get { lock (_gate) return _writesEnabled; } set { lock (_gate) _writesEnabled = value; } }
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
            if (copy.InitialValue.Kind == ScalarKind.Number && (copy.InitialValue.Number < copy.Minimum || copy.InitialValue.Number > copy.Maximum)) throw new ArgumentException("Initial tag value is outside engineering limits.");
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
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Writes require an actor.", nameof(actor));
        if (!Enum.IsDefined(quality)) throw new ArgumentException("Invalid quality.", nameof(quality));
        TagChange change;
        var drain = false;
        lock (_gate)
        {
            if (!_definitions.TryGetValue(path, out var definition)) return new(WriteStatus.NotFound, QualifiedValue.Bad(Quality.BadNotFound, _clock.GetUtcNow()), "Tag was not found.");
            var old = _values[path];
            WriteResult Reject(WriteStatus status, string message)
            {
                Audit.Append(_clock.GetUtcNow(), actor, "write-rejected", path, message);
                return new(status, old, message);
            }
            if (!publish && !_writesEnabled) return Reject(WriteStatus.Disabled, "Provider writes are disabled.");
            if (!publish && !definition.Writable) return Reject(WriteStatus.ReadOnly, "Tag is read-only.");
            if (expected.HasValue && expected.Value != old.Revision) return Reject(WriteStatus.RevisionConflict, "Tag changed since it was read.");
            if (value.Kind != definition.DataType || !value.IsValid()) return Reject(WriteStatus.BadType, "Value does not match tag type.");
            if (value.Kind == ScalarKind.Number && (value.Number < definition.Minimum || value.Number > definition.Maximum)) return Reject(WriteStatus.OutOfRange, "Value is outside engineering limits.");
            if (_pending.Count >= 65536) return Reject(WriteStatus.Disabled, "Subscriber notification queue is full; backpressure prevented this write.");
            var current = new QualifiedValue(value, quality, _clock.GetUtcNow(), ++_sequence);
            _values[path] = current;
            change = new(path, old, current);
            _pending.Enqueue((change, _listeners.Values.ToArray()));
            if (!_publishing) { _publishing = true; drain = true; }
            if (!publish) Audit.Append(current.Timestamp, actor, "write-tag", path, $"{old.Value} -> {value}");
        }
        // One drainer preserves commit order, including when a subscriber writes another tag reentrantly.
        if (drain) Drain();
        return new(WriteStatus.Good, change.Current);
    }
    private void Drain()
    {
        while (true)
        {
            (TagChange Change, Action<TagChange>[] Listeners) next;
            lock (_gate)
            {
                if (!_pending.TryDequeue(out next)) { _publishing = false; return; }
            }
            foreach (var listener in next.Listeners)
            {
                try { listener(next.Change); }
                catch (Exception ex) { try { SubscriberError?.Invoke(ex); } catch { /* Reporting cannot abort already committed publication. */ } }
            }
        }
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
