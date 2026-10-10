using System;
using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 游戏纯实心图标服务。
/// 按元素名称解析并加载 res://icons/{name}.svg，内部缓存 ImageTexture。
/// 支持前缀/别名模糊匹配（如“小麦种子”->“种子”）。
/// </summary>
public static class InkIcon
{
    private static readonly Dictionary<string, Texture2D?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Image> ProcessedImages = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>获取经过高斯平滑羽化处理的原生 Image，供测试与诊断导出。</summary>
    public static Image? GetProcessedImage(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = ResolveName(name);
        if (key == null) return null;
        Get(key);
        return ProcessedImages.TryGetValue(key, out var img) ? img : null;
    }

    /// <summary>
    /// 标准图标库的 61 个规范名，按类别固定顺序。
    /// 顺序稳定，供盲审导出等诊断用途按下标编号（改名即改序，导出脚本依赖它）。
    /// </summary>
    public static readonly IReadOnlyList<string> StandardNames = new[]
    {
        "书本", "以太", "剑", "匕首", "布", "帽", "弓", "弩", "手", "斧",
        "木材", "果实", "水", "法杖", "炖菜", "珊瑚", "甲", "皮", "石材", "种子",
        "秘银", "精金", "纤维", "羊毛", "肉", "腿", "药草", "蜂蜜", "钢", "铁",
        "铁矿", "长枪", "陶罐", "青铜", "面包", "靴", "鱼", "鸡蛋",
        "中毒", "灼烧", "流血", "铁壁", "冻伤", "破甲", "迅捷", "狂暴", "虚弱", "治愈",
        "单手", "双手", "双持", "远程", "法术", "持盾", "格斗",
        "刺剑", "太刀", "戟", "镰刀", "战锤", "圆盾"
    };

    /// <summary>规范名的集合形式（必须先试全名）。</summary>
    private static readonly HashSet<string> StandardIcons =
        new(StandardNames, StringComparer.OrdinalIgnoreCase);

    /// <summary>检查该名称能否匹配到图标。</summary>
    public static bool Has(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return ResolveName(name) != null;
    }

