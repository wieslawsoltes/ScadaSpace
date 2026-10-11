using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ScadaSpace.Controls;

public sealed partial class HmiTreeListView : ListView
{
    private readonly HashSet<HmiTreeListItem> _containers = [];
    private string? _pendingFocusKey;
    internal HmiTree? TreeOwner { get; set; }
    public IReadOnlyList<HmiTreeListItem> RealizedRows => _containers.Where(c => c.IsLoaded && c.Row is not null).ToArray();
    internal HmiTreeListItem? FocusedItem
    {
        get
        {
            var current = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            while (current is not null && current != this) { if (current is HmiTreeListItem item) return item; current = VisualTreeHelper.GetParent(current); }
            return null;
        }
    }
    internal void ResetPendingFocus() => _pendingFocusKey = null;
    internal void FocusRow(TreeRowViewModel row)
    {
        _pendingFocusKey = row.Row.Item.Key;
        ScrollIntoView(row); DispatcherQueue.TryEnqueue(TryPendingFocus);
    }
    private void TryPendingFocus()
    {
        if (_pendingFocusKey is not { } key) return;
        var container = _containers.FirstOrDefault(c => c.IsLoaded && c.Row?.Row.Item.Key == key);
        if (container?.Focus(FocusState.Keyboard) == true) _pendingFocusKey = null;
    }
    protected override DependencyObject GetContainerForItemOverride()
    {
        var container = new HmiTreeListItem { Tree = this };
        container.Loaded += (_, _) => TryPendingFocus();
        return container;
    }
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is HmiTreeListItem container && item is TreeRowViewModel row)
        {
            container.Row = row; _containers.Add(container);
            // Derived containers do not inherit every default ListViewItem style setter on all Uno targets.
            container.Height = row.Height; container.MinHeight = 0; container.Padding = new Thickness(0);
            container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            container.VerticalContentAlignment = VerticalAlignment.Stretch;
            AutomationProperties.SetName(container, row.Label);
            AutomationProperties.SetAutomationId(container, "tree-row-" + row.Path.Replace('/', '-'));
            AutomationProperties.SetLevel(container, row.Row.Item.Depth + 1);
            AutomationProperties.SetPositionInSet(container, row.Row.Item.Index + 1);
            if (_pendingFocusKey is not null) DispatcherQueue.TryEnqueue(TryPendingFocus);
        }
    }
    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is HmiTreeListItem container) { _containers.Remove(container); container.Row = null; }
        base.ClearContainerForItemOverride(element, item);
    }
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    { if (TreeOwner?.HandleTreeKey(e) == true) e.Handled = true; else base.OnKeyDown(e); }
    protected override AutomationPeer OnCreateAutomationPeer() => new TreePeer(this);
    private sealed class TreePeer(HmiTreeListView owner) : ListViewAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;
        protected override string GetClassNameCore() => nameof(HmiTree);
    }
}

public sealed partial class HmiTreeListItem : ListViewItem
{
    internal HmiTreeListView? Tree { get; set; }
    public TreeRowViewModel? Row { get; internal set; }
    protected override AutomationPeer OnCreateAutomationPeer() => new TreeItemPeer(this);
    private sealed class TreeItemPeer(HmiTreeListItem owner) : ListViewItemAutomationPeer(owner), IExpandCollapseProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;
        protected override string GetClassNameCore() => nameof(HmiTreeListItem);
        protected override string GetNameCore() => owner.Row?.Label ?? "";
        protected override object GetPatternCore(PatternInterface patternInterface) => patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(patternInterface);
        public ExpandCollapseState ExpandCollapseState => owner.Row is not { } row || row.Row.Item.Children.Count == 0 ? ExpandCollapseState.LeafNode : row.Row.Expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => Change(true);
        public void Collapse() => Change(false);
        private void Change(bool expanded)
        {
            if (!owner.IsEnabled) throw new InvalidOperationException("Tree is not interactable.");
            if (owner.Row is { } row && row.Row.Item.Children.Count > 0) owner.Tree?.TreeOwner?.SetExpanded(row.Path, expanded);
        }
    }
}
