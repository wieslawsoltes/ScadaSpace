using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using static Suite;

internal static class SymbolPropertyKindsTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("LED authoring exposes all three actual material colors", () => {
            foreach (var property in new[] { "backgroundColor", "diodeOnColor", "diodeOffColor" })
                Assert(SymbolPropertyKinds.IsColor("LEDDisplay", property));
            Assert(!SymbolPropertyKinds.IsColor("LEDDisplay", "color"));
        });
        Add("vessel and tank color editors match their respective rendering inputs", () => {
            foreach (var property in new[] { "liquidColor", "liquidWarningColor" })
                Assert(SymbolPropertyKinds.IsColor("Vessel", property));
            foreach (var property in new[] { "color", "liquidColor", "tankColor", "tankWarningColor", "liquidWarningColor", "stroke" })
                Assert(SymbolPropertyKinds.IsColor("Tank", property));
            Assert(!SymbolPropertyKinds.IsColor("Vessel", "tankColor"));
        });
        Add("drawing and gauge color choices do not advertise unused stroke inputs", () => {
            foreach (var type in new[] { "Rectangle", "Ellipse", "Gauge", "Thermometer", "Pipe", "Line" })
                Assert(SymbolPropertyKinds.IsColor(type, "color"));
            Assert(SymbolPropertyKinds.IsColor("Rectangle", "stroke"));
            Assert(!SymbolPropertyKinds.IsColor("Gauge", "stroke"));
        });
        Add("symbol Boolean editors only cover implemented flags for their families", () => {
            foreach (var property in new[] { "displayStand", "displayAgitator", "displayFillLevel" })
                Assert(SymbolPropertyKinds.IsSwitch("Vessel", property));
            Assert(SymbolPropertyKinds.IsSwitch("Valve", "reverseFlow"));
            Assert(SymbolPropertyKinds.IsSwitch("Tank", "showScale"));
            Assert(!SymbolPropertyKinds.IsSwitch("Pump", "reverseFlow"));
            Assert(!SymbolPropertyKinds.IsSwitch("LEDDisplay", "value"));
        });
        Add("generic machine color and unrelated free-form fields remain separate from state materials", () => {
            foreach (var type in new[] { "Pump", "Motor", "Valve", "Sensor", "Label" })
                Assert(!SymbolPropertyKinds.IsColor(type, "color"));
            Assert(!SymbolPropertyKinds.IsColor("Vessel", "label"));
            Assert(!SymbolPropertyKinds.IsSwitch("Button", "enabled"));
        });
        Add("typed symbol metadata queries are allocation-free", () => {
            for (var i = 0; i < 100; i++) SymbolPropertyKinds.IsColor("Tank", "liquidColor");
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) {
                Assert(SymbolPropertyKinds.IsColor("Tank", "liquidColor"));
                Assert(SymbolPropertyKinds.IsSwitch("Vessel", "displayFillLevel"));
            }
            Assert(GC.GetAllocatedBytesForCurrentThread() - before < 128);
        });
    }
}