    /// <summary>获取图标 Texture2D，若不存在返回 null。</summary>
    public static Texture2D? Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var key = ResolveName(name);
        if (key == null)
            return null;

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var tex = LoadSvg(key);
        Cache[key] = tex;
        return tex;
    }

    /// <summary>在指定矩形区域内居中等比绘制图标。</summary>
    public static bool Draw(CanvasItem ci, string? name, Rect2 rect, bool showBorder = false)
    {
        var tex = Get(name);
        if (tex == null)
            return false;

        if (showBorder)
        {
            ci.DrawRect(rect, InkStyle.Inset);
            InkDraw.Ink(ci, new[]
            {
                rect.Position,
                new Vector2(rect.End.X, rect.Position.Y),
                rect.End,
                new Vector2(rect.Position.X, rect.End.Y),
                rect.Position
            }, InkStyle.Line, 1.2f, 0.4f, 8910);
        }

        var pad = showBorder ? 4f : 0f;
        var drawBox = rect.Grow(-pad);
        if (drawBox.Size.X <= 0 || drawBox.Size.Y <= 0)
            return false;

        var scale = Mathf.Min(drawBox.Size.X / tex.GetWidth(), drawBox.Size.Y / tex.GetHeight());
        var size = (new Vector2(tex.GetWidth(), tex.GetHeight()) * scale).Round();
        var center = drawBox.GetCenter().Round();
        var target = new Rect2(center - size / 2f, size);

        ci.DrawTextureRect(tex, target, false);
        return true;
    }

    private static readonly Dictionary<string, Texture2D?> BuildingCache = new(StringComparer.Ordinal);

    /// <summary>
    /// 设施与房间的图标：res://icons/build/{显示名}.svg，按名精确取、不走物品那套模糊归一
    /// （否则「水井」会落到「水」、「铁砧」落到「铁」、「书架」落到「书本」）。
    /// 白色墨层，按 <paramref name="color"/> 上色（能建＝墨线色，建不了＝压暗色）。
    /// </summary>
    public static bool DrawBuilding(CanvasItem ci, string name, Rect2 rect, Color color)
    {
        if (!BuildingCache.TryGetValue(name, out var tex))
            BuildingCache[name] = tex = LoadSvg($"res://icons/build/{name}.svg", "build/" + name, Colors.White);
        if (tex == null)
            return false;
        var scale = Mathf.Min(rect.Size.X / tex.GetWidth(), rect.Size.Y / tex.GetHeight());
        var size = (new Vector2(tex.GetWidth(), tex.GetHeight()) * scale).Round();
        ci.DrawTextureRect(tex, new Rect2(rect.GetCenter().Round() - size / 2f, size), false, color);
        return true;
    }

    private static string? ResolveName(string raw)
    {
        var clean = raw.Trim();
        if (StandardIcons.Contains(clean))
            return clean;

        // 别名与前缀模糊归一化
        if (clean.EndsWith("种子")) return "种子";
        if (clean.Contains("草药") || clean.Contains("药草")) return "药草";
        if (clean.Contains("铁矿")) return "铁矿";
        if (clean.Contains("石材") || clean.Contains("石头")) return "石材";
        if (clean.Contains("木材") || clean.Contains("原木")) return "木材";
        if (clean.Contains("兽皮") || clean.Contains("皮革")) return "皮";
        if (clean.Contains("肉")) return "肉";
        if (clean.Contains("面包")) return "面包";
        if (clean.Contains("鱼")) return "鱼";
        if (clean.Contains("炖菜") || clean.Contains("浓汤")) return "炖菜";
        if (clean.Contains("蜂")) return "蜂蜜";
        if (clean.Contains("陶罐") || clean.Contains("陶器")) return "陶罐";
        if (clean.Contains("布")) return "布";
        if (clean.Contains("铁")) return "铁";
        if (clean.Contains("钢")) return "钢";
        if (clean.Contains("青铜")) return "青铜";
        if (clean.Contains("秘银")) return "秘银";
        if (clean.Contains("精金")) return "精金";
        if (clean.Contains("以太")) return "以太";
        if (clean.Contains("珊瑚")) return "珊瑚";
        if (clean.Contains("水")) return "水";
        if (clean.Contains("蛋")) return "鸡蛋";
        if (clean.Contains("果")) return "果实";
        if (clean.Contains("书")) return "书本";

        // 武器类匹配
        if (clean.Contains("刺剑") || clean.Contains("细剑")) return "刺剑";
        if (clean.Contains("太刀") || clean.Contains("日本刀")) return "太刀";
        if (clean.Contains("战锤") || clean.Contains("铁锤")) return "战锤";
        if (clean.Contains("镰刀")) return "镰刀";
        if (clean.Contains("戟")) return "戟";
        if (clean.Contains("圆盾")) return "圆盾";
        if (clean.Contains("长枪") || clean.Contains("矛")) return "长枪";
        if (clean.Contains("弩")) return "弩";
        if (clean.Contains("弓")) return "弓";
        if (clean.Contains("斧")) return "斧";
        if (clean.Contains("杖")) return "法杖";
        if (clean.Contains("匕首") || clean.Contains("短刀")) return "匕首";
        if (clean.Contains("剑")) return "剑";

        // 防具类匹配
        if (clean.Contains("帽") || clean.Contains("盔")) return "帽";
        if (clean.Contains("甲") || clean.Contains("胸甲")) return "甲";
        if (clean.Contains("腿") || clean.Contains("护腿")) return "腿";
        if (clean.Contains("手") || clean.Contains("手套") || clean.Contains("护手")) return "手";
        if (clean.Contains("靴") || clean.Contains("鞋")) return "靴";

        return null;
    }

    private static Texture2D? LoadSvg(string name) => LoadSvg($"res://icons/{name}.svg", name, InkStyle.Line);

    private static Texture2D? LoadSvg(string resPath, string name, Color lineColor)
    {
        try
        {
            if (!ResourceLoader.Exists(resPath))
                return null;
            var img = ResourceLoader.Load<Texture2D>(resPath).GetImage();

            // 1. 高质量 Lanczos 下采样到 120x120 (3x SSAA 超采样)
            img.Resize(120, 120, Image.Interpolation.Lanczos);
            img.Convert(Image.Format.Rgba8);

            var w = img.GetWidth();
            var h = img.GetHeight();

            // 2. 提取灰度并构建初始 Alpha 缓冲
            var rawAlpha = new float[w, h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var p = img.GetPixel(x, y);
                    var luma = p.R * 0.299f + p.G * 0.587f + p.B * 0.114f;
                    rawAlpha[x, y] = Mathf.Clamp(luma / 0.92f, 0f, 1f);
                }
            }

            // 3. 执行轻量 3x3 高斯平滑低通滤波，消除高频阶跃振铃锯齿
            var smoothAlpha = new float[w, h];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var sum = 0f;
                    sum += (x > 0 && y > 0 ? rawAlpha[x - 1, y - 1] : rawAlpha[x, y]) * 1f;
                    sum += (y > 0 ? rawAlpha[x, y - 1] : rawAlpha[x, y]) * 2f;
                    sum += (x < w - 1 && y > 0 ? rawAlpha[x + 1, y - 1] : rawAlpha[x, y]) * 1f;

                    sum += (x > 0 ? rawAlpha[x - 1, y] : rawAlpha[x, y]) * 2f;
                    sum += rawAlpha[x, y] * 4f;
                    sum += (x < w - 1 ? rawAlpha[x + 1, y] : rawAlpha[x, y]) * 2f;

                    sum += (x > 0 && y < h - 1 ? rawAlpha[x - 1, y + 1] : rawAlpha[x, y]) * 1f;
                    sum += (y < h - 1 ? rawAlpha[x, y + 1] : rawAlpha[x, y]) * 2f;
                    sum += (x < w - 1 && y < h - 1 ? rawAlpha[x + 1, y + 1] : rawAlpha[x, y]) * 1f;

                    smoothAlpha[x, y] = sum / 16f;
                }
            }

            // 4. 写回图像：骨白墨色 + 256级平滑亚像素羽化 Alpha
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    img.SetPixel(x, y, new Color(lineColor.R, lineColor.G, lineColor.B, smoothAlpha[x, y]));
                }
            }

            // 5. 生成硬件完整 Mipmaps 并指定三线性滤波
            img.GenerateMipmaps();
            ProcessedImages[name] = img;

            var tex = ImageTexture.CreateFromImage(img);
            var canvasTex = new CanvasTexture();
            canvasTex.DiffuseTexture = tex;
            canvasTex.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
            return canvasTex;
        }
        catch
        {
            return null;
        }
    }
}
