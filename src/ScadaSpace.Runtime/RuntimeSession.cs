using System.Collections.ObjectModel;
using ScadaSpace.Core;

namespace ScadaSpace.Runtime;

public sealed record SessionMessage(string Type, IReadOnlyDictionary<string, Scalar> Payload);
public sealed record PopupInstance(string Id, string View, IReadOnlyDictionary<string, Scalar> Parameters);
public enum NavigationKind { Push, Replace, Traverse, Restore }
public sealed record PageNavigation(Guid EntryId, PageAddress Address, PageDefinition Page, IReadOnlyDictionary<string, Scalar> Parameters);
public sealed record NavigationChange(PageNavigation Current, NavigationKind Kind, int Delta = 0);

/// <summary>UI-thread-affine local session. Navigation snapshots, view defaults and input dictionaries never alias.</summary>
public sealed partial class RuntimeSession
{
    public const int MaximumHistory = 128;
    private readonly ProjectRuntime _runtime;
    private readonly List<PageNavigation> _history = [];
    private readonly Dictionary<string, PopupInstance> _popups = new(StringComparer.Ordinal);
    private readonly List<Diagnostic> _diagnostics = [];
    private int _cursor = -1;
    private bool _notifying;
    public RuntimeSession(ProjectRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        if (!Navigate("/", replace: true))
            foreach (var page in runtime.Project.Pages)
            {
                try { if (Navigate(ExpandPage(page), replace: true)) break; }
                catch (ArgumentException) { /* A parameterized-only project may require an explicit initial route. */ }
            }
    }
    public PageNavigation? Current => _cursor < 0 ? null : _history[_cursor];
    public string CurrentPath => Current?.Address.Path ?? "/";
    public string CurrentUrl => Current?.Address.Url ?? "/";
    public bool CanGoBack => _cursor > 0;
    public bool CanGoForward => _cursor >= 0 && _cursor < _history.Count - 1;
    public string Actor { get; set; } = "demo-operator";
    public string? LastError { get; private set; }
    public IReadOnlyCollection<PopupInstance> Popups => _popups.Values.ToArray();
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.AsReadOnly();
    public event Action? Changed;
    public event Action<NavigationChange>? Navigated;
    public event Action<SessionMessage>? Message;

