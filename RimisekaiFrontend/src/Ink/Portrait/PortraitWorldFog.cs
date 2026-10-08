using Godot;
using Rimisekai.Hub;
using Rimisekai.WorldMap;

namespace Rimisekai.Portrait;

/// <summary>
/// 世界层的迷雾遮罩：一格一像素，未探明的格是近乎不透的墨黑，已探明的格全透明。
/// 按会话的探明记录现烘；探明格数变了（走了一程、读了档）才重烘。
/// </summary>
public static class PortraitWorldFog
{
    private static readonly Color Fog = new(0.02f, 0.02f, 0.02f, 0.94f);

    private static WorldMapData? _for;
    private static int _count = -1;
    private static ImageTexture? _texture;

    public static ImageTexture Texture(WorldMapData map, HubSession hub)
    {
        var count = hub.State.Exploration.DiscoveredCount;
        if (_texture != null && ReferenceEquals(_for, map) && _count == count)
            return _texture;
        _for = map;
        _count = count;
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
                image.SetPixel(x, y, hub.IsWorldDiscovered(x, y) ? Colors.Transparent : Fog);
        if (_texture == null)
            _texture = ImageTexture.CreateFromImage(image);
        else
            _texture.Update(image);
        return _texture;
    }
}
