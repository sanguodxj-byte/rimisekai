using Godot;

namespace Rimisekai.Ink;

/// <summary>
/// 技能盘：一具按**六属性**分区的三角格星盘（参考 Blade&Hex）。
///
/// 毂之外分 6 个属性扇区，每扇区由内向外 4 环；每环按弧长分列，每列沿对角切两格。
/// **格内不写字**（格小，写不下），技能名与数值只在右栏详情里显示。
///
/// 画法要点（都是踩过的坑）：
///   ① 只画**真技能**节点；空位/过路格一律不画——否则几百个小三角糊成网格纸。
///   ② 节点**实心填充**（全项目实心填充铁律）：已解锁=骨白实心、未解锁=银灰实心、
///      选中=骨白实心外加一圈更淡的实心光晕。禁止只描空心轮廓。
///   ③ 骨架只画环界同心圆 + 6 条扇区辐条 + 外缘刻度；不画环内列分隔线。
/// </summary>
public static class InkSkillDisc
{
    private const int SectorFont = 26;
    private const int HubFont = 26;
    private const int DetailFont = 26;

    public static void Draw(CanvasItem ci, InkPageModel page, InkSkillDiscModel disc)
    {
        // 1. 左栏星盘面板：细双线外框＋角花
        InkFrame.Panel(ci, InkLayout.SkillDiscPanel, corner: 16f);

        // 2. 边框裁剪内腔：边框外的星盘图形一律被裁剪截断，不出边框
        var clip = InkLayout.SkillDiscContent;

        // 3. 星盘内部元素（网格、瓦片、毂、标签），全部走裁剪绘制
        DrawContent(ci, disc, clip);

        // 4. 面板边框再次描边，确保边框线压在星盘图样之上，线条挺拔干净
        InkFrame.PanelFrame(ci, InkLayout.SkillDiscPanel, corner: 16f);

        // 5. 顶层控件与右栏详情
        DrawResetButton(ci, disc);
        DrawCornerNavButton(ci);
        DrawDetail(ci, page);
    }

    /// <summary>
    /// 技能节点：**实心填充**。
    /// 已解锁=骨白实心、未解锁=银灰实心；选中者先铺一圈更淡的实心光晕再压上本体。
    /// 节点向重心收缩留缝，格与格之间因此透气。
    /// 当聚焦某一扇区时，该扇区保持 100% 鲜明，其余扇区保持 0.40f 半透明连贯映衬，
    /// 超出边框的部分一律由边框裁剪截断。
    /// </summary>
    /// <summary>仅绘制盘体；横屏默认参数不变，手机版自行安排文字、导航与详情。</summary>
    public static void DrawContent(CanvasItem ci, InkSkillDiscModel disc, Rect2 clip,
        float minLineWidth = 0f, bool drawText = true)
    {
        DrawGrid(ci, disc, clip, minLineWidth);
        DrawTiles(ci, disc, clip, minLineWidth);
        DrawHub(ci, disc, clip, minLineWidth, drawText);
        if (drawText)
            DrawSectorLabels(ci, disc, clip);
    }

