using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using ScadaSpace.Tags;
using static Suite;
internal static class PageContextTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("page query context resolves in view bindings and cannot be written", () => {
            var p = PageTests.Project(); var root = p.Views[0].Root; root.Props["value"] = Scalar.From(0); root.Bindings["value"] = new() { Kind = BindingKind.Property, Source = "page.props.urlParams.station", Bidirectional = true };
            using var r = new ProjectRuntime(p); var v = r.CreateView("Home");
            v.SetPageProperties(Scalar.FromJson("{\"urlParams\":{\"station\":\"A\"}}")); v.Evaluate(); Equal("A", v.Read(root, "value").Value.Text);
            Assert(!v.CanWrite(root, "value"));
            Equal(WriteStatus.ReadOnly, v.Write(root, "value", Scalar.From("B"), "tester").Status);
        });
    }
}
