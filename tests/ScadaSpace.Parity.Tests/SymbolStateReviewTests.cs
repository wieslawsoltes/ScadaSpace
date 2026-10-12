using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using static Suite;

internal static class SymbolStateReviewTests
{
    private static DesignerDocument Document() => new(new ProjectDefinition { Views = [new() { Path = "Main" }] });
    private static void Color(SymbolStateDraft draft, string state, string color, string theme = "light") => draft.Set(state, draft.Catalog.Get(state).WithTheme(theme,
        (theme == "light" ? draft.Catalog.Get(state).Light : draft.Catalog.Get(state).Dark) with { PrimaryColor = color }));
    [ModuleInitializer]
    internal static void Register()
    {
        Add("Add uses factory themes rather than an edited project default", () => {
            var draft = new SymbolStateDraft(Document()); Color(draft, "default", "#FF0000"); Color(draft, "default", "#112233", "dark");
            draft.Create("new-state"); var created = draft.Catalog.Get("new-state");
            Equal(SymbolStateCatalog.Empty.Get("default").Light, created.Light);
            Equal(SymbolStateCatalog.Empty.Get("default").Dark, created.Dark); Equal(0, created.Symbols.Count);
        });
        Add("Duplicate intentionally copies current edited themes rather than factory values", () => {
            var draft = new SymbolStateDraft(Document()); Color(draft, "default", "#FF0000"); draft.Create("copy", "default");
            Equal("#FF0000", draft.Catalog.Get("copy").Light.PrimaryColor);
            Color(draft, "copy", "#112233"); draft.ResetTheme("copy", "light"); Equal("#FF0000", draft.Catalog.Get("copy").Light.PrimaryColor);
        });
        Add("state theme Undo follows a renamed state's last-applied baseline", () => {
            var document = Document(); var draft = new SymbolStateDraft(document); draft.Create("service"); Color(draft, "service", "#224466"); draft.Apply(document);
            Color(draft, "service", "#FF0000"); draft.Rename("service", "maintenance"); draft.RevertTheme("maintenance", "light");
            Equal("#224466", draft.Catalog.Get("maintenance").Light.PrimaryColor); Assert(draft.Apply(document));
            Color(draft, "maintenance", "#00FF00"); draft.RevertTheme("maintenance", "light"); Equal("#224466", draft.Catalog.Get("maintenance").Light.PrimaryColor);
        });
        Add("Undo on an unapplied state returns its creation theme", () => {
            var draft = new SymbolStateDraft(Document()); draft.Create("new-state"); var initial = draft.Catalog.Get("new-state").Light;
            Color(draft, "new-state", "#FF0000"); draft.RevertTheme("new-state", "light"); Equal(initial, draft.Catalog.Get("new-state").Light);
        });
        Add("resetting an edited built-in removes redundant overrides without an undo entry", () => {
            var document = Document(); var draft = new SymbolStateDraft(document);
            Color(draft, "running", "#123456"); draft.ResetTheme("running", "light");
            Equal(0, draft.Catalog.States.Count); Assert(!draft.Apply(document)); Equal(0L, document.Generation); Assert(!document.CanUndo);
        });
        Add("a failed state rename leaves both catalog and saved Undo baseline intact", () => {
            var document = Document(); var draft = new SymbolStateDraft(document); draft.Create("service"); Color(draft, "service", "#224466"); draft.Apply(document);
            Color(draft, "service", "#FF0000"); Throws<ArgumentException>(() => draft.Rename("service", "running"));
            draft.RevertTheme("service", "light"); Equal("#224466", draft.Catalog.Get("service").Light.PrimaryColor); Assert(!draft.Apply(document));
        });
        Add("delete and recreate does not reuse the deleted state's saved Undo theme", () => {
            var document = Document(); var draft = new SymbolStateDraft(document); draft.Create("service"); Color(draft, "service", "#224466"); draft.Apply(document);
            draft.Delete("service"); draft.Create("service"); Color(draft, "service", "#FF0000"); draft.RevertTheme("service", "light");
            Equal(SymbolStateCatalog.Empty.Get("default").Light, draft.Catalog.Get("service").Light);
        });
    }
}
