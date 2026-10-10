using System.Text;
using ScadaSpace.Core;
using ScadaSpace.Expressions;
using ScadaSpace.Tags;

namespace ScadaSpace.Runtime;

public enum PropertyChangeOrigin { Binding, Local, Parameter }
public sealed record PropertyChange(string ComponentId, string Path, QualifiedValue Previous, QualifiedValue Current, PropertyChangeOrigin Origin);

/// <summary>Instance-scoped property graph. Runtime writes never mutate the project definition.</summary>
public sealed class ViewInstance
{
    private readonly ITagProvider _tags;
    private readonly ExpressionEngine _expressions;
    private readonly Dictionary<string, ComponentNode> _nodes;
    private readonly Dictionary<string, ComponentNode> _parents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualifiedValue> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualifiedValue> _rawValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualifiedValue> _locals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualifiedValue> _published = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Id, string Path)> _addresses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Scalar> _templates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);
    private readonly List<Diagnostic> _diagnostics = [];
    private long _revision;
    private bool _evaluating;
    public ViewDefinition View { get; }
    public Dictionary<string, Scalar> Parameters { get; }
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
    public IReadOnlyDictionary<string, QualifiedValue> Values => _values;
    public event Action<PropertyChange>? PropertyChanged;

    public ViewInstance(ViewDefinition view, ITagProvider tags, IReadOnlyDictionary<string, Scalar>? parameters = null, ExpressionEngine? expressions = null)
    {
        View = view; _tags = tags; _expressions = expressions ?? new();
        Parameters = new(view.Parameters, StringComparer.Ordinal);
        if (parameters is not null) foreach (var (key, value) in parameters) Parameters[key] = value;
        _nodes = view.Root.DescendantsAndSelf().ToDictionary(x => x.Id, StringComparer.Ordinal);
        foreach (var parent in _nodes.Values) foreach (var child in parent.Children) _parents[child.Id] = parent;
    }
    public void Evaluate()
    {
        if (_evaluating) throw new InvalidOperationException("Recursive view evaluation is not supported.");
        _evaluating = true;
        try
        {
            Invalidate(); _resolving.Clear(); _diagnostics.Clear();
            foreach (var node in _nodes.Values)
            {
                foreach (var property in node.Props.Keys) Read(node, property);
                foreach (var property in node.Custom.Keys) Read(node, "custom." + property);
                foreach (var property in node.Style.Keys) Read(node, "style." + property);
                foreach (var property in node.Bindings.Keys) Read(node, property);
            }
            var changes = new List<PropertyChange>();
            foreach (var (key, current) in _values)
            {
                if (_published.TryGetValue(key, out var old) && (old.Value != current.Value || old.Quality != current.Quality))
                {
                    var (id, path) = _addresses[key];
                    var origin = FindBinding(_nodes[id], path).Binding is not null ? PropertyChangeOrigin.Binding : PropertyChangeOrigin.Local;
                    changes.Add(new(id, path, old, current, origin));
                }
                _published[key] = current;
            }
            // A snapshot is dispatched after graph evaluation; observers may schedule new writes safely.
            foreach (var change in changes)
                if (PropertyChanged is { } handlers)
                    foreach (Action<PropertyChange> handler in handlers.GetInvocationList())
                        try { handler(change); } catch (Exception ex) { _diagnostics.Add(new("PROPERTY_OBSERVER", ex.Message, change.Path)); }
        }
        finally { _evaluating = false; }
    }
    private void Invalidate() { _values.Clear(); _rawValues.Clear(); }
    private static string Qualify(string property) => property.StartsWith("props.", StringComparison.Ordinal) || property.StartsWith("custom.", StringComparison.Ordinal) || property.StartsWith("style.", StringComparison.Ordinal) ? property : "props." + property;
    private static bool Below(string path, string parent) => path.Length > parent.Length && path.StartsWith(parent, StringComparison.Ordinal) && path[parent.Length] is '.' or '[';
    private static string Tail(string path, int length) => path.Length > length && path[length] == '.' ? path[(length + 1)..] : path[length..];
    private static (BindingDefinition? Binding, string Path) FindBinding(ComponentNode node, string path)
    {
        BindingDefinition? binding = null; var best = "";
        foreach (var pair in node.Bindings)
        {
            var candidate = Qualify(pair.Key);
            if ((path == candidate || Below(path, candidate)) && candidate.Length > best.Length) { binding = pair.Value; best = candidate; }
        }
        return (binding, best);
    }
    public QualifiedValue Read(ComponentNode node, string property)
    {
        if (!_nodes.TryGetValue(node.Id, out var own) || !ReferenceEquals(own, node)) throw new ArgumentException("Component belongs to another view instance.");
        var path = Qualify(property); var key = node.Id + "." + path; _addresses[key] = (node.Id, path);
        if (_values.TryGetValue(key, out var cached)) return cached;
        if (_resolving.Count >= 64 || !_resolving.Add(key)) throw new ExpressionException("Cyclic or excessively deep property dependency: " + key);
        var now = DateTimeOffset.UtcNow;
        var quality = Quality.Good; var timestamp = now; long revision = 0; var hasReference = false;
        try
        {
            Scalar Resolve(string reference)
            {
                var value = ResolveReference(reference, node);
                if (value.Quality > quality) quality = value.Quality;
                timestamp = !hasReference || value.Timestamp < timestamp ? value.Timestamp : timestamp;
                hasReference = true; revision = Math.Max(revision, value.Revision);
                if (!value.IsGood) throw new ExpressionException($"{reference}: {value.Quality}");
                return value.Value;
            }
            var (binding, boundPath) = FindBinding(node, path);
            QualifiedValue sample;
            if (binding is null) sample = Literal(node, path);
            else if (boundPath != path)
            {
                // Read a bound object's leaf without forcing unrelated siblings or creating false cycles.
                var parentKey = node.Id + "." + boundPath;
                var parent = _rawValues.TryGetValue(parentKey, out var raw) ? raw : Read(node, boundPath);
                sample = Extract(parent, Tail(path, boundPath.Length));
            }
            else if (!binding.Enabled) sample = QualifiedValue.Bad(Quality.BadDisabled, now);
            else
            {
                var result = binding.Kind switch
                {
                    BindingKind.Tag => Resolve(ResolveTagPath(binding.Source)),
                    BindingKind.Property => Resolve(binding.Source),
                    BindingKind.Expression => _expressions.Evaluate(binding.Source, Resolve),
                    BindingKind.ExpressionStructure => EvaluateStructure(StructureTemplate(binding.Source), Resolve),
                    _ => throw new ExpressionException("Unsupported binding type.")
                };
                foreach (var transform in binding.Transforms)
                {
                    var input = result;
                    result = transform.Kind switch
                    {
                        TransformKind.Expression => _expressions.Evaluate(transform.Expression, reference => reference == "value" ? input : Resolve(reference)),
                        TransformKind.Scale => transform.InputMax == transform.InputMin ? throw new ExpressionException("Scale input range is zero.") : Scalar.From(transform.OutputMin + (input.AsNumber() - transform.InputMin) / (transform.InputMax - transform.InputMin) * (transform.OutputMax - transform.OutputMin)),
                        TransformKind.Format => Scalar.From(NumberFormatting.Format(input.AsNumber(), transform.Format)),
                        TransformKind.Map => transform.Map.TryGetValue(input.ToString(), out var mapped) ? mapped : transform.Map.GetValueOrDefault("*", Scalar.Null),
                        _ => throw new ExpressionException("Unsupported transform.")
                    };
                    if (!result.IsValid()) throw new ExpressionException("Transform result is invalid.");
                }
                sample = new(result, quality, timestamp, revision);
            }
            _rawValues[key] = sample;
            // Assemble bindings targeting descendants into a single immutable container value.
            if (sample.IsGood)
                foreach (var nested in node.Bindings.Keys.Select(Qualify).Where(p => Below(p, path)).OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal))
                {
                    var leaf = Read(node, nested);
                    if (!leaf.IsGood) { sample = sample with { Quality = leaf.Quality }; break; }
                    sample = sample with { Value = PropertyPath.Parse(Tail(nested, path.Length)).Set(sample.Value, leaf.Value), Revision = Math.Max(sample.Revision, leaf.Revision), Timestamp = sample.Timestamp < leaf.Timestamp ? sample.Timestamp : leaf.Timestamp };
                }
            return _values[key] = sample;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException or IndexOutOfRangeException or System.Text.Json.JsonException)
        {
            _diagnostics.Add(new("BINDING_ERROR", ex.Message, key));
            return _values[key] = new(Scalar.Null, quality == Quality.Good ? Quality.BadExpression : quality, timestamp, revision);
        }
        finally { _resolving.Remove(key); }
    }
    private Scalar StructureTemplate(string source)
    {
        if (_templates.TryGetValue(source, out var template)) return template;
        if (_templates.Count >= 256) _templates.Clear();
        return _templates[source] = Scalar.FromJson(source);
    }
    private Scalar EvaluateStructure(Scalar template, Func<string, Scalar> resolve) => template.Kind switch
    {
        ScalarKind.Object => Scalar.Object(template.Fields.Select(p => new KeyValuePair<string, Scalar>(p.Key, EvaluateStructure(p.Value, resolve)))),
        ScalarKind.Array => Scalar.Array(template.Items.Select(p => EvaluateStructure(p, resolve))),
        ScalarKind.String => _expressions.Evaluate(template.Text!, resolve),
        _ => template
    };
    private static QualifiedValue Extract(QualifiedValue parent, string path) => !parent.IsGood ? parent : PropertyPath.Parse(path).TryRead(parent.Value, out var value) ? parent with { Value = value } : parent with { Value = Scalar.Null, Quality = Quality.BadNotFound };
    private static (string Root, string Rest) Split(string path)
    {
        var split = path.IndexOfAny(['.', '[']);
        return split < 0 ? (path, "") : (path[..split], Tail(path, split));
    }
    private Dictionary<string, Scalar> Scope(ComponentNode node, string scope) => scope switch { "props" => node.Props, "custom" => node.Custom, "style" => node.Style, _ => throw new ExpressionException("Unknown property scope.") };
    private QualifiedValue Literal(ComponentNode node, string path)
    {
        var dot = path.IndexOf('.'); var scope = path[..dot]; var property = path[(dot + 1)..]; var fields = Scope(node, scope);
        var key = node.Id + "." + path;
        if (_locals.TryGetValue(key, out var local)) return local;
        if (fields.TryGetValue(property, out var exact)) return QualifiedValue.Good(exact, DateTimeOffset.UtcNow);
        var (root, rest) = Split(property);
        if (rest.Length == 0) return QualifiedValue.Bad(Quality.BadNotFound, DateTimeOffset.UtcNow);
        var rootKey = node.Id + "." + scope + "." + root;
        var sample = _locals.TryGetValue(rootKey, out local) ? local : fields.TryGetValue(root, out var value) ? QualifiedValue.Good(value, DateTimeOffset.UtcNow) : QualifiedValue.Bad(Quality.BadNotFound, DateTimeOffset.UtcNow);
        return Extract(sample, rest);
    }
    private (ComponentNode Node, string Path) ComponentReference(string reference, ComponentNode current)
    {
        var marker = new[] { ".props.", ".custom.", ".style." }.Select(x => reference.IndexOf(x, StringComparison.Ordinal)).Where(x => x >= 0).DefaultIfEmpty(-1).Min();
        if (marker <= 0) throw new ExpressionException("References require component.props.name, component.custom.name, component.style.name or view.params.name.");
        var owner = reference[..marker]; var path = reference[(marker + 1)..];
        if (!path.StartsWith("props.", StringComparison.Ordinal) && !path.StartsWith("custom.", StringComparison.Ordinal) && !path.StartsWith("style.", StringComparison.Ordinal)) throw new ExpressionException("Unknown property scope.");
        ComponentNode? target = owner switch { "this" => current, "root" => View.Root, "parent" => _parents.GetValueOrDefault(current.Id), _ => _nodes.GetValueOrDefault(owner) };
        if (target is null)
        {
            var matching = _nodes.Values.Where(x => x.Name == owner).Take(2).ToArray();
            if (matching.Length == 1) target = matching[0];
        }
        return target is null ? throw new ExpressionException("Unknown or ambiguous component: " + owner) : (target, path);
    }
    public QualifiedValue ResolveReference(string reference, ComponentNode current)
    {
        if (reference.StartsWith('[')) return _tags.Read(reference);
        if (reference.StartsWith("view.params.", StringComparison.Ordinal))
        {
            var path = reference[12..];
            if (Parameters.TryGetValue(path, out var exact)) return QualifiedValue.Good(exact, DateTimeOffset.UtcNow, _revision);
            var (root, rest) = Split(path);
            return Parameters.TryGetValue(root, out var value) && rest.Length > 0 ? Extract(QualifiedValue.Good(value, DateTimeOffset.UtcNow, _revision), rest) : QualifiedValue.Bad(Quality.BadNotFound, DateTimeOffset.UtcNow);
        }
        var (node, property) = ComponentReference(reference, current); return Read(node, property);
    }
    public string ResolveTagPath(string source)
    {
        var path = new StringBuilder();
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '{') { path.Append(source[i]); continue; }
            var end = source.IndexOf('}', i + 1);
            if (end < 0) throw new ExpressionException("Unclosed indirect tag parameter.");
            var key = source[(i + 1)..end];
            if (key.StartsWith("view.params.", StringComparison.Ordinal)) key = key[12..];
            var sample = ResolveReference("view.params." + key, View.Root);
            if (!sample.IsGood) throw new ExpressionException("Unknown indirect tag parameter: " + key);
            var text = sample.Value.ToString();
            if (text.Contains('/') || text.Contains('[') || text.Contains(']') || text.Contains("..")) throw new ExpressionException("Indirect tag parameters cannot change provider or traverse folders.");
            path.Append(text); i = end;
        }
        if (!TagPaths.IsValid(path.ToString())) throw new ExpressionException("Invalid tag path.");
        return path.ToString();
    }
    public bool CanWrite(ComponentNode node, string property)
    {
        var (binding, _) = FindBinding(node, Qualify(property));
        return (binding is null || binding.Enabled && binding.Bidirectional && binding.Kind is BindingKind.Tag or BindingKind.Property && binding.Transforms.Count == 0) && Read(node, property).IsGood;
    }
    public WriteResult Write(ComponentNode node, string property, Scalar value, string actor, long? expectedRevision = null)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("A write actor is required.");
        if (!value.IsValid()) return new(WriteStatus.BadType, QualifiedValue.Bad(Quality.BadType, DateTimeOffset.UtcNow), "Invalid property value.");
        try
        {
            var result = WriteCore(node, Qualify(property), value, actor, expectedRevision, new(StringComparer.Ordinal));
            if (result.Success) Invalidate();
            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or KeyNotFoundException or IndexOutOfRangeException)
        { return new(WriteStatus.BadType, QualifiedValue.Bad(Quality.BadType, DateTimeOffset.UtcNow), ex.Message); }
    }
    private WriteResult WriteCore(ComponentNode node, string path, Scalar value, string actor, long? expected, HashSet<string> visited)
    {
        if (visited.Count >= 64 || !visited.Add(node.Id + "." + path)) throw new ExpressionException("Cyclic property writeback.");
        var current = Read(node, path);
        if (expected.HasValue && expected.Value != current.Revision) return new(WriteStatus.RevisionConflict, current, "Property revision changed.");
        var (binding, boundPath) = FindBinding(node, path);
        if (binding is not null)
        {
            if (!binding.Enabled || !binding.Bidirectional || binding.Kind is not (BindingKind.Tag or BindingKind.Property) || binding.Transforms.Count != 0) return new(WriteStatus.ReadOnly, current, "Binding is not bidirectionally writable.");
            var parent = boundPath == path ? current : Read(node, boundPath);
            if (!parent.IsGood) return new(WriteStatus.Disabled, parent, "Binding source quality is not good.");
            var updated = boundPath == path ? value : PropertyPath.Parse(Tail(path, boundPath.Length)).Set(parent.Value, value);
            WriteResult result;
            if (binding.Kind == BindingKind.Tag) result = _tags.Write(ResolveTagPath(binding.Source), updated, actor, expected ?? parent.Revision);
            else if (binding.Source.StartsWith("view.params.", StringComparison.Ordinal)) result = WriteParameter(binding.Source[12..], updated, expected ?? parent.Revision);
            else
            {
                var (source, sourcePath) = ComponentReference(binding.Source, node);
                result = WriteCore(source, sourcePath, updated, actor, expected ?? parent.Revision, visited);
            }
            return boundPath == path ? result : result with { Current = Extract(result.Current, Tail(path, boundPath.Length)) };
        }
        var dot = path.IndexOf('.'); var scope = path[..dot]; var property = path[(dot + 1)..]; var fields = Scope(node, scope);
        var (root, rest) = fields.ContainsKey(property) ? (property, "") : Split(property);
        var rootPath = scope + "." + root; var rootKey = node.Id + "." + rootPath;
        var previous = Literal(node, rootPath);
        if (rest.Length > 0) value = PropertyPath.Parse(rest).Set(previous.Value, value);
        var sample = previous.IsGood && value == previous.Value ? previous : QualifiedValue.Good(value, DateTimeOffset.UtcNow, ++_revision);
        _locals[rootKey] = sample;
        return new(WriteStatus.Good, rest.Length == 0 ? sample : Extract(sample, rest));
    }
    public WriteResult WriteParameter(string path, Scalar value, long? expectedRevision = null)
    {
        var current = ResolveReference("view.params." + path, View.Root);
        if (expectedRevision.HasValue && current.Revision != expectedRevision) return new(WriteStatus.RevisionConflict, current, "Parameter revision changed.");
        if (!value.IsValid()) return new(WriteStatus.BadType, current, "Invalid parameter value.");
        var (root, rest) = Parameters.ContainsKey(path) ? (path, "") : Split(path);
        if (rest.Length > 0) value = PropertyPath.Parse(rest).Set(Parameters.GetValueOrDefault(root), value);
        Parameters[root] = value; _revision++; Invalidate();
        return new(WriteStatus.Good, ResolveReference("view.params." + path, View.Root));
    }
}
