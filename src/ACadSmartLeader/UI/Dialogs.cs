using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AcadPaletteSetXData.Core;
using ACadSmartLeader.Core;

namespace ACadSmartLeader.UI;

/// <summary>WPF windows with no AutoCAD dependency, also usable by the Windows render smoke check.</summary>
public class LeaderWindow : Window
{
    protected readonly StackPanel Body = new StackPanel { Margin = new Thickness(18) };
    protected readonly Brush ForegroundBrush;
    protected readonly Brush InputBackground;
    public LeaderWindow(string title, bool dark)
    {
        Title = title; Width = 750; Height = 750; MinWidth = 600; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        Background = Brush(dark ? "#3D4451" : "#F3F4F6");
        ForegroundBrush = Brush(dark ? "#F3F4F6" : "#20252D"); Foreground = ForegroundBrush;
        InputBackground = Brush(dark ? "#303641" : "#FFFFFF");
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Body };
        var input = new Style(typeof(TextBox)); input.Setters.Add(new Setter(Control.BackgroundProperty, InputBackground));
        input.Setters.Add(new Setter(Control.ForegroundProperty, ForegroundBrush)); input.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7)));
        input.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("#68717E"))); Resources.Add(typeof(TextBox), input);
    }
    private static SolidColorBrush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    protected TextBox Field(string caption, string value, Panel? panel = null)
    {
        panel ??= Body;
        panel.Children.Add(new TextBlock { Text = caption, Margin = new Thickness(0, 8, 0, 3) });
        var input = new TextBox { Text = value, MinWidth = 120 }; panel.Children.Add(input); return input;
    }
    protected static Button Button(Panel panel, string caption, Action action)
    {
        var button = new Button { Content = caption, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 8, 8, 0) };
        button.Click += (_, _) => action(); panel.Children.Add(button); return button;
    }
    protected StackPanel Row(Panel? parent = null)
    { var row = new StackPanel { Orientation = Orientation.Horizontal }; (parent ?? Body).Children.Add(row); return row; }
    protected void ParameterPicker(SchemaSnapshot schema, Func<TextBox> destination, Action? inserting = null)
    {
        var parameters = new ComboBox { Width = 370, ItemsSource = schema.Parameters.Values.ToArray(), DisplayMemberPath = "Caption", SelectedIndex = 0, Margin = new Thickness(0, 8, 8, 0) };
        var row = Row(); row.Children.Add(parameters);
        Button(row, "Вставить параметр", () => {
            if (!(parameters.SelectedItem is SchemaParameter parameter)) return;
            inserting?.Invoke(); var text = destination(); text.SelectedText = "@@[" + parameter.Id + "]"; text.Focus();
        });
    }
    protected void Failure(Exception e) => MessageBox.Show(this, e.Message, "ACadSmartLeader", MessageBoxButton.OK, MessageBoxImage.Warning);
}

