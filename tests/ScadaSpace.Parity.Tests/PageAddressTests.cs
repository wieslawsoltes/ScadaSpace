using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using static Suite;

internal static class PageAddressTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("all accepted Unicode URLs round-trip through their canonical representation", () => {
            foreach (var input in new[] { "/設備/ポンプ?表示=温度", "/equipment/Łódź?unit=°C", "/equipment/🛠?note=a%2Bb+c", "/equipment/%E6%B3%B5?mode=%E8%BF%90%E8%A1%8C" })
            {
                var a = PageAddress.Parse(input); var b = PageAddress.Parse(a.Url); Equal(a.Url, b.Url);
                Assert(a.Segments.SequenceEqual(b.Segments)); Equal(Scalar.Object(a.Query), Scalar.Object(b.Query));
            }
        });
        Add("canonical URL budget includes percent-encoding expansion", () => {
            Throws<ArgumentException>(() => PageAddress.Parse("/" + new string('界',500)));
            Throws<ArgumentException>(() => PageAddress.Parse("/?q=" + new string('界',500)));
            var near = PageAddress.Parse("/?q=" + new string('界',450)); Assert(near.Url.Length <= PageAddress.MaximumLength); Equal(near.Url, PageAddress.Parse(near.Url).Url);
        });
        Add("URL decoding rejects invalid UTF-8 in both paths and query fields", () => {
            foreach (var invalid in new[] { "%FF", "%C0%AF", "%ED%A0%80", "%E2%82", "%F4%90%80%80" })
            {
                Throws<ArgumentException>(() => PageAddress.Parse("/equipment/" + invalid));
                Throws<ArgumentException>(() => PageAddress.Parse("/?value=" + invalid));
                Throws<ArgumentException>(() => PageAddress.Parse("/?" + invalid + "=x"));
            }
            Throws<ArgumentException>(() => PageAddress.Parse("/?q=\uD800"));
        });
        Add("typed query edits preserve precision, encode delimiters, and delete null fields", () => {
            var original = PageAddress.Parse("/equipment/A?keep=yes&remove=1");
            var result = original.WithQuery(new Dictionary<string, Scalar> { ["remove"] = Scalar.Null, ["number"] = Scalar.From(1.234567891234), ["flag"] = Scalar.From(true), ["label"] = Scalar.From("a+b & x=1 #ok") });
            Equal("1.234567891234", result.Query["number"].Text); Equal("true", result.Query["flag"].Text); Equal("a+b & x=1 #ok", result.Query["label"].Text); Equal("yes", result.Query["keep"].Text);
            Assert(!result.Query.ContainsKey("remove")); Assert(original.Query.ContainsKey("remove")); Equal(result.Url, PageAddress.Parse(result.Url).Url);
        });
        Add("query updates reject structured values and excessive resulting field counts", () => {
            var address = PageAddress.Parse("/");
            Throws<ArgumentException>(() => address.WithQuery(new Dictionary<string, Scalar> { ["data"] = Scalar.Array([]) }));
            var full = address.WithQuery(Enumerable.Range(0,64).ToDictionary(i=>"q"+i,i=>Scalar.From(i)));
            Throws<ArgumentException>(() => full.WithQuery(new Dictionary<string, Scalar> { ["extra"] = Scalar.From(1) }));
            Equal(64, full.Query.Count);
        });
        Add("configured navigation expands captures without merging query into view inputs", () => {
            using var r = new ProjectRuntime(PageTests.Project()); var s = new RuntimeSession(r);
            Assert(s.NavigatePage("/equipment/:equipment", new Dictionary<string, Scalar> { ["equipment"] = Scalar.From("Pump Łódź"), ["unit"] = Scalar.From("bar") }, new Dictionary<string, Scalar> { ["unit"] = Scalar.From("kPa") }));
            Equal("Pump Łódź", s.Current!.Parameters["equipment"].Text); Equal("bar", s.Current.Parameters["unit"].Text); Equal("kPa", s.Current.Address.Query["unit"].Text);
            Assert(s.Back()); Equal("/", s.CurrentPath); Assert(s.Forward()); Equal("Pump Łódź", s.Current!.Parameters["equipment"].Text);
        });
        Add("configured navigation failure leaves history, active page and docks unchanged", () => {
            using var r = new ProjectRuntime(PageTests.Project()); var s = new RuntimeSession(r); var before = s.Current; var dock = s.Docks[0].InstanceId;
            Assert(!s.NavigatePage("/equipment/:equipment", new Dictionary<string, Scalar> { ["equipment"] = Scalar.From("A/B") }));
            Assert(!s.NavigatePage("/equipment/:equipment", query: new Dictionary<string, Scalar> { ["x"] = Scalar.Object([]) }));
            Assert(!s.NavigatePage("/unknown")); Equal(before, s.Current); Equal(dock, s.Docks[0].InstanceId); Assert(!s.CanGoBack);
        });
        Add("configured replacement navigation uses defaults without growing history", () => {
            using var r = new ProjectRuntime(PageTests.Project()); var s = new RuntimeSession(r);
            Assert(s.NavigatePage("/equipment/:equipment", replace:true)); Equal("default", s.Current!.Parameters["equipment"].Text); Assert(!s.CanGoBack);
            var copy = s.Current; Assert(s.NavigatePage("/equipment/:equipment", replace:true)); Equal(copy, s.Current);
        });
    }
}
