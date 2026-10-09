using System.Globalization;

namespace ScadaSpace.Core;

/// <summary>Bounds user-supplied numeric formats before the framework allocates a formatted string.</summary>
public static class NumberFormatting
{
    public static void Validate(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.Length > 128) throw new FormatException("Number formats are limited to 128 characters.");
        if (format.Length > 1 && char.IsAsciiLetter(format[0]) && format.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0)
        {
            if (!int.TryParse(format.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var precision) || precision > 64)
                throw new FormatException("Standard numeric format precision is limited to 64 digits.");
        }
    }
    public static string Format(double value, string format)
    {
        Validate(format);
        if (!double.IsFinite(value)) throw new FormatException("Cannot format a non-finite process value.");
        var result = value.ToString(format, CultureInfo.InvariantCulture);
        if (result.Length > 65536) throw new FormatException("Formatted result exceeds 64 KiB.");
        return result;
    }
}
