"""Validate the distributable without executing AutoCAD reference assemblies."""
import io
from pathlib import PurePosixPath
import sys
import xml.etree.ElementTree as ET
import zipfile


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


if len(sys.argv) == 3 and sys.argv[1] == "--cuix":
    with open(sys.argv[2], "rb") as source:
        check_menu(source.read())
else:
    with zipfile.ZipFile(sys.argv[1]) as package:
        names = set(package.namelist())
        root = "ACadSmartLeader.bundle/"
        manifest = ET.fromstring(package.read(root + "PackageContents.xml"))
        assert manifest.attrib["AppVersion"] == "0.1.0"
        dlls = {n for n in names if n.lower().endswith(".dll")}
        assert dlls == {root + "Contents/" + target + "/ACadSmartLeader.dll" for target in ("AutoCAD2021-2024", "AutoCAD2025")}, dlls
        assert root + "THIRD-PARTY-NOTICES.txt" in names
        for entry in manifest.iter("ComponentEntry"):
            name = root + entry.attrib["ModuleName"].removeprefix("./")
            assert name in names, name
            if name.endswith(".cuix"):
                check_menu(package.read(name))
            else:
                folder = str(PurePosixPath(name).parent) + "/"
                assert folder + "label-rules.json" in names
                assert folder + "demo-schema.json" in names
print("PACKAGE_CHECK_OK")
