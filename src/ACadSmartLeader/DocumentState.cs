using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AcadPaletteSetXData.AutoCAD;
using AcadPaletteSetXData.Core;
using ACadSmartLeader.Core;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace ACadSmartLeader;

internal sealed class UserSettings
{
    public RegenMode UpdateOnRegen { get; set; } = RegenMode.RegenAll;
    public double? Tolerance { get; set; }
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ACadSmartLeader", "settings.json");
    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UserSettings();
            var json = JsonValue.Parse(File.ReadAllText(FilePath)) as JsonObject;
            var settings = new UserSettings();
            if (json?["updateOnRegen"] is JsonString text && Enum.TryParse(text.Value, out RegenMode mode) && Enum.IsDefined(typeof(RegenMode), mode)) settings.UpdateOnRegen = mode;
            if (json?["tolerance"] is JsonNumber number) { var n = number.ToDouble(); TargetGeometry.Tolerance(1, n); settings.Tolerance = n; }
            return settings;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is ArgumentException)
        { System.Diagnostics.Trace.WriteLine("ACadSmartLeader settings: " + e.Message); return new UserSettings(); }
    }
    public void Save()
    {
        if (Tolerance.HasValue) TargetGeometry.Tolerance(1, Tolerance);
        var json = new JsonObject(); json.Add("updateOnRegen", new JsonString(UpdateOnRegen.ToString()));
        if (Tolerance.HasValue) json.Add("tolerance", new JsonNumber(Tolerance.Value));
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json.ToJson());
        if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
    }
}

internal sealed class DocumentState : IDisposable
{
    private readonly Document document;
    private SchemaSnapshot? evaluatorSource;
    private LabelEvaluator? evaluator;
    private string evaluatorKey = "";
    public SchemaCache Schema { get; }
    public RegenSchedule Schedule { get; } = new RegenSchedule();
    public DocumentState(Document document)
    {
        this.document = document;
        Schema = new SchemaCache(() => AutoCadSchemaNod.Read(document.Database).ToResult());
        document.Database.ObjectModified += ObjectChanged;
        document.Database.ObjectErased += ObjectErased;
        document.Database.ObjectReappended += ObjectChanged;
        document.Database.ObjectUnappended += ObjectChanged;
        document.CommandEnded += CommandEnded;
        document.CommandCancelled += CommandFailed;
        document.CommandFailed += CommandFailed;
    }
    public LabelEvaluator Evaluator(SchemaSnapshot schema, IEnumerable<LabelRule> formats, IEnumerable<EntityDataSnapshot> targets)
    {
        var all = formats.ToArray(); var raw = targets.ToArray();
        var key = string.Join("\n", all.Select(r => LabelRules.RuleJson(r).ToCompactJson()).Distinct().OrderBy(s => s, StringComparer.Ordinal));
        if (schema.Schema == null) key += "\n" + string.Join("\n", raw.SelectMany(t => XDataV2.Read(t, schema).Values)
            .Select(v => v.Id + ":" + string.Join(",", v.Stored.Select(s => s.TypeCode))).Distinct().OrderBy(s => s, StringComparer.Ordinal));
        key += "\n" + CultureInfo.CurrentCulture.Name;
        if (!ReferenceEquals(evaluatorSource, schema) || evaluatorKey != key)
        {
            evaluator = new LabelEvaluator(schema, all, rawTargets: raw); evaluatorSource = schema; evaluatorKey = key;
            Trace("Построен снимок форматов.");
        }
        return evaluator!;
    }
    private void ObjectChanged(object sender, ObjectEventArgs e) => Invalidate(e.DBObject);
    private void ObjectErased(object sender, ObjectErasedEventArgs e) => Invalidate(e.DBObject);
    private void Invalidate(DBObject item)
    {
        if (item is Xrecord || item is DBDictionary) { Schema.Invalidate(); evaluatorSource = null; }
    }
    private void CommandEnded(object sender, CommandEventArgs e)
    {
        if (e.GlobalCommandName == "SL_UPDATE") Schedule.Complete();
        else Schedule.CommandEnded(e.GlobalCommandName, Plugin.Settings.UpdateOnRegen);
        if (new[] { "U", "UNDO", "REDO", "MREDO" }.Contains(e.GlobalCommandName, StringComparer.OrdinalIgnoreCase))
        { Schema.Invalidate(); evaluatorSource = null; }
    }
    private void CommandFailed(object sender, CommandEventArgs e)
    { if (e.GlobalCommandName == "SL_UPDATE") Schedule.Complete(); }
    public void Trace(string message)
    { if (Environment.GetEnvironmentVariable("SMARTLEADER_TRACE") == "1") document.Editor.WriteMessage("\n[ACadSmartLeader] " + message); }
    public void Dispose()
    {
        document.Database.ObjectModified -= ObjectChanged; document.Database.ObjectErased -= ObjectErased;
        document.Database.ObjectReappended -= ObjectChanged; document.Database.ObjectUnappended -= ObjectChanged;
        document.CommandEnded -= CommandEnded; document.CommandCancelled -= CommandFailed; document.CommandFailed -= CommandFailed;
        Schedule.Clear();
    }
}
