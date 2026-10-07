using Godot;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 位图装饰件（2026-10-07 主人放开：装饰可用位图，可点区域仍一律由代码登记）。
/// 图在 <c>RimisekaiFrontend/ui_ornaments/</c>（assets/ 不入库，所以另放一处入库目录）：
/// 生图后经脚本去色、按亮度映射为「骨白 Line 色＋透明度」的单色 PNG，背景全透明。
/// 加载时生成 mipmap（项目默认画布纹理过滤是带 mipmap 的线性），缩小绘制不起锯齿。
/// 颜色只经 modulate 的透明度变化，不另取色。
/// </summary>
public static class PortraitOrnaments
{
    private const string Root = "res://RimisekaiFrontend/ui_ornaments/";
    private static Texture2D? _corner;
    private static Texture2D? _crest;
    private static Texture2D? _medallion;

    /// <summary>左上角卷草角花（含三叶、卷叶与一枚斜指向内的鸢尾），256×254。</summary>
    public static Texture2D Corner => _corner ??= Load("corner_filigree.png");

    /// <summary>横向冠饰：正中尖拱套四叶玫瑰，两翼卷草收成细尖，960×199。</summary>
    public static Texture2D CrestTexture => _crest ??= Load("arch_crest.png");

    /// <summary>四叶窗徽框（双线、四斜角小菱），512×507，内腔透明。</summary>
    public static Texture2D Medallion => _medallion ??= Load("quatrefoil_medallion.png");

    private static Texture2D Load(string name)
    {
        using var file = FileAccess.Open(Root + name, FileAccess.ModeFlags.Read);
        var image = new Image();
        image.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength()));
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// 在矩形 r 的四角各贴一枚角花（左上为原图，其余镜像），边长 k（按 r 短边 0.42 收敛，绝不外溢）。
    /// bottom=false 只贴上两角。
    /// </summary>
    public static void Corners(CanvasItem ci, Rect2 r, float k, float alpha, bool bottom = true)
    {
        var size = Mathf.Min(k, Mathf.Min(r.Size.X, r.Size.Y) * 0.42f);
        if (size < 24f)
            return;
        var tex = Corner;
        var mod = new Color(1f, 1f, 1f, alpha);
        void One(Vector2 at, float sx, float sy)
        {
            ci.DrawSetTransform(at, 0f, new Vector2(sx, sy));
            ci.DrawTextureRect(tex, new Rect2(0, 0, size, size), false, mod);
        }
        One(r.Position, 1f, 1f);
        One(new Vector2(r.End.X, r.Position.Y), -1f, 1f);
        if (bottom)
        {
            One(new Vector2(r.End.X, r.End.Y), -1f, -1f);
            One(new Vector2(r.Position.X, r.End.Y), 1f, -1f);
        }
        ci.DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// 冠饰：以 center 为中心、宽 width 横贴（高按原图比例）。
    /// above＝立在 center 所在的那条线上（只有底下约五分之一压过线，主体在线上方）。
    /// </summary>
    public static void Crest(CanvasItem ci, Vector2 center, float width, float alpha, bool above = false)
    {
        var tex = CrestTexture;
        var h = width * tex.GetHeight() / tex.GetWidth();
        var y = above ? center.Y - h * 0.8f : center.Y - h / 2f;
        ci.DrawTextureRect(tex, new Rect2(center.X - width / 2f, y, width, h), false,
            new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>四叶窗徽框：以 center 为中心、边长 size。</summary>
    public static void MedallionFrame(CanvasItem ci, Vector2 center, float size, float alpha)
    {
        var tex = Medallion;
        var h = size * tex.GetHeight() / tex.GetWidth();
        ci.DrawTextureRect(tex, new Rect2(center.X - size / 2f, center.Y - h / 2f, size, h), false,
            new Color(1f, 1f, 1f, alpha));
    }
}
