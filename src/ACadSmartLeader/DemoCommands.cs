using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using AcadPaletteSetXData.AutoCAD;
using AcadPaletteSetXData.Core;
using ACadSmartLeader.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(ACadSmartLeader.DemoCommands))]
namespace ACadSmartLeader;

/// <summary>Reproducible acceptance fixture. SL_DEMO refuses a drawing that already has model entities.</summary>
public sealed class DemoCommands
{
    [CommandMethod("SL_DEMO", CommandFlags.Modal)]
    public void Demo() => Commands.Execute(document => {
        var db = document.Database;
        using var tr = db.TransactionManager.StartTransaction();
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        if (model.Cast<ObjectId>().Any(id => !id.IsErased) || ((DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead)).Contains(DrawingNames.NodRoot))
            throw new InvalidOperationException("SL_DEMO выполняется только в пустом новом чертеже без схемы VELEC.");
        var schema = ParameterSchemaJson.LoadFile(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!, "demo-schema.json")).Schema!;
        WriteSchema(tr, db, schema);
        var snapshot = new SchemaSnapshot(new SchemaNodReadResult(schema, null, Array.Empty<SchemaIssue>()));
        var rules = NodLabelStore.Defaults(); NodLabelStore.Write(tr, db, rules);
        tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
        var block = new BlockTableRecord { Name = "SL_DEMO_MOTOR_" + Guid.NewGuid().ToString("N") };
        var blockId = table.Add(block); tr.AddNewlyCreatedDBObject(block, true);
        var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 5); block.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
        tr.GetObject(model.ObjectId, OpenMode.ForWrite);
        var motor = new BlockReference(Point3d.Origin, blockId); model.AppendEntity(motor); tr.AddNewlyCreatedDBObject(motor, true);
        SetData(tr, db, motor, "MOTOR", ("Device_Number", "М1"), ("Power", 7.5), ("Current", 15.2), ("GC_HeadDevice", "ЩУ1"), ("GC_HDGroup", "3"));
        var cable = new Line(new Point3d(0, -25, 0), new Point3d(70, -25, 0)); model.AppendEntity(cable); tr.AddNewlyCreatedDBObject(cable, true);
        SetData(tr, db, cable, "CABLE", ("Cable_Mark", "ВВГнг-LS"), ("Cable_Cores", 5), ("Cable_Section", 2.5), ("GC_HeadDevice", "ЩУ1"), ("GC_HDGroup", "3"));
        var resolver = new TargetResolver(tr, db, snapshot);
        var culture = CultureInfo.CurrentCulture;
        var evaluator = new LabelEvaluator(snapshot, rules.Rules, culture);
        foreach (var pair in new[] { (Entity: (Entity)motor, Arrow: new Point3d(5,0,0), Landing: new Point3d(30,15,0)), (Entity: (Entity)cable, Arrow: new Point3d(10,-25,0), Landing: new Point3d(40,-10,0)) })
        {
            var values = resolver.Values(pair.Entity); var rule = rules.Select(null, values.Template, pair.Entity.GetType().Name)!;
            using var leader = LeaderFactory.Create(tr, db, model.ObjectId, pair.Arrow, pair.Landing, rule, evaluator.Evaluate(resolver.Snapshot(pair.Entity), rule),
                new LeaderData { Target = pair.Entity.Handle.ToString(), TargetId = values.DeviceId ?? "" }, message => document.Editor.WriteMessage("\n" + message));
        }
        tr.Commit(); document.Editor.WriteMessage("\nSL_DEMO_OK: двигатель и кабель; числа в текущей культуре. Сохраните как examples/demo.dwg.");
    });

    [CommandMethod("SL_DEMO_POWER", CommandFlags.Modal)]
    public void Power() => Commands.Execute(document => {
        using var tr = document.Database.TransactionManager.StartTransaction();
        var resolver = new TargetResolver(tr, document.Database, Plugin.For(document).Schema.Get());
        foreach (var id in LeaderUpdater.All(tr, document.Database))
        {
            var leader = (MLeader)tr.GetObject(id, OpenMode.ForRead);
            if (!LeaderFactory.TryData(leader, out var data, out _)) continue;
            var target = resolver.ByHandle(data!.Target, leader.OwnerId);
            if (target == null || resolver.Values(target).Template != "MOTOR") continue;
            tr.GetObject(target.ObjectId, OpenMode.ForWrite);
            using var buffer = target.GetXDataForApplication(DrawingNames.RegApp);
            var values = buffer.AsArray();
            for (var i = 0; i + 1 < values.Length; i++)
                if (values[i].Value as string == "Power") values[i+1] = new TypedValue(1040, 11.0);
            using var replacement = new ResultBuffer(values); target.XData = replacement; break;
        }
        tr.Commit(); document.Editor.WriteMessage("\nSL_DEMO_POWER_OK: Power = 11.");
    });

    [CommandMethod("SL_DEMO_ASSERT", CommandFlags.Modal)]
    public void Assert() => Commands.Execute(document => {
        var expected = document.Editor.GetDouble("\nОжидаемая мощность на полке (7.5/11): ");
        if (expected.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;
        using var tr = document.Database.TransactionManager.StartOpenCloseTransaction();
        var resolver = new TargetResolver(tr, document.Database, Plugin.For(document).Schema.Get());
        var motorCount = 0; var cableCount = 0;
        foreach (var id in LeaderUpdater.All(tr, document.Database))
        {
            var leader = (MLeader)tr.GetObject(id, OpenMode.ForRead);
            if (!LeaderFactory.TryData(leader, out var data, out _)) continue;
            var target = resolver.ByHandle(data!.Target, leader.OwnerId); if (target == null) continue;
            var text = LeaderFactory.Text(leader);
            if (resolver.Values(target).Template == "MOTOR")
            {
                var suffix = expected.Value.ToString("F2", CultureInfo.CurrentCulture) + " кВт";
                if (!text.EndsWith(suffix, StringComparison.Ordinal)) throw new InvalidOperationException("SL_ASSERT_FAILED: " + leader.Handle + " = " + text + "; ожидается " + suffix);
                motorCount++;
            }
            else if (resolver.Values(target).Template == "CABLE")
            { if (!text.Contains("ЩУ1.3") || !text.Contains("ВВГнг-LS")) throw new InvalidOperationException("SL_ASSERT_FAILED cable: " + text); cableCount++; }
        }
        if (motorCount == 0 || cableCount == 0) throw new InvalidOperationException("SL_ASSERT_FAILED: нет демонстрационных выносок.");
        tr.Commit(); document.Editor.WriteMessage("\nSL_ASSERT_OK: motor=" + motorCount + "; cable=" + cableCount);
    });

    [CommandMethod("SL_DEMO_PERF", CommandFlags.Modal)]
    public void Performance() => Commands.Execute(document => {
        // Bounded fixture: 1000 leaders sharing the motor tests target-read reuse without an unbounded stress loop.
        using (var tr = document.Database.TransactionManager.StartTransaction())
        {
            var leaders = LeaderUpdater.All(tr, document.Database).ToArray();
            if (leaders.Length != 2) throw new InvalidOperationException("Сначала SL_DEMO в новом чертеже (ровно две выноски).");
            var original = (MLeader)tr.GetObject(leaders[0], OpenMode.ForRead);
            var owner = (BlockTableRecord)tr.GetObject(original.OwnerId, OpenMode.ForWrite);
            for (var i = 2; i < 1000; i++) { var copy = (MLeader)original.Clone(); owner.AppendEntity(copy); tr.AddNewlyCreatedDBObject(copy, true); }
            tr.Commit();
        }
        var clock = Stopwatch.StartNew(); LeaderUpdater.Run(document, null, false); clock.Stop();
        document.Editor.WriteMessage("\nSL_PERF: 1000 leaders, " + clock.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + " s; required <= 2 s.");
    });

    private static void SetData(Transaction tr, Database db, Entity entity, string template, params (string Key, object Value)[] pairs)
    {
        var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (!apps.Has(DrawingNames.RegApp)) { tr.GetObject(db.RegAppTableId, OpenMode.ForWrite); var app = new RegAppTableRecord { Name = DrawingNames.RegApp }; apps.Add(app); tr.AddNewlyCreatedDBObject(app, true); }
        var list = new List<TypedValue> { new TypedValue(1001, DrawingNames.RegApp), new TypedValue(1070, (short)2), new TypedValue(1000, "TEMPLATE"), new TypedValue(1000, template), new TypedValue(1000, "DEVICE_ID"), new TypedValue(1000, Guid.NewGuid().ToString("D")) };
        foreach (var pair in pairs) { list.Add(new TypedValue(1000, pair.Key)); list.Add(new TypedValue(pair.Value is double ? 1040 : pair.Value is int ? 1071 : 1000, pair.Value)); }
        using var data = new ResultBuffer(list.ToArray()); entity.XData = data;
    }
    private static void WriteSchema(Transaction tr, Database db, ParameterSchema schema)
    {
        ObjectId Dictionary(ObjectId parentId, string key)
        {
            var parent = (DBDictionary)tr.GetObject(parentId, OpenMode.ForRead);
            if (parent.Contains(key)) return parent.GetAt(key);
            tr.GetObject(parentId, OpenMode.ForWrite); var dictionary = new DBDictionary(); var id = parent.SetAt(key, dictionary); tr.AddNewlyCreatedDBObject(dictionary, true); return id;
        }
        var root = Dictionary(db.NamedObjectsDictionaryId, DrawingNames.NodRoot);
        foreach (var record in SchemaNod.ToRecords(schema, SchemaNodInfo.Now("ACadSmartLeader demo", SchemaImportMode.Replace)))
        {
            var slash = record.Name.IndexOf('/');
            var parentId = slash < 0 ? root : Dictionary(root, record.Name.Substring(0, slash));
            var key = slash < 0 ? record.Name : record.Name.Substring(slash + 1);
            var parent = (DBDictionary)tr.GetObject(parentId, OpenMode.ForWrite);
            using var buffer = new ResultBuffer(record.Values.Select(v => new TypedValue(v.TypeCode, v.Value)).ToArray());
            var created = new Xrecord { Data = buffer }; parent.SetAt(key, created); tr.AddNewlyCreatedDBObject(created, true);
        }
    }
}
