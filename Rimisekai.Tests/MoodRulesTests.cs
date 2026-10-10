using Rimisekai.Character;
using Xunit;

namespace Rimisekai.Tests;

public class MoodRulesTests
{
    [Fact]
    public void Ignored_chat_costs_two_mood_each_capped_at_six_per_day()
    {
        var affect = new Affect();
        for (var i = 0; i < 5; i++)
            affect.TakeIgnoredChat(day: 3);
        Assert.Equal(Affect.Neutral - 6, affect.Mood);

        // 第二天重新计数。
        affect.TakeIgnoredChat(day: 4);
        Assert.Equal(Affect.Neutral - 8, affect.Mood);
    }
}
