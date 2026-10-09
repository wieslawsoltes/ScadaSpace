using ScadaSpace.Alarms;
using ScadaSpace.Core;
using ScadaSpace.Expressions;
using ScadaSpace.History;
using ScadaSpace.Tags;

namespace ScadaSpace.Runtime;

public sealed class ProjectRuntime : IDisposable
{
    public ProjectDefinition Project { get; private set; }
    public AuditLog Audit { get; } = new();
    public TagStore Tags { get; }
    public Historian History { get; }
    public AlarmEngine Alarms { get; }
    public ExpressionEngine Expressions { get; } = new();
    public ProjectRuntime(ProjectDefinition project, TimeProvider? clock = null)
    {
        var errors = ProjectCodec.Validate(project);
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors.Select(x => x.Message)));
        Project = project; Tags = new(project.Tags, Audit, clock); History = new(Tags); Alarms = new(Tags, project.Alarms, Audit, clock);
    }
    public ViewInstance CreateView(string path, IReadOnlyDictionary<string, Scalar>? parameters = null) => new(Project.Views.First(v => v.Path == path), Tags, parameters, Expressions);
    public void UpdateViews(ProjectDefinition project) => Project = project;
    public void Dispose() { Alarms.Dispose(); History.Dispose(); }
}

public sealed record SessionMessage(string Type, IReadOnlyDictionary<string, Scalar> Payload);
public sealed record PopupInstance(string Id, string View, IReadOnlyDictionary<string, Scalar> Parameters);

/// <summary>Local session navigation and declarative event actions. No arbitrary scripting is evaluated.</summary>
public sealed class RuntimeSession(ProjectRuntime runtime)
{
    private readonly Stack<string> _back = new();
    private readonly Dictionary<string, PopupInstance> _popups = new(StringComparer.Ordinal);
    public string CurrentPath { get; private set; } = runtime.Project.Pages.FirstOrDefault()?.Path ?? "/";
    public string Actor { get; set; } = "demo-operator";
    public IReadOnlyCollection<PopupInstance> Popups => _popups.Values;
    public event Action? Changed;
    public event Action<SessionMessage>? Message;
    public bool Navigate(string path)
    {
        if (!runtime.Project.Pages.Any(x => x.Path == path)) return false;
        if (path != CurrentPath) { _back.Push(CurrentPath); CurrentPath = path; Changed?.Invoke(); }
        return true;
    }
    public bool Back() { if (!_back.TryPop(out var path)) return false; CurrentPath = path; Changed?.Invoke(); return true; }
    public void Dispatch(ComponentNode node, string eventName)
    {
        if (!node.Events.TryGetValue(eventName, out var actions)) return;
        foreach (var action in actions)
        {
            switch (action.Kind)
            {
                case ActionKind.WriteTag:
                    var write = runtime.Tags.Write(action.Target, action.Value, Actor);
                    if (!write.Success) throw new InvalidOperationException(write.Message);
                    break;
                case ActionKind.ToggleTag:
                    var old = runtime.Tags.Read(action.Target);
                    var toggle = runtime.Tags.Write(action.Target, Scalar.From(!old.Value.AsBoolean()), Actor, old.Revision);
                    if (!toggle.Success) throw new InvalidOperationException(toggle.Message);
                    break;
                case ActionKind.Navigate:
                    if (!Navigate(action.Target)) throw new InvalidOperationException("Unknown page route: " + action.Target);
                    break;
                case ActionKind.OpenPopup:
                    if (_popups.Count >= 16 || !runtime.Project.Views.Any(x => x.Path == action.Target)) throw new InvalidOperationException("Unknown popup view or popup limit reached.");
                    _popups[action.Target] = new(action.Target, action.Target, new Dictionary<string, Scalar>(action.Parameters)); Changed?.Invoke(); break;
                case ActionKind.ClosePopup: ClosePopup(action.Target); break;
                case ActionKind.SendMessage: Message?.Invoke(new(action.Target, new Dictionary<string, Scalar>(action.Parameters))); break;
                default: throw new InvalidOperationException("Unsupported action.");
            }
        }
    }
    public void ClosePopup(string id) { if (_popups.Remove(id)) Changed?.Invoke(); }
}

/// <summary>Deterministic demonstration only; no network/device connections are made.</summary>
public sealed class PlantSimulator(TagStore tags)
{
    private double _time;
    public bool Running { get; set; } = true;
    public void Advance(TimeSpan elapsed)
    {
        if (!Running) return;
        var dt = Math.Clamp(elapsed.TotalSeconds, 0, 1); _time += dt;
        double Read(string suffix) { var sample = tags.Read("[default]Plant/" + suffix); return sample.IsGood ? sample.Value.AsNumber() : 0; }
        void Publish(string suffix, double value) => tags.Publish("[default]Plant/" + suffix, Scalar.From(value));
        var enabled = tags.Read("[default]Plant/Pump/Enabled").Value.AsBoolean();
        var target = Read("Tank01/Setpoint"); var level = Read("Tank01/Level");
        level = Math.Clamp(level + (enabled ? (target - level) * 0.045 : -0.5) * dt, 0, 100);
        Publish("Tank01/Level", level);
        Publish("Tank02/Level", Math.Clamp(Read("Tank02/Level") + (level - Read("Tank02/Level")) * 0.02 * dt, 0, 100));
        Publish("Pump/Flow", enabled ? 112 + 8 * Math.Sin(_time * 0.25) : 0);
        Publish("Tank01/Temperature", 44 + 8 * Math.Sin(_time * 0.08));
        Publish("Pump/Pressure", enabled ? 3.8 + 0.2 * Math.Sin(_time * 0.4) : 0);
    }
}
