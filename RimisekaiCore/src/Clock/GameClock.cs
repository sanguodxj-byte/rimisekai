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
    public const int DaysPerSeason = 7;

    public int Day { get; private set; } = 1;
    public int Minutes { get; private set; }

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
}
