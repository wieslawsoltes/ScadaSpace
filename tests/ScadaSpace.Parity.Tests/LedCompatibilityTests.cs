using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using static Suite;

internal static class LedCompatibilityTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("legacy LED Boolean sources retain their numeric 0/1 presentation", () => {
            var value = Scalar.From(true);
            Func<string, Scalar> read = key => key == "value" ? value : Scalar.Null;
            Equal("1.0", LedDisplayPresentation.Read(read).Text);
            value = Scalar.From(false); Equal("0.0", LedDisplayPresentation.Read(read).Text);
        });
        Add("explicit new LED modes reject Boolean sources rather than treating them as text", () => {
            Func<string, Scalar> read = key => key == "value" ? Scalar.From(true) : key == "segmentFormat" ? Scalar.From("14 segment") : Scalar.Null;
            Throws<ArgumentException>(() => LedDisplayPresentation.Read(read));
        });
    }
}
