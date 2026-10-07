using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;
using Rimisekai.Housing;
using Rimisekai.Hub;
using Rimisekai.Ink;

namespace Rimisekai.Tools;

/// <summary>
/// 开发期审计工具（不参与正式启动）：把每一屏实际注册的可点热区导成 JSONL，
/// 供手机端适配分析量化「触控目标物理尺寸」。只读，不改任何界面。
/// --audit=&lt;路径&gt;：输出文件。
/// </summary>
public partial class MobileHitAudit : Node
{
    private const string DefaultOut = "D:/123/rimisekai/_ui_mobile/hits.jsonl";

    public override void _Ready()
    {
        var path = DefaultOut;
        var rectsPath = "";
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--audit="))
                path = a["--audit=".Length..];
            else if (a.StartsWith("--rects="))
                rectsPath = a["--rects=".Length..];
        }

        if (rectsPath.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rectsPath)!);
            File.WriteAllText(rectsPath, DumpLayoutRects());
            GD.Print($"rects ok: {rectsPath}");
            GetTree().Quit();
            return;
        }

        var sb = new StringBuilder();
        foreach (var scenario in Scenarios())
            Dump(sb, scenario.name, scenario.prepare);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        GD.Print($"audit ok: {path}");
        GetTree().Quit();
    }

    /// <summary>把 InkLayout 里全部静态 Rect2 面板矩形（字段 + 表达式体属性）原样导出，供真实版式复核。</summary>
    private static string DumpLayoutRects()
    {
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
        var type = typeof(InkLayout);
        var sb = new StringBuilder();
        foreach (var f in type.GetFields(flags))
        {
            if (f.FieldType != typeof(Rect2))
                continue;
            Append(sb, f.Name, (Rect2)f.GetValue(null)!);
        }
        foreach (var p in type.GetProperties(flags))
        {
            if (p.PropertyType != typeof(Rect2) || !p.CanRead)
                continue;
            Append(sb, p.Name, (Rect2)p.GetValue(null)!);
        }
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string name, Rect2 r) =>
        sb.Append("{\"name\":\"").Append(name)
          .Append("\",\"x\":").Append(F(r.Position.X))
          .Append(",\"y\":").Append(F(r.Position.Y))
          .Append(",\"w\":").Append(F(r.Size.X))
          .Append(",\"h\":").Append(F(r.Size.Y)).Append("}\n");

    private static IEnumerable<(string name, System.Action<HubSession, InkViewModel, InkUiState> prepare)> Scenarios()
    {
        yield return ("hub", (_, _, _) => { });
        yield return ("observe", (_, _, ui) => ui.Observing = true);
        yield return ("rename", (_, _, ui) => ui.Renaming = true);
        yield return ("world", (hub, _, _) => hub.SwitchToWorld());
        yield return ("poi", (hub, _, _) =>
        {
            if (hub.State.World.Pois.Count > 0)
                hub.EnterWorldPoi(hub.State.World.Pois[0].Id);
        });
        yield return ("storage", (hub, _, _) =>
        {
            var storable = hub.State.Territory.Facilities.Find(f =>
                f.RoomId == hub.PlayerRoomId && f.Built && f.CanStore);
            if (storable == null)
                return;
            hub.Use(storable.Id);
            hub.ActAtFixture(ActionKind.Store);
        });
        yield return ("stock", (_, _, ui) => ui.OpenPage = InkPage.Stock);
        yield return ("trade", (_, _, ui) => ui.OpenPage = InkPage.Trade);
        yield return ("craft", (_, _, ui) => ui.OpenPage = InkPage.Craft);
        yield return ("status", (_, _, ui) => ui.OpenPage = InkPage.Status);
        yield return ("skills", (_, _, ui) => ui.OpenPage = InkPage.Skills);
        yield return ("schedule", (_, _, ui) => ui.OpenPage = InkPage.Schedule);
        yield return ("develop", (_, _, ui) => ui.OpenPage = InkPage.Develop);
        yield return ("develop_mode", (_, _, ui) =>
        {
            ui.OpenPage = InkPage.Develop;
            ui.DevMode = true;
        });
        yield return ("combat", (_, vm, ui) =>
        {
            var battle = Rimisekai.Combat.BattleSkills.AttackId;
            vm.Combat = Encounters.Start(vm.Hub.State, Foes());
            vm.CombatT = 1f;
            ui.CombatCategory = -1;
            ui.ArmedSkillId = battle;
        });
    }

    private static List<Rimisekai.Catalog.EnemyDef> Foes()
    {
        var list = new List<Rimisekai.Catalog.EnemyDef>();
        foreach (var col in new[] { 1, 4 })
            for (var tier = 1; tier <= 3; tier++)
                list.Add(new Rimisekai.Catalog.EnemyDef
                {
                    Id = $"foe_c{col}_t{tier}", Name = $"敌{col}{tier}",
                    MaxHp = 14, Attack = 3, ThreatTier = tier, Column = col, Speed = 8 + tier,
                });
        list.Add(new Rimisekai.Catalog.EnemyDef
        {
            Id = "boss", Name = "首领", MaxHp = 80, Attack = 8, Defence = 4,
            ThreatTier = 4, Column = 2, Size = 2, Speed = 8,
        });
        return list;
    }

    private static void Dump(StringBuilder sb, string scenario,
        System.Action<HubSession, InkViewModel, InkUiState> prepare)
    {
        var pack = new ContentPack();
        var hub = InkWorldBootstrap.OpenHub(pack, out _);
        var vm = new InkViewModel(hub, pack);
        var ui = new InkUiState();
        prepare(hub, vm, ui);

        var model = InkHubModel.Build(vm, ui);
        foreach (var w in model.Widgets)
        {
            var kind = w.Polygon is { Length: >= 3 } ? "poly" : "rect";
            sb.Append('{')
              .Append("\"scenario\":\"").Append(scenario).Append('"')
              .Append(",\"action\":\"").Append(w.Action).Append('"')
              .Append(",\"index\":").Append(w.Index.ToString(CultureInfo.InvariantCulture))
              .Append(",\"enabled\":").Append(w.Enabled ? "true" : "false")
              .Append(",\"shape\":\"").Append(kind).Append('"')
              .Append(",\"x\":").Append(F(w.Rect.Position.X))
              .Append(",\"y\":").Append(F(w.Rect.Position.Y))
              .Append(",\"w\":").Append(F(w.Rect.Size.X))
              .Append(",\"h\":").Append(F(w.Rect.Size.Y))
              .Append(",\"label\":\"").Append(Esc(w.Label)).Append('"')
              .Append(",\"value\":\"").Append(Esc(w.Value)).Append('"')
              .Append("}\n");
        }
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Esc(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
