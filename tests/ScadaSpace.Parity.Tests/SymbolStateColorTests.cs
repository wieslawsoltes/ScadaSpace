using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using static Suite;

internal static class SymbolStateColorTests
{
    private static SymbolStateCatalog Catalog()
    {
        var state = SymbolStateCatalog.Empty.Get("default");
        return SymbolStateCatalog.Empty.With("default", state.WithTheme("light", state.Light with { TertiaryColor = "#37A78E" }));
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("new vessel definitions inherit the project state's tertiary default", () => {
            var node = ComponentCatalog.Create("Vessel"); var states = Catalog();
            Assert(!node.Props.ContainsKey("liquidColor"));
            var options = SymbolPresentation.Read("Vessel", key => node.Get(key), new(), states: states);
            Assert(options.UseStateLiquidColor); Equal("#37A78E", options.LiquidColor);
        });
        Add("explicit legacy vessel liquid colors override the state without mutating its catalog", () => {
            var node = ComponentCatalog.Create("Vessel"); node.Props["liquidColor"] = Scalar.From("#E15F22"); var states = Catalog();
            var options = SymbolPresentation.Read("Vessel", key => node.Get(key), new(), states: states);
            Assert(!options.UseStateLiquidColor); Equal("#E15F22", options.LiquidColor);
            Equal("#37A78E", options.StateStyle!.TertiaryColor); Equal("#37A78E", states.Get("default").Light.TertiaryColor);
        });
        Add("adding and removing a vessel override invalidates the retained presentation", () => {
            var node = ComponentCatalog.Create("Vessel"); var states = Catalog(); var cache = new SymbolPresentationCache();
            Func<string, Scalar> read = key => node.Get(key); var defaults = new SymbolDefaults();
            var inherited = cache.Read("Vessel", read, defaults, states: states);
            node.Props["liquidColor"] = Scalar.From("#E15F22"); Assert(!cache.Read("Vessel", read, defaults, states: states).UseStateLiquidColor);
            node.Props.Remove("liquidColor"); Assert(cache.Read("Vessel", read, defaults, states: states).UseStateLiquidColor);
            Equal(3L, cache.BuildCount); Equal("#37A78E", inherited.LiquidColor);
        });
    }
}
