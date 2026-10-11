using ScadaSpace.Core;

namespace ScadaSpace.Data;

/// <summary>Build a deterministic hierarchical snapshot from provider definitions. No device I/O or tag writes.</summary>
public static class TagTreeBuilder
{
    private sealed class Branch(string name, string path)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public bool Tag { get; set; }
        public SortedDictionary<string, Branch> Children { get; } = new(StringComparer.Ordinal);
    }
    public static Scalar Build(IEnumerable<TagDefinition> definitions, string root = "")
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (root is null || root.Length > 512 || root.Any(char.IsControl)) throw new ArgumentException("Invalid tag tree root.");
        var providers = new SortedDictionary<string, Branch>(StringComparer.Ordinal); var paths = new HashSet<string>(StringComparer.Ordinal); var count = 0;
        foreach (var tag in definitions)
        {
            if (tag is null || !TagPaths.IsValid(tag.Path)) throw new ArgumentException("Invalid tag tree definition.");
            if (!paths.Add(tag.Path)) throw new ArgumentException("Duplicate tag tree path.");
            if (root.Length > 0 && tag.Path != root && !(root.EndsWith(']') ? tag.Path.StartsWith(root, StringComparison.Ordinal) : tag.Path.StartsWith(root.TrimEnd('/') + "/", StringComparison.Ordinal))) continue;
            var end = tag.Path.IndexOf(']'); var provider = tag.Path[..(end + 1)];
            if (!providers.TryGetValue(provider, out var branch))
            { branch = new(provider, provider); providers.Add(provider, branch); count++; }
            var path = provider;
            foreach (var part in tag.Path[(end + 1)..].Split('/'))
            {
                path += path.EndsWith(']') ? part : "/" + part;
                if (!branch.Children.TryGetValue(part, out var child)) { child = new(part, path); branch.Children.Add(part, child); count++; }
                if (count > DocumentTree.MaximumItems) throw new ArgumentException("Tag tree exceeds its item limit.");
                branch = child;
            }
            branch.Tag = true;
        }
        Scalar Convert(Branch branch) => Scalar.Object(new Dictionary<string, Scalar>
        {
            ["id"] = Scalar.From(branch.Path), ["label"] = Scalar.From(branch.Name),
            ["expanded"] = Scalar.From(branch.Path.EndsWith(']')), ["data"] = Scalar.From(branch.Path),
            ["items"] = Scalar.Array(branch.Children.Values.Select(Convert)), ["tag"] = Scalar.From(branch.Tag)
        });
        return Scalar.Array(providers.Values.Select(Convert));
    }
}
