using System.Globalization;
using System.Text;
using ScadaSpace.Core;
using ScadaSpace.Expressions;
using ScadaSpace.Tags;

namespace ScadaSpace.Runtime;

/// <summary>Instance-scoped property graph. A separate instance is required for every embedded/repeated view.</summary>
public sealed class ViewInstance
{
    private readonly ITagProvider _tags;
    private readonly ExpressionEngine _expressions;
    private readonly Dictionary<string, ComponentNode> _nodes;
    private readonly Dictionary<string, ComponentNode> _parents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QualifiedValue> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);
    private readonly List<Diagnostic> _diagnostics = [];
    public ViewDefinition View { get; }
    public Dictionary<string, Scalar> Parameters { get; }
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
    public IReadOnlyDictionary<string, QualifiedValue> Values => _values;

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
        _values.Clear(); _resolving.Clear(); _diagnostics.Clear();
        foreach (var node in _nodes.Values)
            foreach (var property in node.Props.Keys.Union(node.Bindings.Keys, StringComparer.Ordinal)) Read(node, property);
    }
    public QualifiedValue Read(ComponentNode node, string property)
    {
        var key = node.Id + ".props." + property;
        if (_values.TryGetValue(key, out var cached)) return cached;
        if (_resolving.Count >= 64 || !_resolving.Add(key)) throw new ExpressionException("Cyclic or excessively deep property dependency: " + key);
        var now = DateTimeOffset.UtcNow;
        try
        {
            if (!node.Bindings.TryGetValue(property, out var binding)) return _values[key] = QualifiedValue.Good(node.Get(property), now);
            if (!binding.Enabled) return _values[key] = QualifiedValue.Bad(Quality.BadDisabled, now);
            var quality = Quality.Good;
            var stamp = now;
            long revision = 0;
            Scalar Resolve(string reference)
            {
                var value = ResolveReference(reference, node);
                if (value.Quality > quality) quality = value.Quality;
                stamp = value.Timestamp; revision = Math.Max(revision, value.Revision);
                if (!value.IsGood) throw new ExpressionException($"{reference}: {value.Quality}");
                return value.Value;
            }
            Scalar result;
            switch (binding.Kind)
            {
                case BindingKind.Tag: result = Resolve(ResolveTagPath(binding.Source)); break;
                case BindingKind.Property: result = Resolve(binding.Source); break;
                case BindingKind.Expression: result = _expressions.Evaluate(binding.Source, Resolve); break;
                default: throw new ExpressionException("Unsupported binding type.");
            }
            foreach (var transform in binding.Transforms)
            {
                var input = result;
                result = transform.Kind switch
                {
                    TransformKind.Expression => _expressions.Evaluate(transform.Expression, reference => reference == "value" ? input : Resolve(reference)),
                    TransformKind.Scale => transform.InputMax == transform.InputMin ? throw new ExpressionException("Scale input range is zero.") : Scalar.From(transform.OutputMin + (input.AsNumber() - transform.InputMin) / (transform.InputMax - transform.InputMin) * (transform.OutputMax - transform.OutputMin)),
                    TransformKind.Format => Scalar.From(input.AsNumber().ToString(transform.Format, CultureInfo.InvariantCulture)),
                    TransformKind.Map => transform.Map.TryGetValue(input.ToString(), out var mapped) ? mapped : transform.Map.GetValueOrDefault("*", Scalar.Null),
                    _ => throw new ExpressionException("Unsupported transform.")
                };
                if (!result.IsValid()) throw new ExpressionException("Transform result is invalid.");
            }
            return _values[key] = new(result, quality, stamp, revision);
        }
        catch (Exception ex) when (ex is ExpressionException or InvalidOperationException or ArgumentException or FormatException)
        {
            _diagnostics.Add(new("BINDING_ERROR", ex.Message, key));
            return _values[key] = QualifiedValue.Bad(Quality.BadExpression, now);
        }
        finally { _resolving.Remove(key); }
    }
    private QualifiedValue ResolveReference(string reference, ComponentNode current)
    {
        if (reference.StartsWith('[')) return _tags.Read(reference);
        if (reference.StartsWith("view.params.", StringComparison.Ordinal)) return Parameters.TryGetValue(reference[12..], out var parameter) ? QualifiedValue.Good(parameter, DateTimeOffset.UtcNow) : QualifiedValue.Bad(Quality.BadNotFound, DateTimeOffset.UtcNow);
        var marker = reference.IndexOf(".props.", StringComparison.Ordinal);
        var custom = false;
        if (marker < 0) { marker = reference.IndexOf(".custom.", StringComparison.Ordinal); custom = true; }
        if (marker < 0) throw new ExpressionException("References use component.props.name, this.custom.name or view.params.name.");
        var owner = reference[..marker];
        var property = reference[(marker + (custom ? 8 : 7))..];
        ComponentNode? target = owner switch
        {
            "this" => current, "root" => View.Root,
            "parent" => _parents.GetValueOrDefault(current.Id),
            _ => _nodes.GetValueOrDefault(owner)
        };
        if (target is null)
        {
            var matching = _nodes.Values.Where(x => x.Name == owner).Take(2).ToArray();
            if (matching.Length == 1) target = matching[0];
        }
        if (target is null) throw new ExpressionException("Unknown or ambiguous component: " + owner);
        return custom ? QualifiedValue.Good(target.Custom.GetValueOrDefault(property), DateTimeOffset.UtcNow) : Read(target, property);
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
            if (!Parameters.TryGetValue(key, out var value)) throw new ExpressionException("Unknown indirect tag parameter: " + key);
            var text = value.ToString();
            if (text.Contains('/') || text.Contains('[') || text.Contains(']') || text.Contains("..")) throw new ExpressionException("Indirect tag parameters cannot change provider or traverse folders.");
            path.Append(text); i = end;
        }
        if (!TagPaths.IsValid(path.ToString())) throw new ExpressionException("Invalid tag path.");
        return path.ToString();
    }
    public WriteResult Write(ComponentNode node, string property, Scalar value, string actor, long? expectedRevision = null)
    {
        if (!node.Bindings.TryGetValue(property, out var binding) || !binding.Enabled || binding.Kind != BindingKind.Tag || !binding.Bidirectional || binding.Transforms.Count != 0)
            return new(WriteStatus.ReadOnly, Read(node, property), "Only enabled, untransformed bidirectional tag bindings are writable.");
        try { return _tags.Write(ResolveTagPath(binding.Source), value, actor, expectedRevision); }
        catch (ExpressionException ex) { return new(WriteStatus.NotFound, QualifiedValue.Bad(Quality.BadNotFound, DateTimeOffset.UtcNow), ex.Message); }
    }
}
