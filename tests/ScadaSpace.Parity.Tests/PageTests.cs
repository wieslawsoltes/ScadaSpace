using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Layout;
using ScadaSpace.Runtime;
using ScadaSpace.Tags;
using static Suite;

internal static class PageTests
{
    internal static ProjectDefinition Project()
    {
        var project = new ProjectDefinition { Name = "Page tests" };
        foreach (var path in new[] { "Home", "Equipment", "Navigation", "Details" })
            project.Views.Add(new() { Path = path });
        project.Views[1].Parameters["equipment"] = Scalar.From("default");
        project.Views[1].Parameters["unit"] = Scalar.From("bar");
        project.Pages = [new() { Path = "/", View = "Home" }, new() { Path = "/equipment/:equipment", View = "Equipment", Parameters = new() { ["unit"] = Scalar.From("psi") } }, new() { Path = "/equipment/all", View = "Home" }];
        project.SharedDocks = [new() { Id = "nav", View = "Navigation", Edge = DockEdge.North, Display = DockDisplay.Visible, Size = 64, MinimumSize = 32 }];
        project.Pages[1].Docks = [new() { Id = "details", View = "Details", Edge = DockEdge.East, Content = DockContent.Cover, Modal = true }];
        return project;
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("page addresses normalize paths and decode query strings separately", () => {
            var a = PageAddress.Parse("/equipment/Pump%20A/?unit=bar&name=Line+One&unit=psi");
            Equal("/equipment/Pump%20A", a.Path); Equal("Pump A", a.Segments[1]); Equal("Line One", a.Query["name"].Text); Equal("psi", a.Query["unit"].Text);
            Equal("/equipment/Pump%20A?name=Line%20One&unit=psi", a.Url);
            Throws<NotSupportedException>(() => ((IDictionary<string, Scalar>)a.Query)["unit"] = Scalar.Null);
        });
        Add("invalid external traversal and malformed page URLs are rejected", () => {
            foreach (var url in new[] { "https://example.com/", "//example.com", "/a//b", "/..", "/a/%2e%2e", "/a%2Fb", "/a%3Fb", "/a%23b", "/a%25b", "/a\\b", "/%ZZ", "/?q=%", "/a#b", "/?q=%00" }) Throws<ArgumentException>(() => PageAddress.Parse(url));
            Throws<ArgumentException>(() => PageAddress.Parse("/" + new string('a', 4096)));
            Throws<ArgumentException>(() => PageAddress.Parse("/?" + string.Join('&', Enumerable.Range(0, 65).Select(i => "x" + i + "=1"))));
        });
        Add("route patterns validate identifiers and expand losslessly", () => {
            var pattern = new PageRoutePattern("/equipment/:id");
            Equal("/equipment/Pump%20A", pattern.Expand(new Dictionary<string, Scalar> { ["id"] = Scalar.From("Pump A") }));
            Equal("/equipment/1.23456789", pattern.Expand(new Dictionary<string, Scalar> { ["id"] = Scalar.From(1.23456789) }));
            Assert(pattern.TryMatch(PageAddress.Parse("/equipment/Pump%20A"), out var captures)); Equal("Pump A", captures["id"].Text);
            foreach (var path in new[] { "/:", "/:1a", "/a:id", "/:id/:id", "/a?b=1", "/%61" }) Throws<ArgumentException>(() => new PageRoutePattern(path));
            Throws<ArgumentException>(() => pattern.Expand(new Dictionary<string, Scalar> { ["id"] = Scalar.From("a/b") }));
            Throws<ArgumentException>(() => pattern.Expand(new Dictionary<string, Scalar>()));
        });
        Add("literal routes take precedence over parameter routes", () => {
            using var runtime = new ProjectRuntime(Project()); var session = new RuntimeSession(runtime);
            Assert(session.Navigate("/equipment/all")); Equal("Home", session.Current!.Page.View);
            Assert(session.Navigate("/equipment/Pump%20A?unit=kPa")); Equal("Equipment", session.Current!.Page.View);
            Equal("Pump A", session.Current.Parameters["equipment"].Text); Equal("psi", session.Current.Parameters["unit"].Text); Equal("kPa", session.Current.Address.Query["unit"].Text);
        });
        Add("navigation snapshots copy parameters and preserve them across history", () => {
            using var runtime = new ProjectRuntime(Project()); var session = new RuntimeSession(runtime); var p = new Dictionary<string, Scalar> { ["unit"] = Scalar.From("kPa") };
            Assert(session.Navigate("/equipment/A", p)); var id = session.Current!.EntryId; p["unit"] = Scalar.From("mutated");
            Assert(session.Navigate("/equipment/B")); Assert(session.Back()); Equal(id, session.Current!.EntryId); Equal("kPa", session.Current.Parameters["unit"].Text);
            Assert(session.Forward()); Equal("B", session.Current!.Parameters["equipment"].Text);
            Assert(session.Back()); Assert(session.Navigate("/equipment/C")); Assert(!session.CanGoForward);
            Equal("default", runtime.Project.Views[1].Parameters["equipment"].Text);
        });
        Add("replace and identical navigation do not grow history", () => {
            using var runtime = new ProjectRuntime(Project()); var session = new RuntimeSession(runtime); var events = 0; session.Navigated += _ => events++;
            Assert(session.Navigate("/")); Equal(0, events); Assert(session.Navigate("/equipment/A", replace: true)); Assert(!session.CanGoBack); Equal(1, events);
            var old = session.Current; Assert(!session.Navigate("/missing")); Equal(old, session.Current); Assert(!session.Navigate("/../")); Equal(old, session.Current);
        });
        Add("browser restore selects an existing snapshot without duplicate history", () => {
            using var runtime = new ProjectRuntime(Project()); var s = new RuntimeSession(runtime); var home = s.Current!;
            s.Navigate("/equipment/A"); var a = s.Current!; s.Navigate("/equipment/B");
            Assert(s.RestoreBrowserEntry(home.EntryId, home.Address.Url)); Assert(s.CanGoForward); Assert(!s.CanGoBack);
            Assert(s.Forward()); Equal(a.EntryId, s.Current!.EntryId);
        });
        Add("navigation history is bounded", () => {
            using var runtime = new ProjectRuntime(Project()); var s = new RuntimeSession(runtime);
            for (var i = 0; i < 160; i++) Assert(s.Navigate("/equipment/" + i));
            var count = 0; while (s.Back()) count++; Equal(RuntimeSession.MaximumHistory - 1, count);
        });
        Add("session observers are isolated and reentrant navigation is rejected", () => {
            using var runtime = new ProjectRuntime(Project()); var s = new RuntimeSession(runtime); var seen = 0;
            s.Navigated += _ => throw new Exception("observer failed"); s.Changed += () => { seen++; Assert(!s.Navigate("/")); };
            Assert(s.Navigate("/equipment/A")); Equal("/equipment/A", s.CurrentPath); Equal(1, seen); Equal(1, s.Diagnostics.Count);
        });
        Add("new page and dock schema round-trips and old projects remain readable", () => {
            var p = Project(); p.Pages[1].CornerPriority = CornerPriority.LeftRight;
            var copy = ProjectCodec.Clone(p); Equal("details", copy.Pages[1].Docks[0].Id); Equal(CornerPriority.LeftRight, copy.Pages[1].CornerPriority!.Value);
            Equal(0, ProjectCodec.Validate(DemoProject.Create()).Count);
        });
        Add("page validation rejects ambiguous patterns and invalid dock definitions", () => {
            var p = Project(); p.Pages.Add(new() { Path = "/equipment/:other", View = "Home" }); Assert(ProjectCodec.Validate(p).Any(d => d.Message.Contains("Ambiguous")));
            p = Project(); p.SharedDocks.Add(p.SharedDocks[0]); Assert(ProjectCodec.Validate(p).Any(d => d.Code == "PAGE_INVALID"));
            p = Project(); p.Pages[1].Docks[0] = p.Pages[1].Docks[0] with { Size = double.NaN, View = "Missing" }; Assert(ProjectCodec.Validate(p).Any(d => d.Code == "PAGE_INVALID"));
            p = Project(); p.SharedDocks = null!; Assert(ProjectCodec.Validate(p).Any(d => d.Code == "PAGE_INVALID"));
        });
        Add("shared docks persist and page-local edge overrides replace them", () => {
            var p = Project(); p.Pages[2].Docks = [new() { Id = "replacement", View = "Details", Edge = DockEdge.North }];
            using var r = new ProjectRuntime(p); var s = new RuntimeSession(r); var shared = s.Docks.Single().InstanceId;
            s.Navigate("/equipment/A"); Equal(shared, s.Docks.Single(d => d.Definition.Id == "nav").InstanceId);
            s.Navigate("/equipment/all"); Assert(s.Docks.All(d => d.Definition.Id != "nav")); Assert(s.Docks.Any(d => d.Definition.Id == "replacement"));
            s.Back(); Assert(s.Docks.Single(d => d.Definition.Id == "nav").InstanceId != shared);
        });
        Add("docks open close and retain copied parameter state without mutating defaults", () => {
            using var r = new ProjectRuntime(Project()); var s = new RuntimeSession(r); s.Navigate("/equipment/A");
            var p = new Dictionary<string, Scalar> { ["station"] = Scalar.From("A") }; Assert(s.OpenDock("details", p)); p["station"] = Scalar.From("B");
            var d = s.Docks.Single(x => x.Definition.Id == "details"); Assert(d.IsOpen); Assert(!d.PushesContent); Equal("A", d.Parameters["station"].Text); Equal(0, r.Project.Pages[1].Docks[0].Parameters.Count);
            Assert(s.CloseDock("details")); Assert(!s.Docks.Single(x => x.Definition.Id == "details").IsOpen); Assert(s.ToggleDock("details"));
            Assert(!s.CloseDock("nav")); Assert(s.Docks.Single(x => x.Definition.Id == "nav").IsOpen); Assert(!s.OpenDock("missing"));
        });
        Add("auto docks cross the breakpoint and revert manual overrides", () => {
            var p = Project(); p.SharedDocks.Add(new() { Id = "auto", View = "Details", Edge = DockEdge.West, Display = DockDisplay.Auto, Content = DockContent.Auto, AutoBreakpoint = 900 });
            using var r = new ProjectRuntime(p); var s = new RuntimeSession(r);
            Assert(s.Docks.Single(d => d.Definition.Id == "auto").IsOpen); s.SetViewportWidth(600); var d = s.Docks.Single(d => d.Definition.Id == "auto"); Assert(!d.IsOpen && !d.PushesContent);
            s.OpenDock("auto"); Assert(s.Docks.Single(d => d.Definition.Id == "auto").IsOpen); s.SetViewportWidth(1000); s.CloseDock("auto");
            s.SetViewportWidth(700); s.SetViewportWidth(1000); Assert(s.Docks.Single(d => d.Definition.Id == "auto").IsOpen);
        });
        Add("dock resize clamps limits and is isolated per session", () => {
            using var r = new ProjectRuntime(Project()); var a = new RuntimeSession(r); var b = new RuntimeSession(r); a.Navigate("/equipment/A"); b.Navigate("/equipment/A");
            Assert(a.ResizeDock("details", 99999)); Equal(1200d, a.Docks.Single(d => d.Definition.Id == "details").Size); Equal(240d, b.Docks.Single(d => d.Definition.Id == "details").Size);
            Assert(a.ResizeDock("details", -1)); Equal(48d, a.Docks.Single(d => d.Definition.Id == "details").Size); Assert(!a.ResizeDock("details", double.NaN));
        });
        Add("dock declarative actions and parameterized navigation execute", () => {
            using var r = new ProjectRuntime(Project()); var s = new RuntimeSession(r); var node = new ComponentNode();
            node.Events["onAction"] = [new() { Kind = ActionKind.Navigate, Target = "/equipment/A", Parameters = new() { ["unit"] = Scalar.From("kPa") } }, new() { Kind = ActionKind.OpenDock, Target = "details" }];
            s.Dispatch(node, "onAction"); Equal("kPa", s.Current!.Parameters["unit"].Text); Assert(s.Docks.Single(d => d.Definition.Id == "details").IsOpen);
            node.Events["back"] = [new() { Kind = ActionKind.NavigateBack }]; s.Dispatch(node, "back"); Equal("/", s.CurrentPath);
            node.Events["forward"] = [new() { Kind = ActionKind.NavigateForward }]; s.Dispatch(node, "forward"); Equal("/equipment/A", s.CurrentPath);
        });
        Add("page geometry handles push cover and both corner priorities", () => {
            DockLayoutInput[] docks = [new("n", DockEdge.North, 100, true, true), new("w", DockEdge.West, 200, true, true), new("e", DockEdge.East, 160, true, false)];
            var a = PageLayout.Arrange(docks, 1000, 700, CornerPriority.TopBottom);
            Equal(new RectD(200,100,800,600), a.Primary); Equal(new RectD(0,0,1000,100), a.Docks["n"]); Equal(new RectD(0,100,200,600), a.Docks["w"]);
            var b = PageLayout.Arrange(docks, 1000, 700, CornerPriority.LeftRight); Equal(new RectD(200,0,640,100), b.Docks["n"]); Equal(new RectD(0,0,200,700), b.Docks["w"]);
        });
        Add("constrained page geometry never produces negative or out-of-viewport bounds", () => {
            foreach (var w in new[] { 0d, 16d, 80d, 300d }) foreach (var h in new[] { 0d, 16d, 80d, 300d }) foreach (var corner in Enum.GetValues<CornerPriority>())
            {
                var result = PageLayout.Arrange(Enum.GetValues<DockEdge>().Select(e => new DockLayoutInput(e.ToString(), e, 1200, true, true)), w, h, corner);
                foreach (var rect in result.Docks.Values.Append(result.Primary)) Assert(rect.X >= 0 && rect.Y >= 0 && rect.Width >= 0 && rect.Height >= 0 && rect.Right <= w + .0001 && rect.Bottom <= h + .0001);
            }
        });
        Add("hidden docks do not reserve page space and invalid geometry is rejected", () => {
            Equal(new RectD(0,0,900,600), PageLayout.Arrange([new("n", DockEdge.North, 100, false, true)], 900, 600, CornerPriority.TopBottom).Primary);
            Throws<ArgumentOutOfRangeException>(() => PageLayout.Arrange([], double.NaN, 500, CornerPriority.TopBottom));
            Throws<ArgumentException>(() => PageLayout.Arrange([new("a", DockEdge.North, 1, true, true), new("b", DockEdge.North, 1, true, true)], 900, 600, CornerPriority.TopBottom));
        });
    }
}
