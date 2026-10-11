using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Controls;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private PageConfigurationEditor? _pageEditor;
    private async Task EditPagesAsync()
    {
        if (_preview) return;
        var document = _document; var generation = document.Generation;
        var editor = new PageConfigurationEditor(document.Project); _pageEditor = editor;
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#A73828") };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Page Configuration", PrimaryButtonText = "Apply", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, Content = new StackPanel { Spacing = 6, Children = { editor, error } } };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (!ReferenceEquals(document, _document) || document.Generation != generation) throw new InvalidOperationException("Project changed while the page editor was open. Reopen the editor.");
                var updated = editor.CreateProject();
                document.Execute("Configure pages and docks", project => { project.Pages = updated.Pages; project.SharedDocks = updated.SharedDocks; project.SharedCornerPriority = updated.SharedCornerPriority; });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or System.Text.Json.JsonException)
            { args.Cancel = true; error.Text = ex.Message; }
        };
        try { await dialog.ShowAsync(); }
        finally { _pageEditor = null; }
    }
}
