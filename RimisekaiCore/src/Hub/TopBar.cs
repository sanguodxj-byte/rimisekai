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

    private static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春",
        Season.Summer => "夏",
        Season.Autumn => "秋",
        Season.Winter => "冬",
        _ => "?",
    };

    private static string WeatherName(Weather weather) => weather switch
    {
        Weather.Clear => "晴",
        Weather.Cloud => "多云",
        Weather.Rain => "雨",
        Weather.Snow => "雪",
        Weather.HeavyRain => "暴雨",
        Weather.Thunder => "雷雨",
        Weather.Wind => "大风",
        Weather.HeavySnow => "大雪",
        Weather.Blizzard => "暴雪",
        _ => "?",
    };
}
