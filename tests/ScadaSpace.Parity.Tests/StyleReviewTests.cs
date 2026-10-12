using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Runtime;
using static Suite;

internal static class StyleReviewTests
{
    private static Dictionary<string, Scalar> Values(string color = "#123456") => new() { ["foreground"] = Scalar.From(color) };
    private static DesignerDocument Document()
    {
        var root = new ComponentNode { Id = "root", Name = "Root", Type = "Container" };
        root.Children.Add(new() { Id = "a", Name = "First", StyleClasses = ["Alpha", "Alpha", "Zulu"] });
        root.Children.Add(new() { Id = "b", Name = "Second", StyleClasses = ["Alpha", "Missing"] });
        return new(new ProjectDefinition { Views = [new() { Root = root }], Styles = new() { ["Alpha"] = Values(), ["Zulu"] = Values("#445566") } });
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("merged style budgets accept the union of individually bounded classes", () =>
        {
            var a = Enumerable.Range(0, 128).ToDictionary(i => "a" + i, i => Scalar.From(i));
            var b = Enumerable.Range(0, 128).ToDictionary(i => "b" + i, i => Scalar.From(i));
            var resolver = new StyleResolver(new Dictionary<string, Dictionary<string, Scalar>> { ["Alpha"] = a, ["Bravo"] = b });
            var style = resolver.Resolve(new() { StyleClasses = ["Alpha", "Bravo"], Style = Values() });
            Equal(257, style.Values.Count); StyleRules.ValidateResolvedValues(style.Values);
            Throws<ArgumentException>(() => StyleRules.ValidateValues(style.Values));
            Equal(256, style.UnsupportedProperties.Count);
        });
        Add("resolved style validation still enforces known property ranges and union limits", () =>
        {
            Throws<ArgumentException>(() => StyleRules.ValidateResolvedValues(new Dictionary<string, Scalar> { ["opacity"] = Scalar.From(2) }));
            var tooMany = Enumerable.Range(0, StyleRules.MaximumResolvedProperties + 1).ToDictionary(i => "x" + i, i => Scalar.From(i));
            Throws<ArgumentException>(() => StyleRules.ValidateResolvedValues(tooMany));
        });
        Add("cascade origins track winning classes and inline overrides without aliasing", () =>
        {
            var d = Document(); var node = d.Find("a")!; var resolver = new StyleResolver(d.Project.Styles);
            var first = resolver.Resolve(node); Equal("Zulu", first.Origins["foreground"].ClassName); Assert(!first.Origins["foreground"].IsInline);
            node.Style["foreground"] = Scalar.From("#FFFFFF"); var second = resolver.Resolve(node);
            Assert(second.Origins["foreground"].IsInline); Assert(second.Origins["foreground"].ClassName is null);
            Equal("Zulu", first.Origins["foreground"].ClassName);
            Throws<NotSupportedException>(() => ((IDictionary<string, StyleValueOrigin>)first.Origins).Clear());
        });
        Add("reference counts index distinct component identities including missing classes", () =>
        {
            var draft = new StyleClassDraft(Document()); Equal(2, draft.ReferenceCount("Alpha")); Equal(1, draft.ReferenceCount("Missing"));
            var before = draft.ReferencingComponents("Alpha"); draft.Assign("a", ["Zulu"]);
            Equal(1, draft.ReferenceCount("Alpha")); Equal(2, before.Count); Equal("b", draft.ReferencingComponents("Alpha")[0]);
            Throws<NotSupportedException>(() => ((IList<string>)before).Clear());
        });
        Add("reference-preserving rename keeps dangling assignments as an explicit compatibility mode", () =>
        {
            var d = Document(); var draft = new StyleClassDraft(d); var before = draft.Assignments("a");
            draft.Rename("Alpha", "Theme/Header", StyleRenameMode.PreserveAssignments);
            Assert(ReferenceEquals(before, draft.Assignments("a"))); Equal(2, draft.ReferenceCount("Alpha")); Equal(0, draft.ReferenceCount("Theme/Header"));
            draft.Apply(d); Assert(!d.Project.Styles.ContainsKey("Alpha")); Assert(d.Find("a")!.StyleClasses.Contains("Alpha"));
            Assert(new StyleResolver(d.Project.Styles).Resolve(d.Find("a")!).MissingClasses.Contains("Alpha"));
        });
        Add("reference-updating rename merges existing dangling target references", () =>
        {
            var d = Document(); var draft = new StyleClassDraft(d); draft.Rename("Alpha", "Missing");
            Equal(0, draft.ReferenceCount("Alpha")); Equal(2, draft.ReferenceCount("Missing"));
            Assert(draft.Assignments("b").SequenceEqual(new[] { "Missing" }));
            draft.Delete("Missing"); Equal(0, draft.ReferenceCount("Missing")); Equal(0, draft.Assignments("b").Count);
        });
        Add("reference rewrite retains unrelated assignment snapshot identities", () =>
        {
            var draft = new StyleClassDraft(Document()); var root = draft.Assignments("root");
            draft.Rename("Alpha", "Theme/Header"); Assert(ReferenceEquals(root, draft.Assignments("root")));
            draft.Delete("Zulu"); Assert(ReferenceEquals(root, draft.Assignments("root")));
        });
        Add("atomic style and assignment edits reject invalid assignments without partial class changes", () =>
        {
            var draft = new StyleClassDraft(Document()); var original = draft.Properties("Alpha"); var assigned = draft.Assignments("a");
            Throws<ArgumentException>(() => draft.Update("Alpha", Values("#FFFFFF"), "a", ["Absent"]));
            Assert(ReferenceEquals(original, draft.Properties("Alpha"))); Assert(ReferenceEquals(assigned, draft.Assignments("a")));
            Equal(0L, draft.Revision); Assert(!draft.IsDirty); Equal(2, draft.ReferenceCount("Alpha"));
        });
        Add("atomic style edit owns valid values and updates references with one draft revision", () =>
        {
            var draft = new StyleClassDraft(Document()); var values = Values("#FFFFFF");
            draft.Update("Alpha", values, "a", ["Zulu"]); values.Clear();
            Equal(1L, draft.Revision); Equal("#FFFFFF", draft.Properties("Alpha")["foreground"].Text); Equal(1, draft.ReferenceCount("Alpha"));
            draft.Update("Alpha", Values("#FFFFFF"), "a", ["Zulu"]); Equal(1L, draft.Revision);
        });
        Add("invalid combined edit signatures and rename modes leave the draft unchanged", () =>
        {
            var draft = new StyleClassDraft(Document());
            Throws<ArgumentException>(() => draft.Update("Alpha", Values(), "a"));
            Throws<ArgumentException>(() => draft.Update("Alpha", Values(), assignments: ["Alpha"]));
            Throws<ArgumentOutOfRangeException>(() => draft.Rename("Alpha", "Next", (StyleRenameMode)99));
            Assert(!draft.IsDirty); Equal(0L, draft.Revision);
        });
        Add("restoring only class names does not create a dictionary-order-only undo record", () =>
        {
            var d = Document(); var draft = new StyleClassDraft(d); var old = ProjectCodec.Serialize(d.Project);
            draft.Rename("Zulu", "Temporary", StyleRenameMode.PreserveAssignments);
            draft.Rename("Temporary", "Zulu", StyleRenameMode.PreserveAssignments);
            Assert(!draft.Apply(d)); Assert(!d.CanUndo); Equal(0L, d.Generation); Equal(old, ProjectCodec.Serialize(d.Project));
        });
        Add("style color parsing uses explicit ARGB byte order and canonical round trips", () =>
        {
            Equal(new StyleColor(255, 17, 34, 51), StyleColor.Parse("#112233"));
            Equal(new StyleColor(128, 17, 34, 51), StyleColor.Parse("#80112233"));
            Equal("#00ABCDEF", StyleColor.Parse("#00abcdef").ToString());
            foreach (var text in new[] { "#00000000", "#FFFFFFFF", "#01234567", "#80FF0000" })
                Equal(text, StyleColor.Parse(text).ToString());
        });
        Add("style color parsing rejects malformed text without throwing from TryParse", () =>
        {
            foreach (var text in new string?[] { null, "", "#123", "112233", "#GG1122", " #112233", "#112233 ", "#１２３４５６", "#1234567", "#123456789" })
            { Assert(!StyleColor.TryParse(text, out var value)); Equal(default(StyleColor), value); }
        });
        Add("runtime view updates retain identical style catalogs and reject invalid replacement atomically", () =>
        {
            var d = Document(); using var runtime = new ProjectRuntime(d.Project);
            var catalog = runtime.Styles; var original = runtime.Project;
            var equal = ProjectCodec.Clone(original); equal.Name = "Edited project"; runtime.UpdateViews(equal);
            Assert(ReferenceEquals(catalog, runtime.Styles)); Assert(ReferenceEquals(equal, runtime.Project));
            var invalid = ProjectCodec.Clone(equal); invalid.Styles["Alpha"]["fontSize"] = Scalar.From(0);
            Throws<ArgumentException>(() => runtime.UpdateViews(invalid));
            Assert(ReferenceEquals(catalog, runtime.Styles)); Assert(ReferenceEquals(equal, runtime.Project));
            var changed = ProjectCodec.Clone(equal); changed.Styles["Alpha"]["foreground"] = Scalar.From("#FFFFFF"); runtime.UpdateViews(changed);
            Assert(!ReferenceEquals(catalog, runtime.Styles));
        });
        Add("catalog comparison cannot mistake case-insensitive incoming keys for ordinal equivalents", () =>
        {
            var resolver = new StyleResolver(new Dictionary<string, Dictionary<string, Scalar>> { ["Alpha"] = Values() });
            var otherName = new Dictionary<string, Dictionary<string, Scalar>>(StringComparer.OrdinalIgnoreCase) { ["ALPHA"] = Values() };
            var otherKey = new Dictionary<string, Dictionary<string, Scalar>> { ["Alpha"] = new(StringComparer.OrdinalIgnoreCase) { ["Foreground"] = Scalar.From("#123456") } };
            Assert(!resolver.MatchesCatalog(otherName)); Assert(!resolver.MatchesCatalog(otherKey));
            Assert(resolver.MatchesCatalog(new Dictionary<string, Dictionary<string, Scalar>> { ["Alpha"] = Values() }));
        });
        Add("inline style cache detects a case-only key replacement under an insensitive caller comparer", () =>
        {
            var resolver = new StyleResolver(new Dictionary<string, Dictionary<string, Scalar>>());
            var node = new ComponentNode { Style = Values() }; var old = resolver.Resolve(node);
            node.Style = new(StringComparer.OrdinalIgnoreCase) { ["Foreground"] = Scalar.From("#123456") };
            var next = resolver.Resolve(node); Assert(!ReferenceEquals(old, next));
            Assert(!next.Values.ContainsKey("foreground")); Assert(next.UnsupportedProperties.Contains("Foreground"));
        });
        Add("style drafts preserve case-only property changes rather than treating them as no-ops", () =>
        {
            var draft = new StyleClassDraft(Document());
            draft.SetProperties("Alpha", new Dictionary<string, Scalar>(StringComparer.OrdinalIgnoreCase) { ["Foreground"] = Scalar.From("#123456") });
            Assert(draft.IsDirty); Assert(!draft.Properties("Alpha").ContainsKey("foreground"));
            Assert(draft.Properties("Alpha").ContainsKey("Foreground"));
        });
        Add("cached reference counts and color parsing do not allocate per lookup", () =>
        {
            var draft = new StyleClassDraft(Document());
            for (var i = 0; i < 100; i++) { draft.ReferenceCount("Alpha"); StyleColor.TryParse("#80112233", out _); }
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) { Equal(2, draft.ReferenceCount("Alpha")); Assert(StyleColor.TryParse("#80112233", out _)); }
            Assert(GC.GetAllocatedBytesForCurrentThread() - bytes < 128);
        });
    }
}
