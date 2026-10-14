using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using static Suite;

internal static class PropertyAuthoringTests
{
    private static DesignerDocument Document() => new(DemoProject.Create());
    private static string Id(DesignerDocument d) => d.Project.Views[0].Root.Id;
    private static PropertyCreationDraft Draft(DesignerDocument d, PropertyCreationGroup group = PropertyCreationGroup.Custom) => new(d, Id(d), group);

    [ModuleInitializer]
    internal static void Register()
    {
        Add("property editor text round-trips small large and precise finite numbers", () => {
            foreach (var n in new[] { 0.000000123456789, 1.2345678901234567, double.Epsilon, double.MaxValue, -double.MaxValue, -0d, 9007199254740991d }) {
                var formatted = PropertyValueText.Format(Scalar.From(n)); var parsed = PropertyValueText.Parse(formatted, ScalarKind.Number);
                Equal(BitConverter.DoubleToInt64Bits(n), BitConverter.DoubleToInt64Bits(parsed.Number));
            }
            Equal("1.235", Scalar.From(1.23456789).ToString()); // Process-display formatting is intentionally unchanged.
        });
        Add("property editor text preserves every immutable value kind", () => {
            foreach (var value in new[] { Scalar.Null, Scalar.From(true), Scalar.From(false), Scalar.From("  not JSON \n"), Scalar.FromJson("{\"values\":[1,true,null,\"text\"]}"), Scalar.FromJson("[1,{},[]]") })
                Equal(value, PropertyValueText.Parse(PropertyValueText.Format(value), value.Kind));
        });
        Add("numeric property authoring is independent of current culture", () => {
            var old = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); Equal("1.23456789", PropertyValueText.Format(Scalar.From(1.23456789))); Equal(1.23456789, PropertyValueText.Parse("1.23456789", ScalarKind.Number).Number); }
            finally { CultureInfo.CurrentCulture = old; }
        });
        Add("property authoring rejects invalid null and nonfinite values", () => {
            Equal(Scalar.Null, PropertyValueText.Parse(" null ", ScalarKind.Null)); Equal(Scalar.Null, PropertyValueText.Parse("", ScalarKind.Null));
            Throws<FormatException>(() => PropertyValueText.Parse("42", ScalarKind.Null));
            Throws<ArgumentException>(() => PropertyValueText.Parse("NaN", ScalarKind.Number));
            Throws<ArgumentException>(() => PropertyValueText.Parse("1e999", ScalarKind.Number));
            Throws<FormatException>(() => PropertyValueText.Parse("yes", ScalarKind.Boolean));
        });
        Add("property authoring validates JSON root kind syntax and duplicate keys", () => {
            Throws<FormatException>(() => PropertyValueText.Parse("[]", ScalarKind.Object));
            Throws<FormatException>(() => PropertyValueText.Parse("{}", ScalarKind.Array));
            Throws<JsonException>(() => PropertyValueText.Parse("{", ScalarKind.Object));
            Throws<ArgumentException>(() => PropertyValueText.Parse("{\"a\":1,\"a\":2}", ScalarKind.Object));
        });
        Add("property authoring bounds text names and structured depth", () => {
            Equal(65536, PropertyValueText.Parse(new string('x', 65536), ScalarKind.String).Text!.Length);
            Throws<ArgumentOutOfRangeException>(() => PropertyValueText.Parse(new string('x', 65537), ScalarKind.String));
            Throws<JsonException>(() => PropertyValueText.Parse(new string('[', 25) + "0" + new string(']', 25), ScalarKind.Array));
            foreach (var name in new[] { "", " ", "bad\nname", new string('x', 129) }) Throws<FormatException>(() => PropertyCreationDraft.ValidateName(name));
            PropertyCreationDraft.ValidateName(new string('x', 128)); PropertyCreationDraft.ValidateName("port.value");
        });
        Add("property authoring argument failures do not accept malformed values", () => {
            Throws<ArgumentNullException>(() => PropertyValueText.Parse(null!, ScalarKind.String));
            Throws<ArgumentOutOfRangeException>(() => PropertyValueText.Parse("", (ScalarKind)999));
            Throws<ArgumentException>(() => PropertyValueText.Format(new(ScalarKind.Number, double.NaN)));
            Throws<ArgumentNullException>(() => PropertyCreationDraft.ValidateName(null!));
        });
        Add("all six property kinds can be added by one guarded native-independent API", () => {
            foreach (var (kind, text) in new[] { (ScalarKind.String, "hello"), (ScalarKind.Number, "0.000000123456789"), (ScalarKind.Boolean, "true"), (ScalarKind.Null, "null"), (ScalarKind.Object, "{\"v\":[1,2]}"), (ScalarKind.Array, "[1,true,null]") }) {
                var d = Document(); var draft = Draft(d); Assert(draft.Apply(d, "created", kind, text)); Equal(1L, d.Generation);
                Equal(PropertyValueText.Parse(text, kind), d.Find(Id(d))!.Custom["created"]); Assert(d.CanUndo); Assert(draft.IsApplied);
            }
        });
        Add("property creation does not mutate until Apply and Cancel needs no rollback", () => {
            var d = Document(); var json = ProjectCodec.Serialize(d.Project); _ = Draft(d);
            Equal(json, ProjectCodec.Serialize(d.Project)); Equal(0L, d.Generation); Assert(!d.CanUndo);
        });
        Add("property creation uses explicit categories and preserves same-name neighbors", () => {
            var d = Document(); var id = Id(d); Draft(d, PropertyCreationGroup.Props).Apply(d, "value", ScalarKind.Number, "7");
            Draft(d).Apply(d, "value", ScalarKind.String, "recipe"); Equal(7d, d.Find(id)!.Props["value"].Number); Equal("recipe", d.Find(id)!.Custom["value"].Text);
        });
        Add("duplicate and malformed property additions leave document and history unchanged", () => {
            var d = Document(); Draft(d).Apply(d, "created", ScalarKind.String, "first"); var json = ProjectCodec.Serialize(d.Project); var gen = d.Generation; var bytes = d.HistoryBytes;
            var draft = Draft(d); Throws<InvalidOperationException>(() => draft.Apply(d, "created", ScalarKind.String, "second"));
            Throws<JsonException>(() => draft.Apply(d, "object", ScalarKind.Object, "{"));
            Equal(json, ProjectCodec.Serialize(d.Project)); Equal(gen, d.Generation); Equal(bytes, d.HistoryBytes); Assert(!draft.IsApplied);
            Assert(draft.Apply(d, "object", ScalarKind.Object, "{}"));
        });
        Add("property creation retains selected component and supports undo redo", () => {
            var d = Document(); var id = Id(d); d.Select(id); Draft(d).Apply(d, "object", ScalarKind.Object, "{\"n\":1.23456789}");
            Assert(d.Selection.SetEquals([id])); d.Undo(); Assert(!d.Find(id)!.Custom.ContainsKey("object")); Assert(!d.IsDirty);
            d.Redo(); Equal(1.23456789, d.Find(id)!.Custom["object"].Fields["n"].Number);
        });
        Add("property drafts never retarget after an external selection change", () => {
            var d = Document(); var original = Id(d); var other = d.Project.Views[0].Root.Children[0].Id; var draft = Draft(d);
            d.Select(other); draft.Apply(d, "owned", ScalarKind.Number, "2"); Assert(d.Find(original)!.Custom.ContainsKey("owned")); Assert(!d.Find(other)!.Custom.ContainsKey("owned")); Assert(d.Selection.SetEquals([other]));
        });
        Add("property drafts reject replaced documents and intervening revisions", () => {
            var d = Document(); var draft = Draft(d); Throws<InvalidOperationException>(() => draft.Apply(Document(), "x", ScalarKind.String, "x"));
            d.Execute("other edit", p => p.Name += " changed"); var before = ProjectCodec.Serialize(d.Project);
            Throws<InvalidOperationException>(() => draft.Apply(d, "x", ScalarKind.String, "x")); Equal(before, ProjectCodec.Serialize(d.Project));
        });
        Add("successful property drafts cannot submit twice", () => {
            var d = Document(); var draft = Draft(d); Assert(draft.Apply(d, "x", ScalarKind.String, "first"));
            Assert(!draft.Apply(d, "y", ScalarKind.String, "second")); Equal(1L, d.Generation); Assert(!d.Find(Id(d))!.Custom.ContainsKey("y"));
        });
        Add("postcommit observer exceptions cannot duplicate a created property", () => {
            var d = Document(); var draft = Draft(d); d.Changed += () => throw new InvalidOperationException("observer");
            Throws<InvalidOperationException>(() => draft.Apply(d, "x", ScalarKind.String, "first"));
            Assert(draft.IsApplied); Equal(1L, d.Generation); Assert(!draft.Apply(d, "x", ScalarKind.String, "first"));
        });
        Add("property draft constructors reject invalid ownership and category", () => {
            var d = Document(); Throws<ArgumentNullException>(() => new PropertyCreationDraft(null!, "x", PropertyCreationGroup.Custom));
            Throws<ArgumentNullException>(() => new PropertyCreationDraft(d, null!, PropertyCreationGroup.Custom));
            Throws<ArgumentException>(() => new PropertyCreationDraft(d, "missing", PropertyCreationGroup.Custom));
            Throws<ArgumentOutOfRangeException>(() => new PropertyCreationDraft(d, Id(d), (PropertyCreationGroup)55));
        });
    }
}
