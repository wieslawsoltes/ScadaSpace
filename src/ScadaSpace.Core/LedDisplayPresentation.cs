using System.Globalization;

namespace ScadaSpace.Core;

/// <summary>Immutable segment-display configuration, independent of graphics and UI.</summary>
public sealed record LedDisplayOptions(string SegmentFormat = "14 segment", string NumberFormat = "#,##0.00",
    string BackgroundColor = "#161616", string DiodeOnColor = "#1EC963", string DiodeOffColor = "#193B29", string Locale = "en-US")
{
    public static IReadOnlyList<string> SegmentFormats { get; } = Array.AsReadOnly(new[] { "7 segment", "14 segment" });
    // Common authoring presets. Additional bounded .NET numeric patterns can be supplied through the API.
    public static IReadOnlyList<string> NumberFormats { get; } = Array.AsReadOnly(new[] { "0", "0.0", "0.00", "0.000", "#,##0", "#,##0.0", "#,##0.00", "0%", "0.0%" });
    public static LedDisplayOptions Legacy { get; } = new("7 segment", "0.0", "#22332D", "#8CEE87", "#30473B");
    public bool FourteenSegments => SegmentFormat == "14 segment";
    public void Validate()
    {
        if (SegmentFormat is not ("7 segment" or "14 segment")) throw new ArgumentException("segmentFormat must be 7 segment or 14 segment.");
        if (!StyleColor.TryParse(BackgroundColor, out _) || !StyleColor.TryParse(DiodeOnColor, out _) || !StyleColor.TryParse(DiodeOffColor, out _))
            throw new ArgumentException("LED colors require #RRGGBB or #AARRGGBB.");
        NumberFormatting.Validate(NumberFormat);
        _ = Culture();
    }
    public CultureInfo Culture()
    {
        if (string.IsNullOrWhiteSpace(Locale) || Locale.Length > 64 || Locale.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
            throw new ArgumentException("locale must be a language tag such as en-US, de-DE or pl-PL.");
        return CultureInfo.GetCultureInfo(Locale);
    }
}

public sealed record LedDisplaySnapshot(LedDisplayOptions Options, string Text, double? NumericValue = null)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Options); Options.Validate();
        if (NumericValue is { } number && !double.IsFinite(number)) throw new FormatException("LED value must be finite.");
        LedGlyphs.ValidateText(Text, Options.FourteenSegments);
    }
    public LedDisplaySnapshot WithNumericValue(double value) => NumericValue is null || NumericValue == value ? this
        : LedDisplayPresentation.Create(Scalar.From(value), Options);
}

/// <summary>Typed numeric/text presentation with no silent substitution for unrepresentable values.</summary>
public static class LedDisplayPresentation
{
    public static LedDisplaySnapshot Create(Scalar value, LedDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options); options.Validate();
        string text;
        if (value.Kind == ScalarKind.String) text = value.Text!;
        else if (value.Kind == ScalarKind.Number)
        {
            if (!double.IsFinite(value.Number)) throw new FormatException("LED value must be finite.");
            text = value.Number.ToString(options.NumberFormat, options.Culture());
        }
        else throw new ArgumentException("LED value requires a number or supported segment text.");
        // Keep the original text for accessibility/export; glyph conversion is display-only.
        LedGlyphs.ValidateText(text, options.FourteenSegments);
        return new(options, text, value.Kind == ScalarKind.Number ? value.Number : null);
    }
    public static LedDisplaySnapshot Read(Func<string, Scalar> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var segment = read("segmentFormat");
        var legacy = segment.Kind == ScalarKind.Null;
        var defaults = legacy ? LedDisplayOptions.Legacy : new LedDisplayOptions();
        string Text(Scalar value, string fallback, string key) => value.Kind == ScalarKind.Null ? fallback
            : value.Kind == ScalarKind.String ? value.Text! : throw new ArgumentException(key + " requires text.");
        var format = read("numberFormat"); var legacyFormat = read("format");
        var options = new LedDisplayOptions(Text(segment, defaults.SegmentFormat, "segmentFormat"),
            Text(format.Kind == ScalarKind.Null ? legacyFormat : format, defaults.NumberFormat, "numberFormat"),
            Text(read("backgroundColor"), defaults.BackgroundColor, "backgroundColor"),
            Text(read("diodeOnColor"), defaults.DiodeOnColor, "diodeOnColor"),
            Text(read("diodeOffColor"), defaults.DiodeOffColor, "diodeOffColor"),
            Text(read("locale"), defaults.Locale, "locale"));
        var value = read("value");
        return Create(value.Kind == ScalarKind.Null ? Scalar.From(0) : value, options);
    }
}

/// <summary>One owned presentation snapshot. Every current input is read even on a hit, preserving binding error reporting.</summary>
public sealed class LedDisplayPresentationCache
{
    private static readonly string[] Keys = ["value", "segmentFormat", "numberFormat", "format", "backgroundColor", "diodeOnColor", "diodeOffColor", "locale"];
    private readonly Scalar[] _pending = new Scalar[Keys.Length], _saved = new Scalar[Keys.Length];
    private readonly Func<string, Scalar> _read;
    private LedDisplaySnapshot? _snapshot;
    private bool _reading;
    public long BuildCount { get; private set; }
    public LedDisplayPresentationCache() => _read = key => _pending[Array.IndexOf(Keys, key)];
    public LedDisplaySnapshot Read(Func<string, Scalar> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (_reading) throw new InvalidOperationException("LED presentation reads cannot be reentered.");
        _reading = true;
        try
        {
            var equal = _snapshot is not null;
            for (var i = 0; i < Keys.Length; i++) { _pending[i] = read(Keys[i]); if (_pending[i] != _saved[i]) equal = false; }
            if (equal) return _snapshot!;
            var snapshot = LedDisplayPresentation.Read(_read);
            Array.Copy(_pending, _saved, Keys.Length); _snapshot = snapshot; BuildCount++;
            return snapshot;
        }
        finally { _reading = false; }
    }
}
