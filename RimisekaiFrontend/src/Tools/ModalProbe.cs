using System;
using System.Collections.Generic;
using Godot;
using Rimisekai.Combat;
using Rimisekai.Ink;

namespace Rimisekai.Tools;

/// <summary>
/// 弹窗模块规格与交互逻辑的自动化验证探针。
/// 可在无头模式或开发调试中执行自检。
/// </summary>
public static class ModalProbe
{
    public static void RunAllChecks()
    {
        GD.Print("[ModalProbe] 开始执行通用居中弹窗自检...");

        CheckLayoutCalculations();
        CheckQueueAdvancement();
        CheckFourScenarios();
        CheckHitAndBlockingSemantics();

        GD.Print("[ModalProbe] 全部 4 组自检全部通过！");
    }

    private static void CheckLayoutCalculations()
    {
        // 1. 基础单行短弹窗
        var shortPage = new InkModalPage
        {
            Body = "这是一条简短的单人旁白。",
        };
        var shortLayout = InkLayout.CalculateModalLayout(shortPage);
        Assert(shortLayout.PanelRect.Size.X == 800f, "弹窗宽度必须固定为 800px");
        Assert(Mathf.IsEqualApprox(shortLayout.PanelRect.Position.X, 560f), "弹窗必须水平居中（X=560）");
        Assert(shortLayout.PanelRect.Size.Y >= 180f, "弹窗必须满足最小高度限制");
        var expectedShortY = (1080f - shortLayout.PanelRect.Size.Y) / 2f;
        Assert(Mathf.IsEqualApprox(shortLayout.PanelRect.Position.Y, expectedShortY), "弹窗必须垂直居中");
        Assert(Mathf.IsEqualApprox(shortLayout.ArrowTip.X, 960f), "底部向下的实心三角箭头顶点必须水平居中");

        // 2. 带标题、输入框与长多行文本的大弹窗（高度必须自适应累加）
        var longPage = new InkModalPage
        {
            Title = "测试标题",
            Body = "测试长文本第一行说明。\n测试长文本第二行说明。\n测试长文本第三行说明。\n测试长文本第四行说明。",
            Input = new InkModalInput { Placeholder = "（输入内容）" },
            Choices = new List<InkModalChoice>
            {
                new() { Id = "1", Label = "测试选项一" },
                new() { Id = "2", Label = "测试选项二" },
                new() { Id = "3", Label = "测试选项三" },
            },
        };
        var longLayout = InkLayout.CalculateModalLayout(longPage);
        Assert(longLayout.PanelRect.Size.X == 800f, "宽度始终固定为 800px");
        Assert(longLayout.PanelRect.Size.Y > shortLayout.PanelRect.Size.Y, "长文本多控件弹窗高度必须高于短文本弹窗");
        Assert(longLayout.TitleRect.Size.X > 0, "标题矩形必须存在");
        Assert(longLayout.InputRect.Size.X > 0, "输入框矩形必须存在");
        Assert(longLayout.ChoiceRects.Count == 3, "选项按钮必须生成 3 个矩形");
        Assert(Mathf.IsEqualApprox(longLayout.ArrowTip.X, 960f), "长弹窗底部实心三角箭头依然居中位于底部");
    }

    private static void CheckQueueAdvancement()
    {
        var session = new InkModalSession();
        Assert(!session.IsActive, "初始状态必须非激活");

        var p1Advances = 0;
        var p2Advances = 0;

        var p1 = new InkModalPage { Body = "第 1 幕", OnAdvance = () => p1Advances++ };
        var p2 = new InkModalPage { Body = "第 2 幕", OnAdvance = () => p2Advances++ };

        session.Enqueue(p1);
        session.Enqueue(p2);

        Assert(session.IsActive, "入队后状态必须为激活");
        Assert(session.Current == p1, "当前页必须是第 1 幕");

        var advanced = session.Advance();
        Assert(advanced, "第 1 次推进必须成功进入下一页");
        Assert(p1Advances == 1, "第 1 幕回调必须被调用");
        Assert(session.Current == p2, "当前页必须切换为第 2 幕");

        advanced = session.Advance();
        Assert(!advanced, "第 2 幕为最后一页，推进后必须结束会话");
        Assert(p2Advances == 1, "第 2 幕回调必须被调用");
        Assert(!session.IsActive, "全部推进完毕后状态必须非激活");
    }

