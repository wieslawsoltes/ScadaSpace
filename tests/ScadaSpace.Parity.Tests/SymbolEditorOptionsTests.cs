using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using static Suite;

internal static class SymbolEditorOptionsTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("symbol property choices expose every implemented appearance and appropriate directions", () => {
            foreach (var type in SymbolStateCatalog.Families)
            {
                var choices = SymbolEditorOptions.Choices(type, "appearance");
                Assert(choices.SequenceEqual(new[] { "auto", "mimic", "simple", "p&id" }));
                foreach (var direction in SymbolEditorOptions.Choices(type, "orientation"))
                    new SymbolOptions { Orientation = direction }.Validate(type);
            }
            Assert(SymbolEditorOptions.Choices("Vessel", "orientation").SequenceEqual(new[] { "vertical", "horizontal" }));
        });
        Add("pump variants and feet do not appear on other symbol families", () => {
            Assert(SymbolEditorOptions.Choices("Pump", "variant").SequenceEqual(new[] { "centrifugal", "vacuum" }));
            Equal(5, SymbolEditorOptions.Choices("Pump", "feet").Count);
            var pump = ComponentCatalog.Create("Pump"); Equal("centrifugal", pump.Text("variant")); Equal("bottom", pump.Text("feet"));
            Equal(0, SymbolEditorOptions.Choices("Motor", "variant").Count);
            Equal(0, SymbolEditorOptions.Choices("Valve", "feet").Count);
        });
        Add("native state choices include only assigned family states including custom names", () => {
            var state = new SymbolStateDefinition(new(), new(), new[] { "Pump" });
            var catalog = SymbolStateCatalog.Empty.With("service", state);
            var pumps = SymbolEditorOptions.Choices("Pump", "state", catalog);
            Assert(pumps.Contains("service") && pumps.Contains("running") && !pumps.Contains("open"));
            var valves = SymbolEditorOptions.Choices("Valve", "state", catalog);
            Assert(valves.Contains("partiallyClosed") && !valves.Contains("service") && !valves.Contains("running"));
            foreach (var family in SymbolStateCatalog.Families)
                foreach (var name in SymbolEditorOptions.Choices(family, "state", catalog)) catalog.Resolve(family, name, "light");
        });
        Add("project state choice snapshots survive catalog changes and cannot be modified", () => {
            var original = SymbolEditorOptions.Choices("Pump", "state");
            var catalog = SymbolStateCatalog.Empty.With("running", SymbolStateCatalog.Empty.Get("running").WithSymbols(new[] { "Motor" }));
            Assert(original.Contains("running") && !SymbolEditorOptions.Choices("Pump", "state", catalog).Contains("running"));
            Throws<NotSupportedException>(() => ((IList<string>)original).Add("injected"));
        });
        Add("unknown current state and custom number patterns stay visible without changing choices", () => {
            var choices = SymbolEditorOptions.Choices("Pump", "state"); var selected = SymbolEditorOptions.IncludeCurrent(choices, "missing-state");
            Equal(choices.Count + 1, selected.Count); Equal("missing-state", selected[^1]); Assert(!choices.Contains("missing-state"));
            var format = SymbolEditorOptions.IncludeCurrent(LedDisplayOptions.NumberFormats, "0.0000"); Equal("0.0000", format[^1]);
            Assert(ReferenceEquals(choices, SymbolEditorOptions.IncludeCurrent(choices, "default")));
        });
        Add("static choice lists are immutable and reused without per-query allocations", () => {
            var choices = SymbolEditorOptions.Choices("Pump", "appearance");
            Throws<NotSupportedException>(() => ((IList<string>)choices).Clear());
            for (var i = 0; i < 100; i++) SymbolEditorOptions.IncludeCurrent(choices, "mimic");
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) Assert(ReferenceEquals(choices, SymbolEditorOptions.IncludeCurrent(SymbolEditorOptions.Choices("Motor", "appearance"), "mimic")));
            Assert(GC.GetAllocatedBytesForCurrentThread() - before < 128);
        });
        Add("choice metadata leaves free-form values and unrelated controls unchanged", () => {
            Assert(ReferenceEquals(LedDisplayOptions.SegmentFormats, SymbolEditorOptions.Choices("LEDDisplay", "segmentFormat")));
            Equal(2, SymbolEditorOptions.Choices("Pipe", "route").Count);
            Equal(0, SymbolEditorOptions.Choices("Label", "appearance").Count);
            Equal(0, SymbolEditorOptions.Choices("Pump", "animationSpeed").Count);
            Equal(0, SymbolEditorOptions.Choices("LEDDisplay", "locale").Count);
        });
    }
}
