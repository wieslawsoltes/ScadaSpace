using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ScadaSpace.Controls;
using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Runtime;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private void TogglePreview() { if (_sessionMode || _resources.ActiveView is null) return; _preview = !_preview; OpenView(_view); SetStatus(_preview ? "Preview mode · controls write only to the isolated simulator" : "Design mode · select and edit components"); }
    private void Edit(Action action) { if (_preview) return; try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or JsonException) { SetStatus(ex.Message); } }
    private void Undo() => Edit(() => _document.Undo());
    private void Redo() => Edit(() => _document.Redo());
    private void Copy() { if (_document is null) return; _clipboard = _document.CopySelection(); SetStatus("Copied " + _document.Selection.Count + " components"); }
    private void Cut() { Copy(); Delete(); }
    private void Paste() => Edit(() => { if (_resources.ActiveView is not null && _clipboard.Length > 0) _document.Paste(_clipboard, _document.Project.Views.Single(v => v.Path == _view).Root.Id); });
    private void Duplicate() { if (_preview) return; Copy(); Paste(); }
    private void Delete() => Edit(() => _document.Delete());
    private void Save()
    {
        try { BrowserServices.SaveProject(ProjectCodec.Serialize(_document.Project)); _document.MarkSaved(); UpdateButtons(); SetStatus("Project saved locally"); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException) { SetStatus("Save failed: " + ex.Message); }
    }
    private void Export() { if (_document is not null) SetStatus(BrowserServices.Download("scadaspace-project.json", ProjectCodec.Serialize(_document.Project))); }
    private void ExportHistory()
    {
        var path = _tags.SelectedKey ?? _runtime.Tags.Browse().FirstOrDefault()?.Path;
        if (path is not null) SetStatus(BrowserServices.Download("scadaspace-history.csv", _runtime.History.ExportCsv(path), "text/csv;charset=utf-8"));
    }
    private void ValidateProject()
    {
        var errors = ProjectCodec.Validate(_document.Project); _surface.Host.Instance?.Evaluate();
        var bindings = _surface.Host.Instance?.Diagnostics.Count ?? 0;
        SetStatus(errors.Count == 0 && bindings == 0 ? "Project schema and current view bindings are valid" : $"{errors.Count} project errors, {bindings} current-view binding errors");
    }
    private async Task ImportAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        if (BrowserServices.BeginFileImport()) return;
        await _inspector.EditJsonAsync("Import ScadaSpace project", "", text => { InstallProject(ProjectCodec.Deserialize(text)); SetStatus("Imported project"); }, "Paste an exported ScadaSpace project. Ignition project archives are not compatible.");
    }
    private async Task EditProjectJsonAsync() => await _inspector.EditJsonAsync("Project JSON", ProjectCodec.Serialize(_document.Project), text => { var project = ProjectCodec.Deserialize(text); InstallProject(project); SetStatus("Applied project JSON"); });
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_document.IsDirty) return true;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Unsaved project changes", Content = "Replacing the project will discard unsaved design changes. Runtime tag values and history are not project files.", PrimaryButtonText = "Replace project", CloseButtonText = "Cancel" };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task NewProjectAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        InstallProject(new ProjectDefinition { Name = "New Project", Views = [DemoProject.View("Main", 1040, 760)], Pages = [new() { Path = "/", View = "Main", Title = "Main" }] }); SetStatus("Created an empty project");
    }
    private async Task RestoreDemoAsync() { if (await ConfirmDiscardAsync()) { InstallProject(DemoProject.Create()); _surface.Fit(); SetStatus("Restored isolated water-treatment demonstration"); } }
    private async Task NewViewAsync()
    {
        if (_preview) return;
        var name = new TextBox { Header = "View path", Text = "NewView", MinWidth = 340 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = ControlTheme.Brush("#AD4F35") };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "New view", Content = new StackPanel { Spacing = 10, Children = { name, error } }, PrimaryButtonText = "Create", CloseButtonText = "Cancel" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 128 || name.Text.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '/'))) throw new FormatException("Use letters, numbers, slash, hyphen and underscore.");
                _document.Execute("Add view", p => p.Views.Add(DemoProject.View(name.Text, 1040, 760))); OpenView(name.Text);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException) { args.Cancel = true; error.Text = ex.Message; }
        };
        await dialog.ShowAsync();
    }
    private async Task WriteTagAsync()
    {
        var path = _tags.SelectedKey; if (path is null) return;
        var definition = _runtime.Tags.Browse().First(d => d.Path == path); var old = _runtime.Tags.Read(path);
        var value = new TextBox { Header = definition.DataType + " value" + (definition.Unit.Length > 0 ? " (" + definition.Unit + ")" : ""), Text = old.Value.ToString(), MinWidth = 350 };
        var error = new TextBlock { Foreground = ControlTheme.Brush("#AD4F35"), TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = path, Content = new StackPanel { Spacing = 8, Children = { value, new TextBlock { Text = definition.Writable ? "Writes apply to the local simulator only. Concurrent value changes are rejected." : "This tag is read-only.", TextWrapping = TextWrapping.Wrap, FontSize = 12 }, error } }, PrimaryButtonText = definition.Writable ? "Write" : "", CloseButtonText = "Cancel" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var result = _runtime.Tags.Write(path, Scalar.Parse(value.Text, definition.DataType), "demo-operator", old.Revision);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                RefreshTags(); _surface.Host.Refresh(); SetStatus("Wrote " + path);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException) { args.Cancel = true; error.Text = ex.Message; }
        };
        await dialog.ShowAsync();
    }
    private async Task AboutAsync()
    {
        await new ContentDialog { XamlRoot = XamlRoot, Title = "ScadaSpace 0.1.0", CloseButtonText = "Close", Content = new TextBlock { Text = "Independent C# / Uno Platform SCADA workbench.\n\nReusable project, tag, expression, binding, alarm, history, designer, layout, graphics and control packages.\n\nThis is an isolated simulation, not production industrial-control software. It is not Ignition Perspective and does not have full Perspective compatibility. No proprietary code or visual assets are included.\n\nNot implemented: Ignition Gateway compatibility, Jython, real OPC-UA/device drivers, enterprise authentication, SQL named queries, redundancy and the full Perspective component/API catalog.\n\nGitHub: wieslawsoltes/ScadaSpace", TextWrapping = TextWrapping.Wrap, MaxWidth = 430, FontSize = 13 } }.ShowAsync();
    }
    private void LaunchSession()
    {
        Save();
#if __WASM__
        BrowserServices.OpenSession();
#else
        var window = new Window { Title = "ScadaSpace · isolated session" };
        var page = new WorkbenchPage(sessionMode: true); window.Content = page; window.Closed += (_, _) => page.Dispose(); window.Activate();
#endif
    }
    private void SessionChanged()
    {
        var page = _session.Current?.Page;
        if (_sessionMode) { _pageHost.Refresh(); if (page is not null) _view = page.View; UpdateButtons(); }
        else if (page is not null)
        {
            OpenView(page.View);
            if (_surface.Host.Instance is { } instance)
            {
                foreach (var pair in _session.Current!.Parameters) instance.WriteParameter(pair.Key, pair.Value);
                instance.SetPageProperties(Scalar.Object(new Dictionary<string, Scalar> { ["path"] = Scalar.From(_session.CurrentPath), ["urlParams"] = Scalar.Object(_session.Current.Address.Query) }));
                _surface.Host.Refresh();
            }
        }
        if (_session.Popups.LastOrDefault() is { } popup && !_dialog) RunDialog(async () =>
        {
            var host = new ViewHost(); host.Load(_runtime, popup.View, popup.Parameters, session: _session);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = popup.View, Content = new Viewbox { Child = host, MaxWidth = 600, MaxHeight = 500 }, CloseButtonText = "Close" };
            await dialog.ShowAsync(); host.Dispose(); _session.ClosePopup(popup.Id);
        });
    }
    private async void RunDialog(Func<Task> action)
    {
        if (_dialog) return; _dialog = true;
        try { await action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or FormatException or JsonException) { SetStatus(ex.Message); }
        finally { _dialog = false; }
    }
    private void HandleKeys(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        if (e.Handled || _dialog || _inspector.CreationEditor is not null) return;
        var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
        if (!_sessionMode && ctrl && e.Key == VirtualKey.Tab) { ChangeResources(() => _resources.Cycle(shift ? -1 : 1)); e.Handled = true; return; }
        if (!_sessionMode && ctrl && e.Key == VirtualKey.W && _resources.ActiveView is {} active) { ChangeResources(() => _resources.Close(active)); e.Handled = true; return; }
        if (e.Key == VirtualKey.F5) { TogglePreview(); e.Handled = true; return; }
        if (ctrl && e.Key == VirtualKey.S) { Save(); e.Handled = true; return; }
        if (TextInputFocused(e.OriginalSource as DependencyObject) || _preview || _resources.ActiveView is null) return;
        if (ctrl)
        {
            switch (e.Key) { case VirtualKey.Z: Undo(); break; case VirtualKey.Y: Redo(); break; case VirtualKey.C: Copy(); break; case VirtualKey.X: Cut(); break; case VirtualKey.V: Paste(); break; case VirtualKey.D: Duplicate(); break; default: return; }
            e.Handled = true;
        }
        else
        {
            var step = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0 ? 8 : 1;
            switch (e.Key) { case VirtualKey.Delete: Delete(); break; case VirtualKey.Left: Edit(() => _document.Move(-step, 0)); break; case VirtualKey.Right: Edit(() => _document.Move(step, 0)); break; case VirtualKey.Up: Edit(() => _document.Move(0, -step)); break; case VirtualKey.Down: Edit(() => _document.Move(0, step)); break; default: return; }
            e.Handled = true;
        }
    }
}
