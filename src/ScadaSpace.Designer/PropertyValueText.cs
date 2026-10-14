using System.Globalization;
using ScadaSpace.Core;

namespace ScadaSpace.Designer;

/// <summary>Lossless text for editing, separate from rounded process-display formatting.</summary>
public static class PropertyValueText
{
    public const int MaximumInputLength = 65536;

    public static string Format(Scalar value)
    {
        if (!value.IsValid()) throw new ArgumentException("Invalid property value.", nameof(value));
        return value.Kind switch
        {
            ScalarKind.Number => value.Number.ToString("R", CultureInfo.InvariantCulture),
            ScalarKind.Boolean => value.Boolean ? "true" : "false",
            ScalarKind.String => value.Text ?? "",
            ScalarKind.Null => "null",
            _ => value.ToJson()
        };
    }

    /// <remarks>Structured values use ordinary JSON, not the portable tagged wire format.</remarks>
    public static Scalar Parse(string text, ScalarKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (text.Length > MaximumInputLength) throw new ArgumentOutOfRangeException(nameof(text), "Property input exceeds 65536 characters.");
        if (kind == ScalarKind.Null && !text.AsSpan().Trim().IsEmpty && !text.AsSpan().Trim().SequenceEqual("null"))
            throw new FormatException("Use null for a Null property.");
        var value = Scalar.Parse(text, kind);
        if (!value.IsValid()) throw new FormatException("Invalid property value.");
        return value;
    }
}
