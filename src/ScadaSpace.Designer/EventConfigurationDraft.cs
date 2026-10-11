using ScadaSpace.Core;

namespace ScadaSpace.Designer;

/// <summary>Detached component-scoped editing. Invalid drafts never modify the project; Apply checks the document generation.</summary>
public sealed class EventConfigurationDraft
{
    private readonly DesignerDocument _document;
    private readonly Dictionary<string, IReadOnlyList<ActionConfiguration>> _events = new(StringComparer.Ordinal);
    private long _generation;
    private IReadOnlyList<string>? _eventNames;
    public string ComponentId { get; }
    public string ComponentName { get; }
    public string ComponentType { get; }
    public long Revision { get; private set; }
    public bool IsDirty { get; private set; }
    public IReadOnlyList<string> EventNames => _eventNames ??= Array.AsReadOnly(EventConfigurations.Supported(ComponentType).Concat(_events.Keys).Distinct(StringComparer.Ordinal).ToArray());

    public EventConfigurationDraft(DesignerDocument document, string id)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        var component = document.Find(id) ?? throw new ArgumentException("Unknown component.");
        ComponentId = id; ComponentName = component.Name; ComponentType = component.Type; _generation = document.Generation;
        foreach (var (name, actions) in EventConfigurations.Snapshot(component.Events)) _events.Add(name, actions);
    }

    public IReadOnlyList<ActionConfiguration> Actions(string name) => _events.TryGetValue(name, out var list) ? list : Array.Empty<ActionConfiguration>();

    public void SetActions(string name, IEnumerable<ActionConfiguration> actions)
    {
        EventConfigurations.ValidateName(name); ArgumentNullException.ThrowIfNull(actions);
        var next = actions.Take(EventConfigurations.MaximumActions + 1).ToList();
        if (next.Count > EventConfigurations.MaximumActions) throw new ArgumentException("At most 256 actions are allowed per event.");
        foreach (var action in next) { ArgumentNullException.ThrowIfNull(action); _ = action.ToAction(); }
        if (Actions(name).SequenceEqual(next)) return;
        if (!_events.ContainsKey(name) && _events.Count >= EventConfigurations.MaximumHandlers) throw new ArgumentException("Too many configured events.");
        var membershipChanged = next.Count == 0 || !_events.ContainsKey(name);
        if (next.Count == 0) _events.Remove(name); else _events[name] = next.AsReadOnly();
        if (membershipChanged) _eventNames = null;
        Revision++; IsDirty = true;
    }

    public void Add(string name, ActionConfiguration action) => SetActions(name, Actions(name).Append(action));
    public void Replace(string name, int index, ActionConfiguration action)
    {
        var list = Actions(name).ToArray(); CheckIndex(index, list.Length); list[index] = action; SetActions(name, list);
    }
    public void Remove(string name, int index)
    {
        var list = Actions(name).ToList(); CheckIndex(index, list.Count); list.RemoveAt(index); SetActions(name, list);
    }
    public void Move(string name, int index, int delta)
    {
        var list = Actions(name).ToList(); CheckIndex(index, list.Count);
        if (delta is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(delta));
        var target = index + delta; if (target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]); SetActions(name, list);
    }
    private static void CheckIndex(int index, int count) { if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index)); }

    public bool Apply(DesignerDocument document)
    {
        if (!ReferenceEquals(document, _document) || document.Generation != _generation) throw new InvalidOperationException("The project changed while this editor was open. Reopen it before applying.");
        if (!IsDirty) return false;
        var events = _events.ToDictionary(p => p.Key, p => p.Value.Select(a => a.ToAction()).ToList(), StringComparer.Ordinal);
        EventConfigurations.Validate(events);
        var before = document.Generation;
        try
        {
            document.Execute("Configure component events", _ => (document.Find(ComponentId) ?? throw new InvalidOperationException("Component was removed.")).Events = events);
            _generation = document.Generation; IsDirty = false;
        }
        finally
        {
            // Changed observers run after commit. Do not leave this editor stale when
            // an observer throws after a successful commit, or swallow that exception.
            if (document.Generation == before + 1 && ReferenceEquals(document.Find(ComponentId)?.Events, events))
            { _generation = document.Generation; IsDirty = false; }
        }
        return document.Generation != before;
    }
}
