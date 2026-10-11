using Microsoft.UI.Xaml.Controls;

namespace ScadaSpace.Controls;

public sealed partial class HmiTree
{
    public TextBox FilterInput => FilterBox;
    public string? DataError { get; private set; }
    public void SetDataError(string? error)
    {
        if (DataError == error) return;
        DataError = error;
        StatusText.Foreground = ControlTheme.Brush(error is null ? "#526671" : "#B14E37");
        StatusText.Text = error is null ? $"{_rows.Count} visible / {ItemCount} items · {_state.Selected.Count} selected" : "Tree unavailable: " + error;
    }
}
