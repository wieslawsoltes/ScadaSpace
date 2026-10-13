using ScadaSpace.Core;
using ScadaSpace.Graphics;

namespace ScadaSpace.Controls;

public partial class ViewHost
{
    private HmiSymbol BuildSymbol(ComponentNode node, SymbolKind kind)
    {
        var symbol = new HmiSymbol { Kind = kind, AllowAnimation = !IsEditing };
        var presentation = IndustrialSymbolRenderer.Supports(kind) ? new SymbolPresentationCache() : null;
        var ledPresentation = kind == SymbolKind.LEDDisplay ? new LedDisplayPresentationCache() : null; var quality = Quality.Good;
        Scalar ReadValue(string key)
        {
            var sample = Read(node, key);
            if (sample.Quality == Quality.BadNotFound && !node.Props.ContainsKey(key) && !node.Bindings.ContainsKey(key) && !node.Bindings.ContainsKey("props." + key)) return Scalar.Null;
            if (!sample.IsGood) { quality = sample.Quality; throw new InvalidOperationException(key + ": " + sample.Quality); }
            return sample.Value;
        }
        double Numeric(string key, double fallback)
        {
            var value = ReadValue(key); if (value.Kind == ScalarKind.Null) return fallback;
            if (value.Kind is not (ScalarKind.Number or ScalarKind.Boolean)) throw new ArgumentException(key + " requires a numeric value.");
            return value.AsNumber();
        }
        string TextValue(string key, string fallback)
        {
            var value = ReadValue(key); if (value.Kind == ScalarKind.Null) return fallback;
            if (value.Kind != ScalarKind.String) throw new ArgumentException(key + " requires text."); return value.Text!;
        }
        var read = (Func<string, Scalar>)ReadValue;
        _refresh.Add(() =>
        {
            quality = Quality.Good;
            try
            {
                var led = ledPresentation?.Read(read);
                var value = led is not null ? Scalar.From(led.NumericValue ?? 0) : ReadValue("value"); var number = value.Kind == ScalarKind.Object ? value.Fields.GetValueOrDefault("value", Scalar.From(0)).AsNumber()
                    : value.Kind == ScalarKind.Null ? 0 : value.AsNumber();
                var minimum = Numeric("minimum", 0); var maximum = Numeric("maximum", 100);
                if (kind == SymbolKind.Vessel && value.Kind == ScalarKind.Object) maximum = value.Fields.GetValueOrDefault("capacity", Scalar.From(maximum)).AsNumber();
                if (kind == SymbolKind.Tank) maximum = Numeric("capacity", maximum);
                var options = presentation?.Read(node.Type, read, Session!.Symbols, number, Runtime!.Project.SymbolStates);
                var data = new SymbolData(number, minimum, maximum, TextValue("unit", ""), TextValue("liquidColor", TextValue("color", "#48AFC0")), led?.Options.NumberFormat ?? TextValue("format", "0.0"),
                    TextValue("route", "elbow"), Numeric("strokeWidth", 1), TextValue("stroke", "#9DAAB2"), Numeric("radius", 0), Numeric("warning", 85), Quality.Good, options, kind == SymbolKind.Tank ? TankOptions.Read(read) : null, led);
                SymbolRenderer.Validate(SkiaSharp.SKRect.Create(100, 100), kind, data);
                symbol.SetSnapshot(data); symbol.SetDiagnostic(null);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
            {
                if (symbol.DataError != ex.Message) Error?.Invoke(ex.Message);
                symbol.SetDiagnostic(ex.Message, quality == Quality.Good ? Quality.BadExpression : quality);
            }
        });
        return symbol;
    }
}
