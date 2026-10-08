using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Portrait;

/// <summary>
/// 世界探索遭遇的弹窗：Core 摆出一桩遭遇（<see cref="HubSession.PendingEncounter"/>）后弹一次，
/// 战斗给「迎战 / 绕开或退回」两钮，事件给一枚收下结果的钮。文案全部来自 Core 与 map_defs.json。
/// </summary>
public sealed partial class PortraitHubScreen
{
    /// <summary>已经弹过的那一桩（同一桩不重复弹）。</summary>
    private Encounter? _encounterShown;

    /// <summary>核对用：眼前这桩遭遇的弹窗是否已弹出。</summary>
    public bool DebugEncounterShown => _encounterShown != null && ReferenceEquals(_encounterShown, _vm.Hub.PendingEncounter);

    private bool OfferEncounter()
    {
        var e = _vm.Hub.PendingEncounter;
        if (e == null || ReferenceEquals(e, _encounterShown) || Walking)
            return false;
        _encounterShown = e;
        var page = new InkModalPage { Title = e.Title, Body = e.Body };
        if (e.IsBattle)
        {
            page.Choices.Add(new InkModalChoice { Id = "fight", Label = e.FightLabel, OnSelected = FightEncounter });
            page.Choices.Add(new InkModalChoice { Id = "avoid", Label = e.AvoidLabel, OnSelected = () => SettleEncounter(_vm.Hub.AvoidEncounter) });
        }
        else
        {
            page.Choices.Add(new InkModalChoice { Id = "accept", Label = e.AcceptLabel, OnSelected = () => SettleEncounter(_vm.Hub.AcceptEncounter) });
        }
        ModalWanted!(page);
        return true;
    }

    private void FightEncounter()
    {
        var session = _vm.Hub.FightEncounter();
        if (session == null)
            return;
        BattleWanted?.Invoke(session);
    }

    private void SettleEncounter(System.Action act)
    {
        act();
        if (WorldLayer)
            CenterWorldOnParty();
        QueueRedraw();
    }
}
