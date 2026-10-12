using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Controls;
using ScadaSpace.Designer;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private StyleClassEditor? _styleEditor;
    private void InitializeStyleTools()
    {
        if (_commands["menu-Project"] is not MenuBarItem menu) return;
        var item = new MenuFlyoutItem { Text = "Style Classes…" };
        item.Click += (_, _) => RunDialog(() => EditStyleClassesAsync());
        menu.Items.Add(item); _commands["style-classes"] = item;
    }
    private async Task EditStyleClassesAsync(string? name = null)
    {
        if (_document is null || _preview || _sessionMode) { SetStatus("Style classes are edited in Design mode."); return; }
        var component = _document.Selection.Count == 1 ? _document.Selection.Single() : null;
        var draft = new StyleClassDraft(_document); _styleEditor = new StyleClassEditor(draft, component, name);
        var dialog = ConfigurationDialog("Style Classes", _styleEditor, "OK", "Apply");
        dialog.PrimaryButtonClick += (_, e) => e.Cancel = !_styleEditor.Apply(_document);
        dialog.SecondaryButtonClick += (_, e) => { _styleEditor.Apply(_document); e.Cancel = true; };
        try { await dialog.ShowAsync(); }
        finally { _styleEditor = null; _configurationButtons.Clear(); PublishTelemetry(); }
    }
}
