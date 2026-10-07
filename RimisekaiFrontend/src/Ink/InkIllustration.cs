using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 插画资产管理门面。
/// 路径由 content/illustrations.json 配置表统一驱动，禁止代码硬编码。
/// 支持直接解码 PNG 字节，换图与热更无需重走 Godot 导入管线。
/// </summary>
public static class InkIllustration
{
    public const string ConfigPath = "res://content/illustrations.json";

    private static readonly Dictionary<string, Texture2D> TextureCache = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Dictionary<string, string>> _config = new(StringComparer.OrdinalIgnoreCase);
    private static bool _configLoaded;

    /// <summary>重新加载配置表与清空纹理缓存（支持运行时热更插画）。</summary>
    public static void ReloadConfig()
    {
        _configLoaded = false;
        TextureCache.Clear();
        EnsureConfig();
    }

    private static void EnsureConfig()
    {
        if (_configLoaded)
            return;
        _configLoaded = true;

        var jsonText = ReadConfigFile();
        if (string.IsNullOrEmpty(jsonText))
            return;

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (parsed != null)
                _config = parsed;
        }
        catch
        {
            // 配置解析失败时保持空表，依赖安全兜底
        }
    }

    private static string ReadConfigFile()
    {
        // 1. 优先通过 Godot FileAccess 读取
        if (Godot.FileAccess.FileExists(ConfigPath))
        {
            using var file = Godot.FileAccess.Open(ConfigPath, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
                return file.GetAsText();
        }

        // 2. 备选物理路径探查
        var searchStarts = new[]
        {
            Directory.GetCurrentDirectory(),
            AppDomain.CurrentDomain.BaseDirectory,
            "D:\\123\\rimisekai"
        };

        foreach (var start in searchStarts)
        {
            try
            {
                var cur = new DirectoryInfo(start);
                while (cur != null)
                {
                    var p = Path.Combine(cur.FullName, "content", "illustrations.json");
                    if (File.Exists(p))
                        return File.ReadAllText(p);
                    cur = cur.Parent;
                }
            }
            catch
            {
                // ignore
            }
        }

        return "";
    }

    /// <summary>获取战斗场景背景插画（默认读取 combat.default，配置为图2天然岩石洞窟）。</summary>
    public static Texture2D? GetCombatBackground(string key = "default") =>
        GetFromCategory("combat", key);

    /// <summary>获取房间插画（如 bedroom_day / bedroom_night / courtyard / tavern / workshop）。</summary>
    public static Texture2D? GetRoom(string key) =>
        GetFromCategory("rooms", key);

    /// <summary>获取地形插画（如 dark_forest / storm_mountains）。</summary>
    public static Texture2D? GetTerrain(string key) =>
        GetFromCategory("terrains", key);

    /// <summary>获取聚落插画（如 village / town）。</summary>
    public static Texture2D? GetSettlement(string key) =>
        GetFromCategory("settlements", key);

    /// <summary>兼容旧据点插画接口：优先取 courtyard 庭院，回退取战斗默认背景。</summary>
    public static Texture2D? Get() =>
        GetRoom("courtyard") ?? GetCombatBackground("default");

    /// <summary>
    /// 根据房间名称与当前时刻智能获取房间插画。
    /// 优先从 rooms / terrains / settlements 取匹配项；卧室支持昼夜差分。
    /// </summary>
    public static Texture2D? ForRoom(string roomName, int hour = 12)
    {
        EnsureConfig();
        var night = hour < 6 || hour >= 20;

        if (roomName.Contains("卧") || roomName.Contains("寝") || roomName.Contains("诊所"))
            return (night ? GetRoom("bedroom_night") : null) ?? GetRoom("bedroom_day") ?? GetRoom("bedroom_night");

        if (roomName.Contains("院") || roomName.Contains("草药") || roomName.Contains("池") || roomName.Contains("蜂") || roomName.Contains("果") || roomName.Contains("浴"))
            return GetRoom("courtyard");

        if (roomName.Contains("厅") || roomName.Contains("客") || roomName.Contains("酒") || roomName.Contains("馆") || roomName.Contains("堂") || roomName.Contains("剧") || roomName.Contains("面包") || roomName.Contains("书"))
            return GetRoom("tavern");

        if (roomName.Contains("工") || roomName.Contains("坊") || roomName.Contains("铺") || roomName.Contains("库") || roomName.Contains("锯") || roomName.Contains("铁") || roomName.Contains("匠"))
            return GetRoom("workshop");

        if (roomName.Contains("林") || roomName.Contains("伐木"))
            return GetTerrain("dark_forest");

        if (roomName.Contains("山") || roomName.Contains("矿") || roomName.Contains("采石") || roomName.Contains("塔"))
            return GetTerrain("storm_mountains");

        if (roomName.Contains("田") || roomName.Contains("园") || roomName.Contains("村") || roomName.Contains("场") || roomName.Contains("厩") || roomName.Contains("禽") || roomName.Contains("鸡"))
            return GetSettlement("village");

        if (roomName.Contains("城") || roomName.Contains("街") || roomName.Contains("市") || roomName.Contains("营") || roomName.Contains("堡") || roomName.Contains("镇"))
            return GetSettlement("town");

        return GetRoom("courtyard") ?? GetCombatBackground("default");
    }

    private static Texture2D? GetFromCategory(string category, string key)
    {
        EnsureConfig();
        if (_config.TryGetValue(category, out var dict) && dict.TryGetValue(key, out var path) && !string.IsNullOrEmpty(path))
        {
            return LoadTexture(path);
        }
        return null;
    }

    /// <summary>按资源路径加载并缓存纹理（支持 res:// 与标准文件系统路径）。</summary>
    public static Texture2D? LoadTexture(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        if (TextureCache.TryGetValue(path, out var cached))
            return cached;

        var tex = LoadPng(path);
        if (tex != null)
            TextureCache[path] = tex;
        return tex;
    }

    private static Texture2D? LoadPng(string path)
    {
        // 1. 优先通过 Godot FileAccess 打开
        if (Godot.FileAccess.FileExists(path))
        {
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file != null)
            {
                var image = new Image();
                if (image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) == Error.Ok)
                    return ImageTexture.CreateFromImage(image);
            }
        }

        // 2. 备选物理磁盘路径
        var fsPath = path.StartsWith("res://")
            ? path.Substring("res://".Length)
            : path;

        var candidate = Path.IsPathRooted(fsPath)
            ? fsPath
            : Path.Combine(Directory.GetCurrentDirectory(), fsPath);

        if (File.Exists(candidate))
        {
            try
            {
                var bytes = File.ReadAllBytes(candidate);
                var image = new Image();
                if (image.LoadPngFromBuffer(bytes) == Error.Ok)
                    return ImageTexture.CreateFromImage(image);
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }
}
