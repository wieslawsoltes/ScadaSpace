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
    public HistoryQueryCache HistoryQueries { get; }
    public AlarmEngine Alarms { get; }
    public ExpressionEngine Expressions { get; } = new();
    public ProjectRuntime(ProjectDefinition project, TimeProvider? clock = null)
    {
        var errors = ProjectCodec.Validate(project);
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors.Select(x => x.Message)));
        Project = project; Tags = new(project.Tags, Audit, clock); History = new(Tags); HistoryQueries = new(History); Alarms = new(Tags, project.Alarms, Audit, clock);
    }
    public ViewInstance CreateView(string path, IReadOnlyDictionary<string, Scalar>? parameters = null) => new ViewInstance(Project.Views.First(v => v.Path == path), Tags, parameters, Expressions).WithHistory(History, HistoryQueries);
    public void UpdateViews(ProjectDefinition project) => Project = project;
    public void Dispose() { Alarms.Dispose(); History.Dispose(); }
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
