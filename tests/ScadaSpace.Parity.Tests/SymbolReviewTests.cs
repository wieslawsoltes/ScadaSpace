using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using static Suite;

internal static class SymbolReviewTests
{
    [ModuleInitializer]
    internal static void Register()
    {
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
