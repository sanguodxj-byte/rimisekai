namespace Rimisekai.Defs;

/// <summary>
/// 装备槽位。十格：
/// 武器占主手与副手，防具占帽子/上装/下装/手套/鞋子，饰品占两枚戒指与一条项链。
/// </summary>
public enum EquipSlot
{
    /// <summary>主手。只能放武器。</summary>
    MainHand = 0,

    /// <summary>副手。武器或盾。</summary>
    OffHand = 1,

    /// <summary>帽子。</summary>
    Head = 2,

    /// <summary>上装。</summary>
    Torso = 3,

    /// <summary>下装。</summary>
    Legs = 4,

    /// <summary>手套。</summary>
    Hands = 5,

    /// <summary>鞋子。</summary>
    Feet = 6,

    /// <summary>第一枚戒指。</summary>
    Ring1 = 7,

    /// <summary>第二枚戒指。</summary>
    Ring2 = 8,

    /// <summary>项链。</summary>
    Neck = 9,
}

/// <summary>槽位的分类与显示名。</summary>
public static class EquipSlots
{
    public const int Count = 10;

    public static string Label(EquipSlot slot) => slot switch
    {
        EquipSlot.MainHand => "主手",
        EquipSlot.OffHand => "副手",
        EquipSlot.Head => "帽子",
        EquipSlot.Torso => "上装",
        EquipSlot.Legs => "下装",
        EquipSlot.Hands => "手套",
        EquipSlot.Feet => "鞋子",
        EquipSlot.Ring1 => "戒指一",
        EquipSlot.Ring2 => "戒指二",
        EquipSlot.Neck => "项链",
        _ => "?",
    };

    /// <summary>饰品槽的物品名词。饰器件件不是戒指就是项链。</summary>
    public static string AccessoryNoun(EquipSlot slot) => slot switch
    {
        EquipSlot.Ring1 or EquipSlot.Ring2 => "戒指",
        EquipSlot.Neck => "项链",
        _ => "?",
    };

    /// <summary>这件装备能不能进这个槽。武器只进主副手，防具进五件甲位（盾进副手），饰品进三件饰位。</summary>
    public static bool Accepts(EquipSlot slot, EquipKind kind) => kind switch
    {
        EquipKind.Weapon => slot is EquipSlot.MainHand or EquipSlot.OffHand,
        EquipKind.Armor => slot is EquipSlot.OffHand or EquipSlot.Head or EquipSlot.Torso or EquipSlot.Legs
            or EquipSlot.Hands or EquipSlot.Feet,
        EquipKind.Accessory => slot is EquipSlot.Ring1 or EquipSlot.Ring2 or EquipSlot.Neck,
        _ => false,
    };
}

/// <summary>装备的大类。决定它能进哪些槽。</summary>
public enum EquipKind
{
    /// <summary>武器。进主手/副手。</summary>
    Weapon = 0,

    /// <summary>防具。进帽子/上装/下装/手套/鞋子；盾也算防具，进副手。</summary>
    Armor = 1,

    /// <summary>饰品。进两枚戒指与一条项链，按类型加战斗属性。</summary>
    Accessory = 2,
}
