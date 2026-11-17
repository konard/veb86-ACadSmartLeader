using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        var output = args.Length > 0 ? args[0] : "screenshots"; Directory.CreateDirectory(output);
        var rules = LabelRules.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "label-rules.json")), out _);
        var schema = new SchemaSnapshot(new SchemaNodReadResult(ParameterSchemaJson.LoadFile(Path.Combine(AppContext.BaseDirectory, "schema.json")).Schema, null, Array.Empty<SchemaIssue>()));
        var target = new EntityDataSnapshot("1F", "BlockReference", new[] {
            new DataValue(1001, "BobrovXDATA"), new DataValue(1070, (short)2), new DataValue(1000, "TEMPLATE"), new DataValue(1000, "MOTOR"),
            new DataValue(1000, "Device_Number"), new DataValue(1000, "М1"), new DataValue(1000, "Power"), new DataValue(1040, 7.5)
        }, Array.Empty<DataRecord>());
        CheckParameterInsertion();
        Render(new RulesWindow(rules, schema, target, true), "rules-dark.png");
        Render(new EditWindow(new LeaderData { Rule = "MOTOR" }, rules, schema, target, false), "edit-light.png");
        Render(new SettingsWindow(RegenMode.RegenAll, null, true), "settings-dark.png");

        void CheckParameterInsertion()
        {
            var window = new EditWindow(new LeaderData { Rule = "MOTOR" }, rules, schema, target, false);
            window.Show(); window.Activate(); window.UpdateLayout();
            try
            {
                var fields = Descendants<TextBox>(window).ToArray();
                var picker = Descendants<ComboBox>(window).Single(c => c.Items.Cast<object>().Any(item => item is SchemaParameter));
                var insert = Descendants<Button>(window).Single(b => b.Content as string == "Вставить параметр");
                if (fields.Length != 2 || !fields[0].Focus()) throw new InvalidOperationException("Cannot focus the top format.");
                picker.SelectedItem = schema.Parameters["Power"];
                insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.Result.Top != "@@[Power]" || window.Result.Bottom != null)
                    throw new InvalidOperationException("Insertion must enable only the top override.");
                var overrides = Descendants<CheckBox>(window).ToArray();
                overrides[0].IsChecked = false;
                overrides[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.Result.Top != null) throw new InvalidOperationException("Unchecking must restore the rule format.");
                fields[0].SelectAll();
                insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.Result.Top != "@@[Power]")
                    throw new InvalidOperationException("Reinserting identical text must still enable the override and update the preview.");
                if (!fields[1].Focus()) throw new InvalidOperationException("Cannot focus the bottom format.");
                picker.SelectedItem = schema.Parameters["Device_Number"];
                insert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (window.Result.Bottom != "@@[Device_Number]" || window.Result.Top != "@@[Power]")
                    throw new InvalidOperationException("Insertion must follow the last focused format.");
                Console.WriteLine("WPF_EDIT_INSERT_OK");
            }
            finally { window.Close(); }
        }

        void Render(Window window, string name)
        {
            window.Show(); window.UpdateLayout();
            var content = (ScrollViewer)window.Content;
            Save(name);
            if (content.ScrollableHeight > 0)
            {
                content.ScrollToBottom(); window.UpdateLayout();
                Save(Path.GetFileNameWithoutExtension(name) + "-bottom.png");
            }
            window.Close();

            void Save(string fileName)
            {
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                // Render the window background as well as its client controls. The bitmap's
                // client-sized bounds exclude the native frame that WPF cannot capture.
                bitmap.Render(window);
                var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
                var expected = ((SolidColorBrush)window.Background).Color;
                if (pixel[0] != expected.B || pixel[1] != expected.G || pixel[2] != expected.R || pixel[3] != 255)
                    throw new InvalidOperationException("The render must include the opaque window background.");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, fileName)); encoder.Save(stream);
            }
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
