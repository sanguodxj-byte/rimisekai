namespace Rimisekai.Defs;

/// <summary>建造目录里的一类：设施分类或房间分类（content/defs/build_categories.json，表里的先后即页签先后）。</summary>
public sealed class BuildCategoryDef : Def
{
    /// <summary>这一类装的是设施还是房间。</summary>
    public BuildTarget Target { get; init; }
}

/// <summary>建造的对象：往房里添设施，或往空房/空地里建房间。</summary>
public enum BuildTarget
{
    Facility,
    Room,
}
