using ScadaSpace.Core;

namespace ScadaSpace.Designer;

/// <summary>Project-wide state editing with detached immutable catalogs and one undo transaction per Apply.</summary>
public sealed class SymbolStateDraft
{
    private readonly DesignerDocument _document;
    private long _generation;
    private readonly Dictionary<string, string> _literals = new(StringComparer.Ordinal);
    private SymbolStateCatalog _saved;
    public SymbolStateCatalog Catalog { get; private set; }
    public IReadOnlyList<string> Names => Catalog.Names;
    public bool IsDirty { get; private set; }
    public long Revision { get; private set; }
    public SymbolStateDraft(DesignerDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document)); _generation = document.Generation;
        Catalog = _saved = document.Project.SymbolStates;
        foreach (var node in document.Project.Views.SelectMany(v => v.Root.DescendantsAndSelf()))
            if (SymbolStateCatalog.Families.Contains(node.Type) && node.Props.TryGetValue("state", out var value) && value.Kind == ScalarKind.String)
                _literals.Add(node.Id, value.Text!);
    }
    public void Set(string name, SymbolStateDefinition value)
    { _ = Catalog.Get(name); Change(Catalog.With(name, value)); }
    public void Create(string name, string? source = null)
    {
        SymbolStateCatalog.ValidateName(name);
        if (Names.Contains(name)) throw new ArgumentException("That symbol state already exists.");
        var template = Catalog.Get(source ?? "default");
        Change(Catalog.With(name, new(template.Light, template.Dark, source is null ? Array.Empty<string>() : template.Symbols)));
    }
    public void Rename(string name, string replacement)
    {
        SymbolStateCatalog.ValidateName(replacement); if (name == replacement) return;
        if (SymbolStateCatalog.IsBuiltIn(name)) throw new ArgumentException("Built-in state names cannot be changed.");
        if (Names.Contains(replacement)) throw new ArgumentException("That symbol state already exists.");
        var value = Catalog.Get(name);
        var next = Catalog.WithoutCustom(name).With(replacement, value);
        foreach (var id in _literals.Where(p => p.Value == name).Select(p => p.Key).ToArray()) _literals[id] = replacement;
        Change(next);
    }
    public void Delete(string name) => Change(Catalog.WithoutCustom(name));
    public void ResetTheme(string name, string theme) => Set(name, Catalog.Get(name).ResetTheme(theme));
    public void RevertTheme(string name, string theme)
    {
        SymbolStateCatalog.ValidateTheme(theme);
        var current = Catalog.Get(name);
        var previous = _saved.Names.Contains(name) ? _saved.Get(name) : current;
        Set(name, current.WithTheme(theme, theme == "light" ? previous.Light : previous.Dark));
    }
    private void Change(SymbolStateCatalog next)
    {
        if (ReferenceEquals(Catalog, next)) return;
        Catalog = next; IsDirty = true; Revision++;
    }
    public bool Apply(DesignerDocument document)
    {
        if (!ReferenceEquals(document, _document) || document.Generation != _generation)
            throw new InvalidOperationException("The project changed while the state editor was open. Reopen it before applying.");
        if (!IsDirty) return false;
        var literalChanges = _literals.Any(p => document.Find(p.Key)?.Props.GetValueOrDefault("state") != Scalar.From(p.Value));
        if (!literalChanges && document.Project.SymbolStates.Equivalent(Catalog)) { IsDirty = false; _saved = Catalog; return false; }
        var catalog = Catalog; var before = document.Generation;
        try
        {
            document.Execute("Configure symbol states", project =>
            {
                project.SymbolStates = catalog;
                foreach (var (id, state) in _literals)
                    (document.Find(id) ?? throw new InvalidOperationException("Symbol was removed.")).Props["state"] = Scalar.From(state);
            });
            _generation = document.Generation; _saved = Catalog; IsDirty = false;
        }
        finally
        {
            if (document.Generation == before + 1 && ReferenceEquals(document.Project.SymbolStates, catalog))
            { _generation = document.Generation; _saved = Catalog; IsDirty = false; }
        }
        return document.Generation != before;
    }
}
