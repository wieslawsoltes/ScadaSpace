using Microsoft.UI.Xaml;
using ScadaSpace.Controls;
using Windows.Foundation;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private static readonly string[] StartupCommands = ["preview", "fit", "import", "page-configuration"];
    private readonly Rect[] _startupBounds = new Rect[StartupCommands.Length];
    private int _startupTick = -1, _startupStableSamples;
    private Size _startupSize;
    private bool _nativeReady;

    /// <summary>Passive startup readiness: a model can exist before native font/layout and its first drawing settle.</summary>
    private bool ObserveNativeReadiness()
    {
        if (_nativeReady) return true;
        if (_ticks <= _startupTick) return false;
        _startupTick = _ticks;
        if (ActiveHost.Instance is null || !ActiveHost.IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
        { _startupStableSamples = 0; return false; }
        var size = new Size(ActualWidth, ActualHeight);
        var stable = _startupStableSamples > 0 && _startupSize.Width == size.Width && _startupSize.Height == size.Height;
        _startupSize = size;
        try
        {
            for (var i = 0; i < StartupCommands.Length; i++)
            {
                var control = _commands[StartupCommands[i]];
                if (!control.IsLoaded || control.ActualWidth <= 0 || control.ActualHeight <= 0)
                { _startupStableSamples = 0; return false; }
                var bounds = control.TransformToVisual(null).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                if (!bounds.Equals(_startupBounds[i])) stable = false;
                _startupBounds[i] = bounds;
            }
            // A visible diagnostic is also a completed drawing. Text-only views
            // have no custom canvas to await and use the stable layout condition.
            var hasVisibleSymbol = false; var rendered = false;
            foreach (var element in ActiveHost.Elements.Values)
                if (element is HmiSymbol symbol && symbol.IsInAnimationViewport)
                {
                    hasVisibleSymbol = true;
                    if (symbol.RenderCount > 0 || symbol.RenderError is not null) { rendered = true; break; }
                }
            if (hasVisibleSymbol && !rendered) { _startupStableSamples = 0; return false; }
        }
        catch (InvalidOperationException) { _startupStableSamples = 0; return false; }
        _startupStableSamples = stable ? _startupStableSamples + 1 : 1;
        _nativeReady = _startupStableSamples >= 3;
        return _nativeReady;
    }
}
