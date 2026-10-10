using ScadaSpace.Core;
using ScadaSpace.Tags;

namespace ScadaSpace.Alarms;

public sealed record AlarmEvent(Guid EventId, string AlarmId, string Name, string TagPath, AlarmPriority Priority,
    DateTimeOffset ActiveTime, DateTimeOffset? ClearTime, DateTimeOffset? AckTime, string? AckUser, double Value,
    DateTimeOffset? ShelvedUntil = null)
{
    public bool Active => ClearTime is null;
    public bool Acknowledged => AckTime is not null;
    public string State => $"{(Active ? "Active" : "Cleared")}, {(Acknowledged ? "Acknowledged" : "Unacknowledged")}";
}
public sealed record AlarmJournalEntry(long Sequence, DateTimeOffset Timestamp, Guid EventId, string Name, string Transition, string Actor);

/// <summary>Four-state alarm lifecycle with deadband, on/off delay, explicit acknowledgements and timed shelving.</summary>
public sealed class AlarmEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly ITagProvider _tags;
    private readonly TimeProvider _clock;
    private readonly AuditLog _audit;
    private readonly List<State> _states;
    private readonly Dictionary<Guid, AlarmEvent> _events = [];
    private readonly Queue<AlarmJournalEntry> _journal = [];
    private readonly IDisposable _subscription;
    private long _sequence;
    private readonly int _retention;
    private sealed class State(AlarmDefinition definition)
    {
        public AlarmDefinition Definition { get; } = definition;
        public bool Active;
        public DateTimeOffset? PendingSince;
        public Guid? EventId;
        public DateTimeOffset? ShelvedUntil;
    }
    public AlarmEngine(ITagProvider tags, IEnumerable<AlarmDefinition> definitions, AuditLog? audit = null, TimeProvider? clock = null, int retention = 4096)
    {
        if (retention < 1) throw new ArgumentOutOfRangeException(nameof(retention));
        _tags = tags; _clock = clock ?? TimeProvider.System; _audit = audit ?? new(); _retention = retention;
        _states = definitions.Select(d => new State(new AlarmDefinition { Id = d.Id, Name = d.Name, TagPath = d.TagPath, Mode = d.Mode, Priority = d.Priority, Setpoint = d.Setpoint, Deadband = d.Deadband, ActiveDelaySeconds = d.ActiveDelaySeconds, ClearDelaySeconds = d.ClearDelaySeconds, Enabled = d.Enabled })).ToList();
        _subscription = tags.Subscribe(_ => Evaluate());
        Evaluate();
    }
    public void Evaluate()
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            foreach (var state in _states)
            {
                var d = state.Definition;
                if (state.ShelvedUntil is { } expiry && expiry <= now)
                {
                    state.ShelvedUntil = null;
                    if (state.EventId is { } id && _events.TryGetValue(id, out var ev)) { _events[id] = ev with { ShelvedUntil = null }; Journal(id, d.Name, "Unshelved", "system", now); }
                }
                if (!d.Enabled) { state.PendingSince = null; continue; }
                var sample = _tags.Read(d.TagPath);
                if (!sample.IsGood) { state.PendingSince = null; continue; } // Bad quality never silently clears an alarm.
                double value;
                try { value = sample.Value.AsNumber(); } catch (InvalidOperationException) { continue; }
                var desired = d.Mode switch
                {
                    AlarmMode.High => state.Active ? value > d.Setpoint - d.Deadband : value >= d.Setpoint,
                    AlarmMode.Low => state.Active ? value < d.Setpoint + d.Deadband : value <= d.Setpoint,
                    AlarmMode.Equal => value == d.Setpoint,
                    AlarmMode.NotEqual => value != d.Setpoint,
                    _ => false
                };
                if (desired == state.Active) { state.PendingSince = null; continue; }
                state.PendingSince ??= now;
                var delay = desired ? d.ActiveDelaySeconds : d.ClearDelaySeconds;
                if ((now - state.PendingSince.Value).TotalSeconds < delay) continue;
                state.PendingSince = null; state.Active = desired;
                if (desired)
                {
                    var id = Guid.NewGuid(); state.EventId = id;
                    _events[id] = new(id, d.Id, d.Name, d.TagPath, d.Priority, now, null, null, null, value, state.ShelvedUntil);
                    Journal(id, d.Name, "Active", "system", now);
                }
                else if (state.EventId is { } id && _events.TryGetValue(id, out var ev))
                {
                    _events[id] = ev with { ClearTime = now, Value = value };
                    Journal(id, d.Name, "Cleared", "system", now);
                }
            }
            // Unacknowledged events are intentionally retained: never silently discard a pending operator action.
            var removable = _events.Values.Where(e => !e.Active && e.Acknowledged).OrderBy(e => e.ActiveTime).ToArray();
            foreach (var ev in removable.Take(Math.Max(0, _events.Count - _retention))) _events.Remove(ev.EventId);
        }
    }
    public bool Acknowledge(Guid id, string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Acknowledgement requires an actor.");
        lock (_gate)
        {
            if (!_events.TryGetValue(id, out var ev) || ev.Acknowledged) return false;
            var now = _clock.GetUtcNow();
            _events[id] = ev with { AckTime = now, AckUser = actor };
            Journal(id, ev.Name, "Acknowledged", actor, now);
            _audit.Append(now, actor, "acknowledge-alarm", id.ToString(), ev.Name);
            return true;
        }
    }
    public bool Shelve(Guid id, TimeSpan duration, string actor)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Shelving requires an actor.");
        lock (_gate)
        {
            if (!_events.TryGetValue(id, out var ev) || !ev.Active) return false;
            var now = _clock.GetUtcNow(); var until = now + duration;
            var state = _states.First(x => x.Definition.Id == ev.AlarmId);
            state.ShelvedUntil = until; _events[id] = ev with { ShelvedUntil = until };
            Journal(id, ev.Name, "Shelved", actor, now);
            _audit.Append(now, actor, "shelve-alarm", id.ToString(), until.ToString("O"));
            return true;
        }
    }
    public AlarmEvent[] Status(bool includeShelved = true)
    {
        lock (_gate) return _events.Values.Where(e => (e.Active || !e.Acknowledged) && (includeShelved || e.ShelvedUntil is null || e.ShelvedUntil <= _clock.GetUtcNow())).OrderByDescending(e => e.Priority).ThenByDescending(e => e.ActiveTime).ToArray();
    }
    public AlarmEvent[] Events() { lock (_gate) return _events.Values.OrderByDescending(e => e.ActiveTime).ToArray(); }
    public AlarmJournalEntry[] Journal() { lock (_gate) return _journal.ToArray(); }
    private void Journal(Guid id, string name, string transition, string actor, DateTimeOffset now)
    {
        _journal.Enqueue(new(++_sequence, now, id, name, transition, actor));
        while (_journal.Count > _retention) _journal.Dequeue();
    }
    public void Dispose() => _subscription.Dispose();
}
