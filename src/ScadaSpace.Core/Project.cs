using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScadaSpace.Core;

public enum LayoutKind { Coordinate, FlexRow, FlexColumn, Column, Breakpoint, Tab }
public enum BindingKind { Tag, Property, Expression, ExpressionStructure, TagHistory }
public enum TransformKind { Expression, Scale, Format, Map }
public enum ActionKind { WriteTag, ToggleTag, Navigate, OpenPopup, ClosePopup, SendMessage, OpenDock, CloseDock, ToggleDock, NavigateBack, NavigateForward }

public sealed partial class TransformDefinition
{
    public TransformKind Kind { get; set; }
    public string Expression { get; set; } = "value";
    public double InputMin { get; set; }
    public double InputMax { get; set; } = 100;
    public double OutputMin { get; set; }
    public double OutputMax { get; set; } = 100;
    public string Format { get; set; } = "0.0";
    public Dictionary<string, Scalar> Map { get; set; } = new(StringComparer.Ordinal);
}

public sealed partial class BindingDefinition
{
    public BindingKind Kind { get; set; }
    public string Source { get; set; } = "";
    public bool Bidirectional { get; set; }
    public bool Enabled { get; set; } = true;
    public List<TransformDefinition> Transforms { get; set; } = [];
}

public sealed partial class EventAction
{
    public ActionKind Kind { get; set; }
    public string Target { get; set; } = "";
    public Scalar Value { get; set; }
    public Dictionary<string, Scalar> Parameters { get; set; } = new(StringComparer.Ordinal);
}

public sealed partial class ComponentNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Component";
    public string Type { get; set; } = "Label";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 160;
    public double Height { get; set; } = 48;
    public double Rotation { get; set; }
    public bool Locked { get; set; }
    public LayoutKind Layout { get; set; } = LayoutKind.Coordinate;
    public Dictionary<string, Scalar> Props { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Scalar> Custom { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, Scalar> Style { get; set; } = new(StringComparer.Ordinal);
    public List<string> StyleClasses { get; set; } = [];
    public Dictionary<string, BindingDefinition> Bindings { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<EventAction>> Events { get; set; } = new(StringComparer.Ordinal);
    public List<ComponentNode> Children { get; set; } = [];
    public Scalar Get(string name, Scalar fallback = default) => Props.GetValueOrDefault(name, fallback);
    public string Text(string name, string fallback = "") => Get(name, Scalar.From(fallback)).ToString();
    public double Number(string name, double fallback = 0) { try { return Get(name, Scalar.From(fallback)).AsNumber(); } catch (InvalidOperationException) { return fallback; } }
    public bool Flag(string name, bool fallback = false) => Get(name, Scalar.From(fallback)).AsBoolean();
    public IEnumerable<ComponentNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var node in child.DescendantsAndSelf()) yield return node;
    }
}

public sealed partial class ViewDefinition
{
    public string Path { get; set; } = "Main";
    public double Width { get; set; } = 1000;
    public double Height { get; set; } = 700;
    public Dictionary<string, Scalar> Parameters { get; set; } = new(StringComparer.Ordinal);
    public ComponentNode Root { get; set; } = new() { Name = "root", Type = "Container", Width = 1000, Height = 700 };
}

public sealed partial class PageDefinition
{
    public string Path { get; set; } = "/";
    public string View { get; set; } = "Main";
    public string Title { get; set; } = "Overview";
    public Dictionary<string, Scalar> Parameters { get; set; } = new(StringComparer.Ordinal);
}

public sealed partial class TagDefinition
{
    public string Path { get; set; } = "[default]Tag";
    public ScalarKind DataType { get; set; } = ScalarKind.Number;
    public Scalar InitialValue { get; set; } = Scalar.From(0);
    public bool Writable { get; set; } = true;
    public string Unit { get; set; } = "";
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public bool HistoryEnabled { get; set; } = true;
    public double HistoryDeadband { get; set; }
}

public enum AlarmMode { High, Low, Equal, NotEqual }
public enum AlarmPriority { Diagnostic, Low, Medium, High, Critical }
public sealed partial class AlarmDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "High limit";
    public string TagPath { get; set; } = "";
    public AlarmMode Mode { get; set; } = AlarmMode.High;
    public AlarmPriority Priority { get; set; } = AlarmPriority.Medium;
    public double Setpoint { get; set; } = 80;
    public double Deadband { get; set; } = 2;
    public double ActiveDelaySeconds { get; set; }
    public double ClearDelaySeconds { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed partial class ProjectDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Water Treatment";
    public long Revision { get; set; }
    public List<ViewDefinition> Views { get; set; } = [];
    public List<PageDefinition> Pages { get; set; } = [];
    public List<TagDefinition> Tags { get; set; } = [];
    public List<AlarmDefinition> Alarms { get; set; } = [];
    public Dictionary<string, Dictionary<string, Scalar>> Styles { get; set; } = new(StringComparer.Ordinal);
}

