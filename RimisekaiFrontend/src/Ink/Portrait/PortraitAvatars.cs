using System.Linq;
using Godot;
using Rimisekai.Character;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 角色头像解析：据点主界面头像带取 <c>assets/avatars/</c> 下的 1:1 头像。
///
/// 角色数据里不存身份（身份只是生成器掷骰时的中间量，随特质与武器进入角色），
/// 所以头像按「主角 → 已知名字 → 已授予特质 → 稳定兜底」四段解析，全部确定性、不随帧跳变：
/// 1. 主角固定取 <c>special/avatar_human_paladin.png</c>（开局种子里的「你」）；
/// 2. 开局种子里点名的角色按名字取（与旧状态页口径一致）；
/// 3. 已授予身份特质者按特质取（女仆 / 魔法师）；
/// 4. 其余（生成器掷出的同伴）按 Id 稳定落在一张身份头像上，保证每个角色都有面孔且不跳变。
/// </summary>
public static class PortraitAvatars
{
    private const string Root = "res://assets/avatars/";
    private const string PlayerAvatar = Root + "special/avatar_human_paladin.png";

    /// <summary>31 个身份头像的英文名（与 assets/avatars/identity 目录一致）。</summary>
    private static readonly string[] Identities =
    {
        "alchemist", "apothecary", "archer", "assassin", "astrologer", "bard",
        "blacksmith", "butler", "carpenter", "cook", "courier", "dancer",
        "druid", "gardener", "guard", "hunter", "knight", "mage",
        "maid", "mercenary", "merchant", "monk", "noble", "nun",
        "paladin", "priestess", "ranger", "scholar", "scholar_assistant", "thief", "warrior",
    };

    /// <summary>开局种子点名的角色 → 身份头像。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> ByName = new()
    {
        ["璐米埃尔"] = "maid",
        ["瑞雅莉"] = "knight",
        ["瑞茵"] = "scholar",
    };

    /// <summary>当前会话的名册：没有头像图时字标要与同名册里首字相同的人区分开。领地屏绑定会话时绑上（战斗页与领地屏同一会话）。</summary>
    private static Roster _roster = null!;

    public static void Bind(Roster roster) => _roster = roster;

    /// <summary>
    /// 无图头像的字标：通常取名字首字；名册里另有人首字相同（瑞雅莉 / 瑞茵 / 瑞拉）时，
    /// 框够宽（放得下两个 size 字）就写前两字，否则写名字里第一个与同首字者不同的字（雅 / 茵 / 拉）。
    /// </summary>
    public static string Glyph(string name, float boxWidth, int size)
    {
        if (name.Length < 2)
            return name;
        var twins = _roster.Members.Select(m => m.Name).Where(n => n != name && n.Length > 0 && n[0] == name[0]).ToList();
        if (twins.Count == 0)
            return name[..1];
        if (InkDraw.Measure(name[..2], size).X <= boxWidth * 0.9f)
            return name[..2];
        for (var i = 1; i < name.Length; i++)
        {
            var at = i;
            if (twins.All(t => t.Length <= at || t[at] != name[at]))
                return name.Substring(i, 1);
        }
        return name[..1];
    }

    private static string Identity(string key) => Root + $"identity/{key}_diff1.png";

    public static Texture2D? Resolve(CharacterState? who)
    {
        if (who == null)
            return null;
        if (who.IsMaster)
            return InkIllustration.LoadTexture(PlayerAvatar);
        if (ByName.TryGetValue(who.Name, out var named))
            return InkIllustration.LoadTexture(Identity(named));
        if (who.IsMaid())
            return InkIllustration.LoadTexture(Identity("maid"));
        if (who.IsMage())
            return InkIllustration.LoadTexture(Identity("mage"));
        return InkIllustration.LoadTexture(Identity(Identities[who.Id % Identities.Length]));
    }
}