    private static void DrawTiles(CanvasItem ci, InkSkillDiscModel disc, Rect2 clip, float minLineWidth)
    {
        var byCell = new System.Collections.Generic.Dictionary<(int, int, int, bool), InkSkillTile>();
        foreach (var tile in disc.Tiles)
            byCell[(tile.Sector, tile.Ring, tile.Col, tile.Upper)] = tile;

        var pivot = disc.ViewPivot;
        var zoom = disc.ViewZoom;
        var rotation = disc.ViewRotation;
        var focused = disc.FocusedSector;

        for (var sector = 0; sector < InkLayout.SkillDiscSectors; sector++)
        {
            var isFocusedSector = sector == focused;
            var alphaMult = focused >= 0 ? (isFocusedSector ? 1f : 0.40f) : 1f;

            for (var ring = 0; ring < InkLayout.SkillDiscRings; ring++)
                for (var col = 0; col < InkLayout.SkillDiscColsPerRing[ring]; col++)
                    for (var u = 0; u < 2; u++)
                    {
                        var upper = u == 1;
                        byCell.TryGetValue((sector, ring, col, upper), out var tile);
                        var kind = tile?.Kind ?? InkSkillNodeKind.Filler;
                        var poly = InkLayout.SkillDiscTilePolygon(sector, ring, col, upper);

                        // 视角平移、缩放与对齐旋转投影
                        var tPoly = InkLayout.TransformDiscPolygon(poly, pivot, zoom, rotation);
                        var seed = 7500 + sector * 97 + ring * 11 + col * 3 + u;
                        var selected = tile != null && tile.Id.Length > 0 && tile.Id == disc.SelectedId;

                        if (kind == InkSkillNodeKind.Skill)
                        {
                            if (selected)
                            {
                                var halo = Shrink(tPoly, 0.84f);
                                var glow = new Color(InkStyle.Line, 0.28f * alphaMult);
                                InkDraw.ClippedPolygon(ci, halo, clip, glow, glow, Mathf.Max(1f, minLineWidth), 0.2f, seed);
                            }

                            var baseCol = tile!.Unlocked ? InkStyle.Line : InkStyle.Dim;
                            var body = new Color(baseCol, baseCol.A * alphaMult);
                            var shape = Shrink(tPoly, selected ? 0.72f : 0.64f);
                            InkDraw.ClippedPolygon(ci, shape, clip, body, body, Mathf.Max(1.2f, minLineWidth), 0.2f, seed + 1);
                        }
                        else
                        {
                            // 过路/基础瓦片：实心深灰，格间收缩留缝
                            var shape = Shrink(tPoly, 0.64f);
                            var fill = new Color(InkStyle.Line, 0.12f * alphaMult);
                            var line = new Color(InkStyle.Line, 0.30f * alphaMult);
                            InkDraw.ClippedPolygon(ci, shape, clip, line, fill, Mathf.Max(1.1f, minLineWidth), 0.2f, seed);
                        }
                    }
        }
    }

    /// <summary>把多边形向重心收缩（t=1 原样，t 越小越小），让相邻格之间留出空隙。</summary>
    private static Vector2[] Shrink(Vector2[] poly, float t)
    {
        var c = Vector2.Zero;
        foreach (var p in poly)
            c += p;
        c /= poly.Length;

        var result = new Vector2[poly.Length];
        for (var i = 0; i < poly.Length; i++)
            result[i] = c + (poly[i] - c) * t;
        return result;
    }

