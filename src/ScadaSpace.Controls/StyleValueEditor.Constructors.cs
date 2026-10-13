using ScadaSpace.Core;

namespace ScadaSpace.Controls;

public sealed partial class StyleValueEditor
{
    /// <summary>Preserve the existing one-argument constructor for previously compiled library consumers.</summary>
    public StyleValueEditor(StyleProperty property) : this(property, true) { }
}
