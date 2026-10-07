using System.Collections.Generic;
using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 技能页版式：三角格星盘与右栏技能详情。
/// 星盘是一具铺满整盘的三角格星图，按六属性分 6 扇区、每扇区由内向外 4 环，
/// 每环的列数随半径递增，整盘共 240 格。
/// 绘制与命中判定都从这里取顶点，禁止两处各算一套。
/// </summary>
public static partial class InkLayout
{
    // ---------- 技能页：三角格星盘 ----------
    // 星盘是一具**铺满整盘的三角格**星图（参考 Blade&Hex）：
    //   盘心毂之外，按**六属性**分 6 个扇区（体质/灵巧/智力/魅力/感知/力量），每扇区由内向外 4 环；
    //   每环把该扇区的角度均分成若干列，每列沿对角切成 ▽/▲ 两个三角格。
    //   每环列数随半径递增（弧越长列越多），所以各格大小大致相当。
    //
    // 为什么这样铺（两条曾经踩过的坑）：
    //   ① **环界画成整圈圆弧**，列数在环与环之间可以不同——若按「跨环共边」
    //      推导顶点，内环格会被邻环拉斜，既不对称、线也会互相穿透。
    //   ② 环内只画**该环自己的**列分隔线，不跨环连线；因此线与格都不穿透。
    // 全盘 240 格（刻意的疏布局：环少、格大、格间留缝），格内不写字，技能名只在右栏详情里显示。
    // 一格可属一个节点；技能节点占 1~2 格，其余格是过路/空位（同 Blade&Hex 的 pip/small）。
    // 绘制与命中判定都从这里取顶点，禁止两处各算一套。

    /// <summary>盘心。严格位于左栏大正方形星盘面板正中。</summary>
    public static Vector2 SkillDiscCenter => SkillDiscPanel.GetCenter();

    /// <summary>星盘外缘半径（最外一环的外弧）。在正方形面板内留出均衡的四周呼吸感。</summary>
    public const float SkillDiscRadius = 335f;

    /// <summary>中央毂半径：毂内只放角色名与当前流派，不放格子。</summary>
    public const float SkillDiscHub = 126f;

    /// <summary>扇区数：六个核心属性（速度是纯战斗项，不设扇区）。</summary>
    public const int SkillDiscSectors = 6;

    /// <summary>环数（毂外由内向外）。</summary>
    public const int SkillDiscRings = 4;

    /// <summary>
    /// 每环的列数（每个扇区内）。弧长随半径增大，故越外环列越多，
    /// 让各格宽度大致相当（最小约 35px）。列数只影响本环，环间无需对齐。
    /// </summary>
    public static readonly int[] SkillDiscColsPerRing = { 4, 4, 6, 6 };

    /// <summary>全盘三角格数，即盘的容量。</summary>
    public static int SkillDiscTileCount
    {
        get
        {
            var n = 0;
            for (var ring = 0; ring < SkillDiscRings; ring++)
                n += 2 * SkillDiscColsPerRing[ring] * SkillDiscSectors;
            return n;
        }
    }

    /// <summary>左栏星盘面板：正方形面板放大至铺满纵向（860x860，下延至 1002，消除底部空余），使扇区放大时沿右上对角线呈现绝对的斜角对称。</summary>
    public static readonly Rect2 SkillDiscPanel = new(78f, FullPageTop, FullPageHeight, FullPageHeight);

    /// <summary>星盘绘制裁剪内腔（收进双线框以内，边框外的图形一律被边框裁剪）。</summary>
    public static readonly Rect2 SkillDiscContent = SkillDiscPanel.Grow(-6f);

    /// <summary>右栏详情面板：与左侧等高（860高），紧凑对齐右缘。</summary>
    public static readonly Rect2 SkillDetailPanel = new(950f, FullPageTop, 1842f - 950f, FullPageHeight);

    /// <summary>详情标题（技能名）所在的整行。</summary>
    public static Rect2 SkillDetailTitleRect => DetailTitle(SkillDetailPanel);

    /// <summary>详情说明行（按 \n 分行）。</summary>
    public static Rect2 SkillDetailRow(int index) => new(
        SkillDetailPanel.Position.X + Pad,
        SkillDetailPanel.Position.Y + 96f + index * 40f,
        SkillDetailPanel.Size.X - Pad * 2f, 36f);