    /// <summary>
    /// 盘的骨架：外缘双圈 + 环界同心圆 + 6 条扇区辐条 + 毂圈，外加外缘刻度。
    /// 始终完整投影，超出边框部分一律被边框裁剪截除。
    /// </summary>
    private static void DrawGrid(CanvasItem ci, InkSkillDiscModel disc, Rect2 clip, float minLineWidth)
    {
        var pivot = disc.ViewPivot;
        var zoom = disc.ViewZoom;
        var rotation = disc.ViewRotation;
        var focused = disc.FocusedSector;

        // 外缘双圈：裁剪至边框内
        InkDraw.ClippedArc(ci, pivot, (InkLayout.SkillDiscRadius + 10f) * zoom, 0f, Mathf.Tau,
            clip, new Color(InkStyle.Line, 0.55f), Mathf.Max(1.4f, minLineWidth), 192, 7090);
        InkDraw.ClippedArc(ci, pivot, InkLayout.SkillDiscRadius * zoom, 0f, Mathf.Tau,
            clip, new Color(InkStyle.Line, 0.85f), Mathf.Max(2.6f, minLineWidth), 192, 7091);

        // 环界同心圆：裁剪至边框内
        for (var ring = 0; ring < InkLayout.SkillDiscRings; ring++)
        {
            var r = SkillRingRadius(ring) * zoom;
            var isInnerHub = ring == 0;
            var alpha = isInnerHub ? 0.80f : 0.42f;

            InkDraw.ClippedArc(ci, pivot, r, 0f, Mathf.Tau,
                clip, new Color(InkStyle.Line, alpha),
                Mathf.Max(ring == 0 ? 2.4f : 1.3f, minLineWidth), 192, 7100 + ring);
        }

        // 扇区分界辐条：全部绘制，边框外被裁剪，聚焦扇区边界略微加粗
        for (var sector = 0; sector < InkLayout.SkillDiscSectors; sector++)
        {
            var isFocusedBorder = focused >= 0 && (sector == focused || sector == (focused + 1) % InkLayout.SkillDiscSectors);
            var a = InkLayout.SkillDiscSectorStart(sector);
            var p0 = InkLayout.TransformDiscPoint(InkLayout.SkillDiscPoint(InkLayout.SkillDiscHub, a), pivot, zoom, rotation);
            var p1 = InkLayout.TransformDiscPoint(InkLayout.SkillDiscPoint(InkLayout.SkillDiscRadius, a), pivot, zoom, rotation);
            var spokeAlpha = isFocusedBorder ? 0.95f : (focused >= 0 ? 0.60f : 0.95f);
            var spoke = new Color(InkStyle.Line, spokeAlpha);

            InkDraw.ClippedInkLine(ci, p0, p1, clip, spoke, Mathf.Max(isFocusedBorder ? 3.0f : 1.8f, minLineWidth), 0.15f, 7400 + sector);
            InkDraw.ClippedJewel(ci, p0, 4f, clip, spoke);
            InkDraw.ClippedJewel(ci, p1, 4f, clip, spoke);
        }

        // 外缘刻度：36 格短刻，边框外被裁剪
        for (var t = 0; t < 36; t++)
        {
            var a = Mathf.Tau * t / 36f;
            var isMajor = t % 6 == 0;
            var isMedium = t % 3 == 0;
            var len = isMajor ? 20f : (isMedium ? 11f : 5f);
            var col = isMajor ? InkStyle.Line : (isMedium ? new Color(InkStyle.Line, 0.80f) : new Color(InkStyle.Dim, 0.55f));
            var w = isMajor ? 2.4f : (isMedium ? 1.6f : 1.1f);
            var p0 = InkLayout.TransformDiscPoint(InkLayout.SkillDiscPoint(InkLayout.SkillDiscRadius + 10f, a), pivot, zoom, rotation);
            var p1 = InkLayout.TransformDiscPoint(InkLayout.SkillDiscPoint(InkLayout.SkillDiscRadius + 10f + len, a), pivot, zoom, rotation);
            InkDraw.ClippedInkLine(ci, p0, p1, clip, col, Mathf.Max(w, minLineWidth), 0.15f, 7150 + t);
        }
    }

    /// <summary>第 ring 条环界的半径（ring=0 是毂圈）。</summary>
    private static float SkillRingRadius(int ring) =>
        ring <= 0 ? InkLayout.SkillDiscHub : InkLayout.SkillDiscRingOuter(ring - 1);

    /// <summary>盘心毂：角色名与当前流派。</summary>
    private static void DrawHub(CanvasItem ci, InkSkillDiscModel disc, Rect2 clip, float minLineWidth, bool drawText)
    {
        var pivot = disc.ViewPivot;
        var zoom = disc.ViewZoom;
        var rotation = disc.ViewRotation;
        var focused = disc.FocusedSector;
        var hub = InkLayout.SkillDiscHub * zoom;

        // 内环 + 装饰珠
        InkDraw.ClippedArc(ci, pivot, hub - 20f * zoom, 0f, Mathf.Tau,
            clip, new Color(InkStyle.Line, 0.50f), Mathf.Max(1.5f, minLineWidth), 128, 7080);

        if (focused < 0)
        {
            // 全景模式：中央展示角色名与流派
            for (var k = 0; k < 4; k++)
            {
                var a = -Mathf.Pi / 2f + k * Mathf.Pi / 2f;
                var jp = pivot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (hub - 20f * zoom);
                InkDraw.ClippedJewel(ci, jp, 5f, clip, new Color(InkStyle.Line, 0.85f));
            }

            if (!drawText)
                return;
            var nameRect = new Rect2(pivot.X - hub, pivot.Y - 26f * zoom, hub * 2f, 34f * zoom);
            if (clip.HasPoint(nameRect.GetCenter()))
            {
                InkDraw.TextBounded(ci, nameRect,
                    disc.CharacterName, (int)(HubFont * zoom), 14, InkStyle.Line, "cm");
            }

            var styleRect = new Rect2(pivot.X - hub, pivot.Y + 14f * zoom, hub * 2f, 28f * zoom);
            if (clip.HasPoint(styleRect.GetCenter()))
            {
                InkDraw.TextBounded(ci, styleRect,
                    disc.StyleLine, (int)(18 * zoom), 12, InkStyle.Dim, "cm");
            }
        }
        else
        {
            if (!drawText)
                return;
            // 聚焦模式：对齐左下角，在扇形基底内侧朝向右上角展示角色与流派标识
            var diag = new Vector2(Mathf.Cos(-Mathf.Pi / 4f), Mathf.Sin(-Mathf.Pi / 4f));
            var cornerCenter = pivot + diag * (hub * 0.46f);
            var w = 180f;
            var cornerNameRect = new Rect2(cornerCenter.X - w / 2f, cornerCenter.Y - 24f, w, 28f);
            var cornerStyleRect = new Rect2(cornerCenter.X - w / 2f, cornerCenter.Y + 4f, w, 24f);

            if (clip.HasPoint(cornerNameRect.GetCenter()))
                InkDraw.TextBounded(ci, cornerNameRect, disc.CharacterName, 22, 14, InkStyle.Line, "cm");
            if (clip.HasPoint(cornerStyleRect.GetCenter()))
                InkDraw.TextBounded(ci, cornerStyleRect, disc.StyleLine, 16, 12, InkStyle.Dim, "cm");
        }
    }

