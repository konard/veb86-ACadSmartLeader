using System.Collections;

namespace Autodesk.AutoCAD.DatabaseServices;

// Only the database operations used by NodLabelStore are modeled here.
internal enum OpenMode { ForRead, ForWrite }
internal readonly record struct ObjectId(int Index);
internal readonly record struct DBDictionaryEntry(string Key, ObjectId Value);
internal readonly record struct TypedValue(int TypeCode, object Value);

internal class DBObject
{
    public ObjectId ObjectId { get; set; }
    public bool IsErased { get; private set; }
    public void Erase() => IsErased = true;
}

internal sealed class Database(Transaction tr)
{
    public ObjectId NamedObjectsDictionaryId { get; } = tr.Add(new DBDictionary());
}

internal sealed class Transaction
{
    private readonly List<DBObject> objects = new();

    internal ObjectId Add(DBObject obj)
    {
        obj.ObjectId = new ObjectId(objects.Count);
        objects.Add(obj);
        if (obj is DBDictionary dictionary) dictionary.Transaction = this;
        return obj.ObjectId;
    }

    public DBObject GetObject(ObjectId id, OpenMode mode) => objects[id.Index];
    public void AddNewlyCreatedDBObject(DBObject obj, bool add) { }
}

internal sealed class DBDictionary : DBObject, IEnumerable
{
    private readonly Hashtable entries = new(StringComparer.OrdinalIgnoreCase);
    internal Transaction Transaction { private get; set; } = null!;

    public bool Contains(string key) => entries.ContainsKey(key);
    public ObjectId GetAt(string key) => (ObjectId)entries[key]!;
    public void Remove(string key) => entries.Remove(key);
    public void SetAt(string key, DBObject obj) => entries.Add(key, Transaction.Add(obj));

    public IEnumerator<DBDictionaryEntry> GetEnumerator()
    {
        foreach (DictionaryEntry entry in entries)
            yield return new DBDictionaryEntry((string)entry.Key, (ObjectId)entry.Value!);
    }

    // The interface exposes DictionaryEntry, while the typed enumerator exposes DBDictionaryEntry.
    IEnumerator IEnumerable.GetEnumerator() => entries.GetEnumerator();
}

internal sealed class Xrecord : DBObject
{
    public ResultBuffer? Data { get; set; }
}

internal sealed class ResultBuffer(TypedValue[] values) : IDisposable
{
    public TypedValue[] AsArray() => values;
    public void Dispose() { }
}
