using System.Globalization;

namespace ScadaSpace.Core;

/// <summary>Bounded invariant numeric text for editors. No Int32 narrowing, implicit grouping, or non-finite input.</summary>
public static class NumericInputText
{
    public const int MaximumLength = 512;
    public static string FormatDouble(double value) => double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "";
    public static string FormatInt(long value) => value.ToString(CultureInfo.InvariantCulture);
    public static string FormatUInt(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    public static double? ParseDouble(string? text) => Valid(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    public static long? ParseInt(string? text) => Valid(text) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    public static ulong? ParseUInt(string? text) => Valid(text) && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static bool Valid(string? text) => text is { Length: > 0 and <= MaximumLength };
}
