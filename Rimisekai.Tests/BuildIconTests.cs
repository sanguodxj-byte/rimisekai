using System;
using System.IO;
using System.Linq;
using Rimisekai.Defs;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>建造页每一格都画本件图标：可建的设施与房间各有一张 icons/build/{名}.svg（及其 .import），不许缺、不退回分类字形。</summary>
public sealed class BuildIconTests
{
    public BuildIconTests() => DefaultDefs.EnsureInitialized();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到 project.godot");
    }

    [Fact]
    public void Every_buildable_facility_and_room_has_an_icon()
    {
        var dir = Path.Combine(RepoRoot(), "icons", "build");
        var names = DefDatabase<FacilityDef>.All.Where(d => d.Buildable && d.BuildCategory != "").Select(d => d.Name)
            .Concat(DefDatabase<RoomDef>.All.Where(d => d.Buildable && d.BuildCategory != "").Select(d => d.Name))
            .ToList();
        // 只数内容表里的（带分类）；别的测试临时注册的假定义不算。
        Assert.Equal(43 + 26, names.Count);
        var missing = names.Where(n => !File.Exists(Path.Combine(dir, n + ".svg")) || !File.Exists(Path.Combine(dir, n + ".svg.import"))).ToList();
        Assert.True(missing.Count == 0, "缺图标：" + string.Join("、", missing));
    }
}
