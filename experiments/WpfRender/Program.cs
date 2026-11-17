using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AcadPaletteSetXData.Core;
using ACadSmartLeader.Core;
using ACadSmartLeader.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "screenshots"; Directory.CreateDirectory(output);
        var rules = LabelRules.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "label-rules.json")), out _);
        var schema = new SchemaSnapshot(new SchemaNodReadResult(ParameterSchemaJson.LoadFile(Path.Combine(AppContext.BaseDirectory, "schema.json")).Schema, null, Array.Empty<SchemaIssue>()));
        var target = new EntityDataSnapshot("1F", "BlockReference", new[] {
            new DataValue(1001, "BobrovXDATA"), new DataValue(1070, (short)2), new DataValue(1000, "TEMPLATE"), new DataValue(1000, "MOTOR"),
            new DataValue(1000, "Device_Number"), new DataValue(1000, "М1"), new DataValue(1000, "Power"), new DataValue(1040, 7.5)
        }, Array.Empty<DataRecord>());
        Render(new RulesWindow(rules, schema, target, true) { Height = 1200 }, "rules-dark.png");
        Render(new EditWindow(new LeaderData { Rule = "MOTOR" }, rules, schema, target, false), "edit-light.png");
        Render(new SettingsWindow(RegenMode.RegenAll, null, true), "settings-dark.png");

        void Render(Window window, string name)
        {
            window.Show(); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream); window.Close();
        }
    }
}
