"""Fail the build unless the Uno controls NuGet archive contains its compiled library."""
from __future__ import annotations
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile

folder = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/uno-packages")
archives = sorted(folder.glob("ScadaSpace.Controls.*.nupkg"))
if len(archives) != 1:
    raise SystemExit(f"Expected one controls NuGet archive, found {len(archives)} in {folder}")
archive = archives[0]
with ZipFile(archive) as package:
    names = package.namelist()
    metadata_files = [name for name in names if name.endswith(".nuspec")]
    if len(metadata_files) != 1:
        raise SystemExit("The controls package must contain one NuGet specification")
    root = ET.fromstring(package.read(metadata_files[0]))
    for element in root.iter():
        element.tag = element.tag.rsplit("}", 1)[-1]
    metadata = root.find("metadata")
    if metadata is None or metadata.findtext("id") != "ScadaSpace.Controls":
        raise SystemExit("Unexpected controls package identity")
    assemblies = [name for name in names if name.startswith("lib/") and name.endswith("/ScadaSpace.Controls.dll")]
    if not assemblies or any(len(package.read(name)) < 1024 for name in assemblies):
        raise SystemExit("The controls archive has no compiled public library")
    dependencies = sorted({item.attrib["id"] for item in root.iter("dependency")})
    required = {"ScadaSpace.Data", "ScadaSpace.Graphics", "ScadaSpace.Runtime", "ScadaSpace.Layout", "ScadaSpace.Designer"}
    if not required.issubset(dependencies):
        raise SystemExit("Missing reusable-library package dependencies: " + str(required - set(dependencies)))
    if any(Path(name).suffix.lower() in {".ttf", ".otf", ".woff", ".woff2", ".ttc"} for name in names):
        raise SystemExit("The controls package must reference, not bundle, third-party fonts")
    report = {
        "package": metadata.findtext("id"), "version": metadata.findtext("version"),
        "sha256": hashlib.sha256(archive.read_bytes()).hexdigest(),
        "assemblies": assemblies, "dependencies": dependencies, "verified": True
    }
(folder / "manifest.json").write_text(json.dumps(report, indent=2))
print("PASS controls archive identity, compiled library, dependency closure declarations and asset boundaries")
print(json.dumps(report, indent=2))
