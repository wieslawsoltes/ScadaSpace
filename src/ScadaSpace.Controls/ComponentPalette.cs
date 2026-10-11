using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using Windows.ApplicationModel.DataTransfer;

namespace ScadaSpace.Controls;

/// <summary>Searchable collapsible palette. Native component buttons are constructed once, not per keystroke.</summary>
public partial class ComponentPalette : Grid
{
    private sealed record Item(ComponentDescriptor Descriptor,Button Button,StackPanel Content);
    private sealed class Group(string name,Button header,Grid items)
    { public string Name=name;public Button Header=header;public Grid Items=items;public bool Expanded=true;public List<Item> Entries=[]; }
    private readonly TextBox _search=new() {PlaceholderText="Search components",FontSize=12,MinHeight=27,MaxLength=256,Padding=new Thickness(6,2,6,2)};
    private readonly StackPanel _items=new();
    private readonly List<Group> _groups=[];
    private readonly TextBlock _count=ControlTheme.Text("",10);
    private bool _tiles;
    public event Action<string>? ComponentRequested;
    public event Action<bool>? DisplayModeChanged;
    public int CreatedButtonCount {get;private set;}
    public int VisibleComponentCount {get;private set;}
    public FrameworkElement SearchInput=>_search;
    public Dictionary<string,FrameworkElement> Commands {get;}=new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string,FrameworkElement> Items=>_groups.SelectMany(g=>g.Entries).ToDictionary(e=>e.Descriptor.Type,e=>(FrameworkElement)e.Button,StringComparer.Ordinal);
    public bool Tiles
    {
        get=>_tiles;
        set {if(_tiles==value)return;_tiles=value;Filter();DisplayModeChanged?.Invoke(value);}
    }
    public ComponentPalette()
    {
        RowDefinitions.Add(new() {Height=new GridLength(30)});RowDefinitions.Add(new());RowDefinitions.Add(new() {Height=new GridLength(22)});
        var bar=new Grid();bar.ColumnDefinitions.Add(new());bar.ColumnDefinitions.Add(new() {Width=GridLength.Auto});bar.Children.Add(_search);
        var commands=new StackPanel {Orientation=Orientation.Horizontal};Grid.SetColumn(commands,1);bar.Children.Add(commands);Children.Add(bar);
        void Tool(string glyph,string title,string id,Action callback)
        {var button=new DesignerToolButton(glyph,title,callback,"palette-"+id) {Width=24,MinWidth=0};commands.Children.Add(button);Commands[id]=button;}
        Tool("expand","Expand all categories","expand",()=>ExpandAll(true));Tool("collapse","Collapse all categories","collapse",()=>ExpandAll(false));Tool("tiles","Toggle tile/list display","mode",()=>Tiles=!Tiles);
        var scroll=new ScrollViewer {Content=_items,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);Children.Add(scroll);
        _count.Margin=new Thickness(7,0,0,0);Grid.SetRow(_count,2);Children.Add(_count);
        foreach(var descriptors in ComponentCatalog.All.GroupBy(c=>c.Category).OrderBy(g=>g.Key,StringComparer.Ordinal))
        {
            var grid=new Grid();var header=ControlTheme.Button(descriptors.Key,()=>{});
            header.Height=24;header.MinHeight=0;header.FontSize=11;header.CornerRadius=new CornerRadius(0);header.Padding=new Thickness(5,0,5,0);
            header.BorderThickness=new Thickness(0,1,0,1);header.BorderBrush=ControlTheme.Brush("#D5D9DC");header.Background=ControlTheme.Brush("#E6E8EB");header.HorizontalAlignment=HorizontalAlignment.Stretch;header.HorizontalContentAlignment=HorizontalAlignment.Left;
            var group=new Group(descriptors.Key,header,grid);_groups.Add(group);_items.Children.Add(header);_items.Children.Add(grid);
            header.Click+=(_,_)=>{group.Expanded=!group.Expanded;Filter();};Commands["category-"+group.Name]=header;
            foreach(var descriptor in descriptors)
            {
                var content=new StackPanel {Orientation=Orientation.Horizontal,Spacing=7};content.Children.Add(new DesignerIcon(descriptor.Category,15));content.Children.Add(ControlTheme.Text(descriptor.DisplayName,11));
                var button=ControlTheme.Button(descriptor.DisplayName,()=>ComponentRequested?.Invoke(descriptor.Type),"palette-"+descriptor.Type);
                button.Content=content;button.BorderThickness=new Thickness(0);button.CornerRadius=new CornerRadius(0);button.Background=ControlTheme.Brush("#FFFFFF");button.HorizontalAlignment=HorizontalAlignment.Stretch;button.CanDrag=true;
                button.DragStarting+=(_,e)=>{e.Data.SetText("scadaspace:"+descriptor.Type);e.Data.RequestedOperation=DataPackageOperation.Copy;};
                ToolTipService.SetToolTip(button,descriptor.DisplayName+" — click to insert or drag onto the view");
                group.Entries.Add(new(descriptor,button,content));grid.Children.Add(button);CreatedButtonCount++;
            }
        }
        _search.TextChanged+=(_,_)=>Filter();Filter();
    }
    public void ExpandAll(bool expanded) {foreach(var group in _groups)group.Expanded=expanded;Filter();}
    private void Filter()
    {
        VisibleComponentCount=0;var query=_search.Text.Trim();
        foreach(var group in _groups)
        {
            var matching=group.Entries.Where(e=>e.Descriptor.DisplayName.Contains(query,StringComparison.OrdinalIgnoreCase)||e.Descriptor.Type.Contains(query,StringComparison.OrdinalIgnoreCase)||group.Name.Contains(query,StringComparison.OrdinalIgnoreCase)).ToArray();
            var expanded=query.Length>0||group.Expanded;group.Header.Content=(expanded?"▾  ":"▸  ")+group.Name+"  ("+matching.Length+")";
            group.Header.Visibility=matching.Length>0?Visibility.Visible:Visibility.Collapsed;
            group.Items.Visibility=matching.Length>0&&expanded?Visibility.Visible:Visibility.Collapsed;
            var columns=_tiles?3:1;group.Items.ColumnDefinitions.Clear();group.Items.RowDefinitions.Clear();
            for(var i=0;i<columns;i++)group.Items.ColumnDefinitions.Add(new());
            var index=0;
            foreach(var item in group.Entries)
            {
                var visible=expanded&&matching.Contains(item);item.Button.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
                if(!visible)continue;
                if(index%columns==0)group.Items.RowDefinitions.Add(new() {Height=new GridLength(_tiles?65:26)});
                Grid.SetColumn(item.Button,index%columns);Grid.SetRow(item.Button,index/columns);
                item.Button.Padding=new Thickness(_tiles?2:10,2,_tiles?2:4,2);item.Button.HorizontalContentAlignment=_tiles?HorizontalAlignment.Center:HorizontalAlignment.Left;
                item.Content.Orientation=_tiles?Orientation.Vertical:Orientation.Horizontal;item.Content.Spacing=_tiles?4:7;
                ((DesignerIcon)item.Content.Children[0]).Width=((DesignerIcon)item.Content.Children[0]).Height=_tiles?24:15;
                index++;VisibleComponentCount++;
            }
        }
        _count.Text=$"{VisibleComponentCount} components · {(_tiles?"Tiles":"List")}";
    }
}