    /// <summary>第 sector 个扇区的中线角（弧度）。0 号自正上方起，顺时针排。</summary>
    public static float SkillDiscSectorAngle(int sector) =>
        -Mathf.Pi / 2f + Mathf.Tau * sector / SkillDiscSectors;

    /// <summary>极坐标取点：以盘心为原点，angle 是弧度，radius 是半径。</summary>
    public static Vector2 SkillDiscPoint(float radius, float angle) => new(
        SkillDiscCenter.X + Mathf.Cos(angle) * radius,
        SkillDiscCenter.Y + Mathf.Sin(angle) * radius);

    /// <summary>第 ring 环带的内缘半径。</summary>
    public static float SkillDiscRingInner(int ring) =>
        SkillDiscHub + (SkillDiscRadius - SkillDiscHub) * ring / SkillDiscRings;

    /// <summary>第 ring 环带的外缘半径。</summary>
    public static float SkillDiscRingOuter(int ring) =>
        SkillDiscHub + (SkillDiscRadius - SkillDiscHub) * (ring + 1) / SkillDiscRings;

    /// <summary>第 sector 扇区的起始角（该扇区左边界）。</summary>
    public static float SkillDiscSectorStart(int sector) =>
        SkillDiscSectorAngle(sector) - Mathf.Pi / SkillDiscSectors;

    /// <summary>
    /// 一格的三角顶点（3 点，闭合由调用方补）。
    /// col 是环内列号，upper=false 取该列的下三角（贴内弧）、true 取上三角（贴外弧）。
    /// 整环由这些三角恰好铺满，相邻格共边。
    /// </summary>
    public static Vector2[] SkillDiscTilePolygon(int sector, int ring, int col, bool upper)
    {
        var cols = SkillDiscColsPerRing[ring];
        var span = Mathf.Tau / SkillDiscSectors;
        var step = span / cols;
        var a0 = SkillDiscSectorStart(sector) + step * col;
        var a1 = a0 + step;
        var r0 = SkillDiscRingInner(ring);
        var r1 = SkillDiscRingOuter(ring);

        var innerLo = SkillDiscPoint(r0, a0);
        var innerHi = SkillDiscPoint(r0, a1);
        var outerLo = SkillDiscPoint(r1, a0);
        var outerHi = SkillDiscPoint(r1, a1);

        // 对角线取「左半列走 IL→OH、右半列走 IH→OL」——这样整个扇区（乃至整盘）
        // 关于中轴镜像对称；若每列同向（都走 IL→OH）会形成风车状偏斜，看着就不对称。
        var leftHalf = col * 2 < cols;
        if (leftHalf)
            return upper
                ? new[] { innerLo, outerLo, outerHi }
                : new[] { innerLo, innerHi, outerHi };
        return upper
            ? new[] { innerHi, outerLo, outerHi }
            : new[] { innerLo, innerHi, outerLo };
    }

    /// <summary>每扇区的格数（各环各列的上/下三角之和）。</summary>
    public static int SkillDiscTilesPerSector
    {
        get
        {
            var n = 0;
            for (var ring = 0; ring < SkillDiscRings; ring++)
                n += 2 * SkillDiscColsPerRing[ring];
            return n;
        }
    }

    /// <summary>
    /// 枚举一扇区的格位，**由内向外**：先内环、环内按列、每列先下三角(▽)后上三角(▲)。
    /// 技能按门槛由低到高依次落到这个序列上，于是低阶技能贴近盘心、高阶在外围。
    /// </summary>
    public static IEnumerable<(int Ring, int Col, bool Upper)> SkillDiscCellsInOrder()
    {
        for (var ring = 0; ring < SkillDiscRings; ring++)
            for (var col = 0; col < SkillDiscColsPerRing[ring]; col++)
            {
                yield return (ring, col, false);
                yield return (ring, col, true);
            }
    }

    /// <summary>格的重心。</summary>
    public static Vector2 SkillDiscTileCenter(int sector, int ring, int col, bool upper)
    {
        var poly = SkillDiscTilePolygon(sector, ring, col, upper);
        return (poly[0] + poly[1] + poly[2]) / 3f;
    }

