using System.Runtime.CompilerServices;
using ScadaSpace.Designer;
using static Suite;

internal static class WorkspaceTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("workspace preferences validate and round-trip independently of project data", () => {
            var p = new DesignerPanels { LeftWidth = 280, RightWidth = 330, ComponentsPinned = true, PaletteTiles = true, TagBrowser = false };
            Equal(p, DesignerPanels.Parse(p.Serialize())); Assert(!new DesignerPanels().ComponentsPinned);
        });
        Add("workspace rejects corrupt unknown-version and out-of-range settings", () => {
            foreach (var json in new[] { "null", "[]", "{", "{\"Version\":2}", "{\"LeftWidth\":1}", "{\"ProjectFraction\":1}", new string('x',4097) })
                Throws<FormatException>(() => DesignerPanels.Parse(json));
            Equal(new DesignerPanels(), DesignerPanels.Parse(null));
            Throws<InvalidOperationException>(() => new DesignerPanels { LeftWidth = double.NaN }.Serialize());
        });
        Add("resource activation is a no-op and retains snapshot identity", () => {
            var w = new DesignerWorkspace(); w.Reset(["Overview","Trends"]); var paths = w.OpenViews; var revision = w.Revision;
            for (var i=0;i<1000;i++) Assert(!w.Open("Overview"));
            Assert(ReferenceEquals(paths,w.OpenViews)); Equal(revision,w.Revision);
        });
        Add("closing active resources picks the nearest surviving tab without deleting availability", () => {
            var w = new DesignerWorkspace(); w.Reset(["A","B","C"],"B"); w.Close("B"); Equal("C",w.ActiveView);
            w.Close("C"); Equal("A",w.ActiveView); Assert(w.Open("B")); Equal(2,w.OpenViews.Count);
        });
        Add("closing an inactive resource preserves the active view", () => {
            var w = new DesignerWorkspace(); w.Reset(["A","B","C"],"C"); w.Close("A"); Equal("C",w.ActiveView);
            var revision=w.Revision; Assert(!w.Close("missing")); Equal(revision,w.Revision);
        });
        Add("close others and close all preserve immutable prior tab snapshots", () => {
            var w = new DesignerWorkspace(); w.Reset(["A","B","C"]); var paths=w.OpenViews;
            w.CloseOthers("B"); Equal("B",w.ActiveView); Equal(1,w.OpenViews.Count); Equal(3,paths.Count);
            w.CloseAll(); Assert(w.ActiveView is null); Equal(0,w.OpenViews.Count); w.Open("A"); Equal("A",w.ActiveView);
            Throws<NotSupportedException>(() => ((IList<string>)paths).Clear());
        });
        Add("resource reconciliation prunes removed views without reopening user-closed resources", () => {
            var w = new DesignerWorkspace(); w.Reset(["A","B","C"]); w.Close("B"); w.Reconcile(["B","C","D"]);
            Equal("C",w.ActiveView); Assert(w.OpenViews.SequenceEqual(new[]{"C"})); w.Open("D"); Equal(2,w.OpenViews.Count);
        });
        Add("resource cycling wraps in both directions", () => {
            var w=new DesignerWorkspace(); w.Reset(["A","B","C"]); w.Cycle(-1); Equal("C",w.ActiveView); w.Cycle(1); Equal("A",w.ActiveView);
            w.CloseAll(); w.Cycle(1); Assert(w.ActiveView is null);
        });
        Add("invalid resource changes leave the complete workspace unchanged", () => {
            var w=new DesignerWorkspace(); w.Reset(["A","B"]); var paths=w.OpenViews; var revision=w.Revision;
            Throws<ArgumentException>(()=>w.Reset(["A","A"])); Throws<ArgumentException>(()=>w.Reset(["A"],"missing"));
            Throws<ArgumentException>(()=>w.Reconcile(Enumerable.Range(0,513).Select(i=>i.ToString())));
            Throws<ArgumentException>(()=>w.Open("missing")); Throws<ArgumentException>(()=>w.CloseOthers("missing"));
            Assert(ReferenceEquals(paths,w.OpenViews)); Equal(revision,w.Revision); Equal("A",w.ActiveView);
        });
    }
}