    /// <summary>扇区标签：全景模式与聚焦模式统一环绕分布，聚焦扇区加高亮边框，超出边框则剔除。</summary>
    private static void DrawSectorLabels(CanvasItem ci, InkSkillDiscModel disc, Rect2 clip)
    {
        var pivot = disc.ViewPivot;
        var zoom = disc.ViewZoom;
        var rotation = disc.ViewRotation;
        var focused = disc.FocusedSector;

        for (var s = 0; s < disc.SectorLabels.Count; s++)
        {
            var text = disc.SectorLabels[s];
            if (string.IsNullOrEmpty(text))
                continue;

            var origRect = InkLayout.SkillDiscSectorLabelRect(s);
            var rect = InkLayout.TransformDiscRect(origRect, pivot, zoom, rotation);

            // 超出边框外完全不相交的标签不绘制
            if (!clip.Intersects(rect))
                continue;

            var isFocused = s == focused;
            if (isFocused)
            {
                // 聚焦扇区标签：加浅填与高亮墨线框，被边框裁剪
                var labelBox = new[]
                {
                    rect.Position,
                    new Vector2(rect.End.X, rect.Position.Y),
                    rect.End,
                    new Vector2(rect.Position.X, rect.End.Y),
                    rect.Position,
                };
                InkDraw.ClippedPolygon(ci, labelBox, clip, InkStyle.Line, InkStyle.Hover, 1.8f, 0.3f, 7900 + s);
            }

            var lines = text.Split('\n');
            var y = rect.GetCenter().Y - (lines.Length - 1) * 13f;
            foreach (var line in lines)
            {
                var lineRect = new Rect2(rect.Position.X, y - 14f, rect.Size.X, 28f);
                if (clip.HasPoint(lineRect.GetCenter()))
                {
                    InkDraw.TextBounded(ci, lineRect,
                        line, isFocused ? (int)(SectorFont * 1.1f) : SectorFont, 12,
                        isFocused ? InkStyle.Line : (focused >= 0 ? new Color(InkStyle.Dim, 0.6f) : InkStyle.Line), "cm");
                }
                y += 26f;
            }
        }
    }

    /// <summary>聚焦模式下，左上角提供精致的【返回全景】按钮。</summary>
    private static void DrawResetButton(CanvasItem ci, InkSkillDiscModel disc)
    {
        if (disc.FocusedSector < 0)
            return;

        InkFrame.Button(ci, InkLayout.SkillDiscResetButtonRect, "‹ 全盘视角",
            selected: false, enabled: true, fontSize: 18, centered: true);
    }

    private static Texture2D? _cornerNavSvgTex;
    private static System.DateTime _cornerNavSvgMtime;

    private const string CornerNavSvgSource = @"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""650 174 254 254"" width=""100%"" height=""100%"">
  <!-- Upper-Left Button Wing (左上弧翼：正方形削去同心扇形弧后沿对角线切开) -->
  <path d=""
    M 678.0,178.0
    L 898.0,178.0
    L 796.0,280.0
    A 1018 1018 0 0 0 678.0,178.0
    Z""
    fill=""#0C0C0C"" stroke=""#E9EFEA"" stroke-width=""2.5"" stroke-linejoin=""round""/>

