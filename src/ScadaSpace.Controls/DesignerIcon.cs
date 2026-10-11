using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace ScadaSpace.Controls;

/// <summary>Independent small vector glyphs. No icon fonts, proprietary artwork or raster scaling.</summary>
public sealed partial class DesignerIcon : Viewbox
{
    public DesignerIcon(string name, double size = 16)
    {
        Width = Height = size; IsHitTestVisible = false;
        var canvas = new Canvas { Width = 16, Height = 16 };
        var ink = ControlTheme.Brush("#536572");
        void Line(double x1,double y1,double x2,double y2) => canvas.Children.Add(new Line { X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=ink,StrokeThickness=1.3 });
        void Box(double x,double y,double w,double h,bool fill=false)
        {
            var box=new Rectangle { Width=w,Height=h,Stroke=ink,StrokeThickness=1.2,Fill=fill?ink:null };
            Canvas.SetLeft(box,x);Canvas.SetTop(box,y);canvas.Children.Add(box);
        }
        void Circle(double x,double y,double diameter)
        {
            var circle=new Ellipse { Width=diameter,Height=diameter,Stroke=ink,StrokeThickness=1.3 };
            Canvas.SetLeft(circle,x);Canvas.SetTop(circle,y);canvas.Children.Add(circle);
        }
        void Arrow(bool left)
        {
            Line(3,8,13,8); Line(left?3:13,8,left?7:9,4);Line(left?3:13,8,left?7:9,12);
        }
        switch(name)
        {
            case "save": Box(2,2,12,12);Box(5,2,6,4);Box(5,9,6,5);break;
            case "undo": Arrow(true);Line(13,8,13,13);break;
            case "redo": Arrow(false);Line(3,8,3,13);break;
            case "copy": Box(2,2,8,9);Box(6,5,8,9);break;
            case "paste": Box(4,3,9,11);Box(6,1,5,4);Line(6,8,11,8);Line(6,11,11,11);break;
            case "delete":case "close": Line(4,4,12,12);Line(12,4,4,12);break;
            case "cut":Circle(1,9,5);Circle(9,9,5);Line(3,2,11,11);Line(13,2,5,11);break;
            case "preview":case "play":
                canvas.Children.Add(new Polygon { Points=new PointCollection { new(4,2),new(13,8),new(4,14) },Fill=ink });break;
            case "design":Box(3,3,10,10,true);break;
            case "simulation":Line(5,3,5,13);Line(10,3,10,13);break;
            case "zoom-in":case "zoom-out":Circle(1,1,10);Line(10,10,15,15);Line(3,6,9,6);if(name=="zoom-in")Line(6,3,6,9);break;
            case "fit":Box(1,1,14,14);Line(4,7,4,4);Line(4,4,7,4);Line(12,9,12,12);Line(12,12,9,12);break;
            case "front":Box(1,1,8,8);Box(6,6,9,9,true);break;
            case "back":Box(1,1,8,8,true);Box(6,6,9,9);break;
            case "align-left":Line(2,1,2,15);Box(5,3,9,3,true);Box(5,10,6,3,true);break;
            case "align-top":Line(1,2,15,2);Box(3,5,3,9,true);Box(10,5,3,6,true);break;
            case "import":case "export":Box(2,2,8,12);Line(6,8,15,8);Line(name=="import"?6:15,8,name=="import"?9:12,5);Line(name=="import"?6:15,8,name=="import"?9:12,11);break;
            case "pin":Box(5,2,6,5);Line(4,8,12,8);Line(8,8,8,15);break;
            case "unpin":Line(3,3,13,13);Line(5,10,10,5);Line(4,12,2,14);break;
            case "expand":Line(3,6,8,11);Line(8,11,13,6);break;
            case "collapse":Line(3,10,8,5);Line(8,5,13,10);break;
            case "tiles":case "panels":for(var x=2;x<=10;x+=8)for(var y=2;y<=10;y+=8)Box(x,y,4,4,true);break;
            case "list":for(var y=3;y<=13;y+=5){Box(1,y,2,2,true);Line(6,y+1,15,y+1);}break;
            case "Chart":Line(2,1,2,14);Line(2,14,15,14);Line(3,11,6,5);Line(6,5,10,9);Line(10,9,14,3);break;
            case "Input":Box(1,4,14,9);Line(4,7,10,7);Line(4,10,8,10);break;
            case "Symbols":Circle(2,2,12);Line(8,3,8,13);Line(3,8,13,8);break;
            case "Alarming":
                canvas.Children.Add(new Polygon { Points=new PointCollection {new(8,1),new(15,14),new(1,14)},Stroke=ink,StrokeThickness=1.2 });Line(8,5,8,9);Box(7.5,11,1,1,true);break;
            case "Drawing":Line(2,12,12,2);Line(4,14,14,4);Line(2,12,2,14);Line(2,14,4,14);break;
            case "session":Box(1,2,14,12);Line(1,5,15,5);Line(6,8,12,8);Line(12,8,10,6);Line(12,8,10,10);break;
            case "folder":Box(1,4,14,10);Box(2,2,5,2);break;
            default:Box(2,2,12,12);Line(2,6,14,6);Line(6,6,6,14);break;
        }
        Child=canvas;
    }
}

public sealed partial class DesignerToolButton : Button
{
    private readonly StackPanel _content = new() { Orientation=Orientation.Horizontal,Spacing=5 };
    private readonly TextBlock _label = ControlTheme.Text("",11);
    private string _glyph = "";
    public DesignerToolButton(string glyph,string label,Action action,string? id=null,bool showLabel=false)
    {
        Height=26;MinHeight=0;MinWidth=26;Padding=new Thickness(5,2,5,2);CornerRadius=new CornerRadius(0);
        Background=ControlTheme.Brush("#F2F3F4");BorderThickness=new Thickness(0);Content=_content;
        SetGlyph(glyph);_label.Visibility=showLabel?Visibility.Visible:Visibility.Collapsed;_content.Children.Add(_label);
        SetLabel(label);if(id is not null)AutomationProperties.SetAutomationId(this,id);
        Click+=(_,_)=>action();
    }
    public void SetGlyph(string glyph)
    {
        if(_glyph==glyph)return;_glyph=glyph;
        if(_content.Children.FirstOrDefault() is DesignerIcon)_content.Children.RemoveAt(0);
        _content.Children.Insert(0,new DesignerIcon(glyph));
    }
    public void SetLabel(string label) { _label.Text=label;AutomationProperties.SetName(this,label);ToolTipService.SetToolTip(this,label); }
}
