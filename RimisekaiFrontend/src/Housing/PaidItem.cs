namespace Rimisekai.Housing;

/// <summary>付料的一笔：从哪儿（背包或某件仓储）扣了多少。撤销建造时原样退回原处。</summary>
public readonly record struct PaidItem(int FacilityId, string ItemId, int Count)
{
    /// <summary>扣的是付料人的背包。</summary>
    public const int FromBag = -1;
}
