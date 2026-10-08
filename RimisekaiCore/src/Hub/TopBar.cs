using Rimisekai.Clock;

namespace Rimisekai.Hub;

public readonly record struct HubHeader(string Place, Season Season, Weather Weather, int Hour, int Minute, long Money);

public sealed partial class HubSession
{
    public Weather Weather { get => State.Weather; set => State.Weather = value; }

    public HubHeader Header()
    {
        var place = MapTitle();
        return new HubHeader(place, State.Clock.Season, Weather, State.Clock.Minutes / 60, State.Clock.Minutes % 60, State.Money);
    }

    /// <summary>季节名（全项目唯一一份）：春季 / 夏季 / 秋季 / 冬季，顶栏与日志同词。</summary>
    public static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春季",
        Season.Summer => "夏季",
        Season.Autumn => "秋季",
        Season.Winter => "冬季",
        _ => throw new System.ArgumentOutOfRangeException(nameof(season), season, null),
    };

    /// <summary>天气名（全项目唯一一份）：单字加「天」，复合词原样——晴天 / 阴天 / 雨天 / 雪天 / 暴雨 / 雷雨 / 大风 / 大雪 / 暴雪。
    /// 顶栏与日志都走这里，变天日志与顶栏用同一个词。</summary>
    public static string WeatherName(Weather weather) => weather switch
    {
        Weather.Clear => "晴天",
        Weather.Cloud => "阴天",
        Weather.Rain => "雨天",
        Weather.Snow => "雪天",
        Weather.HeavyRain => "暴雨",
        Weather.Thunder => "雷雨",
        Weather.Wind => "大风",
        Weather.HeavySnow => "大雪",
        Weather.Blizzard => "暴雪",
        _ => throw new System.ArgumentOutOfRangeException(nameof(weather), weather, null),
    };
}
