import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
manifest = json.loads((root / "moonflat-release.json").read_text(encoding="utf-8"))

def properties(project_file: Path) -> dict[str, str]:
    tree = ET.parse(project_file)
    return {
        child.tag: (child.text or "").strip()
        for group in tree.getroot().findall("PropertyGroup")
        for child in list(group)
        if child.text and child.text.strip()
    }

library = properties(root / "src/MoonSharp.Interpreter/MoonFlat.csproj")
smoke = properties(root / "tests/MoonFlat.PackageSmoke/MoonFlat.PackageSmoke.csproj")
actual = {
    "packageId": library.get("PackageId"),
    "version": library.get("Version"),
    "assemblyName": library.get("AssemblyName"),
    "targetFramework": library.get("TargetFramework"),
}
expected = {key: manifest.get(key) for key in actual}
if actual != expected:
    raise SystemExit(f"Package project metadata differs from moonflat-release.json: {actual} != {expected}")

smoke_reference = next((item for item in ET.parse(root / "tests/MoonFlat.PackageSmoke/MoonFlat.PackageSmoke.csproj").getroot().iter("PackageReference")
                        if item.attrib.get("Include") == manifest["packageId"]), None)
if smoke_reference is None or smoke_reference.attrib.get("Version") != manifest["version"]:
    raise SystemExit("Package smoke reference does not match the release package ID and version")

if len(sys.argv) == 2 and sys.argv[1] == "--version":
    print(manifest["version"])
else:
    print(f"MoonFlat release metadata passed: {manifest['packageId']} {manifest['version']} ({manifest['targetFramework']})")
