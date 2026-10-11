using System.Runtime.CompilerServices;
using System.Text.Json;
using ScadaSpace.Core;
using ScadaSpace.History;
using ScadaSpace.Runtime;
using ScadaSpace.Tags;
using static Suite;

internal static class HistoryBindingTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => T; }
    private static string Query(object? start = null, object? end = null, string aggregate = "Raw", object? limit = null) => JsonSerializer.Serialize(new { path="[default]A",start=start??0,end=end??10000,aggregate,returnSize=1,limit=limit??1000 });
    private static ProjectRuntime Make(string? source = null)
    {
        var root = new ComponentNode { Type="Container" }; root.Props["data"]=Scalar.Array([]); root.Custom["start"]=Scalar.From(0); root.Custom["end"]=Scalar.From(10000);
        root.Bindings["data"]=new() {Kind=BindingKind.TagHistory,Source=source??Query()};
        var project = new ProjectDefinition { Views=[new() {Path="History",Root=root}], Tags=[new() {Path="[default]A"}], Pages=[new() {Path="/",View="History"}] };
        var runtime = new ProjectRuntime(project, new Clock());
        for(var i=1;i<=3;i++) runtime.History.Append("[default]A",new(Scalar.From(i*10),Quality.Good,T.AddSeconds(i),i+1));
        return runtime;
    }
    [ModuleInitializer]
    internal static void Register()
    {
        Add("tag history bindings supply typed tall rows and reject writeback", () => {
            using var r=Make(); var view=r.CreateView("History"); var root=view.View.Root; view.Evaluate();
            var result=view.Read(root,"data"); Assert(result.IsGood); Equal(4,result.Value.Items.Count); Equal(30d,result.Value.Items[3].Fields["value"].Number);
            Assert(!view.CanWrite(root,"data")); Equal(WriteStatus.ReadOnly,view.Write(root,"data",Scalar.Array([]),"operator").Status);
        });
        Add("history time expressions react to local property input", () => {
            using var r=Make(Query("{this.custom.start}","{this.custom.end}")); var view=r.CreateView("History"); view.Evaluate();
            Assert(view.Write(view.View.Root,"custom.start",Scalar.From(1500),"operator").Success); view.Evaluate();
            Equal(2,view.Read(view.View.Root,"data").Value.Items.Count); Equal(2000d,view.Read(view.View.Root,"data").Value.Items[0].Fields["timestamp"].Number);
            Equal(0d,view.View.Root.Custom["start"].Number);
        });
        Add("equivalent view queries share cached results without sharing local state", () => {
            using var r=Make(); var a=r.CreateView("History"); var b=r.CreateView("History"); a.Evaluate(); b.Evaluate();
            Assert(ReferenceEquals(a.Read(a.View.Root,"data").Value.Structured,b.Read(b.View.Root,"data").Value.Structured)); Equal(1L,r.HistoryQueries.Reads);
            for(var i=0;i<20;i++) { a.Evaluate(); b.Evaluate(); }
            Equal(1L,r.HistoryQueries.Reads); Assert(r.HistoryQueries.Hits>=41);
        });
        Add("history query errors recover after parameters are corrected", () => {
            using var r=Make(Query("{this.custom.start}","{this.custom.end}")); var v=r.CreateView("History"); v.Evaluate();
            v.Write(v.View.Root,"custom.start",Scalar.From(20000),"operator"); v.Evaluate(); Equal(Quality.BadExpression,v.Read(v.View.Root,"data").Quality);
            v.Write(v.View.Root,"custom.start",Scalar.From(0),"operator"); v.Evaluate(); Assert(v.Read(v.View.Root,"data").IsGood); Equal(0,v.Diagnostics.Count);
        });
        Add("history raw row limits diagnose without silently truncating", () => {
            using var r=Make(Query(limit:2)); var v=r.CreateView("History"); v.Evaluate();
            Assert(!v.Read(v.View.Root,"data").IsGood); Assert(v.Diagnostics.Any(d=>d.Message.Contains("sample limit")));
            v.View.Root.Bindings["data"].Source=Query(limit:10); v.Evaluate(); Equal(4,v.Read(v.View.Root,"data").Value.Items.Count);
        });
        Add("history sample average is explicit rather than pretending time-weighted compatibility", () => {
            using var r=Make(Query(aggregate:"SampleAverage")); var v=r.CreateView("History"); v.Evaluate(); Equal(15d,v.Read(v.View.Root,"data").Value.Items[0].Fields["value"].Number);
            v.View.Root.Bindings["data"].Source=Query(aggregate:"Average"); v.Evaluate(); Assert(v.Diagnostics.Any(d=>d.Message.Contains("time-weighted")));
        });
        Add("bad stored quality stays visible in query rows without fabricating good measurements", () => {
            using var r=Make(); r.History.Append("[default]A",new(Scalar.From(70),Quality.Stale,T.AddSeconds(4),5)); var v=r.CreateView("History"); v.Evaluate();
            var result=v.Read(v.View.Root,"data"); Assert(result.IsGood); Equal("Stale",result.Value.Items[^1].Fields["quality"].Text);
        });
        Add("rolling query windows follow latest retained time deterministically", () => {
            using var r=Make("{\"path\":\"[default]A\",\"windowSeconds\":1}"); var v=r.CreateView("History"); v.Evaluate(); Equal(2,v.Read(v.View.Root,"data").Value.Items.Count);
            r.History.Append("[default]A",new(Scalar.From(50),Quality.Good,T.AddSeconds(5),5)); v.Evaluate(); Equal(1,v.Read(v.View.Root,"data").Value.Items.Count);
        });
        Add("history ISO date bounds and expressions share the same cache request", () => {
            using var r=Make(Query(T.ToString("O"),T.AddSeconds(10).ToString("O"))); var v=r.CreateView("History"); v.Evaluate(); Equal(4,v.Read(v.View.Root,"data").Value.Items.Count);
            v.View.Root.Bindings["data"].Source=Query(); v.Evaluate(); Equal(1L,r.HistoryQueries.Reads);
        });
        Add("history malformed unknown and conflicting options are explicitly rejected", () => {
            using var r=Make(); var v=r.CreateView("History");
            foreach(var source in new[] {"[]","{\"unknown\":1}","{\"path\":\"[default]Missing\"}","{\"path\":\"[default]A\",\"start\":0,\"windowSeconds\":1}",Query(end:1.5),Query(limit:2000),"{\"path\":\"[default]A\",\"path\":\"[default]A\"}"})
            { v.View.Root.Bindings["data"].Source=source; v.Evaluate(); Assert(!v.Read(v.View.Root,"data").IsGood,source); }
        });
        Add("history binding configuration round-trips and rejects bidirectional definitions", () => {
            using var r=Make(); var copy=ProjectCodec.Clone(r.Project); Equal(BindingKind.TagHistory,copy.Views[0].Root.Bindings["data"].Kind);
            copy.Views[0].Root.Bindings["data"].Bidirectional=true; Assert(ProjectCodec.Validate(copy).Count>0);
        });
        Add("unattached views diagnose and can attach a matching shared history cache", () => {
            using var r=Make(); var v=new ViewInstance(r.Project.Views[0],r.Tags); v.Evaluate(); Assert(v.Diagnostics.Any(d=>d.Message.Contains("historian")));
            v.WithHistory(r.History,r.HistoryQueries); v.Evaluate(); Assert(v.Read(v.View.Root,"data").IsGood);
            using var other=Make(); Throws<ArgumentException>(() => v.WithHistory(other.History,r.HistoryQueries));
        });
    }
}
