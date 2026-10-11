namespace ScadaSpace.Core;

public enum DockEdge { North, South, West, East }
public enum DockDisplay { Visible, OnDemand, Auto }
public enum DockContent { Push, Cover, Auto }
public enum CornerPriority { TopBottom, LeftRight }

/// <summary>One independently instantiated view on a page edge. Definitions are never runtime state.</summary>
public sealed record DockDefinition
{
    public string Id { get; init; } = "dock";
    public string View { get; init; } = "Main";
    public DockEdge Edge { get; init; }
    public DockDisplay Display { get; init; } = DockDisplay.OnDemand;
    public DockContent Content { get; init; } = DockContent.Push;
    public double Size { get; init; } = 240;
    public double MinimumSize { get; init; } = 48;
    public double MaximumSize { get; init; } = 1200;
    public double AutoBreakpoint { get; init; } = 800;
    public bool Resizable { get; init; } = true;
    public bool Modal { get; init; }
    public bool ShowHandle { get; init; } = true;
    public Dictionary<string, Scalar> Parameters { get; init; } = new(StringComparer.Ordinal);
}

public sealed partial class PageDefinition
{
    public List<DockDefinition> Docks { get; set; } = [];
    public CornerPriority? CornerPriority { get; set; }
}

public sealed partial class ProjectDefinition
{
    public List<DockDefinition> SharedDocks { get; set; } = [];
    public CornerPriority SharedCornerPriority { get; set; } = CornerPriority.TopBottom;
}

public static partial class ProjectCodec
{
    private static IEnumerable<Diagnostic> ValidatePages(ProjectDefinition project)
    {
        var errors = new List<Diagnostic>();
        void Error(string message, string path) => errors.Add(new("PAGE_INVALID", message, path));
        if (project.SharedDocks is null || !Enum.IsDefined(project.SharedCornerPriority))
        { Error("Invalid shared page settings.", "sharedDocks"); return errors; }
        if (project.Pages.Count > 512) Error("At most 512 page routes are supported.", "pages");
        var views = project.Views.Where(v => v is not null).Select(v => v.Path).ToHashSet(StringComparer.Ordinal);
        void Docks(List<DockDefinition>? docks, string path)
        {
            if (docks is null) { Error("Dock collection cannot be null.", path); return; }
            if (docks.Count > 4) Error("Only one dock per edge is supported.", path);
            var edges = new HashSet<DockEdge>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dock in docks)
            {
                if (dock is null) { Error("Null dock.", path); continue; }
                if (!Enum.IsDefined(dock.Edge) || !edges.Add(dock.Edge) || string.IsNullOrWhiteSpace(dock.Id) || dock.Id.Length > 128 || dock.Id.Any(char.IsControl) || !ids.Add(dock.Id)) Error("Invalid or duplicate dock ID/edge.", path);
                if (!views.Contains(dock.View)) Error("Unknown dock view.", path);
                if (!Enum.IsDefined(dock.Display) || !Enum.IsDefined(dock.Content) || dock.Modal && dock.Display != DockDisplay.OnDemand) Error("Invalid dock mode; modal docks must be on-demand.", path);
                if (!double.IsFinite(dock.Size) || !double.IsFinite(dock.MinimumSize) || !double.IsFinite(dock.MaximumSize) || dock.MinimumSize < 16 || dock.MaximumSize > 100000 || dock.MinimumSize > dock.MaximumSize || dock.Size < dock.MinimumSize || dock.Size > dock.MaximumSize || !double.IsFinite(dock.AutoBreakpoint) || dock.AutoBreakpoint < 0 || dock.AutoBreakpoint > 100000) Error("Invalid dock size or breakpoint.", path);
                if (dock.Parameters is null || dock.Parameters.Count > 256 || dock.Parameters.Any(p => string.IsNullOrEmpty(p.Key) || !p.Value.IsValid())) Error("Invalid dock parameters.", path);
            }
        }
        Docks(project.SharedDocks, "sharedDocks");
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in project.Pages)
        {
            if (page is null) continue;
            try
            {
                var route = new PageRoutePattern(page.Path);
                if (!shapes.Add(route.Shape)) Error("Ambiguous page route shape.", page.Path);
            }
            catch (ArgumentException ex) { Error(ex.Message, page.Path); }
            Docks(page.Docks, page.Path);
            if (page.CornerPriority.HasValue && !Enum.IsDefined(page.CornerPriority.Value)) Error("Invalid corner priority.", page.Path);
            if (page.Parameters is null || page.Parameters.Count > 256 || page.Parameters.Any(p => string.IsNullOrEmpty(p.Key) || !p.Value.IsValid())) Error("Invalid page parameters.", page.Path);
            if (page.Docks is not null && page.Docks.All(d => d is not null) && project.SharedDocks.All(d => d is not null))
            {
                var effective = project.SharedDocks.Where(shared => !page.Docks.Any(d => d.Edge == shared.Edge)).Concat(page.Docks).ToArray();
                if (effective.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != effective.Length) Error("Effective page dock IDs must be unique across shared and local edges.", page.Path);
            }
        }
        return errors;
    }
}
