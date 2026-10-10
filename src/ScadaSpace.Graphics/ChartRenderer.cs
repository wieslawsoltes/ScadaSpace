using System.Globalization;
using ScadaSpace.Core;
using ScadaSpace.History;
using SkiaSharp;

namespace ScadaSpace.Graphics;

public enum ChartKind { Trend, Bar, Pie }
public sealed record PlotSeries(string Name, SKColor Color, IReadOnlyList<QualifiedValue> Samples);
public sealed record ChartOptions(string Title = "Live trends", double Minimum = 0, double Maximum = 100, double WindowSeconds = 120, double? CursorFraction = null);

public static class ChartRenderer
{
    public static readonly SKColor[] Palette = [SKColor.Parse("#31A6BC"), SKColor.Parse("#617EC8"), SKColor.Parse("#D9984A"), SKColor.Parse("#74A96D"), SKColor.Parse("#B175AA")];
    private static readonly SKColor Ink = SKColor.Parse("#365161");
    public static void Draw(SKCanvas canvas, SKRect bounds, ChartKind kind, IReadOnlyList<PlotSeries> series, ChartOptions options)
    {
        if (bounds.Width < 80 || bounds.Height < 60) return;
        canvas.Save(); canvas.ClipRect(bounds); using var p = new CanvasPainter(canvas);
        p.Rect(bounds, SKColors.White, 4); p.Rect(bounds, SKColor.Parse("#D5E0E5"), 4, true);
        p.Text(options.Title, bounds.Left + 12, bounds.Top + 21, 13, Ink);
        var plot = SKRect.Create(bounds.Left + 47, bounds.Top + 37, Math.Max(1, bounds.Width - 63), Math.Max(1, bounds.Height - 73));
        if (kind == ChartKind.Pie) { Pie(p, bounds, series); canvas.Restore(); return; }
        var min = double.IsFinite(options.Minimum) ? options.Minimum : 0;
        var max = double.IsFinite(options.Maximum) && options.Maximum > min ? options.Maximum : min + 100;
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - i * plot.Height / 4;
            p.Line(plot.Left, y, plot.Right, y, SKColor.Parse("#E6EDF1"));
            p.Text((min + (max - min) * i / 4).ToString("0.#", CultureInfo.InvariantCulture), plot.Left - 7, y + 4, 10, Ink, SKTextAlign.Right);
        }
        if (kind == ChartKind.Bar)
        {
            var count = Math.Max(1, series.Count); var barWidth = plot.Width / count;
            for (var i = 0; i < series.Count; i++)
            {
                var sample = series[i].Samples.LastOrDefault();
                if (!sample.IsGood || sample.Value.Kind != ScalarKind.Number) continue;
                var fraction = (float)Math.Clamp((sample.Value.Number - min) / (max - min), 0, 1);
                p.Rect(SKRect.Create(plot.Left + i * barWidth + barWidth * .2f, plot.Bottom - fraction * plot.Height, barWidth * .6f, fraction * plot.Height), series[i].Color, 3);
                p.Text(sample.Value.ToString(), plot.Left + (i + .5f) * barWidth, plot.Bottom - fraction * plot.Height - 5, 11, Ink, SKTextAlign.Center);
            }
        }
        else
        {
            var all = series.SelectMany(s => s.Samples).ToArray();
            var latest = all.Length > 0 ? all.Max(s => s.Timestamp) : DateTimeOffset.UtcNow;
            var earliest = all.Length > 0 ? all.Min(s => s.Timestamp) : latest.AddSeconds(-1);
            var start = earliest > latest.AddSeconds(-Math.Max(1, options.WindowSeconds)) ? earliest : latest.AddSeconds(-Math.Max(1, options.WindowSeconds));
            var duration = Math.Max(1, (latest - start).TotalSeconds);
            canvas.Save(); canvas.ClipRect(plot);
            foreach (var line in series)
            {
                using var path = new SKPath(); var open = false;
                foreach (var sample in Historian.Decimate(line.Samples.Where(s => s.Timestamp >= start).ToArray(), Math.Max(1, (int)plot.Width)))
                {
                    if (!sample.IsGood || sample.Value.Kind != ScalarKind.Number) { open = false; continue; }
                    var x = plot.Left + (float)((sample.Timestamp - start).TotalSeconds / duration) * plot.Width;
                    var y = plot.Bottom - (float)((sample.Value.Number - min) / (max - min)) * plot.Height;
                    if (open) path.LineTo(x, y); else { path.MoveTo(x, y); open = true; }
                }
                p.Path(path, line.Color, true, 1.8f);
            }
            if (options.CursorFraction is { } cursor)
            {
                var x = plot.Left + (float)Math.Clamp(cursor, 0, 1) * plot.Width;
                p.Line(x, plot.Top, x, plot.Bottom, Ink.WithAlpha(170));
            }
            canvas.Restore();
            for (var i = 0; i <= 3; i++) p.Text(start.AddSeconds(duration * i / 3).UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture), plot.Left + plot.Width * i / 3, plot.Bottom + 15, 9, Ink, i == 0 ? SKTextAlign.Left : i == 3 ? SKTextAlign.Right : SKTextAlign.Center);
            if (all.Length < 2) p.Text("Collecting live samples…", plot.MidX, plot.MidY, 12, Ink, SKTextAlign.Center);
        }
        var legendX = bounds.Left + 12;
        foreach (var line in series.Take(5))
        {
            p.Rect(SKRect.Create(legendX, bounds.Bottom - 12, 9, 3), line.Color);
            var label = line.Name.Split('/').LastOrDefault() ?? line.Name;
            if (series.Count(s => s.Name.Split('/').LastOrDefault() == label) > 1) label = string.Join("/", line.Name.Split('/').TakeLast(2));
            p.Text(label, legendX + 13, bounds.Bottom - 7, 10, Ink);
            legendX += Math.Max(100, label.Length * 6 + 25);
        }
        canvas.Restore();
    }
    private static void Pie(CanvasPainter p, SKRect bounds, IReadOnlyList<PlotSeries> series)
    {
        var values = series.Select(s => s.Samples.LastOrDefault()).Select(s => s.IsGood && s.Value.Kind == ScalarKind.Number ? Math.Max(0, s.Value.Number) : 0).ToArray();
        var total = values.Sum();
        var radius = Math.Max(5, Math.Min(bounds.Height - 55, bounds.Width * .55f) / 2);
        var cx = bounds.Left + radius + 18; var cy = bounds.Top + 34 + radius;
        var oval = SKRect.Create(cx - radius, cy - radius, radius * 2, radius * 2);
        var start = -90f;
        for (var i = 0; i < series.Count; i++)
        {
            var sweep = total <= 0 ? 0 : (float)(values[i] / total * 360);
            using var slice = new SKPath(); slice.MoveTo(cx, cy); slice.ArcTo(oval, start, sweep, false); slice.Close(); p.Path(slice, series[i].Color); start += sweep;
            if (bounds.Width > 250)
            {
                p.Rect(SKRect.Create(cx + radius + 14, bounds.Top + 47 + i * 24, 9, 9), series[i].Color);
                p.Text(string.Join("/", series[i].Name.Split('/').TakeLast(2)), cx + radius + 28, bounds.Top + 55 + i * 24, 10, Ink);
            }
        }
        p.Circle(cx, cy, radius * .54f, SKColors.White); p.Text(total.ToString("0.#", CultureInfo.InvariantCulture), cx, cy + 6, 21, Ink, SKTextAlign.Center);
        if (total <= 0) p.Circle(cx, cy, radius * .75f, SKColor.Parse("#E1E9EE"), true, radius * .4f);
    }
}