    public string ExpandPage(PageDefinition page)
    {
        var parameters = new Dictionary<string, Scalar>(_runtime.Project.Views.First(v => v.Path == page.View).Parameters, StringComparer.Ordinal);
        foreach (var pair in page.Parameters) parameters[pair.Key] = pair.Value;
        return new PageRoutePattern(page.Path).Expand(parameters);
    }
    public bool Navigate(string url, IReadOnlyDictionary<string, Scalar>? parameters = null, bool replace = false)
    {
        if (_notifying) return Fail("Navigation cannot be reentered from a notification.");
        try
        {
            var address = PageAddress.Parse(url);
            var candidates = _runtime.Project.Pages.Select(page => (Page: page, Pattern: new PageRoutePattern(page.Path)))
                .OrderByDescending(p => p.Pattern.Specificity, StringComparer.Ordinal);
            foreach (var (page, pattern) in candidates)
            {
                if (!pattern.TryMatch(address, out var captures)) continue;
                var defaults = _runtime.Project.Views.First(v => v.Path == page.View).Parameters;
                var values = new Dictionary<string, Scalar>(defaults, StringComparer.Ordinal);
                foreach (var pair in page.Parameters) values[pair.Key] = pair.Value;
                foreach (var pair in captures) values[pair.Key] = pair.Value;
                if (parameters is not null) foreach (var pair in parameters) values[pair.Key] = pair.Value;
                if (values.Count > 256 || values.Any(p => string.IsNullOrEmpty(p.Key) || !p.Value.IsValid())) return Fail("Invalid navigation parameters.");
                var frozen = new ReadOnlyDictionary<string, Scalar>(values);
                LastError = null;
                if (Current is { } old && old.Address.Url == address.Url && ReferenceEquals(old.Page, page) && Same(old.Parameters, frozen)) return true;
                var next = new PageNavigation(Guid.NewGuid(), address, page, frozen);
                if (replace && _cursor >= 0) _history[_cursor] = next;
                else
                {
                    if (_cursor + 1 < _history.Count) _history.RemoveRange(_cursor + 1, _history.Count - _cursor - 1);
                    _history.Add(next);
                    if (_history.Count > MaximumHistory) _history.RemoveAt(0);
                    _cursor = _history.Count - 1;
                }
                ReconcileDocks(); Notify(new(next, replace ? NavigationKind.Replace : NavigationKind.Push)); return true;
            }
            return Fail("Unknown page route: " + url);
        }
        catch (ArgumentException ex) { return Fail(ex.Message); }
    }
    private static bool Same(IReadOnlyDictionary<string, Scalar> a, IReadOnlyDictionary<string, Scalar> b) => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && value == p.Value);
    private bool Fail(string message) { LastError = message; return false; }
    public bool Back() => Traverse(-1);
    public bool Forward() => Traverse(1);
    private bool Traverse(int delta)
    {
        if (_notifying || _cursor + delta < 0 || _cursor + delta >= _history.Count) return false;
        _cursor += delta; LastError = null; ReconcileDocks(); Notify(new(Current!, NavigationKind.Traverse, delta)); return true;
    }
    /// <summary>Restore an entry selected by browser back/forward, without creating another history entry.</summary>
    public bool RestoreBrowserEntry(Guid entryId, string url)
    {
        if (_notifying) return false;
        var index = _history.FindIndex(p => p.EntryId == entryId && p.Address.Url == url);
        if (index < 0) return Navigate(url, replace: true);
        if (index == _cursor) return true;
        _cursor = index; ReconcileDocks(); Notify(new(Current!, NavigationKind.Restore)); return true;
    }
    private void Notify(NavigationChange? navigation = null)
    {
        _notifying = true;
        try
        {
            if (navigation is not null && Navigated is { } navigated)
                foreach (Action<NavigationChange> observer in navigated.GetInvocationList()) Observe(() => observer(navigation));
            if (Changed is { } changed) foreach (Action observer in changed.GetInvocationList()) Observe(observer);
        }
        finally { _notifying = false; }
    }
    private void Observe(Action observer)
    {
        try { observer(); }
        catch (Exception ex)
        {
            if (_diagnostics.Count == 128) _diagnostics.RemoveAt(0);
            _diagnostics.Add(new("SESSION_OBSERVER", ex.Message));
        }
    }
    private static IReadOnlyDictionary<string, Scalar> Snapshot(IReadOnlyDictionary<string, Scalar> parameters)
    {
        if (parameters.Count > 256 || parameters.Any(p => string.IsNullOrEmpty(p.Key) || !p.Value.IsValid())) throw new ArgumentException("Invalid session parameters.");
        return new ReadOnlyDictionary<string, Scalar>(new Dictionary<string, Scalar>(parameters, StringComparer.Ordinal));
    }
    public void Dispatch(ComponentNode node, string eventName)
    {
        if (_notifying) throw new InvalidOperationException("Actions cannot be dispatched recursively from session notifications.");
        if (!node.Events.TryGetValue(eventName, out var actions)) return;
        foreach (var action in actions.ToArray())
        {
            switch (action.Kind)
            {
                case ActionKind.WriteTag:
                    var write = _runtime.Tags.Write(action.Target, action.Value, Actor);
                    if (!write.Success) throw new InvalidOperationException(write.Message); break;
                case ActionKind.ToggleTag:
                    var old = _runtime.Tags.Read(action.Target);
                    if (!old.IsGood || old.Value.Kind != ScalarKind.Boolean) throw new InvalidOperationException("A toggle requires a good Boolean tag.");
                    var toggle = _runtime.Tags.Write(action.Target, Scalar.From(!old.Value.Boolean), Actor, old.Revision);
                    if (!toggle.Success) throw new InvalidOperationException(toggle.Message); break;
                case ActionKind.Navigate:
                    if (!Navigate(action.Target, action.Parameters)) throw new InvalidOperationException(LastError); break;
                case ActionKind.NavigateBack: Back(); break;
                case ActionKind.NavigateForward: Forward(); break;
                case ActionKind.OpenDock:
                    if (!OpenDock(action.Target, action.Parameters)) throw new InvalidOperationException(LastError); break;
                case ActionKind.CloseDock:
                    if (!CloseDock(action.Target)) throw new InvalidOperationException(LastError); break;
                case ActionKind.ToggleDock:
                    if (!ToggleDock(action.Target)) throw new InvalidOperationException(LastError); break;
                case ActionKind.OpenPopup:
                    if ((!_popups.ContainsKey(action.Target) && _popups.Count >= 16) || !_runtime.Project.Views.Any(x => x.Path == action.Target)) throw new InvalidOperationException("Unknown popup view or popup limit reached.");
                    _popups[action.Target] = new(action.Target, action.Target, Snapshot(action.Parameters)); Notify(); break;
                case ActionKind.ClosePopup: ClosePopup(action.Target); break;
                case ActionKind.SendMessage:
                    var message = new SessionMessage(action.Target, Snapshot(action.Parameters));
                    _notifying = true;
                    try { if (Message is { } handlers) foreach (Action<SessionMessage> observer in handlers.GetInvocationList()) Observe(() => observer(message)); }
                    finally { _notifying = false; }
                    break;
                default: throw new InvalidOperationException("Unsupported action.");
            }
        }
    }
    public void ClosePopup(string id) { if (!_notifying && _popups.Remove(id)) Notify(); }
}
