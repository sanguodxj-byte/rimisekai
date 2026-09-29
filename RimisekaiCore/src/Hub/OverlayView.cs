using System.Collections.Generic;

namespace Rimisekai.Hub;

public enum OverlayKind
{
    Dialogue,
    Story,
    Illustration,
}

public readonly record struct OverlayLine(string Speaker, string Text);

public readonly record struct OverlayChoice(int Id, string Label);

/// <summary>
/// 盖住地图区的一层。与地图等大、完全遮盖。打开时地图不可操作。
/// </summary>
public sealed class MapOverlay
{
    public OverlayKind Kind { get; }
    public string Speaker { get; private set; } = "";
    public string Text { get; private set; } = "";
    public string IllustrationId { get; private set; } = "";
    public List<OverlayChoice> Choices { get; } = new();
    public bool Waiting { get; private set; }

    private readonly Queue<OverlayLine> _lines = new();

    public MapOverlay(OverlayKind kind) => Kind = kind;

    public static MapOverlay Dialogue(string speaker, IEnumerable<string> lines)
    {
        var overlay = new MapOverlay(OverlayKind.Dialogue) { Speaker = speaker };
        foreach (var line in lines)
            overlay._lines.Enqueue(new OverlayLine(speaker, line));
        overlay.Advance();
        return overlay;
    }

    public static MapOverlay Story(IEnumerable<OverlayLine> lines)
    {
        var overlay = new MapOverlay(OverlayKind.Story);
        foreach (var line in lines)
            overlay._lines.Enqueue(line);
        overlay.Advance();
        return overlay;
    }

    public static MapOverlay Illustration(string illustrationId, string caption = "")
    {
        return new MapOverlay(OverlayKind.Illustration)
        {
            IllustrationId = illustrationId,
            Text = caption,
        };
    }

    public void Offer(IEnumerable<OverlayChoice> choices)
    {
        Choices.Clear();
        Choices.AddRange(choices);
        Waiting = Choices.Count > 0;
    }

    public bool Advance()
    {
        if (Waiting)
            return false;
        if (_lines.Count == 0)
            return false;
        var line = _lines.Dequeue();
        Speaker = line.Speaker;
        Text = line.Text;
        return true;
    }

    public bool Choose(int choiceId)
    {
        if (!Waiting || !Choices.Exists(c => c.Id == choiceId))
            return false;
        Choices.Clear();
        Waiting = false;
        return true;
    }

    public bool Exhausted => !Waiting && _lines.Count == 0;
}

public sealed partial class HubSession
{
    public MapOverlay? Overlay { get; private set; }
    public bool MapCovered => Overlay != null;

    public void Show(MapOverlay overlay) => Overlay = overlay;

    public bool AdvanceOverlay()
    {
        if (Overlay == null)
            return false;
        if (Overlay.Waiting)
            return true;
        if (!Overlay.Advance())
            Overlay = null;
        return Overlay != null;
    }

    public bool Choose(int choiceId)
    {
        if (Overlay == null || !Overlay.Choose(choiceId))
            return false;
        if (Overlay.Exhausted)
            Overlay = null;
        return true;
    }

    public void CloseOverlay() => Overlay = null;
}
