using Rimisekai.Defs;

namespace Rimisekai.Ink;

/// <summary>
/// 内容包句柄。全部内容定义在 Core 的 <see cref="ContentDefs"/>（代码 Def，
/// 原 content/*.json 配表已迁移），本类只携带开局种子变体在装配与界面之间传递。
/// </summary>
public sealed class ContentPack
{
    private readonly bool _hard;

    public ContentPack(bool hard = false) => _hard = hard;

    /// <summary>本包对应的开局种子（标准或困难）。</summary>
    public NewGameSeedDef Seed => _hard ? ContentDefs.HardSeed : ContentDefs.StandardSeed;
}
