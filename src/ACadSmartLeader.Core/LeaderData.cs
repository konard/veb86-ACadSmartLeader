using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AcadPaletteSetXData.Core;

namespace ACadSmartLeader.Core;

public enum LeaderState : short { Bound, Detached, FormatError, ManualText }

/// <summary>The versioned BobrovLeader section. Never writes the target's BobrovXDATA.</summary>
public sealed class LeaderData
{
    public const string RegApp = "BobrovLeader";
    public const int MaxStringBytes = 255;
    public string Target { get; set; } = "0";
    public string TargetId { get; set; } = "";
    public string Rule { get; set; } = "";
    public string? Top { get; set; }
    public string? Bottom { get; set; }
    public string Text { get; set; } = "";
    public LeaderState State { get; set; }

    public LeaderData Copy() => (LeaderData)MemberwiseClone();

    public static string TextStamp(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) <= MaxStringBytes && !text.StartsWith("#", StringComparison.Ordinal)) return text;
        using var sha = SHA1.Create();
        return "#" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
    }

    public bool Matches(string text) => Text == TextStamp(text);

    public IReadOnlyList<DataValue> Write()
    {
        if (!ulong.TryParse(Target, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new FormatException("TARGET: ожидается handle (hex).");
        if (!Enum.IsDefined(typeof(LeaderState), State)) throw new FormatException("Неизвестный STATE.");
        var result = new List<DataValue> { new DataValue(1001, RegApp), new DataValue(1070, (short)1) };
        Pair("TARGET", 1005, Target);
        Pair("TARGET_ID", 1000, TargetId);
        Pair("RULE", 1000, Rule);
        if (Top != null) Pair("TOP", 1000, Top);
        if (Bottom != null) Pair("BOTTOM", 1000, Bottom);
        Pair("TEXT", 1000, Text);
        result.Add(new DataValue(1000, "STATE")); result.Add(new DataValue(1070, (short)State));
        return result;

        void Pair(string key, int code, string value)
        {
            if (Encoding.UTF8.GetByteCount(value) > MaxStringBytes)
                throw new FormatException(key + ": строка XDATA длиннее 255 байт; используйте правило.");
            result.Add(new DataValue(1000, key)); result.Add(new DataValue(code, value));
        }
    }

    public static bool TryRead(IReadOnlyList<DataValue> all, out LeaderData? data, out string? error)
    {
        data = null; error = null;
        var start = -1;
        for (var i = 0; i < all.Count; i++)
            if (all[i].TypeCode == 1001 && string.Equals(all[i].Value as string, RegApp, StringComparison.OrdinalIgnoreCase)) { start = i; break; }
        if (start < 0) return false;
        try
        {
            if (start + 1 >= all.Count || all[start + 1].TypeCode != 1070 || Convert.ToInt32(all[start + 1].Value, CultureInfo.InvariantCulture) != 1)
                throw new FormatException("Неподдерживаемая версия BobrovLeader.");
            var fields = new Dictionary<string, DataValue>(StringComparer.Ordinal);
            for (var i = start + 2; i < all.Count && all[i].TypeCode != 1001; i += 2)
            {
                if (all[i].TypeCode != 1000 || !(all[i].Value is string key) || i + 1 >= all.Count || all[i + 1].TypeCode == 1001)
                    throw new FormatException("Повреждённая пара BobrovLeader.");
                if (fields.ContainsKey(key)) throw new FormatException("Повтор ключа: " + key);
                fields.Add(key, all[i + 1]);
            }
            var parsed = new LeaderData {
                Target = String("TARGET", 1005, true)!, TargetId = String("TARGET_ID", 1000, false) ?? "",
                Rule = String("RULE", 1000, true)!, Top = String("TOP", 1000, false), Bottom = String("BOTTOM", 1000, false),
                Text = String("TEXT", 1000, true)!
            };
            if (!fields.TryGetValue("STATE", out var state) || state.TypeCode != 1070) throw new FormatException("Нет STATE.");
            parsed.State = (LeaderState)Convert.ToInt16(state.Value, CultureInfo.InvariantCulture);
            parsed.Write(); // Same validation for loaded data and new writes.
            data = parsed; return true;

            string? String(string key, int code, bool required)
            {
                if (!fields.TryGetValue(key, out var value))
                { if (required) throw new FormatException("Нет " + key); return null; }
                if (value.TypeCode != code || !(value.Value is string text)) throw new FormatException("Неверный тип " + key);
                return text;
            }
        }
        catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException)
        { error = e.Message; return false; }
    }

    public bool SameAs(LeaderData other) => Target == other.Target && TargetId == other.TargetId && Rule == other.Rule &&
        Top == other.Top && Bottom == other.Bottom && Text == other.Text && State == other.State;
}