  <!-- Upper-Left Inner Border -->
  <path d=""
    M 696.0,184.0
    L 884.0,184.0
    L 796.0,272.0
    A 1024 1024 0 0 0 696.0,184.0
    Z""
    fill=""#121212"" stroke=""#8E928F"" stroke-width=""1.4"" stroke-linejoin=""round""/>

  <!-- Upper-Left Solid Triangle (◤ - 切换左侧/上一瓦片，直角在左上，两腰等长48px) -->
  <polygon points=""776,196 824,196 776,244"" fill=""#E9EFEA""/>

  <!-- Lower-Right Button Wing (右下弧翼：关于 x+y=1080 严格轴对称) -->
  <path d=""
    M 902.0,402.0
    L 902.0,182.0
    L 800.0,284.0
    A 1018 1018 0 0 1 902.0,402.0
    Z""
    fill=""#0C0C0C"" stroke=""#E9EFEA"" stroke-width=""2.5"" stroke-linejoin=""round""/>

  <!-- Lower-Right Inner Border -->
  <path d=""
    M 896.0,384.0
    L 896.0,196.0
    L 808.0,284.0
    A 1024 1024 0 0 1 896.0,384.0
    Z""
    fill=""#121212"" stroke=""#8E928F"" stroke-width=""1.4"" stroke-linejoin=""round""/>

  <!-- Lower-Right Solid Triangle (◢ - 切换右侧/下一瓦片，直角在右下，两腰等长48px) -->
  <polygon points=""884,304 884,256 836,304"" fill=""#E9EFEA""/>
</svg>";

    /// <summary>
    /// 星盘面板右上角 SVG 矢量导航按钮：
    /// 正方形去掉一块同心扇形弧（与星盘扇区外弧严格平行），并沿对角线切开形成两个带边框的按钮形状。
    /// 包含左上实心三角（◤ 切换左侧瓦片）与右下实心三角（◢ 切换右侧瓦片），高精度矢量栅格化绘制。
    /// </summary>
    private static void DrawCornerNavButton(CanvasItem ci)
    {
        var rect = InkLayout.SkillDiscCornerNavButtonRect;

        var resPath = "res://assets/disc_nav_button.svg";
        var globalPath = ProjectSettings.GlobalizePath(resPath);
        var fileExists = System.IO.File.Exists(globalPath);
        var curMtime = fileExists ? System.IO.File.GetLastWriteTimeUtc(globalPath) : System.DateTime.MinValue;

        if (_cornerNavSvgTex == null || curMtime != _cornerNavSvgMtime)
        {
            var svgStr = fileExists
                ? System.IO.File.ReadAllText(globalPath)
                : CornerNavSvgSource;

            var img = new Image();
            if (img.LoadSvgFromString(svgStr, scale: 2.0f) == Error.Ok)
            {
                _cornerNavSvgTex = ImageTexture.CreateFromImage(img);
                _cornerNavSvgMtime = curMtime;
            }
        }

        if (_cornerNavSvgTex != null)
        {
            ci.DrawTextureRect(_cornerNavSvgTex, rect, false);
        }
    }

