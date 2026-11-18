using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AcadPaletteSetXData.Core;
using ACadSmartLeader.Core;
using Autodesk.AutoCAD.DatabaseServices;

namespace ACadSmartLeader;

internal static class NodLabelStore
{
    internal const string Root = "VELEC_LEADER";
    public static LabelRules Defaults() => LabelRules.Parse(File.ReadAllText(Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "label-rules.json")), out _);

    public static LabelRules Read(Transaction tr, Database db, out bool stored)
    {
        stored = false;
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(Root)) return Defaults();
        var root = (DBDictionary)tr.GetObject(nod.GetAt(Root), OpenMode.ForRead);
        if (!root.Contains("LABELS")) return Defaults();
        stored = true;
        var labels = (DBDictionary)tr.GetObject(root.GetAt("LABELS"), OpenMode.ForRead);
        var rules = new List<(int Order, JsonObject Rule)>();
        foreach (DBDictionaryEntry entry in labels)
        {
            var fields = ReadFields((Xrecord)tr.GetObject(entry.Value, OpenMode.ForRead));
            if (!(fields["order"] is JsonNumber number) || !number.TryGetInt32(out var order)) throw new FormatException("В правиле нет order: " + entry.Key);
            var rule = LabelRules.RuleFromRecord(fields);
            rules.Add((order, rule));
        }
        var json = new JsonObject(); json.Add("format", new JsonString(LabelRules.Format)); json.Add("version", new JsonNumber(1L));
        json.Add("rules", new JsonArray(rules.OrderBy(r => r.Order).ThenBy(r => (r.Rule["id"] as JsonString)?.Value, StringComparer.Ordinal).Select(r => (JsonValue)r.Rule)));
        if (root.Contains("INFO"))
            foreach (var pair in ReadFields((Xrecord)tr.GetObject(root.GetAt("INFO"), OpenMode.ForRead))) json.Add(pair.Key, pair.Value);
        return LabelRules.Parse(json.ToJson(), out _);
    }

    public static void Write(Transaction tr, Database db, LabelRules rules)
    {
        var errors = rules.Validate();
        if (errors.Count != 0) throw new FormatException(string.Join("\n", errors));
        var root = Dictionary(tr, db.NamedObjectsDictionaryId, Root);
        var labels = Dictionary(tr, root.ObjectId, "LABELS");
        // Use AutoCAD's typed enumerator and snapshot the entries before changing the dictionary.
        var previous = new List<DBDictionaryEntry>();
        foreach (DBDictionaryEntry entry in labels)
            previous.Add(entry);

        if (previous.Count > 0) tr.GetObject(labels.ObjectId, OpenMode.ForWrite);
        foreach (var entry in previous)
        {
            labels.Remove(entry.Key);
            tr.GetObject(entry.Value, OpenMode.ForWrite).Erase();
        }
        for (var i = 0; i < rules.Rules.Count; i++)
        {
            var obj = LabelRules.RecordJson(rules.Rules[i], i);
            WriteRecord(tr, labels, rules.Rules[i].Id, obj);
        }
        WriteRecord(tr, root, "INFO", rules.Extra);
    }

    private static DBDictionary Dictionary(Transaction tr, ObjectId parentId, string key)
    {
        var parent = (DBDictionary)tr.GetObject(parentId, OpenMode.ForRead);
        if (parent.Contains(key)) return (DBDictionary)tr.GetObject(parent.GetAt(key), OpenMode.ForRead);
        tr.GetObject(parentId, OpenMode.ForWrite);
        var created = new DBDictionary(); parent.SetAt(key, created); tr.AddNewlyCreatedDBObject(created, true); return created;
    }

    private static void WriteRecord(Transaction tr, DBDictionary parent, string key, JsonObject fields)
    {
        var values = new List<TypedValue>();
        foreach (var pair in fields)
        {
            var json = pair.Value.ToCompactJson();
            var count = (json.Length + 239) / 240;
            values.Add(new TypedValue(1, pair.Key)); values.Add(new TypedValue(90, count));
            for (var i = 0; i < count; i++) values.Add(new TypedValue(1, json.Substring(i * 240, Math.Min(240, json.Length - i * 240))));
        }
        using var buffer = new ResultBuffer(values.ToArray());
        if (parent.Contains(key)) ((Xrecord)tr.GetObject(parent.GetAt(key), OpenMode.ForWrite)).Data = buffer;
        else
        {
            tr.GetObject(parent.ObjectId, OpenMode.ForWrite);
            var record = new Xrecord { Data = buffer }; parent.SetAt(key, record); tr.AddNewlyCreatedDBObject(record, true);
        }
    }

    private static JsonObject ReadFields(Xrecord record)
    {
        using var buffer = record.Data;
        var values = buffer?.AsArray() ?? Array.Empty<TypedValue>();
        var result = new JsonObject();
        for (var i = 0; i < values.Length;)
        {
            if (values[i].TypeCode != 1 || !(values[i++].Value is string key) || i >= values.Length || values[i].TypeCode != 90)
                throw new FormatException("Повреждённая запись правил.");
            var count = (int)values[i++].Value;
            if (count < 1 || count > values.Length - i) throw new FormatException("Повреждённая длина поля " + key);
            var json = new System.Text.StringBuilder();
            for (var n = 0; n < count; n++)
            { if (values[i].TypeCode != 1) throw new FormatException("Повреждённое поле " + key); json.Append((string)values[i++].Value); }
            result.Add(key, JsonValue.Parse(json.ToString()));
        }
        return result;
    }
}
