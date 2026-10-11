using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Controls;
using ScadaSpace.Core;
using ScadaSpace.Runtime;
using Windows.System;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private readonly SessionPageHost _pageHost = new();
    private readonly TextBox _routeBox = new() { FontSize = 12, MinWidth = 0, MinHeight = 30, Padding = new Thickness(7, 3, 7, 3) };
    private bool _restoringBrowser;
    private ViewHost ActiveHost => _sessionMode ? _pageHost.PrimaryHost : _surface.Host;
    private Grid BuildSessionBar()
    {
        var bar = new Grid { Padding = new Thickness(5, 3, 5, 3), ColumnSpacing = 4, Background = ControlTheme.Brush("#D8E3E9") };
        bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new()); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var back = ControlTheme.Button("Back", () => _session.Back(), "session-back");
        var forward = ControlTheme.Button("Forward", () => _session.Forward(), "session-forward");
        var go = ControlTheme.Button("Go", NavigateFromBar, "session-go");
        _commands["session-back"] = back; _commands["session-forward"] = forward; _commands["session-go"] = go; _commands["session-url"] = _routeBox;
        AutomationProperties.SetAutomationId(_routeBox, "session-url");
        _routeBox.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { NavigateFromBar(); e.Handled = true; } };
        bar.Children.Add(back); Grid.SetColumn(forward, 1); bar.Children.Add(forward); Grid.SetColumn(_routeBox, 2); bar.Children.Add(_routeBox); Grid.SetColumn(go, 3); bar.Children.Add(go);
        return bar;
    }
    private void NavigateFromBar()
    {
        if (!_session.Navigate(_routeBox.Text)) SetStatus(_session.LastError ?? "Navigation failed.");
        else SetStatus("Page " + _session.CurrentUrl);
    }
    private void PageNavigated(NavigationChange change)
    {
        if (!_sessionMode) return;
        if (!_restoringBrowser) BrowserServices.PublishPageNavigation(change, _document.Project.Name);
    }
    private void ReadBrowserNavigation()
    {
        if (!_sessionMode || BrowserServices.TakePageNavigation() is not { } target) return;
        _restoringBrowser = true;
        try
        {
            if (!_session.RestoreBrowserEntry(target.Entry, target.Url)) SetStatus(_session.LastError ?? "Unknown page URL.");
            // Canonicalize the selected entry without creating a second browser history entry.
            if (_session.Current is { } current) BrowserServices.PublishPageNavigation(new(current, NavigationKind.Replace), _document.Project.Name);
        }
        finally { _restoringBrowser = false; }
    }
    private void UpdateSessionBar()
    {
        if (!_sessionMode || _session is null) return;
        if (_routeBox.FocusState == FocusState.Unfocused) _routeBox.Text = _session.CurrentUrl;
        ((Button)_commands["session-back"]).IsEnabled = _session.CanGoBack;
        ((Button)_commands["session-forward"]).IsEnabled = _session.CanGoForward;
    }
    private object? PageTelemetry(Func<FrameworkElement, object?> bounds)
    {
        if (!_sessionMode) return null;
        return new
        {
            url = _session.CurrentUrl, entry = _session.Current?.EntryId, parameters = _session.Current?.Parameters,
            query = _session.Current?.Address.Query, canGoBack = _session.CanGoBack, canGoForward = _session.CanGoForward,
            primary = _pageHost.Layout?.Primary, bounds = bounds(_pageHost), diagnostics = _session.Diagnostics,
            docks = _session.Docks.Select(d => new
            {
                id = d.Definition.Id, instance = d.InstanceId, view = d.Definition.View, open = d.IsOpen, pushes = d.PushesContent, modal = d.Definition.Modal, size = d.Size,
                bounds = _pageHost.Layout?.Docks.GetValueOrDefault(d.Definition.Id),
                handle = _pageHost.DockHandles.TryGetValue(d.Definition.Id, out var handle) ? bounds(handle) : null,
                splitter = _pageHost.DockSplitters.TryGetValue(d.Definition.Id, out var splitter) ? bounds(splitter) : null,
                components = _pageHost.DockHosts.TryGetValue(d.Definition.Id, out var host) && host.Instance is { } instance
                    ? instance.View.Root.DescendantsAndSelf().ToDictionary(n => n.Name, n => new { value = host.Read(n, "value"), text = host.Read(n, "text"), bounds = host.Elements.TryGetValue(n.Id, out var element) ? bounds(element) : null }) : null
            }).ToArray()
        };
    }
}
