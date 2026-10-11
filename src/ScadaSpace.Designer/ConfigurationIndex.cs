using ScadaSpace.Core;

namespace ScadaSpace.Designer;

public enum ConfigurationKind { Binding, Transform, Event, Action, EmbeddedView }
public sealed record ConfigurationReference(string View, string ComponentId, string ComponentName, string Location, ConfigurationKind Kind, string Source)
{
    public override string ToString() => $"{View} / {ComponentName}    {Kind} · {Location}";
}

/// <summary>Generation-indexed, owned configuration inventory. Filtering never traverses the live component tree.</summary>
public sealed class ConfigurationIndex
{
    public const int MaximumEntries = 25000;
    private DesignerDocument? _document;
    private long _generation = -1;
    private IReadOnlyList<ConfigurationReference> _entries = Array.Empty<ConfigurationReference>();
    private IReadOnlyList<ConfigurationReference>? _query;
    private string _text = "";
    private ConfigurationKind? _kind;
    public long BuildCount { get; private set; }
    public long QueryBuildCount { get; private set; }
    public IReadOnlyList<ConfigurationReference> Entries => _entries;

    public void Refresh(DesignerDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (ReferenceEquals(document, _document) && document.Generation == _generation) return;
        var entries = new List<ConfigurationReference>();
        foreach (var view in document.Project.Views)
            foreach (var node in view.Root.DescendantsAndSelf())
            {
                void Add(ConfigurationKind kind, string location, string source)
                {
                    if (entries.Count >= MaximumEntries) throw new InvalidOperationException("Configuration inventory exceeds 25,000 entries.");
                    entries.Add(new(view.Path, node.Id, node.Name, location, kind, source));
                }
                foreach (var (path, binding) in node.Bindings)
                {
                    Add(ConfigurationKind.Binding, path, $"{binding.Kind} · {(binding.Enabled ? "Enabled" : "Disabled")}\n{binding.Source}");
                    for (var i = 0; i < binding.Transforms.Count; i++)
                    {
                        var transform = binding.Transforms[i];
                        Add(ConfigurationKind.Transform, path + "/transform/" + i, System.Text.Json.JsonSerializer.Serialize(transform, ProjectCodec.Options));
                    }
                }
                foreach (var (name, actions) in node.Events)
                {
                    Add(ConfigurationKind.Event, name, $"{actions.Count} actions · {(EventConfigurations.Supported(node.Type).Contains(name) ? "Host event" : "Custom/unwired event")}");
                    for (var i = 0; i < actions.Count; i++) Add(ConfigurationKind.Action, name + "/action/" + i, System.Text.Json.JsonSerializer.Serialize(actions[i], ProjectCodec.Options));
                }
                if (node.Type is "EmbeddedView" or "FlexRepeater") Add(ConfigurationKind.EmbeddedView, "view", node.Text("view"));
            }
        _entries = entries.AsReadOnly(); _document = document; _generation = document.Generation; _query = null; BuildCount++;
    }

    public IReadOnlyList<ConfigurationReference> Query(string text = "", ConfigurationKind? kind = null)
    {
        if (text is null || text.Length > 256 || kind.HasValue && !Enum.IsDefined(kind.Value)) throw new ArgumentException("Invalid configuration filter.");
        text = text.Trim();
        if (_query is not null && text == _text && kind == _kind) return _query;
        // Keep the no-op path above free of capturing lambdas and iterator allocation.
        return Filter(text, kind);
    }
    private IReadOnlyList<ConfigurationReference> Filter(string text, ConfigurationKind? kind)
    {
        var result = new List<ConfigurationReference>();
        foreach (var entry in _entries)
            if ((!kind.HasValue || entry.Kind == kind) && (text.Length == 0 || entry.View.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.ComponentName.Contains(text, StringComparison.OrdinalIgnoreCase) || entry.Location.Contains(text, StringComparison.OrdinalIgnoreCase)
                || entry.Source.Contains(text, StringComparison.OrdinalIgnoreCase))) result.Add(entry);
        _text = text; _kind = kind; QueryBuildCount++; return _query = result.AsReadOnly();
    }
}
