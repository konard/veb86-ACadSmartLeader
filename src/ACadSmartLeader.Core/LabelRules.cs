using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AcadPaletteSetXData.Core;

namespace ACadSmartLeader.Core;

public sealed class LabelRule
{
    public string Id { get; set; } = "";
    public string Caption { get; set; } = "";
    public List<string> Templates { get; set; } = new List<string>();
    public List<string> EntityTypes { get; set; } = new List<string>();
    public string Top { get; set; } = "";
    public string Bottom { get; set; } = "";
    public string Empty { get; set; } = "";
    public string Error { get; set; } = "#ОШИБКА";
    public string Style { get; set; } = "";
    public string Layer { get; set; } = "";
    public JsonObject Extra { get; } = new JsonObject();
    public override string ToString() => Caption + " (" + Id + ")";
    public LabelRule WithFormats(string? top, string? bottom)
    {
        var copy = (LabelRule)MemberwiseClone();
        copy.Top = top ?? Top; copy.Bottom = bottom ?? Bottom;
        return copy;
    }
}

public sealed class LabelRules
{
    public const string Format = "ACadSmartLeader.labels";
    public List<LabelRule> Rules { get; } = new List<LabelRule>();
    public JsonObject Extra { get; } = new JsonObject();

    public LabelRule? Select(string? ruleId, string? template, string entityType)
    {
        var explicitRule = Rules.FirstOrDefault(r => string.Equals(r.Id, ruleId, StringComparison.OrdinalIgnoreCase));
        // A removed group rule leaves the leader's text intact until the rule is restored or replaced.
        return explicitRule ?? (HasGroupSuffix(ruleId) ? null : SelectAutomatic(template, entityType, group: false));
    }

    public LabelRule? SelectGroup(string? template, string entityType) => SelectAutomatic(template, entityType, group: true);

    public static bool HasGroupSuffix(string? ruleId) => ruleId?.EndsWith("_GROUP", StringComparison.OrdinalIgnoreCase) == true;

