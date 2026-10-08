using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 哥特 · 西幻风的位图素材（2026-10-07 主人授权：程序绘制不够精美处可用生成图导入）。
/// 只做画面表现：全屏底图、面板石纹平铺、灰阶角花、灰阶徽饰。不注册任何命中块，不改任何几何。
/// 素材在 res://RimisekaiFrontend/ui/gothic/（入库，不走 assets/）。
/// </summary>
public static class GothicArt
{
    private const string Root = "res://RimisekaiFrontend/ui/gothic/";

    private static Texture2D? _backdrop;
    private static Texture2D? _tile;
    private static Texture2D? _corner;
    private static Texture2D? _crest;
    private static bool _loaded;

    private static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        _backdrop = Tex("backdrop.png");
        _tile = Tex("panel_tile.png");
        _corner = Tex("corner.png");
        _crest = Tex("crest.png");
    }

    /// <summary>编辑器已导入就走 ResourceLoader（导出包里只有导入后的资源），否则直读 PNG。</summary>
    private static Texture2D? Tex(string name)
    {
        var path = Root + name;
        if (ResourceLoader.Exists(path))
            return ResourceLoader.Load<Texture2D>(path);
        return InkIllustration.LoadTexture(path);
    }

    /// <summary>全屏底图：哥特石壁暗纹，覆盖铺满画布；缺图时退回纯色底。</summary>
    public static void Backdrop(CanvasItem ci)
    {
        Load();
        var full = new Rect2(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
        ci.DrawRect(full, InkStyle.Bg);
        if (_backdrop != null)
            PortraitFrame.Cover(ci, _backdrop, full, 0.5f);
    }

    /// <summary>
    /// 全屏底图的一块：与 <see cref="Backdrop"/> 同一套铺法取出 part 那一片原样重画（过界平移时遮住滑出网格的部分）。
    /// </summary>
    public static void BackdropPart(CanvasItem ci, Rect2 part)
    {
        Load();
        ci.DrawRect(part, InkStyle.Bg);
        if (_backdrop == null)
            return;
        var size = _backdrop.GetSize();
        var scale = Mathf.Max(PortraitLayout.CanvasWidth / size.X, PortraitLayout.CanvasHeight / size.Y);
        var src = new Vector2(PortraitLayout.CanvasWidth / scale, PortraitLayout.CanvasHeight / scale);
        var origin = new Vector2((size.X - src.X) / 2f, (size.Y - src.Y) * 0.5f);
        ci.DrawTextureRectRegion(_backdrop, part, new Rect2(origin + part.Position / scale, part.Size / scale));
    }

    /// <summary>石纹平铺：按 512 一块铺满 r（边缘按区域裁），modulate 控制明暗。</summary>
    public static void Tile(CanvasItem ci, Rect2 r, float brightness = 1f, float alpha = 1f)
    {
        Load();
        if (_tile == null)
        {
            ci.DrawRect(r, InkStyle.Panel);
            return;
        }
        var size = _tile.GetSize();
        var mod = new Color(brightness, brightness, brightness, alpha);
        for (var y = r.Position.Y; y < r.End.Y; y += size.Y)
        {
            var h = Mathf.Min(size.Y, r.End.Y - y);
            for (var x = r.Position.X; x < r.End.X; x += size.X)
            {
                var w = Mathf.Min(size.X, r.End.X - x);
                ci.DrawTextureRectRegion(_tile, new Rect2(x, y, w, h), new Rect2(0, 0, w, h), mod);
            }
        }
    }

    /// <summary>
    /// 灰阶角花：贴图本身是左上角，flipX/flipY 镜像出其余三角。at 是框的角点，图向框内长 size。
    /// </summary>
    public static void Corner(CanvasItem ci, Vector2 at, float size, bool flipX, bool flipY, float alpha = 1f)
    {
        Load();
        if (_corner == null)
            return;
        var scale = new Vector2(flipX ? -1f : 1f, flipY ? -1f : 1f);
        ci.DrawSetTransform(PortraitFrame.LayerOffset + at, 0f, scale);
        ci.DrawTextureRect(_corner, new Rect2(0, 0, size, size), false, new Color(1f, 1f, 1f, alpha));
        ci.DrawSetTransform(PortraitFrame.LayerOffset, 0f, Vector2.One);
    }

    /// <summary>四角灰阶角花，尺寸按框短边收敛（不外溢，最大占短边 40%）。</summary>
    public static void Corners(CanvasItem ci, Rect2 r, float size, float alpha = 1f)
    {
        var k = Mathf.Min(size, Mathf.Min(r.Size.X, r.Size.Y) * 0.4f);
        if (k < 24f)
            return;
        Corner(ci, r.Position, k, false, false, alpha);
        Corner(ci, new Vector2(r.End.X, r.Position.Y), k, true, false, alpha);
        Corner(ci, new Vector2(r.Position.X, r.End.Y), k, false, true, alpha);
        Corner(ci, r.End, k, true, true, alpha);
    }

    /// <summary>只画两只上角（抽屉顶沿用）。</summary>
    public static void TopCorners(CanvasItem ci, Rect2 r, float size, float alpha = 1f)
    {
        Corner(ci, r.Position, size, false, false, alpha);
        Corner(ci, new Vector2(r.End.X, r.Position.Y), size, true, false, alpha);
    }

    /// <summary>灰阶徽饰（中央尖拱＋宝珠＋两翼卷草），以 center 为中心、宽 width，高按原图比例。</summary>
    public static void Crest(CanvasItem ci, Vector2 center, float width, float alpha = 1f)
    {
        Load();
        if (_crest == null)
            return;
        var size = _crest.GetSize();
        var h = width * size.Y / size.X;
        ci.DrawTextureRect(_crest, new Rect2(center.X - width / 2f, center.Y - h / 2f, width, h), false,
            new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>徽饰在给定宽度下的高度（排版避让用）。</summary>
    public static float CrestHeight(float width)
    {
        Load();
        if (_crest == null)
            return 0f;
        var size = _crest.GetSize();
        return width * size.Y / size.X;
    }
}
