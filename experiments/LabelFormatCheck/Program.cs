// Checks the approach of the ТЗ, section "Вычисление текста полок": a label format of the leader is evaluated by
// the Core of acadPaletteSetXDATA as an extra CalculatedString added to a copy of the drawing schema, so the
// leader gets the same substitution rules as the palette (enum captions, precision, Да/Нет, defaults, #ОШИБКА).
using System.Globalization;
using System.Text.Json;
using AcadPaletteSetXData.Core;

var repo = args.Length > 0 ? args[0] : throw new ArgumentException("pass the path to acadPaletteSetXDATA");
var rulesPath = args.Length > 1 ? args[1] : Path.Combine("..", "..", "examples", "label-rules.json");
var loaded = ParameterSchemaJson.LoadFile(Path.Combine(repo, "examples", "schema.json"));
if (loaded.Schema == null) throw new InvalidOperationException("schema.json was not loaded");

var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
culture.NumberFormat.NumberDecimalSeparator = ",";

// Label rules of examples/label-rules.json (top and bottom shelf) and one broken rule for the error path.
using var document = JsonDocument.Parse(File.ReadAllText(rulesPath));
var rules = document.RootElement.GetProperty("rules").EnumerateArray()
    .SelectMany(rule => (rule.TryGetProperty("templates", out var templates) ? templates.EnumerateArray().Select(t => t.GetString()!) : new[] { "MOTOR", "CABLE" })
        .Select(template => (Rule: rule.GetProperty("id").GetString()!, Template: template, Top: Optional(rule, "top"), Bottom: Optional(rule, "bottom"))))
    .Append((Rule: "BROKEN", Template: "CABLE", Top: "@@[Unknown_Id]", Bottom: "@@[Cable_Mark"))
    .ToArray();

var schema = ParameterSchemaJson.Clone(loaded.Schema);
for (var i = 0; i < rules.Length; i++)
{
    // An absent shelf is not passed to Core: an empty format is an error there ("не задан format").
    if (rules[i].Top != "") schema.Parameters.Add(new SchemaParameter { Id = $"__LEADER_TOP_{i}", Type = "CalculatedString", Caption = "top", Format = rules[i].Top });
    if (rules[i].Bottom != "") schema.Parameters.Add(new SchemaParameter { Id = $"__LEADER_BOTTOM_{i}", Type = "CalculatedString", Caption = "bottom", Format = rules[i].Bottom });
}
var snapshot = new SchemaSnapshot(new SchemaNodReadResult(schema, null, Array.Empty<SchemaIssue>()));

var motor = V2("MOTOR",
    Pair("Device_Number", "М1"), Pair("NMO_Name", "Насос подпитки"), Pair("GC_HeadDevice", "ЩУ1"), Pair("GC_HDGroup", "3"),
    Pair("Power", 7.5), Pair("Current", 15.2));
var cable = V2("CABLE",
    Pair("Cable_Mark", "ВВГнг-LS"), Pair("Cable_Cores", 5), Pair("Cable_Section", 2.5),
    Pair("GC_HeadDevice", "ЩУ1"), Pair("GC_HDGroup", "3"));

for (var i = 0; i < rules.Length; i++)
{
    var target = rules[i].Template == "MOTOR" ? motor : cable;
    var calculation = new ObjectCalculation(XDataV2.Read(target, snapshot), culture);
    Console.WriteLine($"{rules[i].Rule} on {rules[i].Template}: [{rules[i].Top}] / [{rules[i].Bottom}]");
    Console.WriteLine($"  top    = {calculation.Text($"__LEADER_TOP_{i}")}");
    Console.WriteLine($"  bottom = {calculation.Text($"__LEADER_BOTTOM_{i}")}");
    foreach (var id in new[] { $"__LEADER_TOP_{i}", $"__LEADER_BOTTOM_{i}" })
        if (calculation.Find(id) is { IsError: true } error) Console.WriteLine($"  error  {id}: {error.Error}");
}

// The motor power changes: the same rule re-read gives the new value (what "Обновить" does for every leader).
var changed = V2("MOTOR", Pair("Device_Number", "М1"), Pair("Power", 11.0));
Console.WriteLine($"after change: {new ObjectCalculation(XDataV2.Read(changed, snapshot), culture).Text("__LEADER_BOTTOM_0")}");

static string Optional(JsonElement rule, string name) => rule.TryGetProperty(name, out var value) ? value.GetString()! : "";

static DataValue[] Pair(string id, object value) => new[]
{
    new DataValue(1000, id),
    value switch
    {
        double number => new DataValue(1040, number),
        int number => new DataValue(1071, number),
        bool flag => new DataValue(1070, (short)(flag ? 1 : 0)),
        _ => new DataValue(1000, value)
    }
};

static EntityDataSnapshot V2(string template, params DataValue[][] pairs) => new EntityDataSnapshot(
    "1F", template == "CABLE" ? "Polyline" : "BlockReference",
    new[] { new DataValue(1001, "BobrovXDATA"), new DataValue(1070, (short)2), new DataValue(1000, "TEMPLATE"), new DataValue(1000, template) }
        .Concat(pairs.SelectMany(pair => pair)).ToArray(),
    Array.Empty<DataRecord>());
