using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using static Suite;

internal static class StyleTests
{
    private static Dictionary<string, Scalar> Props(string color) => new() { ["foreground"] = Scalar.From(color) };
    private static DesignerDocument Document()
    {
        var project = new ProjectDefinition { Views = [new() { Path = "Main", Root = new() { Id = "root", Name = "Root", Type = "Container" } }] };
        project.Styles["Alpha"] = Props("#112233"); project.Styles["Zulu"] = Props("#445566");
        project.Views[0].Root.Children.Add(new() { Id = "label", Name = "Label", Type = "Label", StyleClasses = ["Zulu", "Alpha"] });
        return new(project);
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("style classes resolve alphabetically rather than assignment order", () => {
            var d = Document(); var n = d.Find("label")!; var resolver = new StyleResolver(d.Project.Styles);
            Equal("#445566", resolver.Resolve(n).Values["foreground"].Text); n.StyleClasses.Reverse();
            Equal("#445566", resolver.Resolve(n).Values["foreground"].Text);
        });
        Add("inline style values override classes without dropping unrelated fields", () => {
            var d = Document(); d.Project.Styles["Alpha"]["fontSize"] = Scalar.From(22); var n = d.Find("label")!;
            n.Style["foreground"] = Scalar.From("#778899"); var style = new StyleResolver(d.Project.Styles).Resolve(n);
            Equal("#778899", style.Values["foreground"].Text); Equal(22d, style.Values["fontSize"].Number);
        });
        Add("style catalogs own their class maps and old resolved snapshots", () => {
            var d = Document(); var resolver = new StyleResolver(d.Project.Styles); var n = d.Find("label")!; var a = resolver.Resolve(n);
            d.Project.Styles["Zulu"]["foreground"] = Scalar.From("#FFFFFF"); n.Style["opacity"] = Scalar.From(.5);
            Equal("#445566", resolver.Resolve(n).Values["foreground"].Text); Assert(!a.Values.ContainsKey("opacity"));
            Throws<NotSupportedException>(() => ((IDictionary<string, Scalar>)a.Values).Clear());
        });
        Add("style cache catches equal-sized replacements and reuses unchanged reads", () => {
            var d = Document(); var n = d.Find("label")!; n.Style["opacity"] = Scalar.From(.5); var resolver = new StyleResolver(d.Project.Styles);
            var a = resolver.Resolve(n); for (var i = 0; i < 1000; i++) Assert(ReferenceEquals(a, resolver.Resolve(n)));
            Equal(1L, resolver.ResolveCount); n.Style = new() { ["opacity"] = Scalar.From(.7) };
            Equal(.7, resolver.Resolve(n).Values["opacity"].Number); Equal(2L, resolver.ResolveCount);
            n.StyleClasses = ["Alpha"]; Equal("#112233", resolver.Resolve(n).Values["foreground"].Text);
        });
        Add("concurrent style readers share the same immutable result", () => {
            var d = Document(); var n = d.Find("label")!; var resolver = new StyleResolver(d.Project.Styles); var a = resolver.Resolve(n);
            Parallel.For(0, 1000, _ => Assert(ReferenceEquals(a, resolver.Resolve(n)))); Equal(1L, resolver.ResolveCount);
        });
        Add("missing classes and unsupported properties are explicit without dropping data", () => {
            var d = Document(); var n = d.Find("label")!; n.StyleClasses.Add("Missing"); n.Style["vendorSetting"] = Scalar.FromJson("{\"nested\":true}");
            var style = new StyleResolver(d.Project.Styles).Resolve(n); Assert(style.MissingClasses.SequenceEqual(new[] { "Missing" }));
            Assert(style.UnsupportedProperties.SequenceEqual(new[] { "vendorSetting" })); Assert(style.Values["vendorSetting"].Fields["nested"].Boolean);
        });
        Add("style validation rejects malformed known types ranges and color strings", () => {
            foreach (var pair in new[] { new KeyValuePair<string, Scalar>("opacity", Scalar.From(2)), new("fontSize", Scalar.From("12")), new("padding", Scalar.From(-1)), new("foreground", Scalar.From("invalid")), new("background", new Scalar(ScalarKind.String)), new("fontWeight", Scalar.From("Heavy")), new("italic", Scalar.From("true")), new("textAlignment", Scalar.From("Both")) })
                Throws<ArgumentException>(() => StyleRules.ValidateValues(new Dictionary<string, Scalar> { [pair.Key] = pair.Value }));
            StyleRules.ValidateValues(new Dictionary<string, Scalar> { ["background"] = Scalar.From("#00FFFFFF"), ["borderWidth"] = Scalar.From(0) });
        });
        Add("project validation reports null class maps and invalid inline styles without crashing", () => {
            var d = Document(); d.Project.Styles["Broken"] = null!; Assert(ProjectCodec.Validate(d.Project).Any(x => x.Path == "styles"));
            Throws<FormatException>(() => ProjectCodec.Clone(d.Project)); d.Project.Styles.Remove("Broken");
            d.Find("label")!.Style["opacity"] = Scalar.From(-1); Assert(ProjectCodec.Validate(d.Project).Any(x => x.Path!.EndsWith("/style")));
        });
        Add("style draft edits are detached until a single undoable Apply", () => {
            var d = Document(); var draft = new StyleClassDraft(d); draft.SetProperties("Alpha", Props("#FFFFFF"));
            Equal("#112233", d.Project.Styles["Alpha"]["foreground"].Text); Assert(!d.IsDirty); Assert(draft.Apply(d));
            Equal(1L, d.Generation); Equal("#FFFFFF", d.Project.Styles["Alpha"]["foreground"].Text); Assert(!draft.Apply(d));
            d.Undo(); Equal("#112233", d.Project.Styles["Alpha"]["foreground"].Text); d.Redo(); Equal("#FFFFFF", d.Project.Styles["Alpha"]["foreground"].Text);
        });
        Add("style rename updates assignments and preserves prior draft snapshots", () => {
            var d = Document(); var draft = new StyleClassDraft(d); var names = draft.Names; var assignments = draft.Assignments("label");
            draft.Rename("Alpha", "Typography/Heading"); Assert(names.Contains("Alpha")); Assert(assignments.Contains("Alpha"));
            Assert(draft.Assignments("label").Contains("Typography/Heading")); draft.Apply(d);
            Assert(!d.Project.Styles.ContainsKey("Alpha")); Assert(d.Find("label")!.StyleClasses.Contains("Typography/Heading"));
            d.Undo(); Assert(d.Find("label")!.StyleClasses.Contains("Alpha"));
        });
        Add("style deletion removes explicit assignments atomically without changing inline values", () => {
            var d = Document(); d.Find("label")!.Style["opacity"] = Scalar.From(.5); var draft = new StyleClassDraft(d);
            Equal(1, draft.ReferenceCount("Zulu")); draft.Delete("Zulu"); Equal(2, d.Find("label")!.StyleClasses.Count);
            draft.Apply(d); Assert(d.Find("label")!.StyleClasses.SequenceEqual(new[] { "Alpha" })); Equal(.5, d.Find("label")!.Style["opacity"].Number);
        });
        Add("style creation and duplication own properties and can be assigned", () => {
            var d = Document(); var draft = new StyleClassDraft(d); var props = Props("#778899"); draft.Create("New", props); props.Clear();
            draft.Create("Copy", draft.Properties("New")); draft.Assign("label", ["New", "Copy", "New"]); draft.Apply(d);
            Equal("#778899", d.Project.Styles["Copy"]["foreground"].Text); Equal(2, d.Find("label")!.StyleClasses.Count);
        });
        Add("style draft snapshots cannot be mutated by consumers", () => {
            var draft = new StyleClassDraft(Document()); Throws<NotSupportedException>(() => ((IList<string>)draft.Assignments("label")).Clear());
            Throws<NotSupportedException>(() => ((IList<string>)draft.Names).Clear()); Throws<NotSupportedException>(() => ((IDictionary<string, Scalar>)draft.Properties("Alpha")).Clear());
        });
        Add("invalid class operations leave the entire style draft unchanged", () => {
            var draft = new StyleClassDraft(Document()); var names = draft.Names;
            foreach (var name in new[] { "", " / ", "A//B", "../A", "A, B", "A\nB" }) Throws<ArgumentException>(() => draft.Create(name));
            Throws<ArgumentException>(() => draft.Rename("Alpha", "Zulu")); Throws<ArgumentException>(() => draft.Assign("label", ["Absent"]));
            Throws<ArgumentException>(() => draft.SetProperties("Alpha", new Dictionary<string, Scalar> { ["fontSize"] = Scalar.From(1) }));
            Assert(ReferenceEquals(names, draft.Names)); Assert(!draft.IsDirty); Equal(0L, draft.Revision);
        });
        Add("stale style drafts cannot overwrite newer or replaced documents", () => {
            var d = Document(); var draft = new StyleClassDraft(d); draft.Create("New"); d.Execute("Rename project", p => p.Name = "Different");
            Throws<InvalidOperationException>(() => draft.Apply(d)); Throws<InvalidOperationException>(() => draft.Apply(Document())); Assert(!d.Project.Styles.ContainsKey("New"));
        });
        Add("creating then deleting an unapplied style produces no undo record", () => {
            var d = Document(); var draft = new StyleClassDraft(d); draft.Create("New"); draft.Delete("New");
            Assert(!draft.Apply(d)); Assert(!draft.IsDirty); Assert(!d.CanUndo); Equal(0L, d.Generation);
        });
        Add("style drafts acknowledge a committed edit when a Changed observer throws", () => {
            var d = Document(); var draft = new StyleClassDraft(d); draft.Create("New"); Action observer = () => throw new InvalidOperationException("Observer");
            d.Changed += observer; Throws<InvalidOperationException>(() => draft.Apply(d)); Assert(!draft.IsDirty); Equal(1L, d.Generation);
            d.Changed -= observer; Assert(!draft.Apply(d)); draft.Rename("New", "Renamed"); Assert(draft.Apply(d));
        });
        Add("style assignment edits preserve already-missing imported classes but reject new missing ones", () => {
            var d = Document(); d.Find("label")!.StyleClasses.Add("Missing"); var draft = new StyleClassDraft(d);
            draft.Assign("label", ["Missing", "Alpha"]); draft.Apply(d); Assert(d.Find("label")!.StyleClasses.Contains("Missing"));
            Throws<ArgumentException>(() => draft.Assign("label", ["AnotherMissing"]));
        });
        Add("styles preserve unknown values through edit export and import", () => {
            var d = Document(); var draft = new StyleClassDraft(d); draft.Create("Extra", new Dictionary<string, Scalar> { ["futureSetting"] = Scalar.FromJson("[1,2]") });
            draft.Apply(d); var copy = ProjectCodec.Clone(d.Project); Equal(2, copy.Styles["Extra"]["futureSetting"].Items.Count);
        });
    }
}
