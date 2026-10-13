using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ScadaSpace.Designer;
using static Suite;

internal static class PropertySearchModeTests
{
    private static PropertyFilter Filter(string query, PropertySearchSyntax syntax = PropertySearchSyntax.Literal,
        PropertySearchMatch mode = PropertySearchMatch.Anywhere, bool sensitive = false)
    { var filter = new PropertyFilter(); filter.Set(query, false, syntax, mode, sensitive); return filter; }

    [ModuleInitializer]
    internal static void Register()
    {
        Add("property prefix and exact matching use real names as well as qualified paths", () => {
            var f = Filter("color", mode: PropertySearchMatch.Exact); Assert(f.Matches("props.color", "color")); Assert(!f.Matches("props.colorRecipe", "colorRecipe"));
            f.Set("props.color", false, PropertySearchSyntax.Literal, PropertySearchMatch.Exact, false); Assert(f.Matches("props.color", "color"));
            f = Filter("diode", mode: PropertySearchMatch.StartsWith); Assert(f.Matches("props.diodeOnColor", "diodeOnColor")); Assert(!f.Matches("custom.otherdiode", "otherdiode"));
        });
        Add("explicit names preserve dots rather than inferring the last path segment", () => {
            var f = Filter("port.value", mode: PropertySearchMatch.Exact); Assert(f.Matches("custom.port.value", "port.value")); Assert(!f.Matches("custom.port.value", "value"));
            Throws<ArgumentNullException>(() => f.Matches("path", null!));
        });
        Add("case sensitive changes are real revisions even with the same letters", () => {
            var f = Filter("COLOR", sensitive: true); Assert(!f.Matches("props.color")); var revision = f.Revision;
            Assert(f.Set("color", false, PropertySearchSyntax.Literal, PropertySearchMatch.Anywhere, true)); Equal(revision + 1, f.Revision); Assert(f.Matches("props.color"));
        });
        Add("wildcards treat punctuation literally and support one and many characters", () => {
            var f = Filter("diode?*Color", PropertySearchSyntax.Wildcard, PropertySearchMatch.Exact);
            Assert(f.Matches("diodeOnColor")); Assert(f.Matches("diodeOffColor")); Assert(!f.Matches("diodeColor"));
            f = Filter("ports[?].*", PropertySearchSyntax.Wildcard, PropertySearchMatch.Exact);
            Assert(f.Matches("ports[0].value")); Assert(!f.Matches("ports0Xvalue"));
        });
        Add("wildcard anchors honor anywhere prefix and full-string modes", () => {
            foreach (var mode in Enum.GetValues<PropertySearchMatch>()) {
                var f = Filter("a?b", PropertySearchSyntax.Wildcard, mode);
                Assert(f.Matches("acb")); Equal(mode != PropertySearchMatch.Exact, f.Matches("acb-tail"));
                Equal(mode == PropertySearchMatch.Anywhere, f.Matches("head-acb-tail"));
            }
        });
        Add("wildcard zero-width stars empty strings and repeated stars are correct", () => {
            var f = Filter("**", PropertySearchSyntax.Wildcard, PropertySearchMatch.Exact); Assert(f.Matches("")); Assert(f.Matches("anything"));
            f = Filter("a**b", PropertySearchSyntax.Wildcard, PropertySearchMatch.Exact); Assert(f.Matches("ab")); Assert(f.Matches("axyb")); Assert(!f.Matches("ac"));
            f = Filter("?", PropertySearchSyntax.Wildcard, PropertySearchMatch.Exact); Assert(!f.Matches("")); Assert(f.Matches("x")); Assert(!f.Matches("xx"));
        });
        Add("wildcards preserve case policy and do not reinterpret regex escapes", () => {
            Assert(Filter("*COLOR", PropertySearchSyntax.Wildcard).Matches("props.color"));
            Assert(!Filter("*COLOR", PropertySearchSyntax.Wildcard, sensitive: true).Matches("props.color"));
            var f = Filter(@"\d+", PropertySearchSyntax.Wildcard, PropertySearchMatch.Exact); Assert(f.Matches(@"\d+")); Assert(!f.Matches("123"));
        });
        Add("wildcards match a dynamic-programming oracle across all small inputs", () => {
            var patterns = Words("ab*?", 4); var inputs = Words("ab", 4);
            foreach (var pattern in patterns) foreach (var mode in Enum.GetValues<PropertySearchMatch>()) {
                var f = Filter(pattern, PropertySearchSyntax.Wildcard, mode);
                foreach (var input in inputs) Equal(Oracle(pattern, input, mode), f.Matches(input));
            }
        });
        Add("wildcard matching and equivalent updates allocate no per-row objects", () => {
            var f = Filter("props.*Color", PropertySearchSyntax.Wildcard, PropertySearchMatch.Exact);
            for (var i = 0; i < 100; i++) { f.Matches("props.liquidColor"); f.Set(" PROPS.*COLOR ", false, f.Syntax, f.MatchMode, false); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) { Assert(f.Matches("props.liquidColor")); Assert(!f.Set(" PROPS.*COLOR ", false, f.Syntax, f.MatchMode, false)); }
            Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        });
        Add("regular expressions honor alternation anchors and case options", () => {
            var f = Filter(@"^diode(On|Off)Color$", PropertySearchSyntax.RegularExpression); Equal("", f.Error);
            Assert(f.Matches("diodeOnColor")); Assert(f.Matches("DIODEOFFCOLOR")); Assert(!f.Matches("prefixdiodeOnColor"));
            Assert(!Filter("color", PropertySearchSyntax.RegularExpression, sensitive: true).Matches("COLOR"));
        });
        Add("regex match modes wrap the whole alternation rather than one branch", () => {
            var f = Filter("red|blue", PropertySearchSyntax.RegularExpression, PropertySearchMatch.Exact); Assert(f.Matches("red")); Assert(f.Matches("blue")); Assert(!f.Matches("red-tail")); Assert(!f.Matches("head-blue"));
            f = Filter("red|blue", PropertySearchSyntax.RegularExpression, PropertySearchMatch.StartsWith); Assert(f.Matches("blue-tail")); Assert(!f.Matches("head-red"));
        });
        Add("regex escape case must never be normalized into a no-op", () => {
            var f = Filter(@"\d", PropertySearchSyntax.RegularExpression, PropertySearchMatch.Exact); Assert(f.Matches("7")); Assert(!f.Matches("A")); var builds = f.PatternBuildCount;
            Assert(f.Set(@"\D", false, f.Syntax, f.MatchMode, false)); Equal(builds + 1, f.PatternBuildCount); Assert(!f.Matches("7")); Assert(f.Matches("A"));
        });
        Add("regex whitespace is literal and not silently trimmed", () => {
            var f = Filter(" color ", PropertySearchSyntax.RegularExpression, PropertySearchMatch.Exact); Equal(" color ", f.Query);
            Assert(f.Matches(" color ")); Assert(!f.Matches("color"));
        });
        Add("malformed and unsupported regex patterns fail closed and can be repaired", () => {
            foreach (var pattern in new[] { "[", "(", "(?=a)", @"(a)\1" }) {
                var f = Filter(pattern, PropertySearchSyntax.RegularExpression); Assert(f.Error.Length > 0); Assert(!f.Matches("anything"));
                Assert(f.Set("a+", false, f.Syntax, f.MatchMode, false)); Equal("", f.Error); Assert(f.Matches("aaa"));
            }
        });
        Add("regex construction rejects repetition bombs and excessive depth before building", () => {
            foreach (var pattern in new[] { "a{1000000000}", "(a{256}){256}", "(?# [)a{1000000000}", "(?x)# [\na{1000000000}", new string('(', 33) + "a" + new string(')', 33) }) {
                var f = Filter(pattern, PropertySearchSyntax.RegularExpression); Assert(f.Error.Length > 0); Equal(0L, f.PatternBuildCount); Assert(!f.Matches("a"));
            }
        });
        Add("regex budget scanner permits escaped braces classes and Unicode categories", () => {
            foreach (var (pattern, value) in new[] { (@"\{a\}", "{a}"), ("[{}]", "{"), (@"\p{Lu}+", "ABC") }) {
                var f = Filter(pattern, PropertySearchSyntax.RegularExpression, PropertySearchMatch.Exact); Equal("", f.Error); Assert(f.Matches(value));
            }
        });
        Add("regex input size is explicit and literal matching keeps its unrestricted input", () => {
            var text = new string('a', PropertyFilter.MaximumRegexInputLength + 1);
            Throws<ArgumentOutOfRangeException>(() => Filter("a+", PropertySearchSyntax.RegularExpression).Matches(text));
            Assert(Filter("a").Matches(text));
        });
        Add("bound-only toggling and equivalent regex updates reuse the built pattern", () => {
            var f = Filter("a+", PropertySearchSyntax.RegularExpression); var builds = f.PatternBuildCount;
            Assert(!f.Set("a+", false, f.Syntax, f.MatchMode, false)); Assert(f.Set("a+", true, f.Syntax, f.MatchMode, false));
            Equal(builds, f.PatternBuildCount); Assert(!f.Matches("aaa")); Assert(f.Matches("aaa", true));
        });
        Add("invalid enum arguments are atomic and the old overload resets all modes", () => {
            var f = Filter("a*", PropertySearchSyntax.Wildcard, PropertySearchMatch.Exact, true); var revision = f.Revision;
            Throws<ArgumentOutOfRangeException>(() => f.Set("", false, (PropertySearchSyntax)99, f.MatchMode, false));
            Throws<ArgumentOutOfRangeException>(() => f.Set("", false, f.Syntax, (PropertySearchMatch)99, false));
            Equal(revision, f.Revision); Equal("a*", f.Query); f.Set("color"); Equal(PropertySearchSyntax.Literal, f.Syntax); Equal(PropertySearchMatch.Anywhere, f.MatchMode); Assert(!f.CaseSensitive);
        });
        Add("all search syntaxes support configured bindings and empty queries", () => {
            foreach (var syntax in Enum.GetValues<PropertySearchSyntax>()) foreach (var mode in Enum.GetValues<PropertySearchMatch>()) {
                var f = Filter("", syntax, mode); f.Set("", true, syntax, mode, true); Assert(f.Matches("props.value", true)); Assert(!f.Matches("props.value"));
            }
        });
        Add("advanced name search does not depend on the current culture", () => {
            var culture = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); foreach (var syntax in Enum.GetValues<PropertySearchSyntax>()) Assert(Filter("LIT", syntax).Matches("props.litColor")); }
            finally { CultureInfo.CurrentCulture = culture; }
        });
    }

    private static List<string> Words(string alphabet, int length)
    {
        var result = new List<string> { "" }; var level = new List<string> { "" };
        for (var i = 0; i < length; i++) { level = level.SelectMany(prefix => alphabet.Select(c => prefix + c)).ToList(); result.AddRange(level); }
        return result;
    }
    private static bool Oracle(string pattern, string input, PropertySearchMatch mode)
    {
        if (pattern.Length == 0) return true;
        for (var start = 0; start <= input.Length; start++) {
            if (mode != PropertySearchMatch.Anywhere && start != 0) break;
            for (var end = start; end <= input.Length; end++) {
                if (mode == PropertySearchMatch.Exact && end != input.Length) continue;
                var dp = new bool[pattern.Length + 1, end - start + 1]; dp[0, 0] = true;
                for (var p = 1; p <= pattern.Length; p++) for (var n = 0; n <= end - start; n++)
                    dp[p, n] = pattern[p - 1] == '*' ? dp[p - 1, n] || n > 0 && dp[p, n - 1]
                        : n > 0 && dp[p - 1, n - 1] && (pattern[p - 1] == '?' || pattern[p - 1] == input[start + n - 1]);
                if (dp[pattern.Length, end - start]) return true;
            }
        }
        return false;
    }
}
