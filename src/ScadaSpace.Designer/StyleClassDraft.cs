using System.Collections.ObjectModel;
using ScadaSpace.Core;

namespace ScadaSpace.Designer;

public enum StyleRenameMode { UpdateAssignments, PreserveAssignments }

/// <summary>Detached style editing with indexed references and atomic class/assignment updates.</summary>
public sealed class StyleClassDraft
{
    private readonly DesignerDocument _document;
    private long _generation;
    private readonly Dictionary<string, IReadOnlyDictionary<string, Scalar>> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _assignments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _references = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _names;
    public IReadOnlyList<string> Names => _names ??= Array.AsReadOnly(_classes.Keys.Order(StringComparer.Ordinal).ToArray());
    public bool IsDirty { get; private set; }
    public long Revision { get; private set; }
    public StyleClassDraft(DesignerDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document)); _generation = document.Generation;
        StyleRules.ValidateClasses(document.Project.Styles);
        foreach (var (name, properties) in document.Project.Styles) _classes.Add(name, Own(properties));
        foreach (var node in document.Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()))
        {
            StyleRules.ValidateAssignments(node.StyleClasses);
            var names = Array.AsReadOnly(node.StyleClasses.ToArray()); _assignments.Add(node.Id, names);
            IndexReferences(node.Id, names, true);
        }
    }
    private static IReadOnlyDictionary<string, Scalar> Own(IReadOnlyDictionary<string, Scalar> values) =>
        new ReadOnlyDictionary<string, Scalar>(new Dictionary<string, Scalar>(values, StringComparer.Ordinal));
    private static bool Same(IReadOnlyDictionary<string, Scalar> a, IReadOnlyDictionary<string, Scalar> b)
    {
        if (a.Count != b.Count) return false;
        // a is our owned ordinal snapshot. b may use a different key comparer.
        foreach (var pair in b) if (!a.TryGetValue(pair.Key, out var value) || value != pair.Value) return false;
        return true;
    }
    public IReadOnlyDictionary<string, Scalar> Properties(string name) => _classes.TryGetValue(name, out var value) ? value : throw new ArgumentException("Unknown style class.");
    public IReadOnlyList<string> Assignments(string id) => _assignments.TryGetValue(id, out var value) ? value : throw new ArgumentException("Unknown component.");
    /// <summary>Counts distinct components, including dangling imported references, without a project scan.</summary>
    public int ReferenceCount(string name) => _references.TryGetValue(name, out var ids) ? ids.Count : 0;
    public IReadOnlyList<string> ReferencingComponents(string name) => _references.TryGetValue(name, out var ids)
        ? Array.AsReadOnly(ids.Order(StringComparer.Ordinal).ToArray()) : Array.Empty<string>();

    public void Create(string name, IReadOnlyDictionary<string, Scalar>? properties = null)
    {
        StyleRules.ValidateName(name); if (_classes.ContainsKey(name)) throw new ArgumentException("A style with that name already exists.");
        if (_classes.Count >= StyleRules.MaximumClasses) throw new ArgumentException("Too many style classes.");
        properties ??= new Dictionary<string, Scalar>(); StyleRules.ValidateValues(properties);
        _classes.Add(name, Own(properties)); Changed(true);
    }
    public void SetProperties(string name, IReadOnlyDictionary<string, Scalar> properties) => Update(name, properties);

    /// <summary>Validate and own both sides before mutating either side. A rejected assignment cannot retain a partial class edit.</summary>
    public void Update(string name, IReadOnlyDictionary<string, Scalar> properties, string? componentId = null, IEnumerable<string>? assignments = null)
    {
        var previous = Properties(name); StyleRules.ValidateValues(properties);
        if ((componentId is null) != (assignments is null)) throw new ArgumentException("Component and assignment list must be provided together.");
        var nextAssignments = componentId is not null ? PrepareAssignments(componentId, assignments!) : null;
        var propertiesChanged = !Same(previous, properties);
        var assignmentsChanged = componentId is not null && !Assignments(componentId).SequenceEqual(nextAssignments!);
        if (!propertiesChanged && !assignmentsChanged) return;
        var owned = propertiesChanged ? Own(properties) : previous;
        if (propertiesChanged) _classes[name] = owned;
        if (assignmentsChanged) ReplaceAssignments(componentId!, nextAssignments!);
        Changed();
    }
    private IReadOnlyList<string> PrepareAssignments(string id, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names); var previous = Assignments(id);
        var next = names.Take(StyleRules.MaximumAssignments + 1).ToArray(); StyleRules.ValidateAssignments(next);
        if (next.Any(n => !_classes.ContainsKey(n) && !previous.Contains(n, StringComparer.Ordinal)))
            throw new ArgumentException("Cannot assign a missing style class.");
        return Array.AsReadOnly(next.Distinct(StringComparer.Ordinal).ToArray());
    }
    public void Assign(string id, IEnumerable<string> names)
    {
        var next = PrepareAssignments(id, names);
        if (Assignments(id).SequenceEqual(next)) return;
        ReplaceAssignments(id, next); Changed();
    }
    public void Rename(string name, string replacement, StyleRenameMode mode = StyleRenameMode.UpdateAssignments)
    {
        var values = Properties(name); StyleRules.ValidateName(replacement);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (name == replacement) return;
        if (_classes.ContainsKey(replacement)) throw new ArgumentException("A style with that name already exists.");
        _classes.Remove(name); _classes.Add(replacement, values);
        // PreserveAssignments follows Perspective's documented rename behavior. Updating
        // references is an explicit local convenience, retained as the API default.
        if (mode == StyleRenameMode.UpdateAssignments) RewriteReferences(name, replacement);
        Changed(true);
    }
    public void Delete(string name)
    {
        _ = Properties(name); _classes.Remove(name); RewriteReferences(name, null); Changed(true);
    }
    private void IndexReferences(string id, IEnumerable<string> names, bool add)
    {
        foreach (var name in names)
        {
            if (add)
            {
                if (!_references.TryGetValue(name, out var ids)) _references.Add(name, ids = new(StringComparer.Ordinal));
                ids.Add(id);
            }
            else if (_references.TryGetValue(name, out var ids))
            {
                ids.Remove(id); if (ids.Count == 0) _references.Remove(name);
            }
        }
    }
    private void ReplaceAssignments(string id, IReadOnlyList<string> names)
    {
        IndexReferences(id, _assignments[id], false); _assignments[id] = names; IndexReferences(id, names, true);
    }
    private void RewriteReferences(string name, string? replacement)
    {
        if (!_references.TryGetValue(name, out var references)) return;
        foreach (var id in references.ToArray())
        {
            var rewritten = Array.AsReadOnly(_assignments[id].Select(n => n == name ? replacement : n)
                .OfType<string>().Distinct(StringComparer.Ordinal).ToArray());
            ReplaceAssignments(id, rewritten);
        }
    }
    private void Changed(bool names = false) { if (names) _names = null; IsDirty = true; Revision++; }
    private bool MatchesProject(ProjectDefinition project)
    {
        if (_classes.Count != project.Styles.Count) return false;
        foreach (var (name, properties) in _classes)
            if (!project.Styles.TryGetValue(name, out var existing) || !Same(properties, existing)) return false;
        foreach (var node in project.Views.SelectMany(v => v.Root.DescendantsAndSelf()))
            if (!_assignments.TryGetValue(node.Id, out var names) || !names.SequenceEqual(node.StyleClasses)) return false;
        return true;
    }
    public bool Apply(DesignerDocument document)
    {
        if (!ReferenceEquals(document, _document) || document.Generation != _generation)
            throw new InvalidOperationException("The project changed while this editor was open. Reopen it before applying.");
        if (!IsDirty) return false;
        // Rename-then-restore may change Dictionary iteration order without changing any
        // style. Do not create full-project undo snapshots for semantic no-ops.
        if (MatchesProject(document.Project)) { IsDirty = false; return false; }
        var classes = _classes.ToDictionary(p => p.Key, p => new Dictionary<string, Scalar>(p.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        StyleRules.ValidateClasses(classes); var before = document.Generation;
        try
        {
            document.Execute("Edit style classes", project =>
            {
                project.Styles = classes;
                foreach (var node in project.Views.SelectMany(v => v.Root.DescendantsAndSelf()))
                    if (!node.StyleClasses.SequenceEqual(_assignments[node.Id])) node.StyleClasses = _assignments[node.Id].ToList();
            });
            _generation = document.Generation; IsDirty = false;
        }
        finally
        {
            if (document.Generation == before + 1 && ReferenceEquals(document.Project.Styles, classes))
            { _generation = document.Generation; IsDirty = false; }
        }
        return document.Generation != before;
    }
}
