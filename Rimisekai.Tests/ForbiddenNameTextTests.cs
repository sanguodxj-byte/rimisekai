using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Rimisekai.Tests;

/// <summary>
/// 玩家可见文字里禁止出现自造的项目名音译（2026-10-03 主人令：中文「里米塞凯」全项目清除）。
/// 这类字串从来不在内容表里，是写界面代码时顺手造的，所以只能靠扫描兜住。
/// 拉丁拼写（Rimisekai 命名空间、res:// 路径）不在禁列，那是工程标识。
/// </summary>
public sealed class ForbiddenNameTextTests
{
    private static readonly string[] Forbidden =
    {
        "里米塞凯", "里米塞", "黎米塞凯", "利米塞凯", "瑞米塞凯", "里米斯凯",
    };

    private static readonly Regex StringLiteral = new("\"(?:[^\"\\\\\\n]|\\\\.)*\"", RegexOptions.Compiled);

    private static List<string> Offenders(string line) => StringLiteral.Matches(line)
        .Select(m => m.Value)
        .Where(v => Forbidden.Any(v.Contains))
        .ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到 project.godot，测试工程定位失败");
    }

    private static IEnumerable<string> ScannedFiles()
    {
        var root = RepoRoot();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "RimisekaiFrontend", "src"), "*.cs", SearchOption.AllDirectories))
            yield return f;
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "content"), "*.json", SearchOption.AllDirectories))
            yield return f;
    }

    [Fact]
    public void 界面与内容表里不得出现自造译名()
    {
        var hits = new List<string>();
        foreach (var file in ScannedFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                hits.AddRange(Offenders(lines[i]).Select(bad => $"{Path.GetFileName(file)}:{i + 1} 字面量 {bad}"));
        }
        Assert.True(hits.Count == 0, "禁止的自造译名出现在玩家可见文字里：\n" + string.Join("\n", hits));
    }

    [Fact]
    public void 合成夹具_该报的报不该报的不报()
    {
        Assert.NotEmpty(Offenders("InkDraw.Text(this, c, \"里米塞凯\", FontTitle, Line, \"cm\");"));
        Assert.NotEmpty(Offenders("{\"body\": \"欢迎来到里米塞凯\"}".Replace("{", "").Replace("}", "")));
        Assert.Empty(Offenders("var p = \"res://RimisekaiFrontend/src/Ink/Portrait/Portrait.tscn\";"));
        Assert.Empty(Offenders("namespace Rimisekai.Ink; // 注释里出现 里米塞凯 也不算"));
    }
}
