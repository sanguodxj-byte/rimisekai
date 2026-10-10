using System.Linq;
using Rimisekai.Character;
using Xunit;

/// <summary>特质具体影响：由效果方法求差得出，跟着数值走；玩家可见文字不带冒号。</summary>
public class TraitEffectLinesTests
{
    [Fact]
    public void LinesFollowTheEffectMethods()
    {
        Assert.Contains("学习速度 -50%", Traits.EffectLines(Trait.SlowLearner));
        Assert.Contains("夜间工作效率 +15%", Traits.EffectLines(Trait.NightOwl));
        Assert.Contains("陌生时社交好感 -20%", Traits.EffectLines(Trait.SlowWarmer));
    }

    [Fact]
    public void NoColonInAnyLine()
    {
        foreach (var def in Traits.Catalog)
            Assert.DoesNotContain(Traits.EffectLines(def.Trait), l => l.Contains(':') || l.Contains('：'));
    }
}
