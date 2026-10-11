using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Controls;
using ScadaSpace.Core;

namespace ScadaSpace.App;

public sealed partial class WorkbenchPage
{
    private readonly Dictionary<string,TreeViewNode> _explorerNodes=new(StringComparer.Ordinal);
    private Dictionary<string,bool>? _savedExpansion;
    private long _explorerNodesCreated;
    private IReadOnlyList<TagDefinition> _tagDefinitions=Array.Empty<TagDefinition>();
    private int _componentCount;
    private void BuildExplorer()
    {
        var used=new HashSet<string>(StringComparer.Ordinal);var query=_explorerSearch.Text.Trim();
        if(query.Length>0&&_savedExpansion is null)_savedExpansion=_explorerNodes.ToDictionary(p=>p.Key,p=>p.Value.IsExpanded,StringComparer.Ordinal);
        if(query.Length==0&&_savedExpansion is not null)
        {foreach(var (key,expanded) in _savedExpansion)if(_explorerNodes.TryGetValue(key,out var n))n.IsExpanded=expanded;_savedExpansion=null;}
        TreeViewNode Get(string key,string title,string? view=null,string? id=null,bool expanded=false)
        {
            used.Add(key);
            if(!_explorerNodes.TryGetValue(key,out var node)) {node=new(){IsExpanded=expanded};_explorerNodes.Add(key,node);_explorerNodesCreated++;}
            var content=new ExplorerItem(title,view,id);if(!Equals(node.Content,content))node.Content=content;
            return node;
        }
        static void Sync(IList<TreeViewNode> target,IReadOnlyList<TreeViewNode> source)
        {
            for(var i=0;i<source.Count;i++)
            {if(i<target.Count&&ReferenceEquals(target[i],source[i]))continue;target.Remove(source[i]);source[i].Parent?.Children.Remove(source[i]);target.Insert(i,source[i]);}
            while(target.Count>source.Count)target.RemoveAt(target.Count-1);
        }
        bool Match(string text)=>query.Length==0||text.Contains(query,StringComparison.OrdinalIgnoreCase);
        TreeViewNode? Component(ComponentNode c,string path,int depth,bool parentMatches)
        {
            var self=parentMatches||Match(c.Name)||Match(c.Type);
            var children=c.Children.Select(child=>Component(child,path,depth+1,self)).OfType<TreeViewNode>().ToArray();
            var node=Get("c:"+c.Id,c.Name+(c.Bindings.Count>0?"  [binding]":""),path,c.Id,depth==0&&path==_view);
            Sync(node.Children,children);if(!self&&children.Length==0)return null;if(query.Length>0)node.IsExpanded=true;return node;
        }
        var viewNodes=new List<TreeViewNode>();
        foreach(var view in _document.Project.Views)
        {
            var child=Component(view.Root,view.Path,0,Match(view.Path));
            var node=Get("v:"+view.Path,view.Path,view.Path,expanded:view.Path==_view);
            Sync(node.Children,child is null?Array.Empty<TreeViewNode>():new[]{child});
            if(Match(view.Path)||child is not null) {viewNodes.Add(node);if(query.Length>0)node.IsExpanded=true;}
        }
        var project=Get("project",_document.Project.Name,expanded:true);var perspective=Get("perspective","Perspective",expanded:true);var views=Get("views","Views",expanded:true);Sync(views.Children,viewNodes);
        var pages=Get("pages","Page Configuration");
        Sync(pages.Children,_document.Project.Pages.Select(p=>Get("page:"+p.Path,p.Path+" → "+p.View,p.View)).Where(n=>Match(((ExplorerItem)n.Content).Name)).ToArray());
        var styles=Get("styles","Styles");Sync(styles.Children,_document.Project.Styles.Keys.Select(name=>Get("style:"+name,name)).Where(n=>Match(((ExplorerItem)n.Content).Name)).ToArray());
        Sync(perspective.Children,query.Length==0?new[]{views,pages,styles}:new[]{views});Sync(project.Children,new[]{perspective});
        if(query.Length>0){project.IsExpanded=true;perspective.IsExpanded=true;views.IsExpanded=true;}
        Sync(_explorer.RootNodes,new[]{project});
        foreach(var key in _explorerNodes.Keys.Where(k=>!used.Contains(k)).ToArray())_explorerNodes.Remove(key);
    }
    private void RevealExplorerView(string path)
    {
        if(_explorerNodes.TryGetValue("v:"+path,out var view))view.IsExpanded=true;
        var root=_document.Project.Views.FirstOrDefault(v=>v.Path==path)?.Root;
        if(root is not null&&_explorerNodes.TryGetValue("c:"+root.Id,out var node))node.IsExpanded=true;
    }
    private bool TextInputFocused(DependencyObject? source)
    {
        for(var i=0;source is not null&&i<64;i++,source=VisualTreeHelper.GetParent(source))
            if(source is TextBox or NumberBox or HmiNumericEntry or PasswordBox or ComboBox)return true;
        return XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is TextBox or NumberBox or HmiNumericEntry or PasswordBox or ComboBox;
    }
}