    private static void CheckFourScenarios()
    {
        // 场景 1：问答测试
        var questionChosen = "";
        var qPage = InkModalFactory.CreateQuestion(
            "测试问题",
            "测试问题说明文字",
            new[] { ("A", "选项A"), ("B", "选项B") },
            id => questionChosen = id);
        Assert(qPage.HasInteractiveControls, "问答页面有选项，必须被判定为含交互控件");
        Assert(qPage.Choices.Count == 2, "问答页面必须有 2 个选项");
        qPage.Choices[0].OnSelected?.Invoke();
        Assert(questionChosen == "A", "选项回调必须正确执行");

        // 场景 1（输入）：输入测试
        var submittedName = "";
        var namePage = InkModalFactory.CreateInputQuestion(
            "测试输入",
            "测试输入说明文字",
            "（输入内容）",
            12,
            name => submittedName = name);
        Assert(namePage.HasInteractiveControls, "命名弹窗有输入框，必须被判定为含交互控件");
        namePage.Input!.Text = "测试文本";
        namePage.Choices[0].OnSelected?.Invoke();
        Assert(submittedName == "测试文本", "命名输入提交必须正确传递");

        // 场景 2：战后结算
        var battleResult = new BattleResult
        {
            Outcome = CombatOutcome.AttackerWin,
            Rounds = 4,
        };
        battleResult.Rows.Add(new BattleResult.Row
        {
            CharacterId = 1,
            Name = "艾莉丝",
            Survived = true,
            DamageDealt = 120,
            Kills = 2,
            WeaponExp = 30,
            StyleExp = 15,
            Mood = 8,
        });
        var loot = new LootResult { Money = 500 };
        loot.Items.Add(("铁", 2));

        var settleFinished = false;
        var settlePage = InkModalFactory.CreateCombatSettlement(battleResult, loot, new Rimisekai.Housing.Territory(), () => settleFinished = true);
        Assert(!settlePage.HasInteractiveControls, "战后结算为纯展示，必须无强制阻塞控件");
        Assert(settlePage.Title == "战斗胜利", "获胜标题必须为战斗胜利");
        Assert(settlePage.Body.Contains("获得战利品"), "结算内容必须包含战利品");
        Assert(settlePage.Body.Contains("金钱 +500G"), "结算内容必须包含金钱信息");
        settlePage.OnAdvance?.Invoke();
        Assert(settleFinished, "战后结算推进回调必须被触发");

        // 场景 3：询问确认框
        var confirmed = false;
        var confPage = InkModalFactory.CreateConfirmation(
            "测试确认",
            "测试确认提示文字",
            () => confirmed = true);
        Assert(confPage.HasInteractiveControls, "确认框必须有确定与取消选项");
        Assert(confPage.Choices.Count == 2, "确认框包含确定与取消 2 个按钮");
        confPage.Choices[0].OnSelected?.Invoke();
        Assert(confirmed, "点击确定必须触发确认回调");

        // 场景 4：单人无角色剧情演出测试
        var storyPages = new List<InkModalPage>(
            InkModalFactory.CreateNarrativeSequence(new[]
            {
                "测试叙事第一段文字",
                "测试叙事第二段文字",
            }));
        Assert(storyPages.Count == 2, "剧情序列必须拆成 2 页");
        Assert(!storyPages[0].HasInteractiveControls, "单人剧情演出页面必须为纯阅读展示");
    }

    private static void CheckHitAndBlockingSemantics()
    {
        // 验证有控件时的阻断逻辑 vs 纯展示时的全屏推进逻辑
        var pureDisplay = new InkModalPage { Body = "纯展示" };
        var interactive = new InkModalPage
        {
            Body = "带按钮",
            Choices = new List<InkModalChoice> { new() { Label = "选项" } },
        };

        // 纯展示：背景应当是 ModalAdvance（点击任何部分推进）
        var pureAction = pureDisplay.HasInteractiveControls ? InkAction.BlockClick : InkAction.ModalAdvance;
        Assert(pureAction == InkAction.ModalAdvance, "纯展示弹窗背景动作必须是 ModalAdvance");

        // 有控件：背景应当是 BlockClick（阻断误触，必须点击按钮）
        var interAction = interactive.HasInteractiveControls ? InkAction.BlockClick : InkAction.ModalAdvance;
        Assert(interAction == InkAction.BlockClick, "含交互控件弹窗背景动作必须是 BlockClick");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception($"[ModalProbe 断言失败] {message}");
    }
}
