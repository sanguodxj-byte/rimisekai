using System;

namespace Rimisekai.Clock;

/// <summary>
/// 一天 24 小时。据点经营按 6 小时一块委派（对应原作朝昼夕晩的间隔，但用时钟而不是四段名）。
/// 跨过 24:00 才进入下一日。
/// </summary>
public sealed class GameClock
{
    public const int MinutesPerDay = 24 * 60;
    public const int SlotMinutes = 6 * 60;
    public const int SlotsPerDay = MinutesPerDay / SlotMinutes;
    public const int DaysPerSeason = 15;

    /// <summary>早上 8 点：开局时刻，也是睡觉的醒来时刻。</summary>
    public const int WakeMinutes = 8 * 60;
    /// <summary>晚上 8 点：就寝门槛，此前睡不着。</summary>
    public const int BedtimeMinutes = 20 * 60;
    /// <summary>到 8 点睡不满这个时长算晚睡。</summary>
    public const int ShortSleepMinutes = 4 * 60;
    /// <summary>晚睡时延长到睡满的时长。</summary>
    public const int FullSleepMinutes = 8 * 60;

    public int Day { get; private set; } = 1;
    public int Minutes { get; private set; } = WakeMinutes;

    /// <summary>当前小时（0-23）。</summary>
    public int Hour => Minutes / 60;

    /// <summary>自开局累计的总分钟数（含跨日）。</summary>
    public int TotalMinutes => (Day - 1) * MinutesPerDay + Minutes;

    public Season Season => (Season)(((Day - 1) / DaysPerSeason) % 4);
    public int Year => ((Day - 1) / (DaysPerSeason * 4)) + 1;
    public int Week => ((Day - 1) % DaysPerSeason) + 1;
    public int Slot => Minutes / SlotMinutes;

    public void Advance(int minutes)
    {
        if (minutes < 0)
            throw new ArgumentOutOfRangeException(nameof(minutes));
        Minutes += minutes;
        while (Minutes >= MinutesPerDay)
        {
            Minutes -= MinutesPerDay;
            Day++;
        }
    }

    public void SkipToNextDay()
    {
        Minutes = 0;
        Day++;
    }

    public void SetTime(int day, int minutes)
    {
        Day = day < 1 ? 1 : day;
        Minutes = minutes < 0 ? 0 : minutes % MinutesPerDay;
    }
}

public enum Season
{
    Spring = 0,
    Summer = 1,
    Autumn = 2,
    Winter = 3,
}

public enum Weather
{
    Clear,
    Cloud,
    Rain,
    Snow,

    /// <summary>暴雨：恶劣天气，露天淋湿更快。</summary>
    HeavyRain,

    /// <summary>雷雨：恶劣天气，暴雨加偶发惊雷。</summary>
    Thunder,

    /// <summary>大风：恶劣天气，不改降水，只拖慢露天劳作与赶路。</summary>
    Wind,

    /// <summary>大雪：恶劣天气。</summary>
    HeavySnow,

    /// <summary>暴雪：恶劣天气，雪族里最烈的一档。</summary>
    Blizzard,
}