public sealed class RulesWindow : LeaderWindow
{
    private readonly LabelRules rules;
    private readonly SchemaSnapshot schema;
    private readonly EntityDataSnapshot? target;
    private readonly ListBox list = new ListBox { Height = 100, DisplayMemberPath = "Caption" };
    private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
    private readonly TextBlock preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private LabelRule? current;
    private TextBox? activeFormat;
    private bool loading;
    public bool PickTarget { get; private set; }
    public LabelRules Result => rules;
    public RulesWindow(LabelRules source, SchemaSnapshot schema, EntityDataSnapshot? target, bool dark) : base("Правила ACadSmartLeader", dark)
    {
        rules = LabelRules.Parse(source.ToJson(), out _); this.schema = schema; this.target = target;
        Body.Children.Add(new TextBlock { Text = "Правила подписей", FontSize = 22 });
        Body.Children.Add(new TextBlock { Text = "Первое подходящее правило выбирается по шаблону и типу объекта.", Margin = new Thickness(0, 5, 0, 10) });
        Body.Children.Add(list); RefreshList();
        var actions = Row();
        Button(actions, "Добавить", () => { SaveCurrent(); var r = new LabelRule { Id = "RULE_" + Guid.NewGuid().ToString("N").Substring(0, 8), Caption = "Новое правило", Top = "@@[Device_Number]" }; rules.Rules.Add(r); RefreshList(); list.SelectedItem = r; });
        Button(actions, "Удалить", () => { if (current == null) return; var old = current; current = null; rules.Rules.Remove(old); RefreshList(); list.SelectedIndex = rules.Rules.Count > 0 ? 0 : -1; });
        Button(actions, "Выше", () => Move(-1)); Button(actions, "Ниже", () => Move(1));
        foreach (var caption in new[] { "Id", "Название", "Шаблоны (через запятую)", "Типы объектов (через запятую)", "Верхняя полка", "Нижняя полка", "Пустое значение", "Ошибка", "Стиль MLeader", "Слой" })
        {
            var text = Field(caption, ""); fields.Add(caption, text);
            text.TextChanged += (_, _) => { if (!loading) { SaveCurrent(); Preview(); } };
            if (caption == "Название") text.LostKeyboardFocus += (_, _) => list.Items.Refresh();
            if (caption == "Верхняя полка" || caption == "Нижняя полка") text.GotKeyboardFocus += (_, _) => activeFormat = text;
        }
        ParameterPicker(schema, () => activeFormat ?? fields["Верхняя полка"]);
        Body.Children.Add(preview);
        var bottom = Row();
        Button(bottom, "Объект предпросмотра…", () => { SaveCurrent(); PickTarget = true; DialogResult = false; });
        Button(bottom, "Сохранить", () => {
            try { SaveCurrent(); var errors = rules.Validate(); if (errors.Count > 0) throw new FormatException(string.Join("\n", errors)); DialogResult = true; }
            catch (Exception e) when (e is FormatException || e is ArgumentException) { Failure(e); }
        });
        Button(bottom, "Отмена", () => DialogResult = false);
        list.SelectionChanged += (_, _) => { SaveCurrent(); current = list.SelectedItem as LabelRule; LoadCurrent(); };
        list.SelectedIndex = rules.Rules.Count > 0 ? 0 : -1;
    }
    private void RefreshList() { list.ItemsSource = null; list.ItemsSource = rules.Rules; }
    private void Move(int delta)
    {
        if (current == null) return;
        SaveCurrent(); var index = rules.Rules.IndexOf(current); var next = index + delta;
        if (next < 0 || next >= rules.Rules.Count) return;
        var selected = current; rules.Rules.RemoveAt(index); rules.Rules.Insert(next, selected); current = null;
        RefreshList(); list.SelectedItem = selected;
    }
    private void SaveCurrent()
    {
        if (current == null || loading || fields.Count == 0) return;
        current.Id = fields["Id"].Text; current.Caption = fields["Название"].Text;
        current.Templates = Split(fields["Шаблоны (через запятую)"].Text); current.EntityTypes = Split(fields["Типы объектов (через запятую)"].Text);
        current.Top = fields["Верхняя полка"].Text; current.Bottom = fields["Нижняя полка"].Text;
        current.Empty = fields["Пустое значение"].Text; current.Error = fields["Ошибка"].Text;
        current.Style = fields["Стиль MLeader"].Text; current.Layer = fields["Слой"].Text;
    }
    private static List<string> Split(string text) => text.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
    private void LoadCurrent()
    {
        loading = true;
        var r = current ?? new LabelRule();
        var values = new[] { r.Id, r.Caption, string.Join(", ", r.Templates), string.Join(", ", r.EntityTypes), r.Top, r.Bottom, r.Empty, r.Error, r.Style, r.Layer };
        var i = 0; foreach (var field in fields.Values) { field.Text = values[i++]; field.IsEnabled = current != null; }
        loading = false; Preview();
    }
    private void Preview()
    {
        if (current == null) { preview.Text = "Выберите или добавьте правило."; return; }
        var engine = new LabelEvaluator(schema, new[] { current }, rawTargets: target == null ? null : new[] { target });
        var issues = engine.Diagnostics;
        if (target == null) preview.Text = "Выберите объект для предпросмотра.\n" + string.Join("\n", issues);
        else
        {
            var text = engine.Evaluate(target, current);
            preview.Text = "Предпросмотр: " + text.Top + "\n────────────────────────\n" + text.Bottom + "\n" + string.Join("\n", text.Errors.Concat(issues).Distinct());
        }
    }
}

