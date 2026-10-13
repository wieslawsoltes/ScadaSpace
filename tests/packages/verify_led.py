"""Independent consumer of the packed Core, Designer and Runtime LED APIs."""
from __future__ import annotations
import json
import subprocess
import tempfile
from pathlib import Path

feed=Path('artifacts/packages').resolve()
out=Path('TestResults/packages-led');out.mkdir(parents=True,exist_ok=True)
project='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><PackageReference Include="ScadaSpace.Runtime" Version="0.1.0"/><PackageReference Include="ScadaSpace.Designer" Version="0.1.0"/></ItemGroup></Project>'''
source='''using ScadaSpace.Core;
void Check(bool value,string name) { if(!value)throw new Exception(name); Console.WriteLine("PASS "+name); }
var options=new LedDisplayOptions(Locale:"de-DE");
var snapshot=LedDisplayPresentation.Create(Scalar.From(12345.67),options);
Check(snapshot.Text=="12.345,67","packaged display formatting uses selected locale");
Check(LedGlyphs.Get('W',true).Segments!=0,"packaged fourteen-segment alphabet has actual glyph definitions");
var cache=new LedDisplayPresentationCache();var input=Scalar.From(12.34);Func<string,Scalar> read=key=>key=="value"?input:Scalar.Null;
var original=cache.Read(read);Check(ReferenceEquals(original,cache.Read(read)),"packaged LED presentation snapshots are reused");
input=Scalar.From(25);Check(cache.Read(read).Text=="25.0"&&original.Text=="12.3","packaged presentation updates do not mutate older snapshots");
Check(snapshot.WithNumericValue(100).Text=="100,00","packaged numeric property updates reformat the snapshot");
var component=ComponentCatalog.Create("LEDDisplay");Check(component.Text("segmentFormat")=="14 segment","packaged catalog exposes the new display default");
'''
logs=[]
try:
    with tempfile.TemporaryDirectory(prefix='scadaspace-led-consumer-') as directory:
        root=Path(directory);(root/'Consumer.csproj').write_text(project);(root/'Program.cs').write_text(source)
        for command in [['dotnet','restore','Consumer.csproj','--source',str(feed),'-p:NuGetAudit=false'],['dotnet','run','--project','Consumer.csproj','-c','Release','--no-restore']]:
            run=subprocess.run(command,cwd=root,text=True,capture_output=True,timeout=120)
            logs.append(run.stdout+run.stderr);print(logs[-1],end='',flush=True)
            if run.returncode:raise RuntimeError('LED package consumer failed')
        count=sum(line.startswith('PASS ') for line in logs[-1].splitlines())
        if count!=6:raise RuntimeError('Expected six executed LED package checks')
        assets=json.loads((root/'obj/project.assets.json').read_text())
        if any(value['type']=='project' for value in assets['libraries'].values()):raise RuntimeError('Unexpected project reference')
        (out/'results.json').write_text(json.dumps({'tests':count,'failures':0,'libraries':sorted(assets['libraries'])},indent=2))
except Exception as error:
    (out/'results.json').write_text(json.dumps({'failures':1,'error':str(error)},indent=2));raise
finally:
    (out/'consumer.log').write_text('\n'.join(logs))
