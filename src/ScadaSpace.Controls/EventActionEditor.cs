using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using ScadaSpace.Designer;

namespace ScadaSpace.Controls;

/// <summary>Native three-pane event editor. All changes are staged; this control never dispatches actions.</summary>
public sealed partial class EventActionEditor : Grid
{
    private readonly EventConfigurationDraft _draft;
    private readonly ListView _actions = new() { SelectionMode = ListViewSelectionMode.Single, FontSize = 12 };
    private readonly StackPanel _eventList = new() { Spacing = 1 };
    private readonly TextBox _search = new() { PlaceholderText = "Filter events", FontSize = 12, MaxLength = 128 };
    private readonly ComboBox _kind = new() { Header = "Action type", ItemsSource = Enum.GetValues<ActionKind>(), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private readonly TextBox _target = new() { Header = "Target", FontSize = 12, MaxLength = 4096 };
    private readonly ComboBox _choices = new() { Header = "Available targets", HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private readonly ComboBox _valueKind = new() { Header = "Value type", ItemsSource = Enum.GetValues<ScalarKind>(), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 12 };
    private readonly TextBox _value = new() { Header = "Value", FontSize = 12, MaxLength = 65536, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _parameters = new() { Header = "Parameters (JSON object)", FontSize = 12, MaxLength = 65536, AcceptsReturn = true, MinHeight = 74, MaxHeight = 150 };
    private readonly CheckBox _enabled = new() { Content = "Enabled", FontSize = 12 };
    private readonly TextBlock _heading = ControlTheme.Text("", 17);
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = ControlTheme.Brush("#A73828") };
    private readonly StackPanel _settings = new() { Spacing = 7 };
    private readonly Dictionary<string, Button> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _fields = new(StringComparer.Ordinal);
    private readonly string[] _tags, _views, _pages, _docks;
    private bool _sync;
    private string _event = "";
    private int _selected = -1;
    public IReadOnlyDictionary<string, FrameworkElement> EditorFields => _fields;
    public string ActiveEvent => _event;
    public int SelectedAction => _selected;
    public int ActionCount => _draft.Actions(_event).Count;
    public string ErrorText => _error.Text;
    public EventConfigurationDraft Draft => _draft;

    public EventActionEditor(EventConfigurationDraft draft, ProjectDefinition project, string? initialEvent = null)
    {
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        _tags = project.Tags.Select(t => t.Path).Take(512).ToArray();
        _views = project.Views.Select(v => v.Path).ToArray(); _pages = project.Pages.Select(p => p.Path).ToArray();
        _docks = project.SharedDocks.Concat(project.Pages.SelectMany(p => p.Docks)).Select(d => d.Id).Distinct(StringComparer.Ordinal).ToArray();
        Background = ControlTheme.Brush("#F5F6F7");
        RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
        ColumnDefinitions.Add(new() { Width = new GridLength(195) }); ColumnDefinitions.Add(new());
        var events = new Grid { Background = ControlTheme.Brush("#DDE1E4"), Padding = new Thickness(10) };
        events.RowDefinitions.Add(new() { Height = new GridLength(30) }); events.RowDefinitions.Add(new() { Height = new GridLength(38) }); events.RowDefinitions.Add(new());
        events.Children.Add(ControlTheme.Text("Events", 13)); Grid.SetRow(_search, 1); events.Children.Add(_search);
        var scroll = new ScrollViewer { Content = _eventList }; Grid.SetRow(scroll, 2); events.Children.Add(scroll); Children.Add(events);
        string? category = null;
        foreach (var name in draft.EventNames)
        {
            var group = name is "onClick" or "onDoubleClick" or "onContextMenu" ? "Mouse Events"
                : EventConfigurations.Supported(draft.ComponentType).Contains(name) ? "Component Events" : "Custom / unwired";
            if (category != group) { var heading = ControlTheme.Text(group, 11); heading.Margin = new Thickness(3, 9, 0, 4); _eventList.Children.Add(heading); category = group; }
            var button = ControlTheme.Button(name, () => ChangeEvent(name)); button.FontSize = 12; button.Height = 29; button.MinHeight = 0;
            button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(6, 0, 3, 0); button.BorderThickness = new Thickness(0); button.CornerRadius = new CornerRadius(0);
            _eventList.Children.Add(button); _events[name] = button; Track("event-" + name, button);
        }
        var main = new Grid { Padding = new Thickness(14, 10, 10, 8), ColumnSpacing = 12 };
        main.RowDefinitions.Add(new() { Height = new GridLength(38) }); main.RowDefinitions.Add(new() { Height = new GridLength(40) }); main.RowDefinitions.Add(new());
        main.ColumnDefinitions.Add(new() { Width = new GridLength(195) }); main.ColumnDefinitions.Add(new());
        Grid.SetColumnSpan(_heading, 2); main.Children.Add(_heading); Grid.SetRow(_notice, 1); Grid.SetColumnSpan(_notice, 2); main.Children.Add(_notice);
        var organize = new Grid { ColumnSpacing = 4 }; organize.RowDefinitions.Add(new() { Height = new GridLength(27) }); organize.RowDefinitions.Add(new());
        organize.ColumnDefinitions.Add(new()); organize.ColumnDefinitions.Add(new() { Width = new GridLength(28) });
        var title = ControlTheme.Text("Organize Actions", 12); Grid.SetColumnSpan(title, 2); organize.Children.Add(title);
        Grid.SetRow(_actions, 1); organize.Children.Add(_actions);
        var tools = new StackPanel { Spacing = 3 }; Grid.SetRow(tools, 1); Grid.SetColumn(tools, 1); organize.Children.Add(tools);
        void Tool(string text, string id, Action edit)
        {
            var button = ControlTheme.Button(text, () => TryEdit(edit)); button.Width = 27; button.MinWidth = 0; button.Height = 26; button.MinHeight = 0; button.Padding = new Thickness(0);
            tools.Children.Add(button); Track(id, button);
        }
        Tool("+", "add", () => { CommitCurrent(); _draft.Add(_event, new(ActionKind.WriteTag, _tags.FirstOrDefault() ?? "", Scalar.From(0), Scalar.Object([]))); Reload(ActionCount - 1); });
        Tool("×", "remove", () => { if (_selected >= 0) { _draft.Remove(_event, _selected); Reload(Math.Min(_selected, ActionCount - 1)); } });
        Tool("↑", "up", () => Move(-1)); Tool("↓", "down", () => Move(1));
        Tool("⧉", "duplicate", () => { CommitCurrent(); if (_selected >= 0) { _draft.Add(_event, _draft.Actions(_event)[_selected]); Reload(ActionCount - 1); } });
        Grid.SetRow(organize, 2); main.Children.Add(organize);
        _settings.Children.Add(ControlTheme.Text("Configure Action", 13));
        foreach (var control in new UIElement[] { _kind, _target, _choices, _valueKind, _value, _parameters, _enabled }) _settings.Children.Add(control);
        var settingsScroll = new ScrollViewer { Content = _settings }; Grid.SetRow(settingsScroll, 2); Grid.SetColumn(settingsScroll, 1); main.Children.Add(settingsScroll);
        Grid.SetColumn(main, 1); Children.Add(main); Grid.SetRow(_error, 1); Grid.SetColumnSpan(_error, 2); _error.Margin = new Thickness(12, 6, 12, 6); Children.Add(_error);
        Track("search", _search); Track("actions", _actions); Track("kind", _kind); Track("target", _target); Track("choices", _choices);
        Track("value-kind", _valueKind); Track("value", _value); Track("parameters", _parameters); Track("enabled", _enabled);
        _search.TextChanged += (_, _) => { foreach (var (name, button) in _events) button.Visibility = name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed; };
        _actions.SelectionChanged += (_, _) =>
        {
            if (_sync) return;
            var next = _actions.SelectedIndex;
            if (!TryEdit(() => { CommitCurrent(); LoadAction(next); })) { _sync = true; _actions.SelectedIndex = _selected; _sync = false; }
        };
        _kind.SelectionChanged += (_, _) => { if (!_sync) ShowFields(); };
        _choices.SelectionChanged += (_, _) => { if (!_sync && _choices.SelectedItem is string target) _target.Text = target; };
        _event = initialEvent is not null && _events.ContainsKey(initialEvent) ? initialEvent : draft.EventNames.FirstOrDefault(n => draft.Actions(n).Count > 0) ?? draft.EventNames[0];
        Reload(0);
    }
    private void Track(string id, FrameworkElement control) { _fields[id] = control; AutomationProperties.SetAutomationId(control, "event-editor-" + id); }
    public bool TryEdit(Action action)
    {
        try { action(); _error.Text = ""; return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or System.Text.Json.JsonException) { _error.Text = ex.Message; return false; }
    }
    private void ChangeEvent(string name) => TryEdit(() => { CommitCurrent(); _event = name; Reload(0); });
    private void Move(int delta)
    {
        CommitCurrent(); if (_selected < 0) return;
        _draft.Move(_event, _selected, delta); Reload(Math.Clamp(_selected + delta, 0, ActionCount - 1));
    }
    private void Reload(int index)
    {
        _sync = true;
        try
        {
            _actions.ItemsSource = _draft.Actions(_event).Select((a, i) => $"{i + 1}. {a.Kind}{(a.Enabled ? "" : " (disabled)")}").ToArray();
            _selected = ActionCount == 0 ? -1 : Math.Clamp(index, 0, ActionCount - 1); _actions.SelectedIndex = _selected;
            _heading.Text = "Configure [" + _event + "] Actions";
            _notice.Text = EventConfigurations.Supported(_draft.ComponentType).Contains(_event)
                ? "Actions run in order in the local runtime. Configuring an action does not execute it."
                : "Preserved custom event: not automatically raised by this host. No scripting engine is attached.";
            foreach (var (name, button) in _events)
            {
                button.Content = name + (_draft.Actions(name).Count > 0 ? " *" : "");
                button.Background = ControlTheme.Brush(name == _event ? "#48A8CE" : "#FFFFFF");
                button.Foreground = ControlTheme.Brush(name == _event ? "#FFFFFF" : "#354B58");
            }
        }
        finally { _sync = false; }
        LoadAction(_selected);
    }
    private void LoadAction(int index)
    {
        _selected = index; _settings.Visibility = index >= 0 ? Visibility.Visible : Visibility.Collapsed;
        if (index < 0) return;
        var action = _draft.Actions(_event)[index]; _sync = true;
        try
        {
            _kind.SelectedItem = action.Kind; _target.Text = action.Target; _valueKind.SelectedItem = action.Value.Kind;
            _value.Text = action.Value.Kind is ScalarKind.Array or ScalarKind.Object ? action.Value.ToJson()
                : action.Value.Kind == ScalarKind.Number ? NumericInputText.FormatDouble(action.Value.Number) : action.Value.ToString();
            _parameters.Text = action.Parameters.ToJson(); _enabled.IsChecked = action.Enabled;
        }
        finally { _sync = false; }
        ShowFields();
    }
    private void ShowFields()
    {
        if (_kind.SelectedItem is not ActionKind kind) return;
        var writing = kind == ActionKind.WriteTag; var parameters = kind is ActionKind.Navigate or ActionKind.OpenDock or ActionKind.OpenPopup or ActionKind.SendMessage;
        _value.Visibility = _valueKind.Visibility = writing ? Visibility.Visible : Visibility.Collapsed;
        _parameters.Visibility = parameters ? Visibility.Visible : Visibility.Collapsed;
        _target.Visibility = kind is ActionKind.NavigateBack or ActionKind.NavigateForward ? Visibility.Collapsed : Visibility.Visible;
        _target.Header = kind is ActionKind.WriteTag or ActionKind.ToggleTag ? "Tag path" : kind == ActionKind.Navigate ? "Page URL" : kind == ActionKind.SendMessage ? "Message type" : kind is ActionKind.OpenPopup or ActionKind.ClosePopup ? "View path / popup identity" : "Dock ID";
        _sync = true;
        try
        {
            _choices.ItemsSource = kind is ActionKind.WriteTag or ActionKind.ToggleTag ? _tags : kind == ActionKind.Navigate ? _pages
                : kind is ActionKind.OpenPopup or ActionKind.ClosePopup ? _views : kind is ActionKind.OpenDock or ActionKind.CloseDock or ActionKind.ToggleDock ? _docks : Array.Empty<string>();
            _choices.SelectedItem = null; _choices.Visibility = kind is ActionKind.SendMessage or ActionKind.NavigateBack or ActionKind.NavigateForward ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _sync = false; }
    }
    private void CommitCurrent()
    {
        if (_selected < 0) return;
        var previous = _draft.Actions(_event)[_selected]; var kind = (ActionKind)_kind.SelectedItem;
        if (kind is ActionKind.WriteTag or ActionKind.ToggleTag && !TagPaths.IsValid(_target.Text)) throw new FormatException("Choose a provider-qualified tag path.");
        if (kind == ActionKind.Navigate) _ = PageAddress.Parse(_target.Text);
        if (kind is not (ActionKind.NavigateBack or ActionKind.NavigateForward) && string.IsNullOrWhiteSpace(_target.Text)) throw new FormatException("An action target is required.");
        var value = kind == ActionKind.WriteTag ? Scalar.Parse(_value.Text, (ScalarKind)_valueKind.SelectedItem) : previous.Value;
        var parameters = kind is ActionKind.Navigate or ActionKind.OpenDock or ActionKind.OpenPopup or ActionKind.SendMessage ? Scalar.FromJson(_parameters.Text) : previous.Parameters;
        _draft.Replace(_event, _selected, new(kind, _target.Text, value, parameters, _enabled.IsChecked == true));
    }
    public bool Apply(DesignerDocument document) => TryEdit(() => { CommitCurrent(); _draft.Apply(document); Reload(_selected); });
}