public sealed class EditWindow : LeaderWindow
{
    public LeaderData Result { get; }
    public bool Rebind { get; private set; }
    public EditWindow(LeaderData source, LabelRules rules, SchemaSnapshot schema, EntityDataSnapshot? target, bool dark) : base("Редактировать выноску", dark)
    {
        Height = 560; Result = source.Copy();
        var select = new ComboBox { ItemsSource = new[] { "Автоматически по шаблону" }.Concat(rules.Rules.Select(r => r.ToString())).ToArray(), SelectedIndex = Math.Max(0, rules.Rules.FindIndex(r => r.Id == source.Rule) + 1) };
        Body.Children.Add(new TextBlock { Text = "Правило подписи", FontSize = 20, Margin = new Thickness(0, 0, 0, 10) }); Body.Children.Add(select);
        var customTop = new CheckBox { Content = "Свой формат верхней полки", IsChecked = source.Top != null, Foreground = Foreground, Margin = new Thickness(0, 10, 0, 0) }; Body.Children.Add(customTop);
        var top = Field("Верхняя полка", source.Top ?? "");
        var customBottom = new CheckBox { Content = "Свой формат нижней полки", IsChecked = source.Bottom != null, Foreground = Foreground, Margin = new Thickness(0, 10, 0, 0) }; Body.Children.Add(customBottom);
        var bottom = Field("Нижняя полка", source.Bottom ?? "");
        var activeFormat = top;
        top.GotKeyboardFocus += (_, _) => activeFormat = top;
        bottom.GotKeyboardFocus += (_, _) => activeFormat = bottom;
        ParameterPicker(schema, () => activeFormat, () => {
            if (activeFormat == bottom) customBottom.IsChecked = true; else customTop.IsChecked = true;
        });
        var preview = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }; Body.Children.Add(preview);
        void Read() { Result.Rule = select.SelectedIndex <= 0 ? "" : rules.Rules[select.SelectedIndex - 1].Id; Result.Top = customTop.IsChecked == true ? top.Text : null; Result.Bottom = customBottom.IsChecked == true ? bottom.Text : null; }
        void Preview()
        {
            Read();
            var template = target == null ? null : XDataV2.Read(target, schema).Template;
            var rule = rules.Select(Result.Rule, template, target?.EntityType ?? "")?.WithFormats(Result.Top, Result.Bottom);
            if (rule == null) { preview.Text = "Правило не найдено."; return; }
            var evaluator = new LabelEvaluator(schema, new[] { rule }, rawTargets: target == null ? null : new[] { target });
            if (target == null) { preview.Text = "Цель не найдена.\n" + string.Join("\n", evaluator.Diagnostics); return; }
            var text = evaluator.Evaluate(target, rule); preview.Text = "Предпросмотр: " + text.Top + "\n────────────────────────\n" + text.Bottom + "\n" + string.Join("\n", text.Errors);
        }
        select.SelectionChanged += (_, _) => Preview(); top.TextChanged += (_, _) => Preview(); bottom.TextChanged += (_, _) => Preview();
        customTop.Click += (_, _) => Preview(); customBottom.Click += (_, _) => Preview();
        var row = Row();
        Button(row, "Перепривязать…", () => { Read(); Rebind = true; DialogResult = false; });
        Button(row, "Сохранить", () => { try { Read(); Result.Write(); DialogResult = true; } catch (FormatException e) { Failure(e); } });
        Button(row, "Отмена", () => DialogResult = false); Preview();
    }
}

public sealed class SettingsWindow : LeaderWindow
{
    public RegenMode Mode { get; private set; }
    public double? Tolerance { get; private set; }
    public SettingsWindow(RegenMode mode, double? tolerance, bool dark) : base("Настройки ACadSmartLeader", dark)
    {
        Height = 350; MinHeight = 300;
        Body.Children.Add(new TextBlock { Text = "Обновление после регенерации", FontSize = 20 });
        var modes = new ComboBox { ItemsSource = new[] { "Выключено", "REGEN и REGENALL", "Только REGENALL" }, SelectedIndex = (int)mode, Margin = new Thickness(0, 12, 0, 10) }; Body.Children.Add(modes);
        var input = Field("Допуск (пусто: 1% высоты текста, минимум 0.000001)", tolerance?.ToString("R", CultureInfo.CurrentCulture) ?? "");
        var row = Row();
        Button(row, "Сохранить", () => {
            try { Mode = (RegenMode)modes.SelectedIndex; Tolerance = string.IsNullOrWhiteSpace(input.Text) ? (double?)null : double.Parse(input.Text, CultureInfo.CurrentCulture); TargetGeometry.Tolerance(1, Tolerance); DialogResult = true; }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is OverflowException) { Failure(e); }
        });
        Button(row, "Отмена", () => DialogResult = false);
    }
}