    /// <summary>
    /// 右栏详情：技能名标题 + 上下两张无标题卡片（上＝规格，下＝门槛），
    /// 规格卡片底部留一段效果描述（这一招实际做什么），
    /// 卡片按面板高度铺满，避免下方空荡；正文按「标签｜数值」两栏排，行间加细分割线。
    /// </summary>
    private static void DrawDetail(CanvasItem ci, InkPageModel page)
    {
        InkFrame.Panel(ci, InkLayout.SkillDetailPanel, corner: 16f);

        var panel = InkLayout.SkillDetailPanel;
        var left = panel.Position.X + InkLayout.Pad;
        var width = panel.Size.X - InkLayout.Pad * 2f;

        // 没选中技能时右栏只留空面板，不写任何操作指引文字。
        if (page.DetailTitle.Length == 0)
            return;

        // 标题 + 下饰分割细线。
        InkDraw.TextBounded(ci, InkLayout.SkillDetailTitleRect,
            page.DetailTitle, InkLayout.TitleFontSize, 20, InkStyle.Line, "lm");
        var ruleY = InkLayout.SkillDetailTitleRect.End.Y + 6f;
        InkFrame.FadingRule(ci, left, left + width, ruleY);

        // 正文按内容分类拆进两组：规格（种类/威力/射程等）与下卡（流派/附加状态）。
        // 需求条件清单不在这里——它由 DetailRequirements 单独承载，只画一次「需求」标签。
        var allLines = page.DetailNote.Split('\n');
        var specLines = new System.Collections.Generic.List<string>();
        var extraLines = new System.Collections.Generic.List<string>();

        foreach (var l in allLines)
        {
            if (string.IsNullOrWhiteSpace(l)) continue;
            if (l.StartsWith("流派") || l.StartsWith("附加状态"))
                extraLines.Add(l);
            else
                specLines.Add(l);
        }

        // 下卡的每一行：标签 + 内容 + 是否未达成。
        var lowRows = new System.Collections.Generic.List<(string Label, string Value, bool Unmet)>();
        foreach (var l in extraLines)
        {
            var cut = l.IndexOf('　');
            if (cut > 0)
                lowRows.Add((l.Substring(0, cut), l.Substring(cut + 1), false));
            else
                lowRows.Add(("", l, false));
        }

        // 需求：标签只出现一次，各条条件列在其下（首条与标签同行，其余对齐到内容列）。
        for (var i = 0; i < page.DetailRequirements.Count; i++)
        {
            var req = page.DetailRequirements[i];
            var value = req.Unmet ? $"{req.Text}（未达成）" : req.Text;
            lowRows.Add((i == 0 ? "需求" : "", value, req.Unmet));
        }

        const float rowH = 34f;          // 一行文字的高度（与 DrawLabeledRow 一致）
        const float rowGapMin = 6f;
        const float rowGapMax = 52f;     // 行距上限：再大卡片就过高，整块会顶到面板上下沿
        const float cardPadTop = 18f;
        const float cardPadBottom = 18f;
        const float cardGapMin = 22f;
        const float effectGap = 26f;     // 最后一行行格底部到效果描述的留白

        // 效果描述先量好要占几行，好在规格卡片底部给它留位。
        var effectWidth = width - 36f;
        const int effectFont = 18;
        var effectLines = page.DetailEffect.Length == 0
            ? System.Array.Empty<string>()
            : InkDraw.WrapLines(page.DetailEffect, effectWidth, effectFont);
        var effectBlock = effectLines.Count > 0 ? effectGap + effectLines.Count * 28f : 0f;

        var contentTop = ruleY + 16f;
        var contentBottom = panel.End.Y - InkLayout.Pad - 10f;
        var available = contentBottom - contentTop;

        // 行间隙自适应：把富余高度摊进行与行之间（不是堆在卡片底部），
        // 让两张卡片连同内部文字一起正好铺满内容区，卡内不留死白。
        var gapCount = Mathf.Max(0, specLines.Count - 1) + Mathf.Max(0, lowRows.Count - 1);
        var rowsH = (specLines.Count + lowRows.Count) * rowH;
        var fixedH = cardPadTop * 2f + cardPadBottom * 2f + cardGapMin + rowsH + effectBlock;
        var rowGap = rowGapMin;
        if (gapCount > 0 && available > fixedH)
            rowGap = Mathf.Min(rowGapMax, rowGapMin + (available - fixedH) / gapCount);

        // 一行占「行高 + 行间隙」；规格卡片的内容 = 各行整格 + 效果块，
        // 效果块自**最后一行行格的底部**起算，分隔线因此落在行格之外，不会贴成下划线。
        var slotH = rowH + rowGap;
        var specInner = specLines.Count * slotH + effectBlock;
        var gateInner = lowRows.Count * slotH;

        // 两张卡片都贴合各自内容定高。行距到上限后仍有富余，就按内容量摊成两卡内边距，
        // 让卡片撑起来不至于太扁；余下极少的零头成为整块上下对称的留白。
        var slack = available - (cardPadTop * 2f + cardPadBottom * 2f + cardGapMin
                                 + specLines.Count * slotH + lowRows.Count * slotH + effectBlock);
        var padExtra1 = 0f;
        var padExtra2 = 0f;
        if (slack > 0f)
        {
            var weight1 = specInner + cardPadTop + cardPadBottom;
            var weight2 = gateInner + cardPadTop + cardPadBottom;
            var total = weight1 + weight2;
            padExtra1 = slack * weight1 / total;
            padExtra2 = slack * weight2 / total;
        }

        var card1H = cardPadTop + specInner + cardPadBottom + padExtra1;
        var card2H = cardPadTop + gateInner + cardPadBottom + padExtra2;
        var cardGap = cardGapMin;

        var blockH = card1H + cardGap + card2H;
        var card1Top = contentTop + Mathf.Max(0f, (available - blockH) / 2f);
        var card2Top = card1Top + card1H + cardGap;

        // 上卡片：规格（无标题），底部接效果描述。整块内容在卡内垂直居中。
        var card1Rect = new Rect2(left, card1Top, width, card1H);
        InkFrame.Card(ci, card1Rect, InkStyle.Line, false, InkStyle.Inset);

        // 文字在整格内垂直居中、分隔线落在格底，于是与上下两条线等距，不会被读成下划线。
        var start1 = card1Top + (card1H - specInner) / 2f;
        for (var i = 0; i < specLines.Count; i++)
        {
            // 末行下面紧跟效果描述，行分隔线交给那条渐隐线，避免出现紧邻的双线。
            var lastWithEffect = effectLines.Count > 0 && i == specLines.Count - 1;
            var line = specLines[i];
            var cut = line.IndexOf('　');
            var label = cut > 0 ? line.Substring(0, cut) : "";
            var value = cut > 0 ? line.Substring(cut + 1) : line;
            DrawLabeledRow(ci, left + 16f, start1 + i * slotH, width - 32f, slotH,
                label, value, unmet: false, divider: !lastWithEffect);
        }

        if (effectLines.Count > 0)
        {
            // 效果描述接在最后一行行格之外：上面一条渐隐细线分隔，正文折行。
            var effectTop = start1 + specLines.Count * slotH + effectGap;
            InkFrame.FadingRule(ci, left + 16f, left + width - 16f, effectTop - effectGap / 2f);
            var ey = effectTop;
            foreach (var line in effectLines)
            {
                InkDraw.TextBounded(ci, new Rect2(left + 16f, ey, effectWidth, 28f),
                    line, effectFont, 14, InkStyle.Line, "lm");
                ey += 28f;
            }
        }

        // 下卡片：流派与需求（无标题），内容同样在卡内垂直居中。
        var card2Rect = new Rect2(left, card2Top, width, card2H);
        InkFrame.Card(ci, card2Rect, InkStyle.Line, false, InkStyle.Inset);

        var start2 = card2Top + (card2H - gateInner) / 2f;
        for (var i = 0; i < lowRows.Count; i++)
        {
            var (label, value, unmet) = lowRows[i];
            var divider = i < lowRows.Count - 1;
            DrawLabeledRow(ci, left + 16f, start2 + i * slotH, width - 32f, slotH,
                label, value, unmet, divider);
        }
    }

    /// <summary>
    /// 详情一行：文字在整格（行高 + 行间隙）内垂直居中，分隔线落在格底。
    /// 暗标签在左、内容起于统一值列；标签为空时内容仍对齐值列（需求的多条条件即如此）。
    /// 未达成的内容用次级色。
    /// </summary>
    private static void DrawLabeledRow(CanvasItem ci, float left, float top, float width, float slotH,
        string label, string value, bool unmet, bool divider)
    {
        const float valueX = 116f;   // 值列起点
        var textY = top + (slotH - 34f) / 2f;
        if (label.Length > 0)
            InkDraw.TextBounded(ci, new Rect2(left, textY, valueX - 8f, 34f),
                label, DetailFont, 14, InkStyle.Dim, "lm");
        InkDraw.TextBounded(ci, new Rect2(left + valueX, textY, width - valueX, 34f),
            value, DetailFont, 14, unmet ? InkStyle.Dim : InkStyle.Line, "lm");
        if (divider)
            InkFrame.RowDivider(ci, new Rect2(left, top, width, slotH));
    }
}
