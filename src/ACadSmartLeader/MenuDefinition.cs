using System.IO;
using Autodesk.AutoCAD.Customization;

namespace ACadSmartLeader;

/// <summary>Builds a partial CUIX with the oldest supported managed customization API.</summary>
internal static class MenuDefinition
{
    public static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var section = new CustomizationSection(path, "ACADSMARTLEADER");
        var group = new MacroGroup("ACadSmartLeader", section.MenuGroup);
        var ribbon = section.MenuGroup.RibbonRoot;
        var panel = new RibbonPanelSource(ribbon) { Name = "ACadSmartLeader", Text = "Умные выноски", ElementID = "SL_PANEL" };
        ribbon.RibbonPanelSources.Add(panel);
        var tab = new RibbonTabSource(ribbon) { Name = "ACadSmartLeader", Text = "ACadSmartLeader", ElementID = "SL_TAB", DefaultDisplay = DefaultDisplay.AddToWorkspace };
        ribbon.RibbonTabSources.Add(tab);
        var reference = new RibbonPanelSourceReference(tab) { PanelId = panel.ElementID }; tab.Items.Add(reference);
        foreach (var command in new[] { ("Создать", "SL_CREATE"), ("Обновить", "SL_UPDATE"), ("Правила", "SL_RULES") })
        {
            // Cancel an active command while preserving PickFirst when the command line is idle.
            var macro = new MenuMacro(group, command.Item1, "$M=$(if,$(getvar,cmdactive),^C^C,)_" + command.Item2 + " ", command.Item2 + "_MACRO");
            var button = new RibbonCommandButton(panel) { Text = command.Item1, MacroID = macro.ElementID, ElementID = command.Item2 + "_BUTTON", TooltipTitle = command.Item1 };
            panel.Items.Add(button);
        }
        section.Save();
        if (!File.Exists(path)) throw new IOException("Не удалось создать CUIX: " + path);
    }
}
