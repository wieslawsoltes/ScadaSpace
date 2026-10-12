using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.Expressions;

namespace ScadaSpace.Designer;

public sealed partial class DesignerDocument
{
    private sealed record WrapPlan(ComponentNode Parent, ViewDefinition View, ComponentNode[] Children, int Index, double X, double Y, double Width, double Height);

    /// <summary>Checks structural eligibility. Apply also validates and preserves affected binding references.</summary>
    public bool CanWrapSelection(out string? reason)
    {
        try { _ = PlanWrap(); reason = null; return true; }
        catch (InvalidOperationException ex) { reason = ex.Message; return false; }
    }
    private WrapPlan PlanWrap()
    {
        var children = SelectedRoots().ToArray();
        if (children.Length == 0) throw new InvalidOperationException("Select one or more components to wrap.");
        var index = NodeIndex(); var location = index[children[0].Id]; var parent = location.Parent;
        if (parent is null || children.Any(n => !ReferenceEquals(index[n.Id].Parent, parent)))
            throw new InvalidOperationException("Wrapping requires siblings; view roots cannot be wrapped.");
        if (parent.Type != "Container" || parent.Layout != LayoutKind.Coordinate || parent.Flag("percentCoordinates"))
            throw new InvalidOperationException("Wrapping currently requires a pixel-coordinate parent container.");
        if (children.Any(n => n.Locked)) throw new InvalidOperationException("Unlock every selected component before wrapping.");
        var first = parent.Children.IndexOf(children[0]);
        for (var i = 0; i < children.Length; i++)
            if (first + i >= parent.Children.Count || !ReferenceEquals(parent.Children[first + i], children[i]))
                throw new InvalidOperationException("Select a contiguous sibling range so wrapping preserves stacking order.");
        double left = double.PositiveInfinity, top = double.PositiveInfinity, right = double.NegativeInfinity, bottom = double.NegativeInfinity;
        foreach (var child in children)
        {
            var angle = child.Rotation * Math.PI / 180;
            var w = Math.Abs(child.Width * Math.Cos(angle)) + Math.Abs(child.Height * Math.Sin(angle));
            var h = Math.Abs(child.Width * Math.Sin(angle)) + Math.Abs(child.Height * Math.Cos(angle));
            var cx = child.X + child.Width / 2; var cy = child.Y + child.Height / 2;
            left = Math.Min(left, cx - w / 2); top = Math.Min(top, cy - h / 2);
            right = Math.Max(right, cx + w / 2); bottom = Math.Max(bottom, cy + h / 2);
        }
        return new(parent, location.View, children, first, left, top, Math.Max(.001, right - left), Math.Max(.001, bottom - top));
    }

    /// <summary>Atomically groups siblings without moving them on screen or losing IDs, actions or parent bindings.</summary>
    public string WrapSelectionInCoordinateContainer(string name = "CoordinateContainer")
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl)) throw new ArgumentException("Invalid container name.", nameof(name));
        var plan = PlanWrap();
        var names = plan.View.Root.DescendantsAndSelf().Select(n => n.Name).ToHashSet(StringComparer.Ordinal);
        var unique = name; for (var suffix = 2; names.Contains(unique); suffix++) unique = name[..Math.Min(name.Length, 120)] + suffix;
        var parentOwner = ReferenceEquals(plan.Parent, plan.View.Root) ? "root" : plan.Parent.Id;
        var rewritten = plan.Children.Select(n => PreserveParentBindings(n, parentOwner, ReferenceEquals(plan.Parent, plan.View.Root))).ToArray();
        var wrapper = new ComponentNode
        {
            Type = "Container", Name = unique, Layout = LayoutKind.Coordinate,
            X = plan.X, Y = plan.Y, Width = plan.Width, Height = plan.Height,
            Props = new(StringComparer.Ordinal) { ["padding"] = Scalar.From(0), ["gap"] = Scalar.From(8) }
        };
        Execute("Wrap in Coordinate Container", _ =>
        {
            plan.Parent.Children.RemoveRange(plan.Index, plan.Children.Length);
            plan.Parent.Children.Insert(plan.Index, wrapper);
            for (var i = 0; i < plan.Children.Length; i++)
            {
                var child = plan.Children[i]; child.X -= plan.X; child.Y -= plan.Y;
                child.Bindings = rewritten[i]; wrapper.Children.Add(child);
            }
            Selection.Clear(); Selection.Add(wrapper.Id);
        });
        return wrapper.Id;
    }
    private static Dictionary<string, BindingDefinition> PreserveParentBindings(ComponentNode node, string owner, bool isRoot)
    {
        string Reference(string path)
        {
            if (!path.StartsWith("parent.", StringComparison.Ordinal)) return path;
            if (!isRoot && owner is ("this" or "parent" or "root" or "page" or "view") || owner.Contains('{') || owner.Contains('}') || owner.Any(char.IsControl)
                || owner.StartsWith('[') || (owner + ".").StartsWith("view.params.", StringComparison.Ordinal)
                || new[] { ".props.", ".custom.", ".style." }.Any(marker => (owner + ".").Contains(marker, StringComparison.Ordinal)))
                throw new InvalidOperationException("This parent ID cannot be used in a stable binding reference.");
            return owner + path[6..];
        }
        string Expression(string source) => source.Contains("parent.", StringComparison.Ordinal)
            ? ExpressionEngine.RewriteReferences(source, Reference) : source;
        Scalar Structure(Scalar item) => item.Kind switch
        {
            ScalarKind.String => Scalar.From(Expression(item.Text!)),
            ScalarKind.Array => Scalar.Array(item.Items.Select(Structure)),
            ScalarKind.Object => Scalar.Object(item.Fields.Select(p => new KeyValuePair<string, Scalar>(p.Key, Structure(p.Value)))),
            _ => item
        };
        var result = JsonSerializer.Deserialize<Dictionary<string, BindingDefinition>>(JsonSerializer.Serialize(node.Bindings, ProjectCodec.Options), ProjectCodec.Options)!;
        foreach (var binding in result.Values)
        {
            if (binding.Kind == BindingKind.Property) binding.Source = Reference(binding.Source);
            else if (binding.Kind == BindingKind.Expression) binding.Source = Expression(binding.Source);
            else if (binding.Kind == BindingKind.ExpressionStructure && binding.Source.Contains("parent.", StringComparison.Ordinal))
            {
                var original = Scalar.FromJson(binding.Source); var changed = Structure(original);
                if (changed != original) binding.Source = changed.ToJson();
            }
            else if (binding.Kind == BindingKind.TagHistory && binding.Source.Contains("parent.", StringComparison.Ordinal))
            {
                var original = Scalar.FromJson(binding.Source);
                if (original.Kind != ScalarKind.Object) throw new FormatException("Invalid history configuration.");
                var changed = Scalar.Object(original.Fields.Select(p => new KeyValuePair<string, Scalar>(p.Key,
                    p.Key is "start" or "end" or "windowSeconds" or "returnSize" or "limit" && p.Value.Kind == ScalarKind.String ? Scalar.From(Expression(p.Value.Text!)) : p.Value)));
                if (changed != original) binding.Source = changed.ToJson();
            }
            foreach (var transform in binding.Transforms)
                if (transform.Kind == TransformKind.Expression) transform.Expression = Expression(transform.Expression);
        }
        return result;
    }
}
