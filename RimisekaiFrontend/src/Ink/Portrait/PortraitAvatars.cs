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
    private const string PlayerAvatar = Root + "identity_moe/paladin_moe_diff1.png";

    /// <summary>正统萌化身份头像的英文名（与 assets/avatars/identity_moe 目录一致，全量 34 位）。</summary>
    public static readonly string[] Identities =
    {
        "alchemist", "apothecary", "archer", "assassin", "astrologer", "bard",
        "beast_tamer", "blacksmith", "butler", "carpenter", "cook", "courier",
        "dancer", "dragon_knight", "druid", "gardener", "guard", "hunter",
        "knight", "mage", "magic_swordsman", "maid", "mercenary", "merchant",
        "monk", "noble", "nun", "paladin", "priestess", "ranger",
        "scholar", "scholar_assistant", "thief", "warrior",
    };

    /// <summary>中文身份名称 → 英文身份目录标识。</summary>
    public static readonly System.Collections.Generic.Dictionary<string, string> IdentityKeyMap = new()
    {
        ["女仆"] = "maid",
        ["骑士"] = "knight",
        ["商人"] = "merchant",
        ["学者"] = "scholar",
        ["神官"] = "priestess",
        ["魔法师"] = "mage",
        ["战士"] = "warrior",
        ["护卫"] = "guard",
        ["佣兵"] = "mercenary",
        ["弓箭手"] = "archer",
        ["猎人"] = "hunter",
        ["刺客"] = "assassin",
        ["盗贼"] = "thief",
        ["吟游诗人"] = "bard",
        ["舞娘"] = "dancer",
        ["药师"] = "apothecary",
        ["炼金术士"] = "alchemist",
        ["铁匠"] = "blacksmith",
        ["木匠"] = "carpenter",
        ["厨师"] = "cook",
        ["花匠"] = "gardener",
        ["信使"] = "courier",
        ["修女"] = "nun",
        ["僧侣"] = "monk",
        ["德鲁伊"] = "druid",
        ["游侠"] = "ranger",
        ["圣骑士"] = "paladin",
        ["贵族"] = "noble",
        ["管家"] = "butler",
        ["学者助手"] = "scholar_assistant",
        ["星术师"] = "astrologer",
        ["龙骑士"] = "dragon_knight",
        ["魔剑士"] = "magic_swordsman",
        ["驯兽师"] = "beast_tamer",
    };

    /// <summary>开局初始唯一命名伙伴 → 身份头像（仅女仆璐米埃尔）。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, string> ByName = new()
    {
        ["璐米埃尔"] = "maid",
    };

    /// <summary>解析角色的英文身份标识。</summary>
    public static string ResolveIdentityKey(CharacterState? who)
    {
        if (who == null) return "paladin";
        if (!string.IsNullOrEmpty(who.Identity))
        {
            if (IdentityKeyMap.TryGetValue(who.Identity, out var mapped))
                return mapped;
            if (System.Array.IndexOf(Identities, who.Identity) >= 0)
                return who.Identity;
        }
        if (who.IsMaster || who.Name == "你")
            return "paladin";
        if (ByName.TryGetValue(who.Name, out var named))
            return named;
        if (who.IsMaid())
            return "maid";
        if (who.IsMage())
            return "mage";
        return Identities[who.Id % Identities.Length];
    }

    /// <summary>英文身份标识 → 对应中文身份名称。</summary>
    public static string ResolveIdentityLabel(string identityKey)
    {
        foreach (var (k, v) in IdentityKeyMap)
        {
            if (v == identityKey) return k;
        }
        return identityKey;
    }

    /// <summary>某身份包含的立绘差分总数（默认为 5，mage 为 6）。</summary>
    public static int DiffCountFor(string identityKey) => identityKey == "mage" ? 6 : 5;

    public static string PortraitPath(string identityKey, int diff) =>
        $"res://assets/portraits/identity_moe/{identityKey}_moe_diff{diff}.png";

    public static string AvatarPath(string identityKey, int diff) =>
        Root + $"identity_moe/{identityKey}_moe_diff{diff}.png";

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

    public static Texture2D? Resolve(CharacterState? who)
    {
        if (who == null)
            return null;
        var key = ResolveIdentityKey(who);
        var diff = who.PortraitDiff > 0 ? who.PortraitDiff : 1;
        var path = AvatarPath(key, diff);
        var tex = InkIllustration.LoadTexture(path);
        if (tex != null)
            return tex;
        return InkIllustration.LoadTexture(AvatarPath(key, 1));
    }
}
