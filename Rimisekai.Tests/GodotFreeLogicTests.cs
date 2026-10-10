using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 游戏逻辑层不得依赖 Godot（2026-10-10 主人定：前后端合并为单一程序集后，靠扫描守住原 Core 的纯逻辑边界）。
/// 原 RimisekaiCore 的目录并入 RimisekaiFrontend/src 后，编译器不再拦截逻辑代码引用 Godot，
/// 这里扫描这些目录：去掉注释与字符串字面量后，不得出现 Godot 命名空间（using Godot、Godot.X、global::Godot）。
/// 逻辑层可脱离引擎直接单测，就靠这条守住。
/// </summary>
public sealed class GodotFreeLogicTests
{
    private static readonly string[] LogicDirs =
    {
        "Catalog", "Character", "Clock", "Combat", "Command", "Defs", "Housing",
        "Hub", "PoiMap", "Quest", "Save", "Voice", "WorldMap",
    };

    private static readonly string[] LogicFiles =
    {
        Path.Combine("Session", "Session.cs"),
        Path.Combine("Session", "BattleSession.cs"),
    };

    // 依次匹配：块注释、行注释、逐字/插值/普通字符串、字符字面量；命中后整段抹掉。
    private static readonly Regex CommentOrLiteral = new(
        @"/\*.*?\*/|//[^\n]*|@""(?:[^""]|"""")*""|\$?""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)+'",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex GodotReference = new(@"\bGodot\b", RegexOptions.Compiled);

    private static string SrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "RimisekaiFrontend", "src");
    }

    internal static bool ReferencesGodot(string source) =>
        GodotReference.IsMatch(CommentOrLiteral.Replace(source, " "));

    private static IEnumerable<string> LogicSources()
    {
        var src = SrcRoot();
        foreach (var d in LogicDirs)
        {
            var path = Path.Combine(src, d);
            Assert.True(Directory.Exists(path), $"逻辑目录不存在：{path}");
            foreach (var f in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
                yield return f;
        }
        foreach (var f in LogicFiles)
        {
            var path = Path.Combine(src, f);
            Assert.True(File.Exists(path), $"逻辑文件不存在：{path}");
            yield return path;
        }
    }

    [Fact]
    public void Logic_layer_does_not_reference_Godot()
    {
        var files = LogicSources().ToList();
        Assert.NotEmpty(files);
        var offenders = files.Where(f => ReferencesGodot(File.ReadAllText(f))).ToList();
        Assert.True(offenders.Count == 0, "逻辑层不得引用 Godot：\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("using Godot;\nclass A {}", true)]
    [InlineData("class A : Godot.Node {}", true)]
    [InlineData("class A { global::Godot.Vector2 v; }", true)]
    [InlineData("using V = Godot.Vector2;", true)]
    [InlineData("// 供 Godot 虚拟文件系统回填\nclass A {}", false)]
    [InlineData("/// 脱离 Godot 直接测\nclass A {}", false)]
    [InlineData("/* Godot */ class A {}", false)]
    [InlineData("class A { string s = \"Godot\"; }", false)]
    [InlineData("class GodotLike {}", false)]
    public void Detector_fixture(string source, bool expected) =>
        Assert.Equal(expected, ReferencesGodot(source));
}
