using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using static Suite;

internal static class SymbolReviewTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("demo pump and valve states follow the original Boolean tag binding", () => {
            using var runtime = new ProjectRuntime(DemoProject.Create()); var view = runtime.CreateView("Overview");
            var pump = view.View.Root.Children.Single(n => n.Name == "TransferPump"); var valve = view.View.Root.Children.Single(n => n.Name == "OutletValve");
            view.Evaluate(); Equal("running", view.Read(pump, "state").Value.Text); Equal("open", view.Read(valve, "state").Value.Text);
            runtime.Tags.Write("[default]Plant/Pump/Enabled", Scalar.From(false), "test"); view.Evaluate();
            Equal("stopped", view.Read(pump, "state").Value.Text); Equal("closed", view.Read(valve, "state").Value.Text);
            Assert(!view.Read(pump, "value").Value.Boolean); Equal(0, view.Diagnostics.Count);
        });
        Add("component gallery dimensions contain every generated symbol", () => {
            var gallery = DemoProject.Create().Views.Single(v => v.Path == "ComponentGallery");
            Assert(gallery.Root.Children.All(n => n.Y + n.Height <= gallery.Height));
            Assert(gallery.Root.Children.Any(n => n.Type == "Vessel") && gallery.Root.Children.Any(n => n.Type == "Sensor"));
        });
        Add("explicit disabled tank units override legacy units without duplicating percentages", () => {
            var display = Scalar.FromJson("{\"unit\":{\"enabled\":false}}");
            var options = TankOptions.Read(key => key == "valueDisplay" ? display : Scalar.Null);
            Equal("", options.ResolveUnit("m3")); Equal("m3", new TankOptions().ResolveUnit("m3"));
            Equal("", new TankOptions(ValueFormat: "Percent").ResolveUnit("%"));
            Equal("gal", new TankOptions(DisplayUnit: "gal").ResolveUnit("m3"));
        });
        Add("subnormal animation speeds produce finite bounded phases", () => {
            var o = new SymbolOptions { State = "running", AnimationSpeed = double.Epsilon };
            var phase = o.Phase(double.MaxValue); Assert(double.IsFinite(phase) && phase > 0 && phase < 1);
            Assert(o.Phase(-double.MaxValue) >= 0 && o.Phase(-double.MaxValue) < 1);
            Throws<ArgumentOutOfRangeException>(() => (o with { AnimationSpeed = double.NaN }).Phase(1));
        });
        Add("symbol annotation text limits apply to plain strings as well as objects", () => {
            Throws<ArgumentException>(() => SymbolPresentation.Annotation(Scalar.From(new string('x', 1025))));
            Equal("P-101", SymbolPresentation.Annotation(Scalar.From("P-101")).Text);
        });
        Add("vessel structured values reject implicit numeric or Boolean coercion", () => {
            foreach (var json in new[] { "{\"value\":\"50\"}", "{\"value\":50,\"capacity\":true}", "{\"value\":50,\"displayValueAsPercent\":1}" })
            {
                var value = Scalar.FromJson(json);
                Throws<ArgumentException>(() => SymbolPresentation.Read("Vessel", key => key == "value" ? value : Scalar.Null, new()));
            }
        });
        Add("vessel percentage clamps finite extremes without intermediate overflow", () => {
            var v = Scalar.Object(new Dictionary<string, Scalar> { ["value"] = Scalar.From(double.MaxValue), ["capacity"] = Scalar.From(double.Epsilon) });
            Equal("100%", SymbolPresentation.Read("Vessel", key => key == "value" ? v : Scalar.Null, new()).Readout.Text);
        });
        Add("tank nested display options require explicit types", () => {
            foreach (var json in new[] { "{\"enabled\":1}", "{\"format\":42}", "{\"unit\":{\"enabled\":1}}", "{\"unit\":{\"enabled\":true,\"value\":3}}" })
            {
                var value = Scalar.FromJson(json);
                Throws<ArgumentException>(() => TankOptions.Read(key => key == "valueDisplay" ? value : Scalar.Null));
            }
        });
    }
}
