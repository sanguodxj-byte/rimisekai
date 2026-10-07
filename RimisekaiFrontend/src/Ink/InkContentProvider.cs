using Godot;
using Rimisekai.Flow;

namespace Rimisekai.Ink;

/// <summary>
/// 内容目录 provider 的安装（原先埋在 InkRoot 的静态构造里，竖版靠 RunClassConstructor 触发）。
/// 横版根场景删除后，启动路径直接调 Install()。
/// </summary>
public static class InkContentProvider
{
    public static void Install()
    {
        Defs.DefLoader.CustomContentProvider = () =>
        {
            var results = new System.Collections.Generic.List<(string, string)>();
            using var dir = DirAccess.Open("res://content/defs");
            if (dir != null)
            {
                dir.ListDirBegin();
                var file = dir.GetNext();
                while (!string.IsNullOrEmpty(file))
                {
                    if (!dir.CurrentIsDir() && (file.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase)
                        || file.EndsWith(".xml", System.StringComparison.OrdinalIgnoreCase)))
                    {
                        using var fa = FileAccess.Open($"res://content/defs/{file}", FileAccess.ModeFlags.Read);
                        if (fa != null)
                            results.Add((file, fa.GetAsText()));
                    }
                    file = dir.GetNext();
                }
            }
            return results;
        };

        WorldMap.MapCatalog.CustomJsonProvider = () =>
        {
            if (FileAccess.FileExists("res://content/map_defs.json"))
            {
                using var fa = FileAccess.Open("res://content/map_defs.json", FileAccess.ModeFlags.Read);
                return fa?.GetAsText();
            }
            return null;
        };
    }
}
