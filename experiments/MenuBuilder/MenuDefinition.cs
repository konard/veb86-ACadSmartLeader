using System.IO.Compression;
using System.Xml.Linq;

namespace ACadSmartLeader;

// CUI package structure follows Autodesk ADN's MIT-licensed acad-cuix-builder.
// See THIRD-PARTY-NOTICES.txt. No executable AutoCAD assembly is used by this tool.
internal static class MenuDefinition
{
    private static XElement Revision(string name = "ModifiedRev") => new(name,
        new XAttribute("MajorVersion", 24), new XAttribute("MinorVersion", 0), new XAttribute("UserVersion", 0));

    public static void Write(string path)
    {
        var commands = new[] {
            (Caption: "Создать", Id: "SL_CREATE"),
            (Caption: "Создать групповую", Id: "SL_CREATE_GROUP"),
            (Caption: "Обновить", Id: "SL_UPDATE"),
            (Caption: "Правила", Id: "SL_RULES")
        };
        var parts = new Dictionary<string, XElement>();
        parts.Add("Header.cui", new XElement("CustSection",
            new XElement("FileVersion", new XAttribute("MajorVersion", 0), new XAttribute("MinorVersion", 6), new XAttribute("IncrementalVersion", 1), new XAttribute("UserVersion", 0)),
            new XElement("Header", new XElement("FileReferences"), new XElement("CommonConfiguration", new XElement("CommonItems", Revision())))));
        parts.Add("MenuGroup.cui", new XElement("MenuGroup", new XAttribute("Name", "ACADSMARTLEADER"), new XAttribute("DisplayName", "ACadSmartLeader"),
            new XElement("MacroGroup", new XAttribute("Name", "ACadSmartLeader"), commands.Select(c =>
                new XElement("MenuMacro", new XAttribute("UID", c.Id + "_MACRO"), new XElement("Macro", new XAttribute("type", "Any"),
                    Revision("Revision"), Revision(), new XElement("Name", c.Caption),
                    new XElement("Command", "$M=$(if,$(getvar,cmdactive),^C^C,)_" + c.Id + " "),
                    new XElement("HelpString", c.Caption), new XElement("CLICommand", c.Id), new XElement("ToolTip")))))));
        parts.Add("RibbonRoot.cui", new XElement("RibbonRoot",
            new XElement("RibbonPanelSourceCollection", new XElement("RibbonPanelSource", new XAttribute("UID", "SL_PANEL"), new XAttribute("Text", "Умные выноски"), new XAttribute("HiddenInEditor", false),
                Revision(), new XElement("Name", "Умные выноски"), new XElement("RibbonRow", new XAttribute("UID", "SL_ROW"), Revision(), commands.Select(c =>
                    new XElement("RibbonCommandButton", new XAttribute("UID", c.Id + "_BUTTON"), new XAttribute("Id", "AcRibbonCommandButton"),
                        new XAttribute("Text", c.Caption), new XAttribute("ButtonStyle", "LargeWithText"), new XAttribute("MenuMacroID", c.Id + "_MACRO"),
                        new XElement("TooltipTitle", c.Caption), Revision()))))),
            new XElement("RibbonTabSourceCollection", new XElement("RibbonTabSource", new XAttribute("UID", "SL_TAB"), new XAttribute("Text", "ACadSmartLeader"),
                new XAttribute("DefaultDisplay", "AddToWorkspace"), Revision(), new XElement("Name", "ACadSmartLeader"),
                new XElement("RibbonPanelSourceReference", new XAttribute("UID", "SL_PANEL_REF"), new XAttribute("PanelId", "SL_PANEL"), new XAttribute("ResizeStyle", "Default"), Revision())))));
        foreach (var root in new[] { "AcceleratorRoot", "DigitizerButtonRoot", "DoubleClickRoot", "ImageMenuRoot", "MouseButtonRoot", "OverrideRoot", "PopMenuRoot", "QuickAccessToolbarRoot", "QuickPropertiesRoot", "RolloverTooltipRoot", "ScreenMenuRoot", "TabletMenuRoot", "ToolbarRoot", "WorkspaceRoot" })
            parts.Add(root + ".cui", new XElement(root));
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
        parts.Add("_rels/.rels", new XElement(rel + "Relationships", parts.Keys.Select((name, i) => new XElement(rel + "Relationship", new XAttribute("Type", "CUI"), new XAttribute("Target", "/" + name), new XAttribute("Id", "R" + (i + 1).ToString("x16"))))));
        parts.Add("[Content_Types].xml", new XElement(ct + "Types", new XElement(ct + "Default", new XAttribute("Extension", "cui"), new XAttribute("ContentType", "text/xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "text/xml")),
            new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml"))));
        parts.Add("Menu_Package_Info.xml", new XElement("MenuPackageParts", parts.Keys.Where(n => n.EndsWith(".cui", StringComparison.Ordinal)).Concat(new[] { "VirtualMNRRoot", "Menu_Package_Info.xml" }).Select(name =>
            new XElement("PartData", new XAttribute("PartData_Name", "/" + name), new XAttribute("PartData_Modified", "2026-01-01T00:00:00Z")))));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var part in parts)
        {
            var entry = zip.CreateEntry(part.Key); entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open(); new XDocument(part.Value).Save(stream);
        }
    }
}
