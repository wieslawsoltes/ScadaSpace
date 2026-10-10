"""Restore and run a standalone consumer exclusively from the packed engine feed."""
from __future__ import annotations
import json
import subprocess
import tempfile
from pathlib import Path

feed = Path("artifacts/packages").resolve()
out = Path("TestResults/packages")
out.mkdir(parents=True, exist_ok=True)
version = "0.1.0"
project = f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
<ItemGroup><PackageReference Include="ScadaSpace.Runtime" Version="{version}" /><PackageReference Include="ScadaSpace.Data" Version="{version}" /></ItemGroup>
</Project>'''
source = '''using ScadaSpace.Core;
using ScadaSpace.Data;
using ScadaSpace.Runtime;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
var original = Scalar.FromJson("[{\\"id\\":\\"a\\",\\"n\\":100},{\\"id\\":\\"b\\",\\"n\\":2}]");
var table = new DocumentTable(original, keyField: "id");
var rows = table.Query(sorting: [new("n")]);
Check(rows[0].SourceIndex == 1, "packaged table uses typed sorting and stable identity");
var changed = table.WithCell(rows[0].Key, "n", Scalar.From(9));
Check(changed.Items[1].Fields["n"].Number == 9 && original.Items[1].Fields["n"].Number == 2, "packaged document edits do not alias source data");
using var runtime = new ProjectRuntime(DemoProject.Create());
var view = runtime.CreateView("Overview");
view.Evaluate();
Check(view.Diagnostics.Count == 0, "packaged runtime evaluates the sample without an application dependency");
var path = "[default]Plant/Pump/Enabled";
var before = runtime.Tags.Read(path);
var write = runtime.Tags.Write(path, Scalar.From(false), "package-consumer", before.Revision);
Check(write.Success && !runtime.Tags.Read(path).Value.Boolean, "packaged provider accepts revision-checked writes");
'''
logs = []
try:
    with tempfile.TemporaryDirectory(prefix="scadaspace-package-consumer-") as temporary:
        root = Path(temporary)
        (root / "Consumer.csproj").write_text(project)
        (root / "Program.cs").write_text(source)
        for command in [
            ["dotnet", "restore", "Consumer.csproj", "--source", str(feed), "-p:NuGetAudit=false"],
            ["dotnet", "run", "--project", "Consumer.csproj", "-c", "Release", "--no-restore"],
        ]:
            run = subprocess.run(command, cwd=root, text=True, capture_output=True, timeout=120)
            text = run.stdout + run.stderr
            logs.append(text)
            print(text, end="", flush=True)
            if run.returncode:
                raise RuntimeError(f"Standalone package consumer failed ({run.returncode})")
        count = sum(line.startswith("PASS ") for line in logs[-1].splitlines())
        if count != 4:
            raise RuntimeError(f"Expected four executed package checks; got {count}")
        assets = json.loads((root / "obj/project.assets.json").read_text())
        libraries = assets["libraries"]
        if any(item["type"] == "project" for item in libraries.values()):
            raise RuntimeError("Consumer unexpectedly restored a project reference")
        (out / "results.json").write_text(json.dumps({"tests": count, "failures": 0, "libraries": sorted(libraries)}, indent=2))
except Exception as error:
    (out / "results.json").write_text(json.dumps({"failures": 1, "error": str(error)}, indent=2))
    raise
finally:
    (out / "consumer.log").write_text("\n".join(logs))
