using System.Collections.Generic;

namespace Rimisekai.Character;

/// <summary>
/// 情绪与节律状态。心情 0-100，50 为中值；只影响行为选择和工作效率。
/// 回心情只靠：有品级的食物、喜欢的人交谈/接触、凯旋。普通吃饭睡觉不回。
/// </summary>
public sealed class Affect
{
    public const int Neutral = 50;
    public const int RefuseWorkAt = 10;
    public const int DriftPeriodMinutes = 240;
    public const int PatDailyLimit = 3;
    public const int ContactDailyLimit = 3;
    public const int HugDailyLimit = 1;
    public const int KissDailyLimit = 1;
    /// <summary>想找主人说话却等不到（被冷落）一次扣的心情。</summary>
    public const int IgnoredChatPenalty = 2;
    /// <summary>被冷落扣心情的每日上限（与摸头 / 亲密奖励同一套「按日计数」口径）：当天累计扣到这么多就不再扣。</summary>
    public const int IgnoredChatDailyCap = 6;

    public int Mood = Neutral;
    public int ChatDesire;
    public int LastTalkAt = -1;
    public int LastMealDay = -1;
    public int LastMealWindow = -1;
    public int LastMealMinute = -1;
    public int LastPlayDay = -1;
    public int LastBoredDay = -1;
    public int IntimateDay = -1;
    public int[] IntimateRewards = new int[4];
    public int IgnoredChatDay = -1;
    /// <summary>IgnoredChatDay 当天已因被冷落扣掉的心情（正数）。</summary>
    public int IgnoredChatTaken;

    /// <summary>被冷落一次：扣 <see cref="IgnoredChatPenalty"/>，当天累计不超过 <see cref="IgnoredChatDailyCap"/>。</summary>
    public void TakeIgnoredChat(int day)
    {
        if (IgnoredChatDay != day)
        {
            IgnoredChatDay = day;
            IgnoredChatTaken = 0;
        }
        var amount = System.Math.Min(IgnoredChatPenalty, IgnoredChatDailyCap - IgnoredChatTaken);
        if (amount <= 0)
            return;
        IgnoredChatTaken += amount;
        AddMood(-amount);
    }

    /// <summary>领每日亲密奖励。slot：0 摸头、1 身体接触、2 拥抱、3 亲吻。行动不限次数，只限奖励次数。</summary>
    public bool TakeReward(int day, int slot, int limit)
    {
        if (slot < 0 || slot > 3)
            return false;
        if (IntimateDay != day)
        {
            IntimateDay = day;
            System.Array.Clear(IntimateRewards, 0, IntimateRewards.Length);
        }
        if (IntimateRewards[slot] >= limit)
            return false;
        IntimateRewards[slot]++;
        return true;
    }

    /// <summary>打地铺醒来一次，心情掉这么多，并在之后 <see cref="FloorDays"/> 天里压着心情回不去。</summary>
    public const int FloorPenalty = 10;
    /// <summary>一夜地铺压心情的天数。</summary>
    public const int FloorDays = 3;
    /// <summary>地铺压心情的上限：连睡多夜叠到这么多就不再往下叠。</summary>
    public const int FloorPenaltyCap = 30;

    /// <summary>最近几夜打地铺的日子（只留还在作用期内的）。</summary>
    public List<int> FloorNights { get; } = new();

    /// <summary>
    /// 地铺此刻压着的心情：作用期（<see cref="FloorDays"/> 天）内每一夜 <see cref="FloorPenalty"/>，
    /// 至多 <see cref="FloorPenaltyCap"/>。
    /// </summary>
    public int FloorWeight(int day)
    {
        var nights = 0;
        foreach (var d in FloorNights)
            if (day - d < FloorDays)
                nights++;
        return System.Math.Min(FloorPenaltyCap, nights * FloorPenalty);
    }

    /// <summary>心情慢慢回归的那个值：平日是 <see cref="Neutral"/>，睡过地铺就被压低（见 <see cref="FloorWeight"/>）。</summary>
    public int Baseline(int day) => Neutral - FloorWeight(day);

    /// <summary>
    /// 打地铺醒来：记下这一夜，心情按「比昨天多压了多少」往下掉——头一夜 -10、连睡第二夜再 -10、第三夜再 -10，
    /// 叠满 <see cref="FloorPenaltyCap"/> 后接着睡地铺不再多掉；之后几天回归值跟着压低（见 <see cref="Baseline"/>），
    /// 过了作用期那一夜的份退掉，心情随回归慢慢回来。
    /// </summary>
    public void SleptOnFloor(int day)
    {
        var before = FloorWeight(day - 1);
        FloorNights.RemoveAll(d => day - d >= FloorDays);
        if (!FloorNights.Contains(day))
            FloorNights.Add(day);
        AddMood(System.Math.Min(0, before - FloorWeight(day)));
    }

    public void AddMood(int amount) =>
        Mood = System.Math.Clamp(Mood + amount, 0, 100);

    public void AddDesire(int amount) =>
        ChatDesire = System.Math.Clamp(ChatDesire + amount, 0, 100);

    /// <summary>心情工作效率乘数。过低直接拒绝工作。</summary>
    public double Efficiency() => Mood switch
    {
        >= 80 => 1.25,
        >= 65 => 1.1,
        >= 35 => 1.0,
        >= 20 => 0.8,
        _ => 0.6,
    };

    public bool AcceptsWork() => Mood > RefuseWorkAt;
}
