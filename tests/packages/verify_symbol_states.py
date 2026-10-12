"""Independent consumer of the packed Core, Designer and Runtime symbol-state APIs."""
from __future__ import annotations
import json
import subprocess
import tempfile
from pathlib import Path

feed=Path('artifacts/packages').resolve()
out=Path('TestResults/packages-symbol-states');out.mkdir(parents=True,exist_ok=True)
project='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><PackageReference Include="ScadaSpace.Runtime" Version="0.1.0"/><PackageReference Include="ScadaSpace.Designer" Version="0.1.0"/></ItemGroup></Project>'''
source='''using ScadaSpace.Core;
using ScadaSpace.Designer;
using ScadaSpace.Runtime;
void Check(bool value,string name) { if(!value)throw new Exception(name); Console.WriteLine("PASS "+name); }
var state=new SymbolStateDefinition(new(PrimaryColor:"#4488BB",EnableAnimation:true),new(PrimaryColor:"#112244",EnableFlashing:true),new[]{"Motor"});
var catalog=SymbolStateCatalog.Empty.With("service",state);
var project=new ProjectDefinition{SymbolStates=catalog,Views=[new(){Path="Main",Root=new(){Id="motor",Type="Motor",Props=new(){["state"]=Scalar.From("service")}}}]};
var restored=ProjectCodec.Clone(project);
Check(restored.SymbolStates.Equivalent(catalog),"packaged project state themes round-trip");
var document=new DesignerDocument(restored);var draft=new SymbolStateDraft(document);draft.Rename("service","maintenance");draft.Apply(document);
Check(document.Find("motor")!.Text("state")=="maintenance","packaged state rename retains literal references");
document.Undo();Check(document.Find("motor")!.Text("state")=="service","packaged state rename undo restores the original definition");
Func<string,Scalar> read=key=>key=="state"?Scalar.From("service"):Scalar.Null;
var cache=new SymbolPresentationCache();var settings=new SymbolDefaults(Theme:"dark");var presentation=cache.Read("Motor",read,settings,states:catalog);
Check(presentation.StateStyle!.PrimaryColor=="#112244"&&presentation.IsFlashing&&!presentation.IsAnimated,"packaged presentation resolves independent dark-state effects");
Check(ReferenceEquals(presentation,cache.Read("Motor",read,settings,states:catalog))&&cache.BuildCount==1,"packaged state presentation retains its immutable cached result");
using var runtime=new ProjectRuntime(restored);var session=new RuntimeSession(runtime){Symbols=new(Theme:"dark",ReducedMotion:true)};
Check(!cache.Read("Motor",read,session.Symbols,states:catalog).NeedsAnimation,"packaged reduced-motion settings disable both state effects");
'''
logs=[]
try:
    with tempfile.TemporaryDirectory(prefix='scadaspace-symbol-state-consumer-') as directory:
        root=Path(directory);(root/'Consumer.csproj').write_text(project);(root/'Program.cs').write_text(source)
        for command in [['dotnet','restore','Consumer.csproj','--source',str(feed),'-p:NuGetAudit=false'],['dotnet','run','--project','Consumer.csproj','-c','Release','--no-restore']]:
            run=subprocess.run(command,cwd=root,text=True,capture_output=True,timeout=120)
            logs.append(run.stdout+run.stderr);print(logs[-1],end='',flush=True)
            if run.returncode:raise RuntimeError('Symbol state package consumer failed')
        count=sum(line.startswith('PASS ') for line in logs[-1].splitlines())
        if count!=6:raise RuntimeError('Expected six executed symbol state package checks')
        assets=json.loads((root/'obj/project.assets.json').read_text())
        if any(value['type']=='project' for value in assets['libraries'].values()):raise RuntimeError('Unexpected project reference')
        (out/'results.json').write_text(json.dumps({'tests':count,'failures':0,'libraries':sorted(assets['libraries'])},indent=2))
except Exception as error:
    (out/'results.json').write_text(json.dumps({'failures':1,'error':str(error)},indent=2));raise
finally:
    (out/'consumer.log').write_text('\n'.join(logs))
