using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using static Suite;

internal static class SymbolCacheTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("unchanged symbol presentation reads allocate no option or annotation records", () => {
            var values = new Dictionary<string, Scalar> { ["state"] = Scalar.From("running"), ["label"] = Scalar.From("P-101") };
            Func<string, Scalar> read = key => values.GetValueOrDefault(key);
            var cache = new SymbolPresentationCache(); var defaults = new SymbolDefaults(); var snapshot = cache.Read("Pump", read, defaults);
            for (var i = 0; i < 100; i++) cache.Read("Pump", read, defaults);
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) Assert(ReferenceEquals(snapshot, cache.Read("Pump", read, defaults)));
            Assert(GC.GetAllocatedBytesForCurrentThread() - bytes < 128); Equal(1L, cache.BuildCount);
        });
        Add("symbol caches invalidate for content session defaults family and legacy values", () => {
            var values = new Dictionary<string, Scalar>(); Func<string, Scalar> read = key => values.GetValueOrDefault(key);
            var cache = new SymbolPresentationCache(); var defaults = new SymbolDefaults(); var original = cache.Read("Pump", read, defaults);
            values["orientation"] = Scalar.From("left"); Equal("left", cache.Read("Pump", read, defaults).Orientation);
            Equal("simple", cache.Read("Pump", read, new("simple")).Appearance);
            Equal("running", cache.Read("Pump", read, defaults, 1).State);
            Equal("open", cache.Read("Valve", read, defaults, 1).State);
            Equal("right", original.Orientation); Equal(5L, cache.BuildCount);
        });
        Add("bad symbol inputs do not publish partial cache entries and recover cleanly", () => {
            var values = new Dictionary<string, Scalar>(); Func<string, Scalar> read = key => values.GetValueOrDefault(key);
            var cache = new SymbolPresentationCache(); var defaults = new SymbolDefaults(); var original = cache.Read("Pump", read, defaults);
            values["appearance"] = Scalar.From("unsupported"); Throws<ArgumentException>(() => cache.Read("Pump", read, defaults)); Equal(1L, cache.BuildCount);
            values.Clear(); Assert(ReferenceEquals(original, cache.Read("Pump", read, defaults)));
            Throws<InvalidOperationException>(() => cache.Read("Pump", _ => throw new InvalidOperationException("binding failed"), defaults));
            Assert(ReferenceEquals(original, cache.Read("Pump", read, defaults)));
        });
        Add("nested vessel edits invalidate immutable cached annotations", () => {
            var value = Scalar.FromJson("{\"value\":30,\"capacity\":200}");
            Func<string, Scalar> read = key => key == "value" ? value : Scalar.Null;
            var cache = new SymbolPresentationCache(); var defaults = new SymbolDefaults(); var old = cache.Read("Vessel", read, defaults);
            value = Scalar.FromJson("{\"value\":50,\"capacity\":200}"); Equal("25%", cache.Read("Vessel", read, defaults).Readout.Text); Equal("15%", old.Readout.Text);
        });
        Add("presentation callback reentrancy is rejected and the guard is released", () => {
            var cache = new SymbolPresentationCache(); var defaults = new SymbolDefaults();
            Throws<InvalidOperationException>(() => cache.Read("Pump", _ => { cache.Read("Pump", _ => Scalar.Null, defaults); return Scalar.Null; }, defaults));
            Equal("stopped", cache.Read("Pump", _ => Scalar.Null, defaults).State);
            Throws<ArgumentException>(() => cache.Read("Unknown", _ => Scalar.Null, defaults));
            Throws<ArgumentException>(() => cache.Read("Pump", _ => Scalar.Null, defaults, double.NaN));
        });
    }
}
