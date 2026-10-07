using System;
using Rimisekai.Character;
using Rimisekai.Housing;

namespace Rimisekai.Clock;

/// <summary>
/// 天气演化与季节对生产的影响表。
/// 天气按小时步进做马尔可夫演化：每种天气有持续概率（对应现实天气系统的寿命），
/// 发生转变时只在相邻天气间转移；状态空间按季隔离——冬不下雨，非冬不落雪。
/// </summary>
public static class WorldEffects
{
    /// <summary>
    /// 天气演化一步（每小时调用一次）。换季瞬间残留的异季天气
    /// （如冬天的雨）一步归位成多云，再走正常演化。
    /// </summary>
    public static Weather Advance(Weather current, Season season, Random random)
    {
        if (!Belongs(current, season))
            return Weather.Cloud;
        if (random.Next(100) < StayPercent(current))
            return current;
        return NextOf(current, season, random);
    }

    /// <summary>该天气是否属于这个季节会出现的状态。</summary>
    public static bool Belongs(Weather weather, Season season) =>
        season == Season.Winter
            ? weather is Weather.Clear or Weather.Cloud or Weather.Snow
                or Weather.HeavySnow or Weather.Blizzard or Weather.Wind
            : weather is Weather.Clear or Weather.Cloud or Weather.Rain
                or Weather.HeavyRain or Weather.Thunder or Weather.Wind;

    /// <summary>每小时保持不变的概率。均值即一场天气的寿命：晴约一天半，雷雨约七小时。</summary>
    private static int StayPercent(Weather weather) => weather switch
    {
        Weather.Clear => 97,
        Weather.Cloud => 95,
        Weather.Rain => 93,
        Weather.HeavyRain => 90,
        Weather.Thunder => 85,
        Weather.Wind => 90,
        Weather.Snow => 94,
        Weather.HeavySnow => 92,
        Weather.Blizzard => 90,
        _ => 97,
    };

    private static Weather NextOf(Weather current, Season season, Random random) => current switch
    {
        // 晴的出路只有先转多云；风是过客，归于多云或晴。
        Weather.Clear => Weather.Cloud,
        Weather.Wind => random.Next(100) < 60 ? Weather.Cloud : Weather.Clear,
        Weather.Cloud => CloudTurn(season, random),
        Weather.Rain => season == Season.Summer && random.Next(100) < 10
            ? Weather.Thunder
            : Pick(random, (35, Weather.Cloud), (35, Weather.HeavyRain), (20, Weather.Clear)),
        Weather.HeavyRain => season == Season.Summer && random.Next(100) < 20
            ? Weather.Thunder
            : Pick(random, (45, Weather.Rain), (35, Weather.Cloud)),
        Weather.Thunder => Pick(random, (55, Weather.Rain), (25, Weather.HeavyRain), (20, Weather.Cloud)),
        Weather.Snow => Pick(random, (40, Weather.HeavySnow), (30, Weather.Cloud), (15, Weather.Clear)),
        Weather.HeavySnow => Pick(random, (40, Weather.Snow), (30, Weather.Blizzard), (30, Weather.Cloud)),
        Weather.Blizzard => Pick(random, (40, Weather.HeavySnow), (30, Weather.Snow), (30, Weather.Cloud)),
        _ => Weather.Cloud,
    };

    /// <summary>多云是枢纽：往雨族/雪族/晴/风的去向由季节定调。夏季对流最烈，秋雨族转弱，冬季只出雪族。</summary>
    private static Weather CloudTurn(Season season, Random random) => season switch
    {
        Season.Summer => Pick(random,
            (30, Weather.Rain), (30, Weather.Thunder), (20, Weather.HeavyRain),
            (10, Weather.Clear), (10, Weather.Wind)),
        Season.Spring => Pick(random,
            (45, Weather.Rain), (15, Weather.HeavyRain), (10, Weather.Thunder),
            (20, Weather.Clear), (10, Weather.Wind)),
        Season.Autumn => Pick(random,
            (45, Weather.Rain), (15, Weather.HeavyRain), (5, Weather.Thunder),
            (25, Weather.Clear), (10, Weather.Wind)),
        _ => Pick(random,
            (40, Weather.Snow), (35, Weather.Clear), (15, Weather.HeavySnow), (10, Weather.Wind)),
    };

    private static Weather Pick(Random random, params (int Weight, Weather Weather)[] picks)
    {
        var total = 0;
        foreach (var (weight, _) in picks)
            total += weight;
        var roll = random.Next(total);
        foreach (var (weight, weather) in picks)
        {
            if (roll < weight)
                return weather;
            roll -= weight;
        }
        return picks[^1].Weather;
    }

    /// <summary>雨具：背包里有雨伞或雨衣就算带了，自动起效，不占装备位。</summary>
    public const string UmbrellaItemId = "雨伞";
    public const string RaincoatItemId = "雨衣";

    public static bool HasRainGear(CharacterState who) =>
        who.Bag.Get(UmbrellaItemId) > 0 || who.Bag.Get(RaincoatItemId) > 0;

    /// <summary>恶劣天气：暴雨、雷雨、大风、大雪、暴雪。赶路与露天劳作都受拖累。</summary>
    public static bool IsSevere(Weather weather) =>
        weather is Weather.HeavyRain or Weather.Thunder or Weather.Wind
            or Weather.HeavySnow or Weather.Blizzard;

    /// <summary>露天无雨具时的湿度增速（每 5 分钟一格，湿透阈 100）。0 表示淋不湿。</summary>
    public static int WetRatePerTick(Weather weather) => weather switch
    {
        Weather.Rain => 2,
        Weather.HeavyRain or Weather.Thunder => 4,
        _ => 0,
    };

    /// <summary>恶劣天气下经过一个室外房间的体力消耗。</summary>
    public const int SevereMoveStamina = 10;

    /// <summary>这间房是不是室外（带"室外"标签）。</summary>
    public static bool OutdoorRoom(Territory territory, int roomId) =>
        territory.Rooms.Find(r => r.Id == roomId)?.HasTag("室外") == true;

    /// <summary>
    /// 结算一个人这 5 分钟的衣着干湿：露天、正下着雨、又没带雨具才积湿，
    /// 否则晾干一档。湿透只作口上/地文的状态条件，不另扣数值。
    /// </summary>
    public static void SettleWetness(CharacterState who, Territory territory, int roomId, Weather weather)
    {
        var rate = WetRatePerTick(weather);
        if (rate > 0 && !HasRainGear(who) && OutdoorRoom(territory, roomId))
            who.Condition.Soak(rate);
        else
            who.Condition.Dry(1);
    }

    /// <summary>恶劣天气走进室外房间：赶路消耗体力。其余情况不动。</summary>
    public static void SpendMoveStamina(CharacterState? who, Territory territory, int roomId, Weather weather)
    {
        if (who == null || !IsSevere(weather) || !OutdoorRoom(territory, roomId))
            return;
        who.Condition.Spend(SevereMoveStamina, 0);
    }

    public static int YieldPercent(ActionKind action, Season season, Weather weather)
    {
        if (season == Season.Winter && action is ActionKind.Till or ActionKind.Cook)
            return 50;
        if (weather is Weather.Rain or Weather.HeavyRain or Weather.Thunder
            && action is ActionKind.Mine or ActionKind.Fell)
            return 70;
        if (weather is Weather.Snow or Weather.HeavySnow or Weather.Blizzard
            && action is ActionKind.Till or ActionKind.Mine or ActionKind.Fell)
            return 70;
        return 100;
    }
}
