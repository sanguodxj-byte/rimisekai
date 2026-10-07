using System.Collections.Generic;
using Godot;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Tools;

/// <summary>
/// 国际象棋棋子标识核对工具：
/// 真实绘制纯黑底银白墨线下的国际象棋六大棋子大图、各好感度映射棋子，
/// 以及地图网格与设施段槽位（点亮/暗色小兵上限）实景图。
/// 用法：--capture=<输出路径>
/// </summary>
public partial class InkChessCapture : Node
{
    private string _outPath = "";
    private SubViewport? _view;
    private int _frames;

    public override void _Ready()
    {
        _outPath = "核对_国际象棋棋子.png";
        var allArgs = new System.Collections.Generic.List<string>(OS.GetCmdlineUserArgs());
        allArgs.AddRange(OS.GetCmdlineArgs());
        foreach (var a in allArgs)
        {
            if (a.StartsWith("--capture="))
                _outPath = a["--capture=".Length..];
        }

        _view = new SubViewport
        {
            Size = new Vector2I((int)InkLayout.CanvasWidth, (int)InkLayout.CanvasHeight),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        _view.AddChild(new ChessGallerySheet());
        AddChild(_view);
    }

    public override void _Process(double delta)
    {
        if (_view == null)
            return;
        if (++_frames < 5)
            return;

        var img = _view.GetTexture().GetImage();
        var err = img.SavePng(_outPath);
        GD.Print(err == Error.Ok ? $"chess sheet ok: {_outPath}" : $"chess sheet failed: {err}");
        GetTree().Quit();
    }

    private partial class ChessGallerySheet : Node2D
    {
        public override void _Draw()
        {
            // 纯黑底
            DrawRect(new Rect2(Vector2.Zero, InkLayout.CanvasWidth, InkLayout.CanvasHeight), InkStyle.Bg);

            // 主标题
            InkDraw.Text(this, new Vector2(80f, 60f), "国际象棋角色标识规范与实景核对（实心单色白）", 32, InkStyle.Line, "lm");
            DrawLine(new Vector2(80f, 96f), new Vector2(1840f, 96f), InkStyle.Line, 1.6f);

            // ---------------- 板块一：六大棋子造型特写（高 140px，精细斯汤顿古典雕塑线稿） ----------------
            InkDraw.Text(this, new Vector2(80f, 130f), "一、古典国际象棋六大棋子造型（高 140px 实心单色雕塑剪影）", 24, InkStyle.Line, "lm");

            var pieces = new[]
            {
                (InkDraw.ChessPiece.King, "国王 King ♔", "玩家代表（实心拱冠与顶立十字架）"),
                (InkDraw.ChessPiece.Queen, "王后 Queen ♕", "好感 > 600（盛放五齿冠冕与实心宝珠）"),
                (InkDraw.ChessPiece.Rook, "战车 Rook ♖", "好感 100~600 之一（坚毅城堞雉堞）"),
                (InkDraw.ChessPiece.Bishop, "主教 Bishop ♗", "好感 100~600 之一（尖拱法冠与实心顶珠）"),
                (InkDraw.ChessPiece.Knight, "骑士 Knight ♘", "好感 100~600 之一（昂首战马雕像）"),
                (InkDraw.ChessPiece.Pawn, "士兵 Pawn ♙", "好感 < 100 / 空槽上限（收腰与球顶）"),
            };

            var startX = 180f;
            var gapX = 290f;
            var topY = 320f;

            for (var i = 0; i < pieces.Length; i++)
            {
                var (p, name, desc) = pieces[i];
                var cx = startX + i * gapX;

                // 纯黑底，外框纯白单线
                DrawRect(new Rect2(cx - 70f, topY - 150f, 140f, 160f), InkStyle.Bg);
                DrawRect(new Rect2(cx - 70f, topY - 150f, 140f, 160f), InkStyle.Line, false, 1f);

                // 绘制 140px 大棋子（仅单色白）
                InkDraw.Chess(this, new Vector2(cx, topY), 140f, p, isLimitCap: false);

                // 标注
                InkDraw.Text(this, new Vector2(cx, topY + 28f), name, 20, InkStyle.Line, "cm");
                InkDraw.TextFitted(this, new Vector2(cx, topY + 54f), desc, 260f, 14, 11, InkStyle.Line, "cm");
            }

            DrawLine(new Vector2(80f, 430f), new Vector2(1840f, 430f), InkStyle.Line, 1f);

            // ---------------- 板块二：好感度分级规则与对照（中号 48px） ----------------
            InkDraw.Text(this, new Vector2(80f, 465f), "二、好感度分级点亮规则（中号 48px · 实心单色白）", 24, InkStyle.Line, "lm");

            var rules = new[]
            {
                ("玩家本人", "国王（实心白）", InkDraw.ChessPiece.King, false),
                ("好感度 > 600", "王后（实心白）", InkDraw.ChessPiece.Queen, false),
                ("好感 100~600 (A)", "城堡（实心白）", InkDraw.ChessPiece.Rook, false),
                ("好感 100~600 (B)", "主教（实心白）", InkDraw.ChessPiece.Bishop, false),
                ("好感 100~600 (C)", "骑士（实心白）", InkDraw.ChessPiece.Knight, false),
                ("好感 < 100", "小兵（实心白）", InkDraw.ChessPiece.Pawn, false),
                ("空槽/上限标识", "小兵（实心暗灰）", InkDraw.ChessPiece.Pawn, true),
            };

            var ruleStartX = 160f;
            var ruleGapX = 250f;
            var ruleY = 570f;

            for (var i = 0; i < rules.Length; i++)
            {
                var (title, sub, p, isCap) = rules[i];
                var cx = ruleStartX + i * ruleGapX;

                InkDraw.Chess(this, new Vector2(cx, ruleY), 54f, p, isLimitCap: isCap);
                InkDraw.Text(this, new Vector2(cx, ruleY + 22f), title, 18, InkStyle.Line, "cm");
                InkDraw.Text(this, new Vector2(cx, ruleY + 44f), sub, 15, InkStyle.Line, "cm");
            }

            DrawLine(new Vector2(80f, 650f), new Vector2(1840f, 650f), InkStyle.Line, 1f);

            // ---------------- 板块三：游戏界面实景落地模拟 ----------------
            InkDraw.Text(this, new Vector2(80f, 685f), "三、游戏界面实景效果（仅单色白 · 地图网格角色标识 & 设施段槽位点亮）", 24, InkStyle.Line, "lm");

            // 1. 地图网格实景（左侧）
            var mapRect = new Rect2(80f, 730f, 320f, 240f);
            InkFrame.Panel(this, mapRect, fill: InkStyle.Panel);
            InkDraw.Text(this, new Vector2(mapRect.GetCenter().X, mapRect.Position.Y + 26f), "[卧室] (当前所在)", 20, InkStyle.Line, "cm");

            // 模拟房内 4 名在场角色：玩家(国王)、亲密伴侣(王后)、好友(战车)、新同伴(小兵)
            var sampleRoomCards = new[]
            {
                new CharacterCard(1, "你", true, 1, 1, 0),
                new CharacterCard(2, "莉莉", false, 1, 2, 750),     // 王后
                new CharacterCard(3, "艾莉亚", false, 1, 3, 350),   // 战车/中阶
                new CharacterCard(4, "新雇员", false, 1, 1, 50),    // 小兵
            };

            var mapFootY = mapRect.End.Y - 24f;
            var mapStep = 56f;
            var mapStartX = mapRect.GetCenter().X - mapStep * 1.5f;

            for (var i = 0; i < sampleRoomCards.Length; i++)
            {
                var cx = mapStartX + i * mapStep;
                var card = sampleRoomCards[i];
                var p = InkDraw.PieceFor(card);
                InkDraw.Chess(this, new Vector2(cx, mapFootY), 32f, p, isLimitCap: false);
            }

            InkDraw.Text(this, new Vector2(mapRect.GetCenter().X, mapRect.End.Y + 28f), "地图网格：国王常显 + 随行角色棋子", 16, InkStyle.Line, "cm");

            // 2. 设施段槽位实景（右侧）
            var fixtureArea = new Rect2(520f, 730f, 1320f, 260f);
            InkFrame.Panel(this, fixtureArea, fill: InkStyle.Panel);
            InkFrame.Title(this, fixtureArea, "设施段实景（右侧槽位：点亮为实心白棋子，空槽上限为暗色实心小兵）", 22);

            var sampleRows = new[]
            {
                ("◇ 大双人床", 2, 2, true, new[] { sampleRoomCards[0], sampleRoomCards[1] }), // 2/2 满员：玩家国王 + 伴侣王后
                ("◇ 长条餐桌", 3, 6, false, new[] { sampleRoomCards[1], sampleRoomCards[2], sampleRoomCards[3] }), // 3/6：王后 + 战车 + 小兵，余 3 个暗色实心小兵
                ("◇ 铁匠台", 1, 1, false, new[] { sampleRoomCards[2] }), // 1/1 满员：艾莉亚(战车)正在锻造
                ("◇ 休息长椅", 0, 3, false, System.Array.Empty<CharacterCard>()), // 0/3 全空：3 个暗色实心小兵上限标识
            };

            var rowY = fixtureArea.Position.Y + 65f;
            var rowH = 42f;

            for (var r = 0; r < sampleRows.Length; r++)
            {
                var (name, used, cap, playerHere, occs) = sampleRows[r];
                var rowRect = new Rect2(fixtureArea.Position.X + 20f, rowY + r * (rowH + 6f), fixtureArea.Size.X - 40f, rowH);

                if (playerHere)
                    DrawRect(rowRect, InkStyle.Hover);

                // 设施名
                InkDraw.Text(this, new Vector2(rowRect.Position.X + 16f, rowRect.GetCenter().Y), name, 20, InkStyle.Line, "lm");

                // 右侧槽位（仅单色白）
                var baseX = rowRect.End.X - 20f - 34f;
                for (var k = 0; k < cap; k++)
                {
                    var x = baseX - (cap - 1 - k) * 34f;
                    var basePt = new Vector2(x, rowRect.GetCenter().Y + 9f);

                    if (k < used)
                    {
                        var card = k < occs.Length ? occs[k] : new CharacterCard(0, "", false, 0, 0, 0);
                        var p = InkDraw.PieceFor(card);
                        InkDraw.Chess(this, basePt, 22f, p, isLimitCap: false);
                    }
                    else
                    {
                        // 上限标识：仅单色白点虚线小兵
                        InkDraw.Chess(this, basePt, 22f, InkDraw.ChessPiece.Pawn, isLimitCap: true);
                    }
                }

                if (playerHere)
                {
                    InkDraw.Text(this, new Vector2(baseX - (cap - 1) * 34f - 18f, rowRect.GetCenter().Y), "▶ 你在此", 18, InkStyle.Line, "rm");
                }
            }
        }
    }
}