    private LabelRule? SelectAutomatic(string? template, string entityType, bool group)
    {
        bool Accepts(LabelRule rule) => HasGroupSuffix(rule.Id) == group &&
            (rule.EntityTypes.Count == 0 || rule.EntityTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase));
        return Rules.FirstOrDefault(rule => Accepts(rule) && rule.Templates.Contains(template ?? "", StringComparer.OrdinalIgnoreCase)) ??
            Rules.FirstOrDefault(rule => Accepts(rule) && rule.Templates.Count == 0);
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Rules)
        {
            if (!Regex.IsMatch(rule.Id, "^[A-Za-z0-9_]+$") || !ids.Add(rule.Id)) errors.Add(rule.Id + ": неверный или повторяющийся id.");
            if (string.IsNullOrWhiteSpace(rule.Caption)) errors.Add(rule.Id + ": не задан caption.");
            if (string.IsNullOrEmpty(rule.Top) && string.IsNullOrEmpty(rule.Bottom)) errors.Add(rule.Id + ": задайте top или bottom.");
            if (System.Text.Encoding.UTF8.GetByteCount(rule.Id) > LeaderData.MaxStringBytes) errors.Add(rule.Id + ": id длиннее 255 байт.");
        }
        return errors;
    }

    public string ToJson()
    {
        var root = new JsonObject(); root.Add("format", new JsonString(Format)); root.Add("version", new JsonNumber(1L));
        root.Add("rules", new JsonArray(Rules.Select(r => (JsonValue)RuleJson(r))));
        foreach (var pair in Extra) root.Add(pair.Key, pair.Value);
        return root.ToJson() + "\n";
    }

    public static LabelRules Parse(string json, out IReadOnlyList<string> warnings)
    {
        var notices = new List<string>();
        var root = JsonValue.Parse(json) as JsonObject ?? throw new FormatException("Корень правил должен быть объектом.");
        if (Text(root, "format") != Format || !(root["version"] is JsonNumber n) || !n.TryGetInt32(out var version) || version != 1)
            throw new FormatException("Неподдерживаемый формат/версия правил.");
        if (!(root["rules"] is JsonArray array)) throw new FormatException("rules: ожидается массив.");
        var result = new LabelRules();
        foreach (var value in array)
        {
            var obj = value as JsonObject ?? throw new FormatException("Правило должно быть объектом.");
            var rule = new LabelRule {
                Id = Text(obj, "id"), Caption = Text(obj, "caption"), Top = Text(obj, "top"), Bottom = Text(obj, "bottom"),
                Templates = Strings(obj, "templates"), EntityTypes = Strings(obj, "entityTypes"), Empty = Text(obj, "empty"),
                Error = Text(obj, "error", "#ОШИБКА"), Style = Text(obj, "style"), Layer = Text(obj, "layer")
            };
            Extras(obj, rule.Extra, new[] { "id", "caption", "templates", "entityTypes", "top", "bottom", "empty", "error", "style", "layer" }, rule.Id);
            result.Rules.Add(rule);
        }
        Extras(root, result.Extra, new[] { "format", "version", "rules" }, "rules");
        var errors = result.Validate();
        if (errors.Count > 0) throw new FormatException(string.Join("\n", errors));
        warnings = notices;
        return result;

        void Extras(JsonObject obj, JsonObject target, string[] known, string context)
        {
            foreach (var pair in obj.Where(p => !known.Contains(p.Key, StringComparer.Ordinal)))
            { target.Add(pair.Key, pair.Value); notices.Add(context + ": неизвестное поле " + pair.Key + " сохранено."); }
        }
    }

    public static JsonObject RuleJson(LabelRule r)
    {
        var obj = new JsonObject();
        void Put(string key, string value) => obj.Add(key, new JsonString(value));
        Put("id", r.Id); Put("caption", r.Caption); Put("top", r.Top); Put("bottom", r.Bottom);
        obj.Add("templates", new JsonArray(r.Templates.Select(s => (JsonValue)new JsonString(s))));
        obj.Add("entityTypes", new JsonArray(r.EntityTypes.Select(s => (JsonValue)new JsonString(s))));
        Put("empty", r.Empty); Put("error", r.Error); Put("style", r.Style); Put("layer", r.Layer);
        foreach (var pair in r.Extra) obj.Add(pair.Key, pair.Value);
        return obj;
    }

    // Keep JSON extensions in their own field so a future JSON "order"/"extensions" field
    // cannot collide with the drawing's record metadata.
    public static JsonObject RecordJson(LabelRule rule, int order)
    {
        var result = new JsonObject();
        foreach (var pair in RuleJson(rule).Where(p => !rule.Extra.Any(e => e.Key == p.Key))) result.Add(pair.Key, pair.Value);
        result.Add("order", new JsonNumber((long)order));
        result.Add("extensions", rule.Extra);
        return result;
    }

    public static JsonObject RuleFromRecord(JsonObject record)
    {
        var result = new JsonObject();
        foreach (var pair in record.Where(p => p.Key != "order" && p.Key != "extensions")) result.Add(pair.Key, pair.Value);
        if (record["extensions"] is JsonObject extra)
            foreach (var pair in extra) result.Add(pair.Key, pair.Value);
        return result;
    }

    private static string Text(JsonObject obj, string key, string fallback = "") => obj[key] is null ? fallback :
        obj[key] is JsonString text ? text.Value : throw new FormatException(key + ": ожидается строка.");
    private static List<string> Strings(JsonObject obj, string key)
    {
        if (obj[key] is null) return new List<string>();
        if (!(obj[key] is JsonArray list) || list.Any(v => !(v is JsonString))) throw new FormatException(key + ": ожидается массив строк.");
        return list.Cast<JsonString>().Select(v => v.Value).ToList();
    }
}
