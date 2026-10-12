using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using static Suite;

internal static class HierarchyReferenceGuardTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("wrapping rejects parent IDs that collide with reference grammar before changing children", () =>
        {
            foreach (var parentId in new[] { "[provider]Tag", "view.params", "view.params.name", "node.props", "node.custom", "node.style" })
            {
                var parent = new ComponentNode { Id = parentId, Name = "Outer", Type = "Container" };
                parent.Children.Add(new() { Id = "child", Name = "Child", Type = "Label", Bindings = new()
                    { ["text"] = new() { Kind = BindingKind.Property, Source = "parent.custom.number" } } });
                var root = new ComponentNode { Id = "root-id", Name = "root", Type = "Container", Children = [parent] };
                var document = new DesignerDocument(new() { Views = [new() { Path = "Main", Root = root }] });
                document.Select("child"); var before = ProjectCodec.Serialize(document.Project);
                Throws<InvalidOperationException>(() => document.WrapSelectionInCoordinateContainer());
                Equal(before, ProjectCodec.Serialize(document.Project)); Equal(0L, document.Generation);
            }
        });
    }
}
