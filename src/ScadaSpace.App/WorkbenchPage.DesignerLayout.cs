using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Controls;
using ScadaSpace.Designer;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private DesignerPanels _panels=new();
    private readonly DesignerWorkspace _resources=new();
    private DockPane _projectPane=null!,_tagPane=null!,_propertyPane=null!,_componentPane=null!;
    private DockSplitter _leftVertical=null!,_rightVertical=null!;
    private Button _componentRail=null!;
    private readonly Grid _emptyWorkspace=new();
    private readonly TextBox _explorerSearch=new() {PlaceholderText="Filter project",FontSize=12,MaxLength=256,MinHeight=27,Padding=new Thickness(6,2,6,2)};
    private readonly Dictionary<string,List<ToggleMenuFlyoutItem>> _panelMenus=new(StringComparer.Ordinal);
    private bool _paletteOpen,_panelSavePending;
    private long _lastTabRevision=-1;
    private void LoadPanelPreferences()
    {
        try { _panels=DesignerPanels.Parse(BrowserServices.LoadWorkspace()); }
        catch(Exception ex) when(ex is FormatException or IOException or InvalidOperationException) { _panels=new(); }
        _leftWidth=_panels.LeftWidth;_rightWidth=_panels.RightWidth;_palette.Tiles=_panels.PaletteTiles;
    }
    private void BuildDesignerUi()
    {
        var root=new Grid();root.RowDefinitions.Add(new() {Height=new GridLength(27)});root.RowDefinitions.Add(new() {Height=new GridLength(29)});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new() {Height=new GridLength(23)});
        var top=new Grid {Background=ControlTheme.Brush("#E7E9EB"),BorderBrush=ControlTheme.Brush("#BDC3C8"),BorderThickness=new Thickness(0,0,0,1)};
        top.ColumnDefinitions.Add(new() {Width=GridLength.Auto});top.ColumnDefinitions.Add(new());top.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var menu=new MenuBar {VerticalAlignment=VerticalAlignment.Center};
        AddMenu(menu,"File",("New project…",()=>RunDialog(NewProjectAsync)),("Open project file…",()=>RunDialog(ImportAsync)),("Save project",Save),("Export project JSON",Export),("Restore demonstration…",()=>RunDialog(RestoreDemoAsync)));
        AddMenu(menu,"Edit",("Undo",Undo),("Redo",Redo),("Cut",Cut),("Copy",Copy),("Paste",Paste),("Duplicate",Duplicate),("Delete",Delete));
        AddMenu(menu,"View",("Preview / design",TogglePreview),("Fit view",()=>_surface.Fit()),("Actual size",()=>_surface.Zoom=1),("Toggle grid",()=>_surface.ShowGrid=!_surface.ShowGrid),("Reset Panels",ResetPanels));
        var panelMenu=new MenuFlyoutSubItem {Text="Panels"};AddPanelItems(panelMenu.Items);menu.Items[2].Items.Insert(0,panelMenu);
        AddMenu(menu,"Project",("New view…",()=>RunDialog(NewViewAsync)),("Page configuration…",()=>RunDialog(EditPagesAsync)),("Edit project JSON…",()=>RunDialog(EditProjectJsonAsync)),("Validate project",ValidateProject));
        AddMenu(menu,"Component",("Align left",()=>Edit(()=>_document.Align(Alignment.Left))),("Align top",()=>Edit(()=>_document.Align(Alignment.Top))),("Bring to front",()=>Edit(()=>_document.BringToFront(true))),("Send to back",()=>Edit(()=>_document.BringToFront(false))),("Duplicate",Duplicate),("Delete",Delete));
        AddMenu(menu,"Tools",("Launch isolated session",LaunchSession),("Export tag history CSV",ExportHistory));
        AddMenu(menu,"Help",("About / compatibility",()=>RunDialog(AboutAsync)));
        top.Children.Add(menu);_title.HorizontalAlignment=HorizontalAlignment.Right;_title.Margin=new Thickness(8,0,12,0);Grid.SetColumn(_title,1);top.Children.Add(_title);
        var badge=new Border {Background=ControlTheme.Brush("#E1EBE6"),Margin=new Thickness(0,3,6,3),Padding=new Thickness(6,0,6,0),Child=ControlTheme.Text("LOCAL SIMULATION",9,"#426D5B")};Grid.SetColumn(badge,2);top.Children.Add(badge);root.Children.Add(top);
        _toolbar.Margin=new Thickness(4,1,4,1);_toolbar.Spacing=1;
        var toolScroll=new ScrollViewer {Content=_toolbar,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=ControlTheme.Brush("#F1F2F3")};
        AddTool("Save project (Ctrl+S)",Save,"save");AddTool("Undo (Ctrl+Z)",Undo,"undo");AddTool("Redo (Ctrl+Y)",Redo,"redo");Separator();
        AddTool("Cut",Cut,"cut");AddTool("Copy",Copy,"copy");AddTool("Paste",Paste,"paste");AddTool("Delete",Delete,"delete");Separator();
        AddTool("Preview (F5)",TogglePreview,"preview");AddTool("Zoom in",()=>_surface.Zoom*=1.15,"zoom-in");AddTool("Zoom out",()=>_surface.Zoom/=1.15,"zoom-out");AddTool("Fit view",()=>_surface.Fit(),"fit");_toolbar.Children.Add(_zoomText);Separator();
        AddTool("Bring to front",()=>Edit(()=>_document.BringToFront(true)),"front");AddTool("Send to back",()=>Edit(()=>_document.BringToFront(false)),"back");AddTool("Align left",()=>Edit(()=>_document.Align(Alignment.Left)),"align-left");AddTool("Align top",()=>Edit(()=>_document.Align(Alignment.Top)),"align-top");Separator();
        AddTool("Pause simulation",()=>{_simulator.Running=!_simulator.Running;UpdateButtons();},"simulation");
        AddTool("Export project",Export,"export");AddTool("Import project",()=>RunDialog(ImportAsync),"import");AddTool("Page configuration",()=>RunDialog(EditPagesAsync),"page-configuration");AddTool("Launch session",LaunchSession,"launch-session");
        Grid.SetRow(toolScroll,1);root.Children.Add(toolScroll);
        foreach(var width in new[]{new GridLength(_leftWidth),new GridLength(4),new GridLength(1,GridUnitType.Star),new GridLength(4),new GridLength(_rightWidth),new GridLength(22)})_workspace.ColumnDefinitions.Add(new(){Width=width});
        _left.RowDefinitions.Add(new());_left.RowDefinitions.Add(new(){Height=new GridLength(4)});_left.RowDefinitions.Add(new());
        var project=new Grid();project.RowDefinitions.Add(new(){Height=new GridLength(29)});project.RowDefinitions.Add(new());project.Children.Add(_explorerSearch);Grid.SetRow(_explorer,1);project.Children.Add(_explorer);
        _explorer.FontSize=12;_explorer.Resources["TreeViewItemMinHeight"]=24d;
        _projectPane=new("Project Browser",project,"project-pane",()=>SetPanel("project",false));_left.Children.Add(_projectPane);
        _leftVertical=new(){Vertical=true};_leftVertical.DragDelta+=delta=>{var fraction=(_left.RowDefinitions[0].ActualHeight+delta)/Math.Max(1,_left.ActualHeight-4);_panels=_panels with{ProjectFraction=Math.Clamp(fraction,.15,.85)};ApplyPanels();};Grid.SetRow(_leftVertical,1);_left.Children.Add(_leftVertical);
        _tags.AddCommand("Write…",_=>RunDialog(WriteTagAsync));_tagPane=new("Tag Browser · [default]",_tags,"tags-pane",()=>SetPanel("tags",false));Grid.SetRow(_tagPane,2);_left.Children.Add(_tagPane);_workspace.Children.Add(_left);
        var leftSplit=new DockSplitter();leftSplit.DragDelta+=delta=>{_leftWidth=Math.Clamp(_leftWidth+delta,180,450);_panels=_panels with{LeftWidth=_leftWidth};ApplyPanels();};Grid.SetColumn(leftSplit,1);_workspace.Children.Add(leftSplit);
        var middle=new Grid();middle.RowDefinitions.Add(new(){Height=_sessionMode?new GridLength(38):new GridLength(0)});middle.RowDefinitions.Add(new());middle.RowDefinitions.Add(new(){Height=_sessionMode?new GridLength(0):new GridLength(30)});
        if(_sessionMode)middle.Children.Add(BuildSessionBar());
        Grid.SetRow(_surface,1);middle.Children.Add(_surface);Grid.SetRow(_pageHost,1);middle.Children.Add(_pageHost);_surface.Visibility=_sessionMode?Visibility.Collapsed:Visibility.Visible;_pageHost.Visibility=_sessionMode?Visibility.Visible:Visibility.Collapsed;
        var empty=new StackPanel {HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Spacing=10};empty.Children.Add(ControlTheme.Text("No resource open",20));empty.Children.Add(ControlTheme.Text("Open a view in the Project Browser. Closing tabs does not delete views.",12));
        var reopen=ControlTheme.Button("Open first view",()=>{if(_document is not null)OpenView(_document.Project.Views[0].Path);},"reopen-view");empty.Children.Add(reopen);_commands["reopen-view"]=reopen;
        _emptyWorkspace.Children.Add(empty);_emptyWorkspace.Background=ControlTheme.Brush("#D7DADD");_emptyWorkspace.Visibility=Visibility.Collapsed;Grid.SetRow(_emptyWorkspace,1);middle.Children.Add(_emptyWorkspace);
        var resources=new Grid();resources.ColumnDefinitions.Add(new(){Width=GridLength.Auto});resources.ColumnDefinitions.Add(new());
        var settings=new DesignerToolButton("panels","Page configuration",()=>RunDialog(EditPagesAsync),"resource-settings");resources.Children.Add(settings);_commands["resource-settings"]=settings;
        Grid.SetColumn(_viewTabs,1);resources.Children.Add(_viewTabs);Grid.SetRow(resources,2);middle.Children.Add(resources);
        Grid.SetColumn(middle,2);_workspace.Children.Add(middle);
        var rightSplit=new DockSplitter();rightSplit.DragDelta+=delta=>{_rightWidth=Math.Clamp(_rightWidth-delta,220,480);_panels=_panels with{RightWidth=_rightWidth};ApplyPanels();};Grid.SetColumn(rightSplit,3);_workspace.Children.Add(rightSplit);
        _right.RowDefinitions.Add(new());_right.RowDefinitions.Add(new(){Height=new GridLength(4)});_right.RowDefinitions.Add(new());
        _propertyPane=new("Perspective Property Editor",_inspector,"property-pane",()=>SetPanel("properties",false));Grid.SetRow(_propertyPane,2);_right.Children.Add(_propertyPane);
        _rightVertical=new(){Vertical=true};_rightVertical.DragDelta+=delta=>{_panels=_panels with{PaletteFraction=Math.Clamp((_right.RowDefinitions[0].ActualHeight+delta)/Math.Max(1,_right.ActualHeight-4),.15,.85)};ApplyPanels();};Grid.SetRow(_rightVertical,1);_right.Children.Add(_rightVertical);
        _componentPane=new("Perspective Components",_palette,"components-pane",()=>SetPanel("components",false),()=>{_panels=_panels with{ComponentsPinned=!_panels.ComponentsPinned};_paletteOpen=false;ApplyPanels();});_right.Children.Add(_componentPane);Grid.SetColumn(_right,4);_workspace.Children.Add(_right);
        _componentRail=ControlTheme.Button("Components",()=>{_paletteOpen=!_paletteOpen;UpdateResponsiveLayout();},"components-rail");_componentRail.Width=22;_componentRail.Height=134;_componentRail.MinWidth=0;_componentRail.Padding=new Thickness(0);_componentRail.VerticalAlignment=VerticalAlignment.Top;
        var rail=new Canvas{Width=20,Height=130};var railIcon=new DesignerIcon("tiles",14);Canvas.SetLeft(railIcon,3);Canvas.SetTop(railIcon,5);rail.Children.Add(railIcon);
        var railText=ControlTheme.Text("Components",11);railText.Width=95;railText.Height=16;railText.RenderTransform=new RotateTransform{Angle=90};Canvas.SetLeft(railText,18);Canvas.SetTop(railText,26);rail.Children.Add(railText);_componentRail.Content=rail;AutomationProperties.SetName(_componentRail,"Perspective Components");ToolTipService.SetToolTip(_componentRail,"Show or hide component palette");Grid.SetColumn(_componentRail,5);_workspace.Children.Add(_componentRail);_commands["components-rail"]=_componentRail;
        Grid.SetRow(_workspace,2);root.Children.Add(_workspace);
        var status=new Grid {Background=ControlTheme.Brush("#E5E7E9"),BorderBrush=ControlTheme.Brush("#B9C0C5"),BorderThickness=new Thickness(0,1,0,0)};status.ColumnDefinitions.Add(new(){Width=GridLength.Auto});status.ColumnDefinitions.Add(new());status.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var chooser=new DesignerToolButton("panels","Panel Chooser",()=>{},"panel-chooser"){Height=22};var chooserMenu=new MenuFlyout();AddPanelItems(chooserMenu.Items);var reset=new MenuFlyoutItem{Text="Reset Panels"};reset.Click+=(_,_)=>ResetPanels();chooserMenu.Items.Add(new MenuFlyoutSeparator());chooserMenu.Items.Add(reset);chooser.Flyout=chooserMenu;status.Children.Add(chooser);_commands["panel-chooser"]=chooser;
        _status.Margin=new Thickness(6,0,5,0);Grid.SetColumn(_status,1);status.Children.Add(_status);_metrics.Margin=new Thickness(5,0,8,0);Grid.SetColumn(_metrics,2);status.Children.Add(_metrics);Grid.SetRow(status,3);root.Children.Add(status);Content=root;
        _commands["project-filter"]=_explorerSearch;_commands["project-pane-close"]=_projectPane.CloseButton!;_commands["tags-pane-close"]=_tagPane.CloseButton!;_commands["property-pane-close"]=_propertyPane.CloseButton!;_commands["components-pane-close"]=_componentPane.CloseButton!;_commands["components-pane-pin"]=_componentPane.PinButton!;
        _commands["left-splitter"]=leftSplit;_commands["right-splitter"]=rightSplit;
        _explorerSearch.TextChanged+=(_,_)=>{if(_document is not null)BuildExplorer();};
        _palette.DisplayModeChanged+=tiles=>{_panels=_panels with{PaletteTiles=tiles};_panelSavePending=true;};
        _surface.PointerPressed+=(_,_)=>HidePalettePeek();_explorer.PointerPressed+=(_,_)=>HidePalettePeek();
        _viewTabs.OpenRequested+=path=>OpenView(path);_viewTabs.CloseRequested+=path=>ChangeResources(()=>_resources.Close(path));_viewTabs.CloseOthersRequested+=path=>ChangeResources(()=>_resources.CloseOthers(path));_viewTabs.CloseAllRequested+=()=>ChangeResources(_resources.CloseAll);
        UpdateResponsiveLayout();
    }
    private bool PanelVisible(string key)=>key switch {"project"=>_panels.ProjectBrowser,"tags"=>_panels.TagBrowser,"properties"=>_panels.PropertyEditor,_=>_panels.Components};
    private void AddPanelItems(IList<MenuFlyoutItemBase> items)
    {
        foreach(var (key,label) in new[]{("project","Project Browser"),("tags","Tag Browser"),("properties","Perspective Property Editor"),("components","Perspective Components")})
        {
            var item=new ToggleMenuFlyoutItem {Text=label,IsChecked=PanelVisible(key)};item.Click+=(_,_)=>SetPanel(key,item.IsChecked);items.Add(item);
            if(!_panelMenus.TryGetValue(key,out var list))_panelMenus[key]=list=[];list.Add(item);
        }
    }
    private void SetPanel(string key,bool visible)
    {
        _panels=key switch {"project"=>_panels with{ProjectBrowser=visible},"tags"=>_panels with{TagBrowser=visible},"properties"=>_panels with{PropertyEditor=visible},_=>_panels with{Components=visible}};
        if(key=="components")_paletteOpen=visible&&!_panels.ComponentsPinned;
        ApplyPanels();
    }
    private void ResetPanels() { _panels=new();_leftWidth=_panels.LeftWidth;_rightWidth=_panels.RightWidth;_paletteOpen=false;_palette.Tiles=false;ApplyPanels();SetStatus("Panel layout reset"); }
    private void HidePalettePeek() { if(!_paletteOpen||_panels.ComponentsPinned)return;_paletteOpen=false;UpdateResponsiveLayout(); }
    private void ApplyPanels() { _panelSavePending=true;UpdateResponsiveLayout(); }
    private void FlushPanelPreferences()
    {
        if(!_panelSavePending)return;_panelSavePending=false;
        try { BrowserServices.SaveWorkspace(_panels.Serialize()); }
        catch(Exception ex) when(ex is FormatException or IOException or InvalidOperationException) { SetStatus("Panel settings were not saved: "+ex.Message); }
    }
    private void ArrangeDesignerPanels()
    {
        if(_componentPane is null)return;
        var showLeft=!_sessionMode&&ActualWidth>=1050&&(_panels.ProjectBrowser||_panels.TagBrowser);
        var palette=_panels.Components&&(_panels.ComponentsPinned||_paletteOpen);
        var showRight=!_sessionMode&&ActualWidth>=850&&(_panels.PropertyEditor||palette);
        _workspace.ColumnDefinitions[0].Width=new(showLeft?_leftWidth:0);_workspace.ColumnDefinitions[1].Width=new(showLeft?4:0);
        _workspace.ColumnDefinitions[4].Width=new(showRight?_rightWidth:0);_workspace.ColumnDefinitions[3].Width=new(showRight?4:0);
        _workspace.ColumnDefinitions[5].Width=new(!_sessionMode&&_panels.Components&&!_panels.ComponentsPinned?22:0);
        _left.Visibility=showLeft?Visibility.Visible:Visibility.Collapsed;_right.Visibility=showRight?Visibility.Visible:Visibility.Collapsed;
        _projectPane.Visibility=_panels.ProjectBrowser?Visibility.Visible:Visibility.Collapsed;_tagPane.Visibility=_panels.TagBrowser?Visibility.Visible:Visibility.Collapsed;
        var both=_panels.ProjectBrowser&&_panels.TagBrowser;
        _left.RowDefinitions[0].Height=new(_panels.ProjectBrowser?both?_panels.ProjectFraction:1:0,GridUnitType.Star);
        _left.RowDefinitions[1].Height=new(both?4:0);_left.RowDefinitions[2].Height=new(_panels.TagBrowser?both?1-_panels.ProjectFraction:1:0,GridUnitType.Star);
        _componentPane.Visibility=palette?Visibility.Visible:Visibility.Collapsed;_propertyPane.Visibility=_panels.PropertyEditor?Visibility.Visible:Visibility.Collapsed;
        var stacked=palette&&_panels.ComponentsPinned&&_panels.PropertyEditor;
        _right.RowDefinitions[0].Height=new(stacked?_panels.PaletteFraction:1,GridUnitType.Star);_right.RowDefinitions[1].Height=new(stacked?4:0);_right.RowDefinitions[2].Height=new(stacked?1-_panels.PaletteFraction:0,GridUnitType.Star);
        Grid.SetRow(_propertyPane,stacked?2:0);Grid.SetRowSpan(_propertyPane,stacked?1:3);Grid.SetRowSpan(_componentPane,stacked?1:3);
        _componentPane.VerticalAlignment=_paletteOpen&&!_panels.ComponentsPinned?VerticalAlignment.Top:VerticalAlignment.Stretch;
        _componentPane.Height=_paletteOpen&&!_panels.ComponentsPinned?Math.Min(390,Math.Max(180,ActualHeight-100)):double.NaN;
        _rightVertical.Visibility=stacked?Visibility.Visible:Visibility.Collapsed;_componentPane.SetPinned(_panels.ComponentsPinned);
        _componentRail.Visibility=!_sessionMode&&_panels.Components&&!_panels.ComponentsPinned?Visibility.Visible:Visibility.Collapsed;
        foreach(var (key,items) in _panelMenus)foreach(var item in items)item.IsChecked=PanelVisible(key);
    }
    private void ChangeResources(Action action)
    {
        if(_sessionMode)return;var old=_resources.ActiveView;action();BuildTabs();
        if(old!=_resources.ActiveView) { if(_resources.ActiveView is {} next)OpenView(next);else CloseWorkspace(); }
        PublishTelemetry();
    }
    private void CloseWorkspace()
    {
        _view="";_surface.Dispose();_surface.Visibility=Visibility.Collapsed;_emptyWorkspace.Visibility=Visibility.Visible;
        _document.Select(null);_inspector.Inspect(_document,null,_surface.Host);UpdateButtons();SetStatus("All resources closed · project changes are retained");
    }
}
