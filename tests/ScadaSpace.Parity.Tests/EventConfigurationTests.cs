using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Runtime;
using static Suite;

internal static class EventConfigurationTests
{
    private static ProjectDefinition Project()
    {
        var root = new ComponentNode { Id = "root", Type = "Container", Name = "root" };
        root.Children.Add(new() { Id = "button", Type = "Button", Name = "Button" });
        return new() { Views = [new() { Path = "Main", Root = root }], Pages = [new() { Path = "/", View = "Main" }], Tags = [new() { Path = "[default]A" }, new() { Path = "[default]B" }] };
    }
    private static ActionConfiguration Write(double n) => new(ActionKind.WriteTag, "[default]A", Scalar.From(n), Scalar.Object([]));
    [ModuleInitializer]
    internal static void Register()
    {
        Add("event drafts stage detached changes and commit once with undo redo", () => {
            var d = new DesignerDocument(Project()); var draft = new EventConfigurationDraft(d,"button");
            draft.Add("onActionPerformed",Write(10)); Equal(0,d.Find("button")!.Events.Count); Assert(!d.IsDirty);
            Assert(draft.Apply(d)); Equal(1L,d.Generation); Equal(10d,d.Find("button")!.Events["onActionPerformed"][0].Value.Number);
            Assert(!draft.Apply(d)); d.Undo(); Equal(0,d.Find("button")!.Events.Count); d.Redo(); Equal(10d,d.Find("button")!.Events["onActionPerformed"][0].Value.Number);
        });
        Add("cancelled draft changes do not alias existing action dictionaries", () => {
            var p=Project(); var original=Write(3).ToAction(); original.Parameters["x"]=Scalar.From(1); p.Views[0].Root.Children[0].Events["onAction"]=[original];
            var d=new DesignerDocument(p);var draft=new EventConfigurationDraft(d,"button");draft.Replace("onAction",0,Write(9));
            Equal(3d,d.Find("button")!.Events["onAction"][0].Value.Number);Equal(1d,d.Find("button")!.Events["onAction"][0].Parameters["x"].Number);
        });
        Add("event reorder removal disable and repeated Apply preserve order", () => {
            var d=new DesignerDocument(Project()); var draft=new EventConfigurationDraft(d,"button"); draft.Add("onClick",Write(1));draft.Add("onClick",Write(2) with {Enabled=false});draft.Move("onClick",1,-1);
            var old=draft.Actions("onClick");draft.Remove("onClick",1);Equal(2,old.Count);Assert(!draft.Actions("onClick")[0].Enabled);
            draft.Apply(d);draft.Add("onDoubleClick",Write(3));draft.Apply(d);Equal(2L,d.Generation);
        });
        Add("stale event editors cannot overwrite intervening edits or replacement documents", () => {
            var d=new DesignerDocument(Project());var draft=new EventConfigurationDraft(d,"button");draft.Add("onClick",Write(1));d.Execute("rename",p=>p.Name="New name");
            Throws<InvalidOperationException>(()=>draft.Apply(d));Throws<InvalidOperationException>(()=>draft.Apply(new DesignerDocument(Project())));Equal(0,d.Find("button")!.Events.Count);
        });
        Add("invalid event draft updates leave prior configuration untouched", () => {
            var draft=new EventConfigurationDraft(new DesignerDocument(Project()),"button");draft.Add("onClick",Write(2));var revision=draft.Revision;
            Throws<ArgumentException>(()=>draft.SetActions("onClick",Enumerable.Repeat(Write(1),257)));
            Throws<ArgumentException>(()=>draft.Replace("onClick",0,Write(1) with {Parameters=Scalar.Array([])}));
            Throws<ArgumentException>(()=>draft.Add("\n",Write(1)));Equal(revision,draft.Revision);Equal(2d,draft.Actions("onClick")[0].Value.Number);
        });
        Add("custom unwired event definitions are retained without claiming host support", () => {
            var p=Project();p.Views[0].Root.Children[0].Events["customEvent"]=[Write(2).ToAction()];
            var d=new DesignerDocument(p);var draft=new EventConfigurationDraft(d,"button");Assert(draft.EventNames.Contains("customEvent"));Assert(!EventConfigurations.Supported("Button").Contains("customEvent"));
            draft.Add("onActionPerformed",Write(1));draft.Apply(d);Assert(d.Find("button")!.Events.ContainsKey("customEvent"));
        });
        Add("project validation rejects malformed action tails before deserialization succeeds", () => {
            var p=Project();p.Views[0].Root.Children[0].Events["onClick"]=[Write(1).ToAction(),null!];Assert(ProjectCodec.Validate(p).Count>0);
            Throws<FormatException>(()=>ProjectCodec.Clone(p));
        });
        Add("dispatch skips disabled actions while retaining their wire configuration", () => {
            using var r=new ProjectRuntime(Project());var s=new RuntimeSession(r);var n=r.Project.Views[0].Root.Children[0];
            n.Events["onAction"]=[Write(2).ToAction(),(Write(3) with {Enabled=false}).ToAction()];s.Dispatch(n,"onAction");Equal(2d,r.Tags.Read("[default]A").Value.Number);
            var copy=ProjectCodec.Clone(r.Project);Assert(!copy.Views[0].Root.Children[0].Events["onAction"][1].Enabled);
        });
        Add("dispatch owns remaining action values and targets before invoking observers", () => {
            using var r=new ProjectRuntime(Project());var s=new RuntimeSession(r);var n=r.Project.Views[0].Root.Children[0];var tail=Write(7).ToAction();
            n.Events["onAction"]=[new(){Kind=ActionKind.SendMessage,Target="change"},tail];s.Message+=_=>{tail.Target="[default]B";tail.Value=Scalar.From(99);};
            s.Dispatch(n,"onAction");Equal(7d,r.Tags.Read("[default]A").Value.Number);Equal(0d,r.Tags.Read("[default]B").Value.Number);
        });
        Add("invalid dispatch plans fail before executing a valid prefix", () => {
            using var r=new ProjectRuntime(Project());var s=new RuntimeSession(r);var n=r.Project.Views[0].Root.Children[0];
            n.Events["onAction"]=[Write(7).ToAction(),new(){Kind=(ActionKind)999}];Throws<ArgumentException>(()=>s.Dispatch(n,"onAction"));Equal(0d,r.Tags.Read("[default]A").Value.Number);
            n.Events["onAction"]=[Write(3).ToAction()];s.Dispatch(n,"onAction");Equal(3d,r.Tags.Read("[default]A").Value.Number);
        });
        Add("tag observer recursion is rejected without interrupting the outer action plan", () => {
            using var r=new ProjectRuntime(Project());var session=new RuntimeSession(r);var n=r.Project.Views[0].Root.Children[0];
            n.Events["onClick"]=[Write(5).ToAction()];var blocked=false;
            using var observer=r.Tags.Subscribe(_=>{try{session.Dispatch(n,"onClick");}catch(InvalidOperationException){blocked=true;}});
            session.Dispatch(n,"onClick");Assert(blocked);Equal(5d,r.Tags.Read("[default]A").Value.Number);
        });
        Add("dispatch guard is released after a target write fails", () => {
            using var r=new ProjectRuntime(Project());var session=new RuntimeSession(r);var n=r.Project.Views[0].Root.Children[0];
            n.Events["onClick"]=[(Write(5) with {Target="[default]Missing"}).ToAction()];Throws<InvalidOperationException>(()=>session.Dispatch(n,"onClick"));
            n.Events["onClick"]=[Write(9).ToAction()];session.Dispatch(n,"onClick");Equal(9d,r.Tags.Read("[default]A").Value.Number);
        });
        Add("configuration inventories capture bindings transforms events actions and reuse", () => {
            var p=Project();var n=p.Views[0].Root.Children[0];n.Bindings["text"]=new(){Kind=BindingKind.Expression,Source="'Test'",Transforms=[new(){Kind=TransformKind.Expression,Expression="value"}]};n.Events["onClick"]=[Write(3).ToAction()];
            p.Views[0].Root.Children.Add(new(){Id="embedded",Name="Child",Type="EmbeddedView",Props=new(){["view"]=Scalar.From("Main")}});
            var d=new DesignerDocument(p);var index=new ConfigurationIndex();index.Refresh(d);Equal(5,index.Entries.Count);Equal(1,index.Query("Test",ConfigurationKind.Binding).Count);
            Assert(index.Entries.All(e=>e.View=="Main"));
        });
        Add("configuration snapshots and identical filters are reused across idle refresh", () => {
            var p=Project();p.Views[0].Root.Children[0].Events["onClick"]=[Write(1).ToAction()];var d=new DesignerDocument(p);var index=new ConfigurationIndex();index.Refresh(d);var rows=index.Query("button");
            for(var i=0;i<1000;i++){index.Refresh(d);Assert(ReferenceEquals(rows,index.Query("button")));}Equal(1L,index.BuildCount);Equal(1L,index.QueryBuildCount);
            d.Execute("rename",_=>d.Find("button")!.Name="Renamed");index.Refresh(d);Equal(0,index.Query("button").Count);Equal("Button",rows[0].ComponentName);
            Throws<NotSupportedException>(()=>((IList<ConfigurationReference>)rows).Clear());
        });
        Add("configuration search rejects invalid filters without corrupting cached results", () => {
            var index=new ConfigurationIndex();index.Refresh(new DesignerDocument(Project()));var rows=index.Query();
            Throws<ArgumentException>(()=>index.Query(new string('x',257)));Throws<ArgumentException>(()=>index.Query(kind:(ConfigurationKind)99));Assert(ReferenceEquals(rows,index.Query()));
        });
    }
}
