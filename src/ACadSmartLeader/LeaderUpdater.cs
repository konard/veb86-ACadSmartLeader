using System;
using System.Collections.Generic;
using System.Linq;
using AcadPaletteSetXData.Core;
using ACadSmartLeader.Core;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ACadSmartLeader;

internal static class LeaderUpdater
{
    public static IEnumerable<ObjectId> All(Transaction tr, Database db)
    {
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            var record = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!record.IsLayout || record.IsFromExternalReference) continue;
            foreach (ObjectId entity in record)
                if (!entity.IsErased && entity.ObjectClass.IsDerivedFrom(Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(MLeader)))) yield return entity;
        }
    }
    public static bool Arrow(MLeader leader, out Point3d arrow)
    {
        var lines = Lines(leader);
        arrow = Point3d.Origin;
        if (lines.Length != 1) return false;
        arrow = leader.GetFirstVertex(lines[0]); return true;
    }
    public static int[] Lines(MLeader leader) => leader.GetLeaderIndexes().Cast<int>()
        .SelectMany(index => leader.GetLeaderLineIndexes(index).Cast<int>()).ToArray();
    public static void Run(Document document, IEnumerable<ObjectId>? selected, bool force)
    {
        var db = document.Database; var ed = document.Editor;
        var state = Plugin.For(document);
        using var tr = db.TransactionManager.StartTransaction();
        var rules = NodLabelStore.Read(tr, db, out _); var schema = state.Schema.Get();
        var resolver = new TargetResolver(tr, db, schema);
        var work = new List<(MLeader Leader, LeaderData Data, Entity? Target, LabelRule? Rule)>();
        var total = 0; var changed = 0; var unchanged = 0; var rebound = 0; var detached = 0; var errors = 0; var skipped = 0; var manual = 0;
        void Warn(string text) => ed.WriteMessage("\n" + text);
        if (schema.Schema == null) Warn("В чертеже нет схемы VELEC: значения выводятся без справочников и точности.");
        foreach (var id in (selected ?? All(tr, db)).Distinct())
        {
            if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is MLeader leader)) continue;
            if (!LeaderFactory.TryData(leader, out var data, out var error))
            { if (error != null) { total++; errors++; Warn(leader.Handle + ": " + error); } continue; }
            total++;
            if (leader.ContentType != ContentType.MTextContent) { errors++; Warn(leader.Handle + ": требуется содержимое MText."); continue; }
            if (((LayerTableRecord)tr.GetObject(leader.LayerId, OpenMode.ForRead)).IsLocked)
            { skipped++; Warn(leader.Handle + ": слой заблокирован; пропущена."); continue; }
            var owner = tr.GetObject(leader.OwnerId, OpenMode.ForRead) as BlockTableRecord;
            if (owner?.IsLayout != true || owner.IsFromExternalReference) { skipped++; continue; }
            if (!Arrow(leader, out var arrow)) { errors++; Warn(leader.Handle + ": требуется ровно одна стрелка."); continue; }
            var target = resolver.Resolve(data!, arrow, leader.OwnerId, TargetGeometry.Tolerance(leader.TextHeight, Plugin.Settings.Tolerance), out var offTarget);
            if (offTarget) Warn(leader.Handle + ": цель найдена по DEVICE_ID; стрелка не на объекте.");
            var values = target == null ? null : resolver.Values(target);
            if (values?.Format == XDataFormat.None) Warn(leader.Handle + ": у цели нет данных.");
            var rule = target == null ? null : rules.Select(data!.Rule, values!.Template, target.GetType().Name)?.WithFormats(data!.Top, data.Bottom);
            work.Add((leader, data!, target, rule));
        }
        var evaluator = state.Evaluator(schema, work.Where(w => w.Rule != null).Select(w => w.Rule!), resolver.Snapshots);
        var calculations = new Dictionary<ObjectId, ObjectCalculation>();
        foreach (var item in work)
        {
            var leader = item.Leader; var currentText = LeaderFactory.Text(leader);
            LabelText? label = null;
            if (item.Target != null && item.Rule != null)
            {
                if (!calculations.TryGetValue(item.Target.ObjectId, out var calculation))
                    calculations.Add(item.Target.ObjectId, calculation = evaluator.Calculate(resolver.Snapshot(item.Target)));
                label = evaluator.Evaluate(calculation, item.Rule);
                foreach (var error in label.Errors) Warn(leader.Handle + ": " + error);
            }
            var plan = new LeaderUpdatePlan(item.Data, currentText, item.Target?.Handle.ToString(),
                item.Target == null ? null : resolver.Values(item.Target).DeviceId, label, force);
            if (item.Target != null && item.Rule == null)
            { plan.Data.State = LeaderState.FormatError; Warn(leader.Handle + ": правило не найдено для шаблона " + resolver.Values(item.Target).Template); }
            if (plan.Data.State == LeaderState.Detached) { detached++; Warn(leader.Handle + ": отвязана; цель " + item.Data.Target + "."); }
            if (plan.Data.State == LeaderState.FormatError) errors++;
            if (plan.Data.State == LeaderState.ManualText) { manual++; Warn(leader.Handle + ": ручной текст сохранён."); }
            if (plan.Rebound) { rebound++; Warn(leader.Handle + ": перепривязана: " + item.Data.Target + " → " + plan.Data.Target); }
            if (!plan.Changed && plan.Data.SameAs(item.Data)) { unchanged++; continue; }
            plan.Data.Write(); // Validate before opening any leader for write.
            tr.GetObject(leader.ObjectId, OpenMode.ForWrite);
            if (currentText != plan.Text)
            {
                using var text = leader.MText; text.Contents = plan.Text; leader.MText = text;
                if (label != null) LeaderFactory.SetAttachment(leader, label);
            }
            LeaderFactory.WriteData(tr, db, leader, plan.Data); changed++;
        }
        tr.Commit();
        Warn($"Выносок: {total}, изменено: {changed}, без изменений: {unchanged}, перепривязано: {rebound}, отвязано: {detached}, ошибок: {errors}, ручной текст: {manual}, пропущено: {skipped}.");
        if (detached + errors + manual > 0) Warn("Выбрать проблемные выноски: SL_SELECT_PROBLEMS.");
        state.Trace("Целей прочитано: " + resolver.Snapshots.Count() + "; расчётов объектов: " + calculations.Count);
    }
}