public static partial class ProjectCodec
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, MaxDepth = 64,
        Converters = { new JsonStringEnumConverter() }
    };
    public static string Serialize(ProjectDefinition project) => JsonSerializer.Serialize(project, Options);
    public static ProjectDefinition Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new FormatException("Project exceeds the 8 MiB limit.");
        var project = JsonSerializer.Deserialize<ProjectDefinition>(json, Options) ?? throw new FormatException("Project is null.");
        var errors = Validate(project);
        if (errors.Count > 0) throw new FormatException(string.Join("\n", errors.Select(x => $"{x.Path}: {x.Message}")));
        return project;
    }
    public static ProjectDefinition Clone(ProjectDefinition project) => Deserialize(Serialize(project));
    public static List<Diagnostic> Validate(ProjectDefinition project)
    {
        var errors = new List<Diagnostic>();
        void Error(string message, string path) => errors.Add(new("PROJECT_INVALID", message, path));
        if (project.SchemaVersion != 1) Error("Unsupported schema version.", "schemaVersion");
        if (string.IsNullOrWhiteSpace(project.Name)) Error("Name is required.", "name");
        if (project.Views is null || project.Tags is null || project.Pages is null || project.Alarms is null || project.Styles is null)
        { Error("Project collections cannot be null.", "project"); return errors; }
        if (project.Views.Count is < 1 or > 512) Error("Projects need 1–512 views.", "views");
        if (project.Tags.Count > 100000) Error("Too many tags.", "tags");
        var viewNames = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        bool Finite(double n) => double.IsFinite(n) && Math.Abs(n) <= 1000000;
        void Node(ComponentNode? node, string path, int depth)
        {
            if (node is null) { Error("Null component.", path); return; }
            if (++count > 10000 || depth > 24) { Error("Component count or nesting limit exceeded.", path); return; }
            if (string.IsNullOrWhiteSpace(node.Id) || !ids.Add(node.Id)) Error("Component IDs must be globally unique.", path);
            if (!Finite(node.X) || !Finite(node.Y) || !Finite(node.Width) || !Finite(node.Height) || !Finite(node.Rotation) || node.Width <= 0 || node.Height <= 0) Error("Invalid component bounds.", path);
            if (node.Props is null || node.Style is null || node.Custom is null || node.Bindings is null || node.Children is null || node.Events is null || node.StyleClasses is null)
            { Error("Component collections cannot be null.", path); return; }
            foreach (var value in node.Props.Values.Concat(node.Style.Values).Concat(node.Custom.Values)) if (!value.IsValid()) Error("Invalid property value.", path);
            foreach (var binding in node.Bindings.Values)
            {
                if (binding is null || binding.Source is null || binding.Source.Length > 16384 || !Enum.IsDefined(binding.Kind) || binding.Transforms is null) { Error("Invalid binding.", path); continue; }
                if (binding.Bidirectional && (binding.Kind is not (BindingKind.Tag or BindingKind.Property) || binding.Transforms.Count > 0)) Error("Bidirectional writes require an untransformed tag or property binding.", path);
                foreach (var t in binding.Transforms)
                    if (t is null || !Enum.IsDefined(t.Kind) || t.Map is null || t.Expression is null || t.Format is null || !Finite(t.InputMin) || !Finite(t.InputMax) || !Finite(t.OutputMin) || !Finite(t.OutputMax)) Error("Invalid transform.", path);
            }
            foreach (var child in node.Children) Node(child, path + "/" + child?.Name, depth + 1);
        }
        foreach (var view in project.Views)
        {
            if (view is null) { Error("Null view.", "views"); continue; }
            if (string.IsNullOrWhiteSpace(view.Path) || !viewNames.Add(view.Path)) Error("View paths must be unique.", "views");
            if (!Finite(view.Width) || !Finite(view.Height) || view.Width <= 0 || view.Height <= 0 || view.Parameters is null) Error("Invalid view.", view.Path);
            Node(view.Root, view.Path, 0);
        }
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in project.Pages)
            if (page is null || page.Path is null || !page.Path.StartsWith('/') || !routes.Add(page.Path) || !viewNames.Contains(page.View) || page.Parameters is null) Error("Invalid or duplicate page route or unknown view.", "pages");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in project.Tags)
        {
            if (tag is null) { Error("Null tag.", "tags"); continue; }
            if (!TagPaths.IsValid(tag.Path) || !paths.Add(tag.Path)) Error("Invalid or duplicate tag path.", tag.Path);
            if (!Enum.IsDefined(tag.DataType) || tag.InitialValue.Kind != tag.DataType || !tag.InitialValue.IsValid()) Error("Initial value must match the declared type.", tag.Path);
            if ((tag.Minimum.HasValue && !double.IsFinite(tag.Minimum.Value)) || (tag.Maximum.HasValue && !double.IsFinite(tag.Maximum.Value)) || tag.Minimum > tag.Maximum || !double.IsFinite(tag.HistoryDeadband) || tag.HistoryDeadband < 0) Error("Invalid tag limits.", tag.Path);
            if (tag.DataType == ScalarKind.Number && (tag.InitialValue.Number < tag.Minimum || tag.InitialValue.Number > tag.Maximum)) Error("Initial value outside limits.", tag.Path);
        }
        var alarmIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alarm in project.Alarms)
            if (alarm is null || !alarmIds.Add(alarm.Id) || !paths.Contains(alarm.TagPath) || !double.IsFinite(alarm.Setpoint) || !double.IsFinite(alarm.Deadband) || alarm.Deadband < 0 || !double.IsFinite(alarm.ActiveDelaySeconds) || alarm.ActiveDelaySeconds < 0 || !double.IsFinite(alarm.ClearDelaySeconds) || alarm.ClearDelaySeconds < 0 || !Enum.IsDefined(alarm.Mode) || !Enum.IsDefined(alarm.Priority)) Error("Invalid alarm definition or unknown tag.", "alarms");
        errors.AddRange(ValidatePages(project));
        return errors;
    }
}

public static class TagPaths
{
    public static bool IsValid(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path[0] != '[' || path.Any(char.IsControl)) return false;
        var end = path.IndexOf(']');
        return end > 1 && end < path.Length - 1 && !path[(end + 1)..].StartsWith('/') && !path.EndsWith('/') && !path.Contains("//") && !path.Contains("..") && !path[(end + 1)..].Contains('[') && !path[(end + 1)..].Contains(']');
    }
}
