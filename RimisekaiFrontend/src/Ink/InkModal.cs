using System;
using System.Collections.Generic;
using System.Linq;
using Rimisekai.Combat;

namespace Rimisekai.Ink;

/// <summary>弹窗内的选项条目。</summary>
public sealed class InkModalChoice
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public Action? OnSelected { get; init; }
}

/// <summary>弹窗内的文本输入框。</summary>
public sealed class InkModalInput
{
    public string Placeholder { get; set; } = "（输入内容）";
    public string Text { get; set; } = "";
    public int MaxChars { get; set; } = 24;
    public bool Focused { get; set; } = true;
    public Action<string>? OnSubmit { get; init; }
}

/// <summary>单个弹窗页定义：标题、折行正文、输入框与选项。</summary>
public sealed class InkModalPage
{
    /// <summary>弹窗标题（可选，留空则不绘制标题栏与分割线）。</summary>
    public string Title { get; set; } = "";

    /// <summary>主体文本（支持多行，按弹窗宽度自动折行）。</summary>
    public string Body { get; set; } = "";

    /// <summary>输入框（可选）。</summary>
    public InkModalInput? Input { get; set; }

    /// <summary>选项列表（可选）。</summary>
    public List<InkModalChoice> Choices { get; set; } = new();

    /// <summary>当页面推进或确认关闭时的回调。</summary>
    public Action? OnAdvance { get; set; }

    /// <summary>战后结算结构化数据（可选，存在时渲染器走专属精美版式）。</summary>
    public InkModalSettlementData? Settlement { get; set; }

    /// <summary>怪物图鉴结构化详情（可选，属性、技能与掉落分区绘制）。</summary>
    public InkModalMonsterCodexData? MonsterCodex { get; set; }

    /// <summary>物品详情结构化数据（可选，存在时按「暗标签＋亮数值」逐行排）。</summary>
    public InkModalItemData? Item { get; set; }

    /// <summary>是否存在交互控件（输入框或选项）。若存在则点击空白处不跳过。</summary>
    public bool HasInteractiveControls => Input != null || Choices.Count > 0;
}

/// <summary>物品详情：标题下一行副题（可空），其下逐行字段。</summary>
public sealed class InkModalItemData
{
    public string Subtitle { get; init; } = "";
    /// <summary>装备品质：精致及以上时详情页铺一层上飘的稀有度雾。</summary>
    public Rimisekai.Defs.Quality? Quality { get; init; }
    public IReadOnlyList<Rimisekai.Defs.DetailLine> Lines { get; init; } = Array.Empty<Rimisekai.Defs.DetailLine>();
}

/// <summary>战后结算界面的结构化数据。</summary>
public sealed class InkModalSettlementData
{
    public int Rounds { get; init; }
    public List<Row> Rows { get; init; } = new();
    public long Money { get; init; }
    /// <summary>战利品物品，已按单件市场价值从高到低排好（同价按名字）；金钱另记在 Money。</summary>
    public List<Loot> Items { get; init; } = new();

    /// <summary>Quality：装备实例的品质（定稀有度雾），材料等定义物品为 null。</summary>
    public readonly record struct Loot(string ItemId, string Label, int MarketValue, int Count, Rimisekai.Defs.Quality? Quality);

    public sealed class Row
    {
        public string Name { get; init; } = "";
        public int Level { get; init; } = 1;
        public int DamageDealt { get; init; }
        public string WeaponName { get; init; } = "剑";
        public int WeaponLevel { get; init; } = 1;
        public float WeaponRatio { get; init; } = 0.5f;
        public int WeaponExp { get; init; }
        public string StyleName { get; init; } = "单手";
        public int StyleLevel { get; init; } = 1;
        public float StyleRatio { get; init; } = 0.5f;
        public int StyleExp { get; init; }
        /// <summary>本场让武器 / 流派熟练升了级：等级后挂上升箭头。</summary>
        public bool WeaponLevelUp { get; init; }
        public bool StyleLevelUp { get; init; }
    }
}

