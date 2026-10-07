namespace Rimisekai.Defs;

/// <summary>
/// 默认 Def 初始化器门面。数据已全部迁移至 content/defs/ 数据表，
/// 由 DefLoader 统一装载入 DefDatabase，本类不再包含任何硬编码数据。
/// </summary>
public static class DefaultDefs
{
    public static void EnsureInitialized()
    {
        DefLoader.EnsureInitialized();
    }
}
