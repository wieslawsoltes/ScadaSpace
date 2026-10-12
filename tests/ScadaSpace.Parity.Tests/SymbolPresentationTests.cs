using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using static Suite;

internal static class SymbolPresentationTests
{
    private static SymbolOptions Read(string type, string json = "{}", SymbolDefaults? defaults = null, double value = 0)
    {
        var props = Scalar.FromJson(json).Fields;
        return SymbolPresentation.Read(type, key => props.GetValueOrDefault(key), defaults ?? new(), value);
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("industrial symbol defaults resolve auto appearance and animation deterministically", () => {
            var o=Read("Pump","{\"appearance\":\"auto\",\"animationSpeed\":\"auto\",\"state\":\"running\"}",new("simple",40));
            Equal("simple",o.Appearance);Equal(40d,o.AnimationSpeed);Assert(o.IsAnimated);
        });
        Add("reduced motion disables animation without changing configured state", () => {
            var o=Read("Motor","{\"state\":\"running\",\"animationSpeed\":80}",new(ReducedMotion:true));Equal("running",o.State);Assert(!o.IsAnimated);Equal(0d,o.Phase(4));
        });
        Add("legacy Boolean machine values map to operational states", () => {
            Equal("running",Read("Pump",value:1).State);Equal("stopped",Read("Motor",value:0).State);Equal("open",Read("Valve",value:1).State);Equal("closed",Read("Valve",value:0).State);
        });
        Add("all documented machine and valve state names are accepted without conflation", () => {
            foreach(var s in new[]{"default","running","stopped","faulted"}) Equal(s,Read("Sensor","{\"state\":\""+s+"\"}").State);
            foreach(var s in new[]{"default","open","failedToOpen","partiallyClosed","closed","failedToClose"}) Equal(s,Read("Valve","{\"state\":\""+s+"\"}").State);
            Assert(Read("Valve","{\"state\":\"failedToClose\"}").IsFaulted);
        });
        Add("invalid states orientations and animated percentages fail explicitly", () => {
            foreach(var json in new[]{"{\"state\":\"unknown\"}","{\"appearance\":\"svg\"}","{\"orientation\":\"diagonal\"}","{\"animationSpeed\":101}","{\"animationSpeed\":\"fast\"}"}) Throws<ArgumentException>(()=>Read("Pump",json));
            Throws<ArgumentException>(()=>Read("Vessel","{\"orientation\":\"right\"}"));Throws<ArgumentException>(()=>Read("Valve","{\"state\":\"running\"}"));
        });
        Add("symbol variants feet and reversed flow remain independent", () => {
            var o=Read("Pump","{\"variant\":\"vacuum\",\"feet\":\"left\",\"orientation\":\"bottom\",\"reverseFlow\":true}");
            Equal("vacuum",o.Variant);Equal("left",o.Feet);Equal("bottom",o.Orientation);Assert(o.ReverseFlow);
        });
        Add("symbol annotation objects preserve position justification and bounded text colors", () => {
            var o=Read("Sensor","{\"label\":{\"text\":\"Flow 01\",\"location\":\"left\",\"justify\":\"right\",\"style\":{\"fontSize\":18,\"color\":\"#8000AABB\"}},\"value\":{\"text\":\"42 m3/h\",\"location\":\"top\"}}");
            Equal("Flow 01",o.Label.Text);Equal("left",o.Label.Location);Equal(18d,o.Label.FontSize);Equal("42 m3/h",o.Readout.Text);Equal("top",o.Readout.Location);
        });
        Add("annotation invalid colors locations types and unsupported CSS are diagnosed", () => {
            foreach(var json in new[]{"{\"label\":42}","{\"label\":{\"location\":\"float\"}}","{\"label\":{\"style\":{\"fontSize\":1}}}","{\"label\":{\"style\":{\"color\":\"red\"}}}","{\"label\":{\"style\":{\"cssText\":\"x\"}}}"}) Throws<ArgumentException>(()=>Read("Pump",json));
        });
        Add("vessel options retain capacity fill controls and percentage readout", () => {
            var o=Read("Vessel","{\"orientation\":\"horizontal\",\"displayAgitator\":false,\"liquidOpacity\":0.25,\"value\":{\"value\":50,\"capacity\":200,\"displayValueAsPercent\":true}}");
            Equal("25%",o.Readout.Text);Equal(.25,o.LiquidOpacity);Assert(!o.DisplayAgitator);Equal("horizontal",o.Orientation);
        });
        Add("symbol fractions support opposite binary64 extremes without overflow", () => {
            Equal(.5,SymbolPresentation.Fraction(0,-double.MaxValue,double.MaxValue));Equal(0d,SymbolPresentation.Fraction(-3,0,100));Equal(1d,SymbolPresentation.Fraction(103,0,100));
            Equal(.5,SymbolPresentation.Fraction(1.5,1,2));Throws<ArgumentException>(()=>SymbolPresentation.Fraction(double.NaN,0,100));Throws<ArgumentException>(()=>SymbolPresentation.Fraction(1,10,10));
        });
        Add("symbol phases remain bounded for large or negative finite time", () => {
            var o=new SymbolOptions{State="running",AnimationSpeed=50};Equal(0d,o.Phase(0));Equal(.5,o.Phase(1));Equal(0d,o.Phase(2));
            foreach(var t in new[]{-1d,1e30,double.MaxValue})Assert(o.Phase(t)>=0&&o.Phase(t)<1);Throws<ArgumentOutOfRangeException>(()=>o.Phase(double.NaN));
        });
        Add("symbol session defaults are isolated and reject invalid replacements", () => {
            using var r=new ProjectRuntime(DemoProject.Create());var a=new RuntimeSession(r);var b=new RuntimeSession(r);a.Symbols=new("p&id",0);
            Equal("mimic",b.Symbols.AutoAppearance);Throws<ArgumentException>(()=>a.Symbols=new("unsupported"));Equal("p&id",a.Symbols.AutoAppearance);
        });
    }
}
