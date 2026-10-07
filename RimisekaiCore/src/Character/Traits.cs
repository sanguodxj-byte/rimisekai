using System.Collections.Generic;
using Rimisekai.Housing;

namespace Rimisekai.Character;

/// <summary>
/// 素质（Trait）的唯一入口：查询、授予、全量目录与所有游戏性/对话侧效果都从这里走。
/// 计算与数值在 PersonalityTraits；系统代码不允许直接读 Trait 枚举做判定
/// （架构测试锁死，允许清单：本文件、PersonalityTraits、CharacterGenerator、VoiceGenerationHub）。
/// </summary>
public static class Traits
{
    public static bool Has(this CharacterState character, Trait trait) =>
        character.Talents.Contains((int)trait);

    public static bool Grant(this CharacterState character, Trait trait) =>
        character.Talents.Add((int)trait);

    /// <summary>全量目录：40 条光谱特质 + 17 条机制素质。</summary>
    public static IReadOnlyList<PersonalityTraits.Def> Catalog => PersonalityTraits.Catalog;

    /// <summary>光谱特质全表（含身份标识与已并入别名）。</summary>
    public static IReadOnlyList<PersonalityTraits.Def> AllDefs => PersonalityTraits.All;

    /// <summary>生成器的抽取池：光谱六组 52 条。</summary>
    public static IReadOnlyList<PersonalityTraits.Def> Pool => PersonalityTraits.Pool;

    /// <summary>互斥对（光谱特质名）。</summary>
    public static (string A, string B)[] Exclusions => PersonalityTraits.Exclusions;

    // ---------- 学习 / 社交判定 ----------

    public static int LearnPercent(this CharacterState character) =>
        PersonalityTraits.LearnPercentTotal(character);

    public static int TalkDifficulty(this CharacterState character) =>
        PersonalityTraits.TalkDifficultyTotal(character);

    public static bool WillWork(this CharacterState character, bool hardLabor) =>
        PersonalityTraits.WillWork(character, hardLabor);

    public static bool RequiresWage(this CharacterState character) =>
        PersonalityTraits.RequiresWage(character);

    public static bool AcceptsInvite(this CharacterState character) =>
        PersonalityTraits.AcceptsInvite(character);

    public static bool QuickChant(this CharacterState character) =>
        character.Has(Trait.QuickChant);

    public static bool IsMaid(this CharacterState character) =>
        character.Has(Trait.Maid);

    public static bool IsMage(this CharacterState character) =>
        character.Has(Trait.Mage);

    // ---------- 光谱效果 ----------

    public static int WorkProgressPercent(this CharacterState c, ActionKind action, int hour) =>
        PersonalityTraits.WorkProgressPercent(c, action, hour);

    public static int MoodGainPercent(this CharacterState c) =>
        PersonalityTraits.MoodGainPercent(c);

    public static int ScaledMood(this CharacterState c, int mood) =>
        PersonalityTraits.ScaledMood(c, mood);

    public static int ScaledFavor(this CharacterState c, SocialKind kind, int favor) =>
        PersonalityTraits.ScaledFavor(c, kind, favor);

    public static int MealStamina(this CharacterState c, int stamina) =>
        PersonalityTraits.MealStamina(c, stamina);

    public static int MealSpirit(this CharacterState c, int spirit) =>
        PersonalityTraits.MealSpirit(c, spirit);

    public static int MealMoodPercent(this CharacterState c) =>
        PersonalityTraits.MealMoodPercent(c);

    public static int RestSpiritBonus(this CharacterState c) =>
        PersonalityTraits.RestSpiritBonus(c);

    public static int SeekBudgetDelta(this CharacterState c) =>
        PersonalityTraits.SeekBudgetDelta(c);

    // ---------- 机制素质效果 ----------

    public static int ChatDesireBonus(this CharacterState c) =>
        PersonalityTraits.ChatDesireBonus(c);

    public static int WakeHourFor(this CharacterState c) =>
        PersonalityTraits.WakeHourFor(c);

    public static int BedHourFor(this CharacterState c) =>
        PersonalityTraits.BedHourFor(c);

    public static int LoiterTicksFor(this CharacterState c) =>
        PersonalityTraits.LoiterTicksFor(c);

    public static int SitWeightDelta(this CharacterState c) =>
        PersonalityTraits.SitWeightDelta(c);

    public static int WanderChance(this CharacterState c) =>
        PersonalityTraits.WanderChance(c);

    /// <summary>某特质的具体数值影响（界面逐行列示）。</summary>
    public static IReadOnlyList<string> EffectLines(Trait trait) =>
        PersonalityTraits.EffectLines(trait);

    /// <summary>可对外展示的素质列表。</summary>
    public static IReadOnlyList<Trait> Visible(this CharacterState character)
    {
        var list = new List<Trait>();
        foreach (var id in character.Talents)
        {
            if (System.Enum.IsDefined(typeof(Trait), id))
                list.Add((Trait)id);
        }
        return list;
    }
}
