namespace Rimisekai.Character;

/// <summary>
/// 亲密门槛：好感够不够做某件亲近的事。档位见社交（摸头 100 / 接触 300 / 拥抱 500 / 亲吻 600）与同床 800。
/// 判定＝基础档 × 特质百分比（<see cref="PersonalityTraits.TouchGatePercent"/>），再按心情修正
/// （心情 50 为中点，每 10 点心情折 4 档）。会话侧的社交拒绝与领地侧的「上不上同一张床」共用这一条。
/// </summary>
public static class Intimacy
{
    /// <summary>同床：比亲吻（600）更高——一起过夜是更亲近的事。</summary>
    public const int ShareBed = 800;

    public static bool Accepts(CharacterState who, int favor)
    {
        var gate = favor * PersonalityTraits.TouchGatePercent(who) / 100
            - (int)((who.Affect.Mood - 50) * 0.4f);
        return who.Condition.Favor >= System.Math.Max(0, gate);
    }

    /// <summary>肯不肯和主人睡同一张床。</summary>
    public static bool SharesBed(CharacterState who) => Accepts(who, ShareBed);
}