    /// <summary>格的外接矩形（向外留 2px），供命中与悬停高亮取包围盒。</summary>
    public static Rect2 SkillDiscTileBounds(int sector, int ring, int col, bool upper)
    {
        var poly = SkillDiscTilePolygon(sector, ring, col, upper);
        var minX = Mathf.Min(poly[0].X, Mathf.Min(poly[1].X, poly[2].X));
        var minY = Mathf.Min(poly[0].Y, Mathf.Min(poly[1].Y, poly[2].Y));
        var maxX = Mathf.Max(poly[0].X, Mathf.Max(poly[1].X, poly[2].X));
        var maxY = Mathf.Max(poly[0].Y, Mathf.Max(poly[1].Y, poly[2].Y));
        return new Rect2(minX - 2f, minY - 2f, maxX - minX + 4f, maxY - minY + 4f);
    }

    /// <summary>扇区标签的尺寸（流派名 + 熟练两行）。</summary>
    public const float SkillDiscSectorLabelWidth = 100f;
    public const float SkillDiscSectorLabelHeight = 42f;

    /// <summary>
    /// 扇区标签（流派名 + 熟练）落位：扇区中线、盘缘之外。
    /// 距离按标签自身半宽半高**外接**出去算——否则横向扇区的标签内端会伸进盘里，
    /// 与外环格线、刻度互相穿插。
    /// </summary>
    public static Rect2 SkillDiscSectorLabelRect(int sector)
    {
        var a = SkillDiscSectorAngle(sector);
        var half = new Vector2(SkillDiscSectorLabelWidth / 2f, SkillDiscSectorLabelHeight / 2f);
        var dist = SkillDiscRadius + 8f
                   + Mathf.Abs(Mathf.Cos(a)) * half.X
                   + Mathf.Abs(Mathf.Sin(a)) * half.Y;
        var c = SkillDiscPoint(dist, a);
        return new Rect2(c.X - half.X, c.Y - half.Y,
            SkillDiscSectorLabelWidth, SkillDiscSectorLabelHeight);
    }

    /// <summary>盘心毂里的文字框（角色名与当前流派）。</summary>
    public static Rect2 SkillDiscHubLabelRect(int index)
    {
        var w = SkillDiscHub * 1.6f;
        return new Rect2(SkillDiscCenter.X - w / 2f,
            SkillDiscCenter.Y - 30f + index * 34f, w, 32f);
    }

    /// <summary>聚焦指定扇区时的放大倍率（让扇区在 768x768 正方形面板内饱满充盈）。</summary>
    public const float SkillDiscFocusedZoom = 2.45f;

    /// <summary>星盘面板左下角顶点：放大的扇区两个边缘直线交汇对齐于此。</summary>
    public static Vector2 SkillDiscPanelCornerBL => new(SkillDiscPanel.Position.X, SkillDiscPanel.End.Y);

    /// <summary>聚焦指定扇区时的旋转角（使扇区中线精准旋转对齐至 -45 度右上对角线，俩边缘直线对齐左下角）。</summary>
    public static float SkillDiscSectorRotation(int sector)
    {
        var targetAngle = -Mathf.Pi / 4f;
        var sectorAngle = SkillDiscSectorAngle(sector);
        var diff = targetAngle - sectorAngle;
        while (diff > Mathf.Pi) diff -= Mathf.Tau;
        while (diff < -Mathf.Pi) diff += Mathf.Tau;
        return diff;
    }

