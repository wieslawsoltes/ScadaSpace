using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using ScadaSpace.Tags;
using static Suite;

internal static class BindingTests
{
    private static (ComponentNode Root, ViewInstance View, TagStore Tags) Create()
    {
        var root = new ComponentNode { Name = "root", Type = "Container" };
        root.Props["data"] = Scalar.FromJson("{\"rows\":[{\"value\":12,\"label\":\"A\"}],\"unit\":\"C\"}");
        root.Props["input"] = Scalar.From(0);
        root.Custom["settings"] = Scalar.FromJson("{\"scale\":2}");
        var tags = new TagStore([new() { Path = "[default]Data", DataType = ScalarKind.Object, InitialValue = root.Props["data"] }, new() { Path = "[default]Value", InitialValue = Scalar.From(6) }]);
        var view = new ViewDefinition { Root = root }; view.Parameters["equipment"] = Scalar.FromJson("{\"id\":\"Tank01\",\"setpoint\":50}");
        return (root, new ViewInstance(view, tags), tags);
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("nested properties and custom properties resolve in expressions", () => {
            var (root, view, _) = Create(); root.Bindings["input"] = new() { Kind = BindingKind.Expression, Source = "{this.props.data.rows[0].value} * {this.custom.settings.scale}" };
            view.Evaluate(); Equal(24d, view.Read(root, "input").Value.Number); Equal(0, view.Diagnostics.Count);
        });
        Add("runtime local writes update dependent bindings without mutating the model", () => {
            var (root, view, _) = Create(); root.Bindings["input"] = new() { Kind = BindingKind.Property, Source = "this.props.data.rows[0].value" };
            view.Evaluate(); Assert(view.Write(root, "data.rows[0].value", Scalar.From(30), "operator").Success); view.Evaluate();
            Equal(30d, view.Read(root, "input").Value.Number); Equal(12d, root.Props["data"].Fields["rows"].Items[0].Fields["value"].Number);
        });
        Add("bidirectional property bindings follow nested source paths", () => {
            var (root, view, _) = Create(); root.Bindings["input"] = new() { Kind = BindingKind.Property, Source = "this.props.data.rows[0].value", Bidirectional = true };
            view.Evaluate(); Assert(view.CanWrite(root, "input")); Assert(view.Write(root, "input", Scalar.From(27), "operator").Success);
            Equal(27d, view.Read(root, "data.rows[0].value").Value.Number);
        });
        Add("bidirectional bindings forward through property aliases to tags", () => {
            var (root, view, tags) = Create(); root.Bindings["data"] = new() { Kind = BindingKind.Tag, Source = "[default]Data", Bidirectional = true };
            root.Bindings["input"] = new() { Kind = BindingKind.Property, Source = "this.props.data.rows[0].value", Bidirectional = true };
            view.Evaluate(); var result = view.Write(root, "input", Scalar.From(44), "operator"); Assert(result.Success, result.Message);
            Equal(44d, tags.Read("[default]Data").Value.Fields["rows"].Items[0].Fields["value"].Number);
            Equal("A", tags.Read("[default]Data").Value.Fields["rows"].Items[0].Fields["label"].Text); Equal(44d, result.Current.Value.Number);
        });
        Add("nested tag writes reject stale revisions atomically", () => {
            var (root, view, tags) = Create(); root.Bindings["data"] = new() { Kind = BindingKind.Tag, Source = "[default]Data", Bidirectional = true };
            view.Evaluate(); var old = view.Read(root, "data.rows[0].value"); tags.Write("[default]Data", Scalar.FromJson("{\"rows\":[{\"value\":99,\"label\":\"B\"}] }"), "other");
            Equal(WriteStatus.RevisionConflict, view.Write(root, "data.rows[0].value", Scalar.From(11), "operator", old.Revision).Status);
            Equal(99d, tags.Read("[default]Data").Value.Fields["rows"].Items[0].Fields["value"].Number);
        });
        Add("two view instances isolate local state", () => {
            var (root, view, tags) = Create(); var other = new ViewInstance(view.View, tags);
            view.Write(root, "input", Scalar.From(9), "operator"); Equal(0d, other.Read(root, "input").Value.Number); Equal(9d, view.Read(root, "input").Value.Number);
        });
        Add("custom and style writes are instance-scoped", () => {
            var (root, view, _) = Create(); Assert(view.Write(root, "custom.settings.scale", Scalar.From(4), "operator").Success);
            Assert(view.Write(root, "style.opacity", Scalar.From(.5), "operator").Success);
            Equal(4d, view.Read(root, "custom.settings.scale").Value.Number); Equal(.5, view.Read(root, "style.opacity").Value.Number); Equal(2d, root.Custom["settings"].Fields["scale"].Number);
        });
        Add("expression structures evaluate nested leaves atomically", () => {
            var (root, view, tags) = Create(); root.Bindings["data"] = new() { Kind = BindingKind.ExpressionStructure, Source = "{\"value\":\"{[default]Value} * 2\",\"nested\":[\"'ready'\",true]}" };
            view.Evaluate(); var data = view.Read(root, "data"); Assert(data.IsGood); Equal(12d, data.Value.Fields["value"].Number); Equal("ready", data.Value.Fields["nested"].Items[0].Text);
            tags.Publish("[default]Value", Scalar.From(7)); view.Evaluate(); Equal(14d, view.Read(root, "data.value").Value.Number);
        });
        Add("failed expression structure leaves propagate quality", () => {
            var (root, view, _) = Create(); root.Bindings["data"] = new() { Kind = BindingKind.ExpressionStructure, Source = "{\"ok\":\"1\",\"bad\":\"{[default]Missing}\"}" };
            view.Evaluate(); Equal(Quality.BadNotFound, view.Read(root, "data").Quality); Assert(view.Diagnostics.Count > 0);
        });
        Add("bindings targeting descendants assemble object snapshots", () => {
            var (root, view, _) = Create(); root.Bindings["data.rows[0].value"] = new() { Kind = BindingKind.Expression, Source = "{[default]Value} + 20" };
            view.Evaluate(); Equal(26d, view.Read(root, "data").Value.Fields["rows"].Items[0].Fields["value"].Number); Equal(26d, view.Read(root, "data.rows[0].value").Value.Number);
        });
        Add("nested sibling dependencies do not create false cycles", () => {
            var (root, view, _) = Create(); root.Bindings["data"] = new() { Kind = BindingKind.Tag, Source = "[default]Data" };
            root.Bindings["data.rows[0].value"] = new() { Kind = BindingKind.Expression, Source = "len({this.props.data.unit}) + 10" };
            view.Evaluate(); Equal(11d, view.Read(root, "data.rows[0].value").Value.Number); Equal(0, view.Diagnostics.Count);
        });
        Add("cyclic nested bindings remain diagnosed", () => {
            var (root, view, _) = Create(); root.Bindings["input"] = new() { Kind = BindingKind.Property, Source = "this.props.input", Bidirectional = true };
            view.Evaluate(); Equal(Quality.BadExpression, view.Read(root, "input").Quality); Assert(!view.Write(root, "input", Scalar.From(2), "operator").Success);
        });
        Add("property writeback cannot bypass readonly sources", () => {
            var (root, view, _) = Create(); root.Bindings["data"] = new() { Kind = BindingKind.Tag, Source = "[default]Data" };
            root.Bindings["input"] = new() { Kind = BindingKind.Property, Source = "this.props.data.rows[0].value", Bidirectional = true };
            view.Evaluate(); Equal(WriteStatus.ReadOnly, view.Write(root, "input", Scalar.From(2), "operator").Status);
        });
        Add("structured parameters support indirect tags and writeback", () => {
            var (root, view, _) = Create(); Equal("[default]Plant/Tank01/Level", view.ResolveTagPath("[default]Plant/{view.params.equipment.id}/Level"));
            root.Bindings["input"] = new() { Kind = BindingKind.Property, Source = "view.params.equipment.setpoint", Bidirectional = true };
            view.Evaluate(); Assert(view.Write(root, "input", Scalar.From(75), "operator").Success); Equal(75d, view.Parameters["equipment"].Fields["setpoint"].Number); Equal(50d, view.View.Parameters["equipment"].Fields["setpoint"].Number);
        });
        Add("property change events are value-aware and observer-isolated", () => {
            var (root, view, _) = Create(); var seen = new List<PropertyChange>(); view.Evaluate(); view.PropertyChanged += _ => throw new Exception("observer fault"); view.PropertyChanged += seen.Add;
            view.Write(root, "input", Scalar.From(2), "operator"); view.Evaluate(); Equal(1, seen.Count); Equal(PropertyChangeOrigin.Local, seen[0].Origin); Assert(view.Diagnostics.Any(d => d.Code == "PROPERTY_OBSERVER"));
            view.Evaluate(); Equal(1, seen.Count);
        });
        Add("component names containing dots remain resolvable", () => {
            var (root, view, _) = Create(); root.Name = "Tank.One"; root.Bindings["input"] = new() { Kind = BindingKind.Property, Source = "Tank.One.props.data.rows[0].value" };
            view.Evaluate(); Equal(12d, view.Read(root, "input").Value.Number);
        });
        Add("empty and malformed expression structure definitions diagnose", () => {
            var (root, view, _) = Create(); root.Bindings["data"] = new() { Kind = BindingKind.ExpressionStructure, Source = "{broken}" }; view.Evaluate(); Equal(Quality.BadExpression, view.Read(root, "data").Quality);
        });
    }
}