/// <summary>怪物图鉴详情的结构化内容，避免把不同类型的数据拼成无层级正文。</summary>
public sealed class InkModalMonsterCodexData
{
    public int RecordCount { get; init; }
    public List<Metric> Attributes { get; init; } = new();
    public List<string> Skills { get; init; } = new();
    public List<Drop> Drops { get; init; } = new();

    public sealed class Metric
    {
        public string Label { get; init; } = "";
        public string Value { get; init; } = "";
    }

    public sealed class Drop
    {
        public string Name { get; init; } = "";
        public string Quantity { get; init; } = "";
        public string Chance { get; init; } = "";
    }
}

/// <summary>弹窗会话与队列管理器，支持单页与多页连续流转。</summary>
public sealed class InkModalSession
{
    private readonly Queue<InkModalPage> _pages = new();

    public InkModalPage? Current { get; private set; }

    public bool IsActive => Current != null;

    /// <summary>入队单个弹窗页。</summary>
    public void Enqueue(InkModalPage page)
    {
        if (Current == null)
            Current = page;
        else
            _pages.Enqueue(page);
    }

    /// <summary>入队连续弹窗序列。</summary>
    public void EnqueueRange(IEnumerable<InkModalPage> pages)
    {
        foreach (var p in pages)
            Enqueue(p);
    }

    /// <summary>推进到下一个弹窗或结束。</summary>
    public bool Advance()
    {
        if (Current == null)
            return false;

        Current.OnAdvance?.Invoke();

        if (_pages.Count > 0)
        {
            Current = _pages.Dequeue();
            return true;
        }

        Current = null;
        return false;
    }

    /// <summary>直接清空并关闭所有弹窗。</summary>
    public void DismissAll()
    {
        Current = null;
        _pages.Clear();
    }
}

/// <summary>四大典型应用场景的弹窗构造工厂。</summary>
public static class InkModalFactory
{
    /// <summary>场景 1：开局问答单页。</summary>
    public static InkModalPage CreateQuestion(
        string title,
        string question,
        IReadOnlyList<(string Id, string Label)> options,
        Action<string> onChosen)
    {
        var page = new InkModalPage
        {
            Title = title,
            Body = question,
        };
        foreach (var opt in options)
        {
            var choiceId = opt.Id;
            page.Choices.Add(new InkModalChoice
            {
                Id = choiceId,
                Label = opt.Label,
                OnSelected = () => onChosen(choiceId),
            });
        }
        return page;
    }

    /// <summary>场景 1（带输入）：开局命名或自定义文本问答。</summary>
    public static InkModalPage CreateInputQuestion(
        string title,
        string question,
        string placeholder,
        int maxChars,
        Action<string> onSubmit)
    {
        var input = new InkModalInput
        {
            Placeholder = placeholder,
            MaxChars = maxChars,
            OnSubmit = onSubmit,
        };

        var page = new InkModalPage
        {
            Title = title,
            Body = question,
            Input = input,
        };

        page.Choices.Add(new InkModalChoice
        {
            Id = "confirm",
            Label = "确定",
            OnSelected = () => onSubmit(input.Text),
        });

        return page;
    }

