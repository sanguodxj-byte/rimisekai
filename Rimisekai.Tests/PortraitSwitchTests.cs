using System;
using System.IO;
using Rimisekai.Character;
using Rimisekai.Save;
using Xunit;

namespace Rimisekai.Tests;

public class PortraitSwitchTests
{
    [Fact]
    public void CharacterState_StoresIdentity_And_PortraitDiff()
    {
        var roster = new Roster();
        var player = roster.Add("你", master: true);
        Assert.Equal("圣骑士", player.Identity);
        Assert.Equal(1, player.PortraitDiff);

        var maid = roster.Add("璐米埃尔");
        Assert.Equal("女仆", maid.Identity);
        Assert.Equal(1, maid.PortraitDiff);

        player.PortraitDiff = 4;
        Assert.Equal(4, player.PortraitDiff);
    }

    [Fact]
    public void Generator_Assigns_Identity_And_Default_PortraitDiff()
    {
        var roster = new Roster();
        var gen = new CharacterGenerator(new System.Random(42));
        var c = gen.Generate(roster);
        Assert.False(string.IsNullOrEmpty(c.State.Identity));
        Assert.Equal(1, c.State.PortraitDiff);
    }

    [Fact]
    public void SaveAndRestore_Preserves_PortraitDiff_And_Identity()
    {
        var state = new GameState();
        var player = state.Roster.Add("你", master: true);
        player.PortraitDiff = 3;
        player.Identity = "圣骑士";

        var companion = state.Roster.Add("米拉");
        companion.Identity = "学者";
        companion.PortraitDiff = 5;

        var captured = SaveSystem.Capture(state);
        var restored = SaveSystem.Restore(captured);

        var restPlayer = restored.Roster.Master;
        Assert.NotNull(restPlayer);
        Assert.Equal("圣骑士", restPlayer.Identity);
        Assert.Equal(3, restPlayer.PortraitDiff);

        var restComp = restored.Roster.Find("米拉");
        Assert.NotNull(restComp);
        Assert.Equal("学者", restComp.Identity);
        Assert.Equal(5, restComp.PortraitDiff);
    }

    [Fact]
    public void All_34_Identities_Have_All_Diff_Portraits_And_Avatars_On_Disk()
    {
        var identities = new[]
        {
            "alchemist", "apothecary", "archer", "assassin", "astrologer", "bard",
            "beast_tamer", "blacksmith", "butler", "carpenter", "cook", "courier",
            "dancer", "dragon_knight", "druid", "gardener", "guard", "hunter",
            "knight", "mage", "magic_swordsman", "maid", "mercenary", "merchant",
            "monk", "noble", "nun", "paladin", "priestess", "ranger",
            "scholar", "scholar_assistant", "thief", "warrior",
        };

        var rootDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        var portraitDir = Path.Combine(rootDir, "assets", "portraits", "identity_moe");
        var avatarDir = Path.Combine(rootDir, "assets", "avatars", "identity_moe");

        Assert.True(Directory.Exists(portraitDir), $"目录不存在: {portraitDir}");
        Assert.True(Directory.Exists(avatarDir), $"目录不存在: {avatarDir}");

        foreach (var id in identities)
        {
            var count = id == "mage" ? 6 : 5;
            for (var d = 1; d <= count; d++)
            {
                var portraitFile = Path.Combine(portraitDir, $"{id}_moe_diff{d}.png");
                var avatarFile = Path.Combine(avatarDir, $"{id}_moe_diff{d}.png");
                Assert.True(File.Exists(portraitFile), $"立绘缺失: {portraitFile}");
                Assert.True(File.Exists(avatarFile), $"头像缺失: {avatarFile}");
            }
        }
    }
}
