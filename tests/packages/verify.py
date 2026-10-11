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
<ItemGroup><PackageReference Include="ScadaSpace.Runtime" Version="{version}" /><PackageReference Include="ScadaSpace.Data" Version="{version}" /><PackageReference Include="ScadaSpace.Designer" Version="{version}" /></ItemGroup>
</Project>'''
source = '''using ScadaSpace.Core;
using ScadaSpace.Data;
using ScadaSpace.Designer;
using ScadaSpace.Runtime;
using ScadaSpace.History;

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
var tree = new DocumentTree(TagTreeBuilder.Build(runtime.Tags.Browse()));
Check(tree.Items.Any(n => n.Data.Text == path), "packaged tag hierarchy retains full provider paths");
var state = new DocumentTreeState(tree);
var pump = tree.Items.First(n => n.Data.Text == path);
state.Reveal(pump.Path); state.Select([pump.Path]);
Check(state.Visible().Any(r => r.Item.Key == pump.Key) && state.SelectionData.Items[0].Fields["value"].Text == path, "packaged tree reveals selected context without Uno");
var levelPath = "[default]Plant/Tank01/Level";
var queryRequest = new HistoryQueryRequest(levelPath,DateTimeOffset.MinValue,DateTimeOffset.MaxValue);
var historyResult = runtime.HistoryQueries.Get(queryRequest);
Check(historyResult.Data.Items.Count == 1 && ReferenceEquals(historyResult,runtime.HistoryQueries.Get(queryRequest)), "packaged historian returns shared immutable query snapshots");
view.View.Root.Bindings["historyData"] = new BindingDefinition { Kind = BindingKind.TagHistory, Source = """{"path":"[default]Plant/Tank01/Level","start":0,"end":4102444800000}""" };
view.Evaluate();
Check(view.Read(view.View.Root,"historyData").Value.Items.Count == 1 && !view.CanWrite(view.View.Root,"historyData"), "packaged runtime executes read-only TagHistory bindings");
var document = new DesignerDocument(DemoProject.Create());
var component = document.Project.Views[0].Root;
var draft = new EventConfigurationDraft(document,component.Id);
draft.Add("onClick",new ActionConfiguration(ActionKind.WriteTag,"[default]Plant/Tank01/Setpoint",Scalar.From(45),Scalar.Object([])));
draft.Apply(document);
Check(document.IsDirty && component.Events["onClick"].Count == 1, "packaged event draft commits a detached component edit");
var configurations = new ConfigurationIndex(); configurations.Refresh(document);
var actionRows = configurations.Query("onClick",ConfigurationKind.Action);
Check(actionRows.Count == 1 && ReferenceEquals(actionRows,configurations.Query("onClick",ConfigurationKind.Action)), "packaged configuration index reuses owned query snapshots");

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
        if count != 10:
            raise RuntimeError(f"Expected ten executed package checks; got {count}")
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
