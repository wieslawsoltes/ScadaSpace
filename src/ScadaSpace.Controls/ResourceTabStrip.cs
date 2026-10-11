using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ScadaSpace.Controls;

/// <summary>Keyed native resource tabs with non-destructive close commands. Unchanged tabs keep their controls.</summary>
public sealed partial class ResourceTabStrip : Grid
{
    private sealed record Tab(Grid Frame,Button Open,TextBlock Label,Button Close);
    private readonly Dictionary<string,Tab> _tabs=new(StringComparer.Ordinal);
    private readonly Dictionary<string,FrameworkElement> _menuItems=new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string,FrameworkElement> ContextMenuItems => _menuItems;
    private readonly StackPanel _items=new() {Orientation=Orientation.Horizontal};
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string,FrameworkElement> _openButtons = new(StringComparer.Ordinal), _closeButtons = new(StringComparer.Ordinal);
    private readonly ReadOnlyDictionary<string,FrameworkElement> _openSnapshot, _closeSnapshot;
    public event Action<string>? OpenRequested;
    public event Action<string>? CloseRequested;
    public event Action<string>? CloseOthersRequested;
    public event Action? CloseAllRequested;
    public long CreatedTabCount {get;private set;}
    public IReadOnlyDictionary<string,FrameworkElement> OpenButtons=>_openSnapshot;
    public IReadOnlyDictionary<string,FrameworkElement> CloseButtons=>_closeSnapshot;
    public ResourceTabStrip()
    {
        _openSnapshot=new(_openButtons);_closeSnapshot=new(_closeButtons);
        Background=ControlTheme.Brush("#D5D8DB");
        _scroll=new ScrollViewer {Content=_items,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Children.Add(_scroll);
    }
    public void Update(IReadOnlyList<string> paths,string? active)
    {
        var keep=paths.ToHashSet(StringComparer.Ordinal);
        foreach(var path in _tabs.Keys.Where(p=>!keep.Contains(p)).ToArray()) { _items.Children.Remove(_tabs[path].Frame);_tabs.Remove(path);_openButtons.Remove(path);_closeButtons.Remove(path);foreach(var command in new[]{"Close","Close Others","Close All"})_menuItems.Remove(path+":"+command); }
        for(var index=0;index<paths.Count;index++)
        {
            var path=paths[index];
            if(!_tabs.TryGetValue(path,out var tab))
            {
                var label=ControlTheme.Text(path[(path.LastIndexOf('/')+1)..],11);
                var content=new StackPanel {Orientation=Orientation.Horizontal,Spacing=5};content.Children.Add(new DesignerIcon("view",13));content.Children.Add(label);
                var open=ControlTheme.Button(path,()=>OpenRequested?.Invoke(path),"view-"+path);
                open.Content=content;open.Height=27;open.MinHeight=0;open.Padding=new Thickness(7,2,3,2);open.BorderThickness=new Thickness(0);open.Background=ControlTheme.Brush("#00000000");
                AutomationProperties.SetName(open,path);ToolTipService.SetToolTip(open,path);
                var close=new DesignerToolButton("close","Close "+path,()=>CloseRequested?.Invoke(path),"close-view-"+path) {Width=22,Height=25,MinWidth=0,Padding=new Thickness(4)};
                var frame=new Grid {BorderThickness=new Thickness(0,2,1,0)};frame.ColumnDefinitions.Add(new() {Width=GridLength.Auto});frame.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
                frame.Children.Add(open);Grid.SetColumn(close,1);frame.Children.Add(close);
                var menu=new MenuFlyout();
                void Item(string title,Action callback) {var item=new MenuFlyoutItem {Text=title};item.Click+=(_,_)=>callback();menu.Items.Add(item);_menuItems[path+":"+title]=item;}
                Item("Close",()=>CloseRequested?.Invoke(path));Item("Close Others",()=>CloseOthersRequested?.Invoke(path));Item("Close All",()=>CloseAllRequested?.Invoke());
                frame.ContextFlyout=menu;tab=new(frame,open,label,close);_tabs.Add(path,tab);_openButtons.Add(path,open);_closeButtons.Add(path,close);CreatedTabCount++;
            }
            if(index>=_items.Children.Count || !ReferenceEquals(_items.Children[index],tab.Frame)) { _items.Children.Remove(tab.Frame);_items.Children.Insert(index,tab.Frame); }
            tab.Frame.Background=ControlTheme.Brush(path==active?"#FFFFFF":"#DADDE0");
            tab.Frame.BorderBrush=ControlTheme.Brush(path==active?"#47A8CE":"#BAC1C6");
            tab.Close.Background=ControlTheme.Brush(path==active?"#FFFFFF":"#DADDE0");
        }
    }
}
