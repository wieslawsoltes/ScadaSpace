using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace ScadaSpace.Controls;

/// <summary>One demand-driven composition subscription, capped at 30 Hz. No per-symbol timers and no process writes.</summary>
internal static class SymbolAnimation
{
    private static readonly HashSet<HmiSymbol> Active = [];
    private static readonly long Start = Stopwatch.GetTimestamp();
    private static double _last;
    public static double ElapsedSeconds => Stopwatch.GetElapsedTime(Start).TotalSeconds;
    public static void Update(HmiSymbol symbol)
    {
        if (!symbol.Animating) { Remove(symbol); return; }
        if (!Active.Add(symbol)) return;
        if (Active.Count == 1) CompositionTarget.Rendering += Render;
    }
    public static void Remove(HmiSymbol symbol)
    {
        if (Active.Remove(symbol) && Active.Count == 0) CompositionTarget.Rendering -= Render;
    }
    private static void Render(object? sender, object args)
    {
        var time = ElapsedSeconds; if (time - _last < 1d / 30) return; _last = time;
        foreach (var symbol in Active) symbol.AnimateFrame();
    }
}
