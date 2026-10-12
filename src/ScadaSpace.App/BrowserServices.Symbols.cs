using System.Runtime.CompilerServices;
using ScadaSpace.Controls;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    private sealed class ObservedSymbolIdentity { public string Id { get; } = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<HmiSymbol, ObservedSymbolIdentity> SymbolIdentities = new();
    internal static object ObserveSymbol(HmiSymbol symbol) => new
    {
        id = SymbolIdentities.GetValue(symbol, static _ => new()).Id,
        kind = symbol.Kind, value = symbol.Value, minimum = symbol.Minimum, maximum = symbol.Maximum,
        quality = symbol.Quality, options = symbol.Data.Options, tank = symbol.Data.Tank,
        dataError = symbol.DataError, renderError = symbol.RenderError,
        pictures = symbol.PictureBuildCount, renders = symbol.RenderCount,
        animationFrames = symbol.AnimationInvalidations, animating = symbol.Animating, animationVisible = symbol.IsInAnimationViewport
    };
}
