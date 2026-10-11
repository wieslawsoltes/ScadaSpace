using ScadaSpace.Core;

namespace ScadaSpace.Runtime;

public sealed partial class RuntimeSession
{
    /// <summary>Navigate a configured page pattern using typed parameters and separately encoded query values.</summary>
    public bool NavigatePage(string pattern, IReadOnlyDictionary<string, Scalar>? parameters = null, IReadOnlyDictionary<string, Scalar>? query = null, bool replace = false)
    {
        if (_notifying) return Fail("Navigation cannot be reentered from a notification.");
        try
        {
            var page = _runtime.Project.Pages.FirstOrDefault(p => p.Path == pattern);
            if (page is null) return Fail("Unknown page pattern: " + pattern);
            var values = new Dictionary<string, Scalar>(_runtime.Project.Views.First(v => v.Path == page.View).Parameters, StringComparer.Ordinal);
            foreach (var pair in page.Parameters) values[pair.Key] = pair.Value;
            if (parameters is not null) foreach (var pair in parameters) values[pair.Key] = pair.Value;
            // Validate everything before Navigate changes history or reconciles dock instances.
            Snapshot(values);
            var address = PageAddress.Parse(new PageRoutePattern(pattern).Expand(values));
            if (query is not null) address = address.WithQuery(query);
            return Navigate(address.Url, values, replace);
        }
        catch (ArgumentException ex) { return Fail(ex.Message); }
    }
}
