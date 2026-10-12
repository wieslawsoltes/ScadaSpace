using ScadaSpace.Controls;

namespace ScadaSpace.App;

internal static partial class BrowserServices
{
    internal static object ObserveSymbol(HmiSymbol symbol) => new
    {
        kind = symbol.Kind, value = symbol.Value, minimum = symbol.Minimum, maximum = symbol.Maximum,
        quality = symbol.Quality, options = symbol.Data.Options, tank = symbol.Data.Tank,
        dataError = symbol.DataError, renderError = symbol.RenderError,
        pictures = symbol.PictureBuildCount, renders = symbol.RenderCount,
        animationFrames = symbol.AnimationInvalidations, animating = symbol.Animating
    };
}