    /// <summary>
    /// 将星盘点按当前视角进行原点平移、缩放与对齐旋转投影。
    /// 无论聚焦缩放到哪个扇区，绘制和命中检测都统一调用此方法，确保像素级完全吻合。
    /// </summary>
    public static Vector2 TransformDiscPoint(Vector2 pt, Vector2 origin, float zoom, float rotation = 0f)
    {
        if (Mathf.Abs(zoom - 1f) < 0.001f && Mathf.Abs(rotation) < 0.001f && origin.DistanceSquaredTo(SkillDiscCenter) < 1f)
            return pt;
        var v = pt - SkillDiscCenter;
        var r = v.Length();
        if (r < 0.001f)
            return origin;
        var a = Mathf.Atan2(v.Y, v.X) + rotation;
        return origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r * zoom);
    }

    /// <summary>多边形所有顶点按当前视角进行投影。</summary>
    public static Vector2[] TransformDiscPolygon(Vector2[] poly, Vector2 origin, float zoom, float rotation = 0f)
    {
        if (Mathf.Abs(zoom - 1f) < 0.001f && Mathf.Abs(rotation) < 0.001f && origin.DistanceSquaredTo(SkillDiscCenter) < 1f)
            return poly;
        var res = new Vector2[poly.Length];
        for (var i = 0; i < poly.Length; i++)
            res[i] = TransformDiscPoint(poly[i], origin, zoom, rotation);
        return res;
    }

    /// <summary>轴对齐矩形按当前视角进行投影（变换中心，保持轴对齐尺寸）。</summary>
    public static Rect2 TransformDiscRect(Rect2 r, Vector2 origin, float zoom, float rotation = 0f)
    {
        if (Mathf.Abs(zoom - 1f) < 0.001f && Mathf.Abs(rotation) < 0.001f && origin.DistanceSquaredTo(SkillDiscCenter) < 1f)
            return r;
        var center = TransformDiscPoint(r.GetCenter(), origin, zoom, rotation);
        var w = r.Size.X;
        var h = r.Size.Y;
        return new Rect2(center.X - w / 2f, center.Y - h / 2f, w, h);
    }

    /// <summary>计算多边形顶点的紧密外接矩形。</summary>
    public static Rect2 PolygonBounds(Vector2[] poly)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        for (var i = 0; i < poly.Length; i++)
        {
            var p = poly[i];
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new Rect2(minX - 2f, minY - 2f, maxX - minX + 4f, maxY - minY + 4f);
    }

    /// <summary>扇区聚焦状态下，左上方“返回全景”按钮的矩形。</summary>
    public static readonly Rect2 SkillDiscResetButtonRect = new(96f, 188f, 116f, 36f);

    /// <summary>星盘面板右上角 SVG 矢量导航按钮的矩形（大尺寸 254x254，紧贴面板右上角，正方形削去 80% 以上同心扇形弧后沿对角线切开）。</summary>
    public static readonly Rect2 SkillDiscCornerNavButtonRect = new(650f, 174f, 254f, 254f);

    /// <summary>左上弧翼按钮（◤ 切换左侧/上一瓦片）的热区多边形。</summary>
    public static Vector2[] SkillDiscCornerNavTriangleUpper(Rect2 r)
    {
        var sx = r.Size.X / 254f;
        var sy = r.Size.Y / 254f;
        return new Vector2[]
        {
            new(r.Position.X + (678.0f - 650f) * sx, r.Position.Y + (178.0f - 174f) * sy),
            new(r.Position.X + (898.0f - 650f) * sx, r.Position.Y + (178.0f - 174f) * sy),
            new(r.Position.X + (796.0f - 650f) * sx, r.Position.Y + (280.0f - 174f) * sy),
            new(r.Position.X + (735.0f - 650f) * sx, r.Position.Y + (225.0f - 174f) * sy),
        };
    }

    /// <summary>右下弧翼按钮（◢ 切换右侧/下一瓦片）的热区多边形。</summary>
    public static Vector2[] SkillDiscCornerNavTriangleLower(Rect2 r)
    {
        var sx = r.Size.X / 254f;
        var sy = r.Size.Y / 254f;
        return new Vector2[]
        {
            new(r.Position.X + (902.0f - 650f) * sx, r.Position.Y + (402.0f - 174f) * sy),
            new(r.Position.X + (902.0f - 650f) * sx, r.Position.Y + (182.0f - 174f) * sy),
            new(r.Position.X + (800.0f - 650f) * sx, r.Position.Y + (284.0f - 174f) * sy),
            new(r.Position.X + (855.0f - 650f) * sx, r.Position.Y + (345.0f - 174f) * sy),
        };
    }

    /// <summary>
    /// 全景模式下，获取一个扇区扇面的封闭多边形（由内环弧、两侧分界辐条、外环弧封闭而成）。
    /// 点击扇区内的任意位置均可拾取并放大聚焦该扇区。
    /// </summary>
    public static Vector2[] SkillDiscSectorWedgePolygon(int sector, int steps = 16)
    {
        var a0 = SkillDiscSectorStart(sector);
        var span = Mathf.Tau / SkillDiscSectors;
        var a1 = a0 + span;

        var pts = new List<Vector2>(steps * 2 + 2);
        // 外弧
        for (var i = 0; i <= steps; i++)
        {
            var a = a0 + (a1 - a0) * i / steps;
            pts.Add(SkillDiscPoint(SkillDiscRadius, a));
        }
        // 内弧（反向闭合回起点）
        for (var i = steps; i >= 0; i--)
        {
            var a = a0 + (a1 - a0) * i / steps;
            pts.Add(SkillDiscPoint(SkillDiscHub, a));
        }
        return pts.ToArray();
    }


}
