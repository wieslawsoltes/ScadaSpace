using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using static Suite;

internal static class SymbolStateTests
{
    private static SymbolStateDefinition State(string color = "#224466") => new(new(PrimaryColor: color), new(PrimaryColor: "#112233"), new[] { "Motor", "Vessel" });
    private static DesignerDocument Document() => new(new ProjectDefinition { Views = [new() { Path = "Main", Root = new() { Id = "root", Type = "Container", Children = [new() { Id = "motor", Type = "Motor", Props = new() { ["state"] = Scalar.From("service") } }] } }] });
    [ModuleInitializer]
    internal static void Register()
    {
        Add("symbol states include all built-in families and customizable partial valve state", () => {
            var catalog = SymbolStateCatalog.Empty; Equal(9, catalog.Names.Count);
            Assert(catalog.Resolve("Valve", "partiallyClosed", "light").StrokeColor.Length > 0);
            Assert(catalog.Resolve("Motor", "running", "light").EnableAnimation);
            Throws<ArgumentException>(() => catalog.Resolve("Motor", "open", "light"));
            Throws<ArgumentException>(() => catalog.Resolve("Valve", "running", "light"));
        });
        Add("symbol state assignments and catalogs own their input collections", () => {
            var symbols = new[] { "Motor" }; var state = new SymbolStateDefinition(new(), new(), symbols);
            var source = new Dictionary<string, SymbolStateDefinition> { ["service"] = state }; var catalog = new SymbolStateCatalog(source);
            symbols[0] = "Pump"; source.Clear(); Assert(catalog.Get("service").Symbols.SequenceEqual(new[] { "Motor" }));
            Throws<NotSupportedException>(() => ((IList<string>)state.Symbols).Clear());
            Throws<NotSupportedException>(() => ((IDictionary<string, SymbolStateDefinition>)catalog.States).Clear());
        });
        Add("symbol names families themes and colors are validated before publication", () => {
            foreach (var name in new[] { "", " bad", "a/b", "a b", new string('a', 65) }) Throws<ArgumentException>(() => SymbolStateCatalog.Empty.With(name, State()));
            Throws<ArgumentException>(() => new SymbolStateDefinition(new(PrimaryColor: "red"), new(), new[] { "Motor" }));
            Throws<ArgumentException>(() => new SymbolStateDefinition(new(), new(), new[] { "Motor", "Motor" }));
            Throws<ArgumentException>(() => new SymbolStateDefinition(new(), new(), new[] { "Unknown" }));
            Throws<ArgumentException>(() => SymbolStateCatalog.Empty.Resolve("Pump", "default", "night"));
        });
        Add("symbol catalog snapshots retain ordinal matching even from case-insensitive dictionaries", () => {
            var values = new Dictionary<string, SymbolStateDefinition>(StringComparer.OrdinalIgnoreCase) { ["service"] = State() };
            var catalog = new SymbolStateCatalog(values);
            Throws<ArgumentException>(() => catalog.Get("Service")); Equal("#224466", catalog.Resolve("Motor", "service", "light").PrimaryColor);
        });
        Add("symbol catalog bounds reject excessive overrides without mutating the old catalog", () => {
            var values = Enumerable.Range(0, 129).ToDictionary(i => "s" + i, _ => State());
            Throws<ArgumentException>(() => new SymbolStateCatalog(values)); Equal(0, SymbolStateCatalog.Empty.States.Count);
        });
        Add("project symbol states round-trip including reset baselines and applied families", () => {
            var p = Document().Project; p.SymbolStates = p.SymbolStates.With("service", State().WithTheme("light", new(PrimaryColor: "#ABCDEF")));
            var restored = ProjectCodec.Clone(p); Assert(p.SymbolStates.Equivalent(restored.SymbolStates));
            Equal("#224466", restored.SymbolStates.Get("service").ResetTheme("light").Light.PrimaryColor);
            Assert(!ReferenceEquals(p.SymbolStates, restored.SymbolStates));
        });
        Add("old project JSON loads built-in states without requiring a new schema", () => {
            var p = ProjectCodec.Deserialize("{\"schemaVersion\":1,\"views\":[{\"path\":\"Main\"}]}");
            Equal(9, p.SymbolStates.Names.Count); Equal(0, p.SymbolStates.States.Count);
            var previous = p.SymbolStates; Throws<ArgumentNullException>(() => p.SymbolStates = null!); Assert(ReferenceEquals(previous, p.SymbolStates));
        });
        Add("symbol state lookup and stable names allocate no per-read snapshots", () => {
            var c = SymbolStateCatalog.Empty.With("service", State()); var names = c.Names; var style = c.Resolve("Motor", "service", "light");
            for (var i = 0; i < 100; i++) c.Resolve("Motor", "service", "light");
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) { Assert(ReferenceEquals(style, c.Resolve("Motor", "service", "light"))); Assert(ReferenceEquals(names, c.Names)); }
            Assert(GC.GetAllocatedBytesForCurrentThread() - before < 128);
        });
        Add("symbol state edits are detached until an undoable Apply", () => {
            var d = Document(); var draft = new SymbolStateDraft(d); draft.Create("service"); draft.Set("service", State());
            Assert(!d.Project.SymbolStates.Names.Contains("service")); Assert(draft.Apply(d)); Equal(1L, d.Generation);
            Equal("#224466", d.Project.SymbolStates.Get("service").Light.PrimaryColor); Assert(!draft.Apply(d));
            d.Undo(); Assert(!d.Project.SymbolStates.Names.Contains("service")); d.Redo(); Assert(d.Project.SymbolStates.Names.Contains("service"));
        });
        Add("symbol state cancellation and stale document rejection preserve committed definitions", () => {
            var d = Document(); var draft = new SymbolStateDraft(d); draft.Create("temporary");
            Assert(!d.Project.SymbolStates.Names.Contains("temporary"));
            d.Execute("Rename project", p => p.Name = "Changed"); Throws<InvalidOperationException>(() => draft.Apply(d));
            Assert(!d.Project.SymbolStates.Names.Contains("temporary"));
        });
        Add("symbol state renaming preserves literal references in the same transaction", () => {
            var d = Document(); var draft = new SymbolStateDraft(d); draft.Create("service"); draft.Set("service", State()); draft.Apply(d);
            draft.Rename("service", "maintenance"); Assert(draft.Apply(d)); Equal("maintenance", d.Find("motor")!.Text("state"));
            d.Undo(); Equal("service", d.Find("motor")!.Text("state")); Assert(d.Project.SymbolStates.Names.Contains("service"));
        });
        Add("built-in states cannot be deleted or renamed", () => {
            var draft = new SymbolStateDraft(Document()); Throws<ArgumentException>(() => draft.Delete("running"));
            Throws<ArgumentException>(() => draft.Rename("running", "custom")); Assert(!draft.IsDirty);
        });
        Add("duplicate symbol states capture their own original theme for later resets", () => {
            var draft = new SymbolStateDraft(Document()); draft.Set("default", State()); draft.Create("copied", "default");
            draft.Set("copied", draft.Catalog.Get("copied").WithTheme("light", new(PrimaryColor: "#998877")));
            draft.ResetTheme("copied", "light"); Equal("#224466", draft.Catalog.Get("copied").Light.PrimaryColor);
            Assert(draft.Catalog.Get("copied").Symbols.Contains("Motor"));
        });
        Add("new symbol states begin without applied families", () => {
            var draft = new SymbolStateDraft(Document()); draft.Create("new-state"); Equal(0, draft.Catalog.Get("new-state").Symbols.Count);
            Throws<ArgumentException>(() => draft.Catalog.Resolve("Pump", "new-state", "light"));
        });
        Add("symbol theme revert uses last Apply and reset uses creation settings", () => {
            var d = Document(); var draft = new SymbolStateDraft(d); draft.Create("service"); draft.Set("service", State()); draft.Apply(d);
            draft.Set("service", draft.Catalog.Get("service").WithTheme("light", new(PrimaryColor: "#FF0000")));
            draft.RevertTheme("service", "light"); Equal("#224466", draft.Catalog.Get("service").Light.PrimaryColor);
        });
        Add("reverted symbol catalog and literal edits do not add document history", () => {
            var d = Document(); var draft = new SymbolStateDraft(d); draft.Create("temporary"); draft.Delete("temporary");
            Assert(!draft.Apply(d)); Equal(0L, d.Generation); Assert(!d.CanUndo);
        });
        Add("committed symbol state drafts recover after a Changed observer throws", () => {
            var d = Document(); var draft = new SymbolStateDraft(d); draft.Create("service");
            System.Action observer = () => throw new InvalidOperationException("observer"); d.Changed += observer;
            Throws<InvalidOperationException>(() => draft.Apply(d)); Assert(!draft.IsDirty); Assert(!draft.Apply(d)); Equal(1L, d.Generation);
            d.Changed -= observer; draft.Delete("service"); Assert(draft.Apply(d));
        });
    }
}
