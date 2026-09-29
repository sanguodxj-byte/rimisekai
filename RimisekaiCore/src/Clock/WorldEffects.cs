using System;
using Rimisekai.Housing;

namespace Rimisekai.Clock;

/// <summary>天气与季节对采集的影响表。数值是百分比，100 为不变。</summary>
public static class WorldEffects
{
    public static Weather RollWeather(Season season, Random? random = null)
    {
        random ??= new Random();
        var roll = random.Next(100);
        return season switch
        {
            Season.Spring => roll < 50 ? Weather.Clear : roll < 75 ? Weather.Cloud : Weather.Rain,
            Season.Summer => roll < 45 ? Weather.Clear : roll < 65 ? Weather.Cloud : Weather.Rain,
            Season.Autumn => roll < 40 ? Weather.Clear : roll < 70 ? Weather.Cloud : Weather.Rain,
            Season.Winter => roll < 30 ? Weather.Clear : roll < 60 ? Weather.Cloud : Weather.Snow,
            _ => Weather.Clear,
        };
    }

    public static int YieldPercent(ActionKind action, Season season, Weather weather)
    {
        if (season == Season.Winter && action is ActionKind.Till or ActionKind.Cook)
            return 50;
        if (weather == Weather.Rain && action is ActionKind.Mine or ActionKind.Fell)
            return 70;
        if (weather == Weather.Snow && action is ActionKind.Till or ActionKind.Mine or ActionKind.Fell or ActionKind.DrawWater)
            return 70;
        return 100;
    }
}
