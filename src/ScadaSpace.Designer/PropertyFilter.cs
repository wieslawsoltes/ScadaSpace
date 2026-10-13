using System.Text.RegularExpressions;

namespace ScadaSpace.Designer;

public enum PropertySearchSyntax { Literal, Wildcard, RegularExpression }
public enum PropertySearchMatch { Anywhere, StartsWith, Exact }

/// <summary>Bounded name/path search for an inspector; contains no document or UI state.</summary>
public sealed class PropertyFilter
{
    public const int MaximumQueryLength = 256;
    public const int MaximumRegexInputLength = 1024;
    private Regex? _regex;
    public string Query { get; private set; } = "";
    public bool BoundOnly { get; private set; }
    public PropertySearchSyntax Syntax { get; private set; }
    public PropertySearchMatch MatchMode { get; private set; }
    public bool CaseSensitive { get; private set; }
    public string Error { get; private set; } = "";
    public long Revision { get; private set; }
    public long PatternBuildCount { get; private set; }

    // Preserve the original compiled-consumer signature and default literal behavior.
    public bool Set(string query, bool boundOnly = false) => Set(query, boundOnly,
        PropertySearchSyntax.Literal, PropertySearchMatch.Anywhere, false);

    /// <remarks>Invalid patterns become an explicit nonmatching state. Invalid arguments
    /// leave the prior state intact. Regex syntax is the bounded nonbacktracking subset.</remarks>
    public bool Set(string query, bool boundOnly, PropertySearchSyntax syntax, PropertySearchMatch match, bool caseSensitive)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > MaximumQueryLength) throw new ArgumentOutOfRangeException(nameof(query), "Property filters are limited to 256 characters.");
        if (!Enum.IsDefined(syntax)) throw new ArgumentOutOfRangeException(nameof(syntax));
        if (!Enum.IsDefined(match)) throw new ArgumentOutOfRangeException(nameof(match));
        // Regex whitespace and escape letter case are semantic (notably \\d versus \\D).
        var normalized = syntax == PropertySearchSyntax.RegularExpression ? query.AsSpan() : query.AsSpan().Trim();
        var comparison = caseSensitive || syntax == PropertySearchSyntax.RegularExpression ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var samePattern = Syntax == syntax && MatchMode == match && CaseSensitive == caseSensitive && normalized.Equals(Query.AsSpan(), comparison);
        if (samePattern && BoundOnly == boundOnly) return false;
        if (!samePattern)
        {
            var text = normalized.ToString(); Regex? regex = null; var error = "";
            if (syntax == PropertySearchSyntax.RegularExpression && text.Length > 0)
            {
                error = ValidateRegexBudget(text);
                if (error.Length == 0)
                {
                    var pattern = match switch { PropertySearchMatch.StartsWith => @"\A(?:" + text + ")", PropertySearchMatch.Exact => @"\A(?:" + text + @")\z", _ => text };
                    try
                    {
                        regex = new Regex(pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant
                            | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(25));
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                    { error = "Invalid or unsupported regular expression: " + ex.Message; }
                    PatternBuildCount++;
                }
            }
            Query = text; _regex = regex; Error = error; Syntax = syntax; MatchMode = match; CaseSensitive = caseSensitive;
        }
        BoundOnly = boundOnly; Revision++; return true;
    }

    /// <summary>Match a qualified path. Regex evaluation can throw on its time or input budget.</summary>
    public bool Matches(string path, bool bound = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        return (!BoundOnly || bound) && Error.Length == 0 && MatchText(path);
    }

    /// <summary>Search a real property name and its qualified path without parsing dotted names.</summary>
    public bool Matches(string path, string name, bool bound = false)
    {
        ArgumentNullException.ThrowIfNull(path); ArgumentNullException.ThrowIfNull(name);
        return (!BoundOnly || bound) && Error.Length == 0 && (MatchText(name) || MatchText(path));
    }

    private bool MatchText(string text)
    {
        if (Query.Length == 0) return true;
        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (Syntax == PropertySearchSyntax.Literal)
            return MatchMode switch { PropertySearchMatch.StartsWith => text.StartsWith(Query, comparison), PropertySearchMatch.Exact => text.Equals(Query, comparison), _ => text.Contains(Query, comparison) };
        if (Syntax == PropertySearchSyntax.Wildcard) return MatchWildcard(text.AsSpan(), comparison);
        if (text.Length > MaximumRegexInputLength) throw new ArgumentOutOfRangeException(nameof(text), "Regular expression input exceeds 1024 characters.");
        return _regex!.IsMatch(text);
    }

    // A rolling wildcard NFA: O(query length * input length), bounded stack space,
    // no recursion, regex construction, backtracking, or heap allocation per row.
    // '*' matches zero or more UTF-16 units; '?' matches exactly one. Other text is literal.
    private bool MatchWildcard(ReadOnlySpan<char> text, StringComparison comparison)
    {
        var pattern = Query.AsSpan();
        Span<bool> states = stackalloc bool[MaximumQueryLength + 1]; states.Clear(); states[0] = true;
        for (var j = 1; j <= pattern.Length; j++) states[j] = states[j - 1] && pattern[j - 1] == '*';
        if (MatchMode != PropertySearchMatch.Exact && states[pattern.Length]) return true;
        for (var i = 0; i < text.Length; i++)
        {
            var diagonal = states[0]; states[0] = MatchMode == PropertySearchMatch.Anywhere;
            for (var j = 1; j <= pattern.Length; j++)
            {
                var previous = states[j];
                states[j] = pattern[j - 1] == '*' ? states[j - 1] || previous
                    : diagonal && (pattern[j - 1] == '?' || pattern.Slice(j - 1, 1).Equals(text.Slice(i, 1), comparison));
                diagonal = previous;
            }
            if (MatchMode != PropertySearchMatch.Exact && states[pattern.Length]) return true;
        }
        return states[pattern.Length];
    }

    private static string ValidateRegexBudget(string pattern)
    {
        // NonBacktracking bounds matching, not malicious pattern construction. Do not
        // expand counted repetitions, and bound nesting before entering the framework.
        var inClass = false; var depth = 0;
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                if (++i < pattern.Length && (pattern[i] is 'p' or 'P') && i + 1 < pattern.Length && pattern[i + 1] == '{')
                { var end = pattern.IndexOf('}', i + 2); if (end >= 0) i = end; }
                continue;
            }
            if (c == '[') { inClass = true; continue; }
            if (c == ']') { inClass = false; continue; }
            if (inClass) continue;
            if (c is '{' or '}') return "Counted repetitions are not supported in property search; use *, + or ? (escape literal braces).";
            if (c == '(' && i + 1 < pattern.Length && pattern[i + 1] == '?' && (i + 2 >= pattern.Length || pattern[i + 2] != ':'))
                return "Only ordinary and noncapturing groups are supported; configure case sensitivity in the search options.";
            if (c == '(' && ++depth > 32) return "Regular expressions are limited to 32 nested groups.";
            if (c == ')' && depth > 0) depth--;
        }
        return "";
    }
}
