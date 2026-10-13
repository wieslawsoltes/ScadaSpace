using System.Globalization;
using System.Runtime.CompilerServices;
using ScadaSpace.Designer;
using static Suite;

internal static class PropertyFilterTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("empty property filtering includes bound and unbound fields", () => {
            var filter = new PropertyFilter(); Assert(filter.Matches("props.value")); Assert(filter.Matches("props.value", true));
            Assert(!filter.Set("")); Equal(0L, filter.Revision);
        });
        Add("property filtering searches qualified names without matching unrelated values", () => {
            var filter = new PropertyFilter(); filter.Set("props.liquid");
            Assert(filter.Matches("props.liquidColor")); Assert(!filter.Matches("custom.liquidColor")); Assert(!filter.Matches("props.color"));
        });
        Add("property filters match section and partial property names", () => {
            var filter = new PropertyFilter(); filter.Set("COLOR"); Assert(filter.Matches("props.liquidColor"));
            filter.Set("position."); Assert(filter.Matches("position.width")); Assert(!filter.Matches("props.width"));
        });
        Add("bound-only properties combine with the current literal filter", () => {
            var filter = new PropertyFilter(); filter.Set("", true); Assert(filter.Matches("props.value", true)); Assert(!filter.Matches("props.value"));
            filter.Set("color", true); Assert(filter.Matches("props.color", true)); Assert(!filter.Matches("props.value", true)); Assert(!filter.Matches("props.color"));
        });
        Add("property query normalization preserves revision and string identity on no-op changes", () => {
            var filter = new PropertyFilter(); filter.Set(" color "); var text = filter.Query; var revision = filter.Revision;
            Assert(!filter.Set("\tCOLOR ")); Equal(revision, filter.Revision); Assert(ReferenceEquals(text, filter.Query));
            Assert(filter.Set("color", true)); Equal(revision + 1, filter.Revision);
        });
        Add("clearing a property filter resets both query and binding mode", () => {
            var filter = new PropertyFilter(); filter.Set("color", true); Assert(filter.Set(""));
            Equal("", filter.Query); Assert(!filter.BoundOnly); Assert(filter.Matches("meta.name"));
        });
        Add("invalid property filter changes leave the previous query intact", () => {
            var filter = new PropertyFilter(); filter.Set("color", true); var revision = filter.Revision;
            Throws<ArgumentNullException>(() => filter.Set(null!)); Throws<ArgumentOutOfRangeException>(() => filter.Set(new string('x', 257)));
            Equal("color", filter.Query); Assert(filter.BoundOnly); Equal(revision, filter.Revision);
            filter.Set(new string('x', 256)); Equal(256, filter.Query.Length);
        });
        Add("literal property filters do not interpret patterns or markup", () => {
            var filter = new PropertyFilter(); filter.Set("[0]"); Assert(filter.Matches("custom.ports[0]")); Assert(!filter.Matches("custom.ports0"));
            filter.Set(".*"); Assert(!filter.Matches("props.value")); Assert(filter.Matches("custom.literal.*"));
            Throws<ArgumentNullException>(() => filter.Matches(null!));
        });
        Add("property filtering is ordinal rather than current-culture dependent", () => {
            var original = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR"); var filter = new PropertyFilter(); filter.Set("LIT"); Assert(filter.Matches("props.litColor")); }
            finally { CultureInfo.CurrentCulture = original; }
        });
        Add("matching and equivalent property filter updates allocate no per-row strings", () => {
            var filter = new PropertyFilter(); filter.Set("color", true);
            for (var i = 0; i < 100; i++) { filter.Matches("props.liquidColor", true); filter.Set(" COLOR ", true); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) { Assert(filter.Matches("props.liquidColor", true)); Assert(!filter.Matches("props.value", true)); Assert(!filter.Set(" COLOR ", true)); }
            Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        });
    }
}
