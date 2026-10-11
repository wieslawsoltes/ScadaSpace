using System.Collections.ObjectModel;

namespace ScadaSpace.Core;

public sealed partial class EventAction
{
    /// <summary>Disabled actions remain configured but are skipped by dispatch.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Owned immutable action configuration, suitable for editors and dispatch snapshots.</summary>
public sealed record ActionConfiguration(ActionKind Kind, string Target, Scalar Value, Scalar Parameters, bool Enabled = true)
{
    public static ActionConfiguration From(EventAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!Enum.IsDefined(action.Kind) || action.Target is null || action.Target.Length > 4096 || action.Target.Any(char.IsControl)
            || !action.Value.IsValid() || action.Parameters is null || action.Parameters.Count > 256
            || action.Parameters.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 256 || p.Key.Any(char.IsControl) || !p.Value.IsValid()))
            throw new ArgumentException("Invalid action type, target, value or parameters.");
        return new(action.Kind, action.Target, action.Value, Scalar.Object(action.Parameters), action.Enabled);
    }

    public EventAction ToAction()
    {
        if (Parameters.Kind != ScalarKind.Object || !Parameters.IsValid()) throw new ArgumentException("Action parameters must be an object.");
        var action = new EventAction { Kind = Kind, Target = Target, Value = Value, Enabled = Enabled, Parameters = new(Parameters.Fields, StringComparer.Ordinal) };
        _ = From(action); // Validate before returning a mutable, detached instance.
        return action;
    }
}

public static class EventConfigurations
{
    public const int MaximumHandlers = 64;
    public const int MaximumActions = 256;

    public static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl)) throw new ArgumentException("Use an event name of 1–128 visible characters.");
    }

    public static IReadOnlyList<ActionConfiguration> Snapshot(IReadOnlyList<EventAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count > MaximumActions) throw new ArgumentException("At most 256 actions are allowed per event.");
        return Array.AsReadOnly(actions.Select(ActionConfiguration.From).ToArray());
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<ActionConfiguration>> Snapshot(IReadOnlyDictionary<string, List<EventAction>> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count > MaximumHandlers) throw new ArgumentException("At most 64 handlers are allowed per component.");
        var result = new Dictionary<string, IReadOnlyList<ActionConfiguration>>(StringComparer.Ordinal);
        foreach (var (name, actions) in events) { ValidateName(name); result.Add(name, Snapshot(actions)); }
        return new ReadOnlyDictionary<string, IReadOnlyList<ActionConfiguration>>(result);
    }

    /// <summary>Only events wired by the current Uno host are advertised as supported.</summary>
    public static IReadOnlyList<string> Supported(string type)
    {
        var names = new List<string> { "onClick", "onDoubleClick", "onContextMenu" };
        if (type == "Button") names.InsertRange(0, ["onActionPerformed", "onAction"]);
        if (type is "Tree" or "TagBrowseTree") names.Insert(0, "onItemClicked");
        if (type == "Table") names.InsertRange(0, ["onRowClick", "onEditCellCommit"]);
        return names.AsReadOnly();
    }
}
