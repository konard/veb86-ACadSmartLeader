using AcadPaletteSetXData.Core;
using ACadSmartLeader.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Xunit;

namespace ACadSmartLeader;

public sealed class NodLabelStoreTests
{
    [Fact]
    public void SavingAgainReplacesExistingRuleRecords()
    {
        var tr = new Transaction();
        var db = new Database(tr);
        var original = new LabelRules();
        original.Rules.Add(Rule("KEEP", "Old text"));
        original.Rules.Add(Rule("REMOVE", "Remove me"));
        NodLabelStore.Write(tr, db, original);

        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        var root = (DBDictionary)tr.GetObject(nod.GetAt(NodLabelStore.Root), OpenMode.ForRead);
        var labels = (DBDictionary)tr.GetObject(root.GetAt("LABELS"), OpenMode.ForRead);
        var oldKeep = tr.GetObject(labels.GetAt("KEEP"), OpenMode.ForRead);
        var oldRemove = tr.GetObject(labels.GetAt("REMOVE"), OpenMode.ForRead);

        var edited = new LabelRules();
        edited.Rules.Add(Rule("NEW", "New rule"));
        edited.Rules.Add(Rule("keep", "Edited text"));
        edited.Rules[1].Extra.Add("custom", new JsonNumber(42L));
        edited.Extra.Add("source", new JsonString("drawing"));
        NodLabelStore.Write(tr, db, edited);

        var saved = NodLabelStore.Read(tr, db, out var stored);
        Assert.True(stored);
        Assert.Equal(edited.ToJson(), saved.ToJson());
        Assert.False(labels.Contains("REMOVE"));
        Assert.True(oldKeep.IsErased);
        Assert.True(oldRemove.IsErased);
    }

    private static LabelRule Rule(string id, string top) => new() { Id = id, Caption = id, Top = top };
}
