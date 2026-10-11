using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rimisekai.Catalog;

/// <summary>
/// 挑战等级（CR），取值照 D&amp;D：0、1/8、1/4、1/2、1–30。内容表里一律写成字符串（"1/4"、"5"）。
/// 内部按八分之一记（<see cref="Eighths"/>），比较、分桶都是整数运算。
/// CR 对应我方等级、经验值预算见 <see cref="Defs.ChallengeDef"/>。
/// </summary>
[JsonConverter(typeof(ChallengeRatingConverter))]
public readonly record struct ChallengeRating(int Eighths) : IComparable<ChallengeRating>
{
    public const int Max = 30;

    public static ChallengeRating Zero => new(0);

    /// <summary>整数 CR（1–30）。</summary>
    public static ChallengeRating Whole(int cr) =>
        cr is >= 1 and <= Max ? new(cr * 8) : throw new ArgumentOutOfRangeException(nameof(cr), cr, "整数 CR 只取 1–30");

    /// <summary>按内容表写法解析："0"、"1/8"、"1/4"、"1/2"、"1"…"30"；别的写法直接抛。</summary>
    public static ChallengeRating Parse(string text) => text switch
    {
        "0" => new(0),
        "1/8" => new(1),
        "1/4" => new(2),
        "1/2" => new(4),
        _ when int.TryParse(text, out var whole) && whole is >= 1 and <= Max && whole.ToString() == text => new(whole * 8),
        _ => throw new FormatException($"CR 只取 0、1/8、1/4、1/2、1–30，读到「{text}」"),
    };

    public double Value => Eighths / 8.0;

    /// <summary>升几档整数 CR（委托板正主按星数加码用），封顶 30；分数 CR 先进到 1 再加。</summary>
    public ChallengeRating Plus(int whole) =>
        whole <= 0 ? this : Whole(Math.Min(Max, Math.Max(1, Eighths / 8) + whole));

    public int CompareTo(ChallengeRating other) => Eighths.CompareTo(other.Eighths);

    public static bool operator <(ChallengeRating a, ChallengeRating b) => a.Eighths < b.Eighths;
    public static bool operator >(ChallengeRating a, ChallengeRating b) => a.Eighths > b.Eighths;
    public static bool operator <=(ChallengeRating a, ChallengeRating b) => a.Eighths <= b.Eighths;
    public static bool operator >=(ChallengeRating a, ChallengeRating b) => a.Eighths >= b.Eighths;

    public override string ToString() => Eighths switch
    {
        0 => "0",
        1 => "1/8",
        2 => "1/4",
        4 => "1/2",
        _ when Eighths % 8 == 0 => (Eighths / 8).ToString(),
        _ => throw new InvalidOperationException($"非法 CR 八分值 {Eighths}"),
    };
}

public sealed class ChallengeRatingConverter : JsonConverter<ChallengeRating>
{
    public override ChallengeRating Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? ChallengeRating.Parse(reader.GetString()!)
            : throw new JsonException("CR 一律写成字符串，如 \"1/4\"、\"5\"");

    public override void Write(Utf8JsonWriter writer, ChallengeRating value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