    /// <summary>场景 2：战后结算弹窗（美化版）。</summary>
    public static InkModalPage CreateCombatSettlement(
        BattleResult result,
        LootResult loot,
        Rimisekai.Housing.Territory territory,
        Action? onFinished = null)
    {
        var isWin = result.Outcome == CombatOutcome.AttackerWin;
        var title = isWin ? "战斗胜利" : "战斗结束";

        var settlement = new InkModalSettlementData
        {
            Rounds = result.Rounds,
            Money = loot.Money,
        };

        foreach (var r in result.Rows)
        {
            var wName = WeaponNameOf(r.Weapon);
            var sName = StyleNameOf(r.Style);

            // 等级与进度条都按落账后的熟练累计算（每 ExpPerLevel 一级），显示等级至少 1。
            const int per = Rimisekai.Character.Proficiency.ExpPerLevel;
            var wLevel = System.Math.Max(1, r.WeaponTotalExp / per);
            var sLevel = System.Math.Max(1, r.StyleTotalExp / per);

            settlement.Rows.Add(new InkModalSettlementData.Row
            {
                Name = r.Name,
                Level = System.Math.Max(1, r.Level),
                DamageDealt = r.DamageDealt,
                WeaponName = wName,
                WeaponLevel = wLevel,
                WeaponRatio = r.WeaponTotalExp % per / (float)per,
                WeaponLevelUp = wLevel > System.Math.Max(1, r.WeaponLevel),
                WeaponExp = r.WeaponExp,
                StyleName = sName,
                StyleLevel = sLevel,
                StyleRatio = r.StyleTotalExp % per / (float)per,
                StyleLevelUp = sLevel > System.Math.Max(1, r.StyleLevel),
                StyleExp = r.StyleExp,
            });
        }

        // 价值排序：单件市场价值高的（高稀有度装备、高阶材料）排在上面。
        settlement.Items.AddRange(loot.Items
            .Select(item =>
            {
                var info = Rimisekai.Defs.Items.Info(territory, item.ItemId)!.Value;
                return new InkModalSettlementData.Loot(item.ItemId, info.Label, info.MarketValue, item.Count, info.Quality);
            })
            .OrderByDescending(l => l.MarketValue)
            .ThenBy(l => l.Label, StringComparer.Ordinal));

        return new InkModalPage
        {
            Title = title,
            Settlement = settlement,
            OnAdvance = onFinished,
        };
    }

    /// <summary>场景 3：询问玩家是否确定（二次确认框）。</summary>
    public static InkModalPage CreateConfirmation(
        string title,
        string message,
        Action onConfirm,
        Action? onCancel = null)
    {
        return new InkModalPage
        {
            Title = title,
            Body = message,
            Choices = new List<InkModalChoice>
            {
                new()
                {
                    Id = "confirm",
                    Label = "确定",
                    OnSelected = onConfirm,
                },
                new()
                {
                    Id = "cancel",
                    Label = "取消",
                    OnSelected = onCancel,
                },
            },
        };
    }

    /// <summary>
    /// 场景 4：弹窗剧情演出——语义上为「无对方角色/场景时的剧情/描述」（如背景故事、一部分教程）。
    /// 仅作为通用排版容器，严禁硬编码任何预设文案；支持传入多段段落，自动逐句推进，点击画面任意部分推进下一句或结束。
    /// </summary>
    public static IEnumerable<InkModalPage> CreateNarrativeSequence(
        IEnumerable<string> paragraphs,
        string title = "",
        Action? onCompleted = null)
    {
        var list = new List<InkModalPage>();
        foreach (var p in paragraphs)
        {
            list.Add(new InkModalPage
            {
                Title = title,
                Body = p,
            });
        }
        if (list.Count > 0 && onCompleted != null)
            list[^1].OnAdvance = onCompleted;

        return list;
    }

    private static string WeaponNameOf(Rimisekai.Character.WeaponType type) => type switch
    {
        Rimisekai.Character.WeaponType.Sword => "剑",
        Rimisekai.Character.WeaponType.Axe => "斧",
        Rimisekai.Character.WeaponType.Spear => "长枪",
        Rimisekai.Character.WeaponType.Bow => "弓",
        Rimisekai.Character.WeaponType.Staff => "法杖",
        Rimisekai.Character.WeaponType.Dagger => "匕首",
        Rimisekai.Character.WeaponType.Crossbow => "弩",
        Rimisekai.Character.WeaponType.Unarmed => "格斗",
        _ => "剑",
    };

    private static string StyleNameOf(Rimisekai.Character.StyleType style) => style switch
    {
        Rimisekai.Character.StyleType.OneHand => "单手",
        Rimisekai.Character.StyleType.TwoHand => "双手",
        Rimisekai.Character.StyleType.DualWield => "双持",
        Rimisekai.Character.StyleType.Ranged => "远程",
        Rimisekai.Character.StyleType.Spell => "法术",
        Rimisekai.Character.StyleType.Shield => "持盾",
        Rimisekai.Character.StyleType.Unarmed => "格斗",
        _ => "单手",
    };
}
