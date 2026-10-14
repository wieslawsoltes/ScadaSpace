using ScadaSpace.Core;

namespace ScadaSpace.Designer;

public enum PropertyCreationGroup { Props, Custom }

/// <summary>A component-owned, revision-checked add operation. Parsing never changes the document.</summary>
public sealed class PropertyCreationDraft
{
    private readonly DesignerDocument _document;
    private long _generation;
    public string ComponentId { get; }
    public string ComponentName { get; }
    public PropertyCreationGroup Group { get; }
    public bool IsApplied { get; private set; }

    public PropertyCreationDraft(DesignerDocument document, string componentId, PropertyCreationGroup group)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(componentId);
        if (!Enum.IsDefined(group)) throw new ArgumentOutOfRangeException(nameof(group));
        var node = document.Find(componentId) ?? throw new ArgumentException("Unknown component.", nameof(componentId));
        _document = document; _generation = document.Generation;
        ComponentId = componentId; ComponentName = node.Name; Group = group;
    }

    public static void ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
            throw new FormatException("Use a non-empty property name of at most 128 characters, without control characters.");
    }

    public bool Apply(DesignerDocument document, string name, ScalarKind kind, string text)
    {
        if (!ReferenceEquals(document, _document) || document.Generation != _generation)
            throw new InvalidOperationException("The project changed while this editor was open. Reopen it before applying.");
        if (IsApplied) return false;
        ValidateName(name);
        var value = PropertyValueText.Parse(text, kind);
        var node = document.Find(ComponentId) ?? throw new InvalidOperationException("Component was removed.");
        var target = Group == PropertyCreationGroup.Custom ? node.Custom : node.Props;
        if (target.ContainsKey(name)) throw new InvalidOperationException("That property already exists in this category.");
        var before = document.Generation;
        try
        {
            document.Execute("Add " + Group.ToString().ToLowerInvariant() + " property", _ => target.Add(name, value));
        }
        finally
        {
            // Changed observers can throw after a committed edit. Do not allow a
            // second Add to repeat that transaction, and do not swallow the error.
            if (document.Generation == before + 1 && target.TryGetValue(name, out var saved) && saved == value)
            { _generation = document.Generation; IsApplied = true; }
        }
        return IsApplied;
    }
}
