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

    public int Mood = Neutral;
    public int ChatDesire;
    public int LastTalkAt = -1;
    public int LastMealDay = -1;
    public int LastMealWindow = -1;
    public int LastPlayDay = -1;
    public int LastBoredDay = -1;
    public int IntimateDay = -1;
    public int[] IntimateRewards = new int[4];

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
