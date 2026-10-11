using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using static Suite;

internal static class ConfigurationReviewTests
{
    private static DesignerDocument Document() => new(new ProjectDefinition
    {
        Views = [new() { Path = "Main", Root = new() { Id = "component", Name = "Component", Type = "Button" } }]
    });
    private static ActionConfiguration Action(double value) => new(ActionKind.WriteTag, "[default]Value", Scalar.From(value), Scalar.Object([]));

    [ModuleInitializer]
    internal static void Register()
    {
        Add("event draft readers retain immutable handler and event name snapshots", () =>
        {
            var draft = new EventConfigurationDraft(Document(), "component"); draft.Add("onClick", Action(1));
            var names = draft.EventNames; var actions = draft.Actions("onClick");
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                Assert(ReferenceEquals(names, draft.EventNames)); Assert(ReferenceEquals(actions, draft.Actions("onClick")));
            }
            Assert(GC.GetAllocatedBytesForCurrentThread() - allocated < 128);
            draft.Replace("onClick", 0, Action(2));
            Equal(1d, actions[0].Value.Number); Equal(2d, draft.Actions("onClick")[0].Value.Number);
            Assert(ReferenceEquals(names, draft.EventNames));
            Throws<NotSupportedException>(() => ((IList<ActionConfiguration>)actions).Clear());
        });
        Add("custom event membership invalidates names without mutating a prior snapshot", () =>
        {
            var draft = new EventConfigurationDraft(Document(), "component"); var names = draft.EventNames;
            draft.Add("custom/action/7", Action(1)); Assert(draft.EventNames.Contains("custom/action/7"));
            Assert(!names.Contains("custom/action/7")); var next = draft.EventNames;
            draft.Remove("custom/action/7", 0); Assert(!draft.EventNames.Contains("custom/action/7"));
            Assert(next.Contains("custom/action/7"));
        });
        Add("restoring an event draft before Apply does not create an undo entry", () =>
        {
            var document = Document(); var draft = new EventConfigurationDraft(document, "component");
            draft.Add("onClick", Action(1)); draft.Remove("onClick", 0);
            Assert(!draft.Apply(document)); Assert(!draft.IsDirty); Equal(0L, document.Generation); Assert(!document.CanUndo);
        });
        Add("event draft records a successful commit even when a Changed observer throws", () =>
        {
            var document = Document(); var draft = new EventConfigurationDraft(document, "component");
            draft.Add("onClick", Action(4));
            System.Action brokenObserver = () => throw new InvalidOperationException("observer failed after commit");
            document.Changed += brokenObserver;
            Throws<InvalidOperationException>(() => draft.Apply(document));
            Equal(1L, document.Generation); Equal(4d, document.Find("component")!.Events["onClick"][0].Value.Number);
            Assert(!draft.IsDirty); Assert(!draft.Apply(document));
            document.Changed -= brokenObserver;
            draft.Replace("onClick", 0, Action(5)); Assert(draft.Apply(document)); Equal(2L, document.Generation);
        });
        Add("configuration references retain exact event names and action indexes without parsing display paths", () =>
        {
            var document = Document(); var draft = new EventConfigurationDraft(document, "component");
            draft.Add("custom/action/7", Action(1)); draft.Add("custom/action/7", Action(2)); draft.Apply(document);
            var index = new ConfigurationIndex(); index.Refresh(document);
            var actions = index.Query(kind: ConfigurationKind.Action); Equal(2, actions.Count);
            Equal("custom/action/7", actions[0].EventName); Equal<int?>(0, actions[0].ActionIndex);
            Equal("custom/action/7", actions[1].EventName); Equal<int?>(1, actions[1].ActionIndex);
            var handler = index.Query(kind: ConfigurationKind.Event).Single(); Equal("custom/action/7", handler.EventName); Assert(handler.ActionIndex is null);
        });
    }
}
