using System.Globalization;

namespace ScadaSpace.Core;

/// <summary>Local #RRGGBB / #AARRGGBB color syntax, independent of Skia and Uno. Eight digits are ARGB, not CSS RGBA.</summary>
public readonly record struct StyleColor(byte Alpha, byte Red, byte Green, byte Blue)
{
    public static bool TryParse(string? text, out StyleColor color)
    {
        color = default;
        if (text is null || text.Length is not (7 or 9) || text[0] != '#') return false;
        // Do not accept signs, embedded whitespace, Unicode lookalikes or locale formats.
        foreach (var ch in text.AsSpan(1)) if (!char.IsAsciiHexDigit(ch)) return false;
        if (!uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var bits)) return false;
        color = new(text.Length == 7 ? (byte)255 : (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits);
        return true;
    }
    public static StyleColor Parse(string text) => TryParse(text, out var value) ? value
        : throw new FormatException("Color requires #RRGGBB or #AARRGGBB (ARGB).");
    public override string ToString() => $"#{Alpha:X2}{Red:X2}{Green:X2}{Blue:X2}";
}
