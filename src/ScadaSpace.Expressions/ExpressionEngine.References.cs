using System.Text;

namespace ScadaSpace.Expressions;

public sealed partial class ExpressionEngine
{
    private sealed record ReferenceOccurrence(string Path, int Start, int Length);

    /// <summary>Rewrites only parser-recognized references, preserving quoted text and all other source bytes.</summary>
    public static string RewriteReferences(string source, Func<string, string> rewrite)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(rewrite);
        if (source.Length > 16384) throw new ExpressionException("Expression exceeds 16 KiB.");
        var references = new List<ReferenceOccurrence>();
        _ = new Parser(source, references).Parse();
        StringBuilder? result = null; var offset = 0;
        foreach (var occurrence in references)
        {
            var path = rewrite(occurrence.Path);
            if (path == occurrence.Path) continue;
            if (string.IsNullOrWhiteSpace(path) || path.Length > 16384 || path.Any(c => c is '{' or '}' || char.IsControl(c)))
                throw new ExpressionException("Invalid replacement reference.", occurrence.Start);
            result ??= new StringBuilder();
            result.Append(source, offset, occurrence.Start - offset).Append('{').Append(path).Append('}');
            offset = occurrence.Start + occurrence.Length;
            if (result.Length > 16384) throw new ExpressionException("Rewritten expression exceeds 16 KiB.");
        }
        if (result is null) return source;
        result.Append(source, offset, source.Length - offset);
        if (result.Length > 16384) throw new ExpressionException("Rewritten expression exceeds 16 KiB.");
        return result.ToString();
    }
}
