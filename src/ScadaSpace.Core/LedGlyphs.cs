namespace ScadaSpace.Core;

public enum LedMark { None, Space, Dot, Comma, Colon, Percent, Plus, Slash, Degree, Invisible }
public readonly record struct LedGlyph(ushort Segments, LedMark Mark = LedMark.None, float Advance = 34);

/// <summary>Independent seven/fourteen-segment alphabet. Unsupported characters are diagnosed, never rendered as blank data.</summary>
public static class LedGlyphs
{
    public const int MaximumCharacters = 64;
    private const ushort A=1, B=2, C=4, D=8, E=16, F=32, G1=64, G2=128,
        H=256, I=512, J=1024, K=2048, L=4096, M=8192, G=G1|G2;
    public static void ValidateText(string text, bool fourteenSegments)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumCharacters) throw new FormatException("LED text exceeds 64 characters.");
        foreach (var ch in text) _ = Get(ch, fourteenSegments);
    }
    public static LedGlyph Get(char character, bool fourteenSegments)
    {
        switch (character)
        {
            case ' ': case '\u00A0': case '\u202F': return new(0, LedMark.Space, 16);
            case '.': case '\u066B': return new(0, LedMark.Dot, 10);
            case ',': case '\u066C': return new(0, LedMark.Comma, 10);
            case ':': return new(0, LedMark.Colon, 12);
            case '%': case '\u066A': return new(0, LedMark.Percent, 30);
            case '+': return new(0, LedMark.Plus, 28);
            case '/': return new(0, LedMark.Slash, 28);
            case '°': return new(0, LedMark.Degree, 16);
            case '\u061C': case '\u200E': case '\u200F': return new(0, LedMark.Invisible, 0);
        }
        // Arabic decimal/group symbols are covered above. Numeric digits remain semantically identical.
        if (character is >= '\u0660' and <= '\u0669') character = (char)('0' + character - '\u0660');
        if (character is >= '\u06F0' and <= '\u06F9') character = (char)('0' + character - '\u06F0');
        var ch = char.ToUpperInvariant(character);
        int mask = ch switch
        {
            '0'=>A|B|C|D|E|F, '1'=>B|C, '2'=>A|B|D|E|G, '3'=>A|B|C|D|G,
            '4'=>B|C|F|G, '5'=>A|C|D|F|G, '6'=>A|C|D|E|F|G, '7'=>A|B|C,
            '8'=>A|B|C|D|E|F|G, '9'=>A|B|C|D|F|G, '-'=>G, '\u2212'=>G, '_'=>D,
            'A'=>A|B|C|E|F|G, 'C'=>A|D|E|F, 'E'=>A|D|E|F|G, 'F'=>A|E|F|G,
            'G'=>A|C|D|E|F|G2, 'H'=>B|C|E|F|G, 'J'=>B|C|D|E,
            'L'=>D|E|F, 'O'=>A|B|C|D|E|F, 'P'=>A|B|E|F|G,
            'S'=>A|C|D|F|G, 'U'=>B|C|D|E|F,
            _=>-1
        };
        if (fourteenSegments)
            mask = ch switch
            {
                'B'=>A|B|C|D|G2|I|L, 'D'=>A|B|C|D|I|L, 'I'=>A|D|I|L,
                'K'=>E|F|G1|J|M, 'M'=>B|C|E|F|H|J, 'N'=>B|C|E|F|H|M,
                'Q'=>A|B|C|D|E|F|M, 'R'=>A|B|E|F|G|M, 'T'=>A|I|L,
                'V'=>E|F|J|K, 'W'=>B|C|E|F|K|M, 'X'=>H|J|K|M,
                'Y'=>H|J|L, 'Z'=>A|D|J|K, _=>mask
            };
        else
            mask = ch switch { 'B'=>C|D|E|F|G, 'D'=>B|C|D|E|G, 'I'=>B|C, 'N'=>C|E|G,
                'R'=>E|G, 'T'=>D|E|F|G, 'Y'=>B|C|D|F|G, _=>mask };
        if (mask < 0) throw new FormatException("Character U+" + ((int)character).ToString("X4") + " is not supported by the " + (fourteenSegments ? "14" : "7") + " segment display.");
        return new((ushort)mask);
    }
    public static float Measure(string text, bool fourteenSegments)
    {
        ValidateText(text, fourteenSegments); var width=0f;
        foreach (var ch in text) width += Get(ch, fourteenSegments).Advance;
        return Math.Max(1, width);
    }
}
