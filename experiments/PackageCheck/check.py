"""Validate the distributable without executing AutoCAD reference assemblies."""
import io
import argparse
import json
from pathlib import Path, PurePosixPath
import xml.etree.ElementTree as ET
import zipfile

TARGETS = {
    "AutoCAD2020": ("R23.1", "R23.1"),
    "AutoCAD2021-2024": ("R24.0", "R24.3"),
    "AutoCAD2025": ("R25.0", None),
}
COMMANDS = {
    "SL_CREATE", "SL_UPDATE", "SL_EDIT", "SL_RULES", "SL_RULES_IMPORT",
    "SL_RULES_EXPORT", "SL_SETTINGS", "SL_SELECT_PROBLEMS",
}


def project_version():
    props = Path(__file__).resolve().parents[2] / "Directory.Build.props"
    return ET.parse(props).findtext("PropertyGroup/Version")


def check_manifest(manifest, version):
    assert manifest.tag == "ApplicationPackage"
    assert manifest.attrib["Name"] == "ACadSmartLeader"
    assert manifest.attrib["AppVersion"] == version
    components = manifest.findall("Components")
    assert len(components) == len(TARGETS), "Expected separate 2020, 2021–2024 and 2025+ components"
    for component, (target, releases) in zip(components, TARGETS.items()):
        runtime = component.find("RuntimeRequirements")
        assert runtime is not None
        assert runtime.attrib["OS"] == "Win64"
        assert runtime.attrib["Platform"] == "AutoCAD*"
        assert (runtime.get("SeriesMin"), runtime.get("SeriesMax")) == releases, target
        entries = component.findall("ComponentEntry")
        assert len(entries) == 2, target
        dll, menu = entries
        prefix = "./Contents/" + target + "/ACadSmartLeader"
        assert dll.attrib["ModuleName"] == prefix + ".dll"
        assert dll.attrib["AppType"] == ".Net"
        assert dll.attrib["LoadOnAutoCADStartup"].lower() == "true"
        assert dll.attrib["LoadOnCommandInvocation"].lower() == "true"
        commands = dll.findall("Commands/Command")
        assert len(commands) == len(COMMANDS)
        assert {c.attrib["Global"] for c in commands} == COMMANDS
        assert all(c.attrib["Local"] == c.attrib["Global"] for c in commands)
        assert menu.attrib["ModuleName"] == prefix + ".cuix"


def check_menu(data):
    with zipfile.ZipFile(io.BytesIO(data)) as menu:
        names = set(menu.namelist())
        roots = {name: ET.fromstring(menu.read(name)) for name in names}
        assert {"Header.cui", "MenuGroup.cui", "RibbonRoot.cui", "_rels/.rels", "[Content_Types].xml"} <= names
        for relationship in roots["_rels/.rels"]:
            assert relationship.attrib["Target"].lstrip("/") in names
        macros = {m.attrib["UID"]: m.findtext("Macro/Command") for m in roots["MenuGroup.cui"].iter("MenuMacro")}
        expected = {"SL_CREATE", "SL_UPDATE", "SL_RULES"}
        assert len(macros) == 3
        assert {command.split(")_")[-1].strip() for command in macros.values()} == expected
        assert all(command.startswith("$M=$(if,$(getvar,cmdactive),^C^C,)_") and command.endswith(" ") for command in macros.values())
        buttons = list(roots["RibbonRoot.cui"].iter("RibbonCommandButton"))
        assert len(buttons) == 3
        assert {button.attrib["MenuMacroID"] for button in buttons} == set(macros)
        panels = {p.attrib["UID"] for p in roots["RibbonRoot.cui"].iter("RibbonPanelSource")}
        assert all(p.attrib["PanelId"] in panels for p in roots["RibbonRoot.cui"].iter("RibbonPanelSourceReference"))


def check_package(path, version):
    with zipfile.ZipFile(path) as package:
        names = set(package.namelist())
        assert len(names) == len(package.namelist()), "Duplicate archive entries"
        assert all("\\" not in n and not n.startswith("/") and ".." not in PurePosixPath(n).parts for n in names)
        root = "ACadSmartLeader.bundle/"
        manifest = ET.fromstring(package.read(root + "PackageContents.xml"))
        check_manifest(manifest, version)
        dlls = {n for n in names if n.lower().endswith(".dll")}
        assert dlls == {root + "Contents/" + target + "/ACadSmartLeader.dll" for target in TARGETS}, dlls
        assert root + "THIRD-PARTY-NOTICES.txt" in names
        instructions = package.read("INSTALL.md").decode("utf-8-sig")
        assert "%APPDATA%\\Autodesk\\ApplicationPlugins\\ACadSmartLeader.bundle" in instructions
        expected_files = {"INSTALL.md", root + "PackageContents.xml", root + "THIRD-PARTY-NOTICES.txt"}
        for entry in manifest.iter("ComponentEntry"):
            name = root + entry.attrib["ModuleName"].removeprefix("./")
            assert name in names, name
            expected_files.add(name)
            if name.endswith(".cuix"):
                check_menu(package.read(name))
            else:
                assert package.read(name).startswith(b"MZ"), name
                folder = str(PurePosixPath(name).parent) + "/"
                for data in ("label-rules.json", "demo-schema.json"):
                    expected_files.add(folder + data)
                    json.loads(package.read(folder + data))
        assert {n for n in names if not n.endswith("/")} == expected_files, "Unexpected or missing payload files"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", nargs="?")
    parser.add_argument("--cuix")
    parser.add_argument("--manifest")
    parser.add_argument("--version", default=project_version())
    args = parser.parse_args()
    if args.cuix:
        check_menu(Path(args.cuix).read_bytes())
    elif args.manifest:
        check_manifest(ET.parse(args.manifest).getroot(), args.version)
    elif args.package:
        check_package(args.package, args.version)
    else:
        parser.error("Supply an archive, --cuix or --manifest")
    print("PACKAGE_CHECK_OK")
