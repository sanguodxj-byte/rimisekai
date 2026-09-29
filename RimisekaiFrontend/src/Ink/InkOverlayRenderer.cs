using Godot;
using Rimisekai.Hub;

namespace Rimisekai.Ink;

public static class InkOverlayRenderer
{
    public static void Draw(CanvasItem ci, InkHubModel model)
    {
        var overlay = model.Overlay;
        if (overlay == null)
            return;

        var rect = InkLayout.OverlayContent;

        ci.DrawRect(rect, new Color(InkStyle.Panel, 0.97f));
        InkDraw.Paper(ci, rect.Grow(-4f), seed: 7260);
        InkDraw.Ink(ci, new[]
        {
            rect.Position,
            new Vector2(rect.End.X, rect.Position.Y),
            rect.End,
            new Vector2(rect.Position.X, rect.End.Y),
            rect.Position,
        }, InkStyle.Line, 2f, 0.4f, 7261);
        InkDraw.Ink(ci, new[]
        {
            rect.Position + new Vector2(5f, 5f),
            new Vector2(rect.End.X - 5f, rect.Position.Y + 5f),
            rect.End - new Vector2(5f, 5f),
            new Vector2(rect.Position.X + 5f, rect.End.Y - 5f),
            rect.Position + new Vector2(5f, 5f),
        }, InkStyle.Dim, 1f, 0.3f, 7262);

        InkDraw.Jewel(ci, new Vector2(rect.GetCenter().X, rect.Position.Y + 2.5f), 3f, InkStyle.Line);
        InkDraw.Jewel(ci, new Vector2(rect.GetCenter().X, rect.End.Y - 2.5f), 3f, InkStyle.Line);

        if (overlay.Kind == OverlayKind.Illustration)
        {
            InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 40f, rect.GetCenter().Y - 20f, rect.Size.X - 80f, 40f),
                $"［插画：{overlay.IllustrationId}］", 26, 18, InkStyle.Line, "cm");
            if (overlay.Text.Length > 0)
                InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 40f, rect.End.Y - 60f, rect.Size.X - 80f, 30f),
                    overlay.Text, 20, 14, InkStyle.Dim, "cm");
            return;
        }

        var textToDisplay = overlay.Revealed <= 0
            ? ""
            : overlay.Revealed < overlay.Text.Length
                ? overlay.Text[..overlay.Revealed]
                : overlay.Text;

        if (overlay.Kind == OverlayKind.Dialogue)
        {
            DrawPortrait(ci, overlay);
            DrawMeters(ci, overlay);

            var splitX = (InkLayout.ChatPortrait.End.X + InkLayout.ChatBody.Position.X) / 2f;
            InkFrame.Divider(ci,
                new Vector2(splitX, InkLayout.ChatPortrait.Position.Y + 8f),
                new Vector2(splitX, rect.End.Y - 12f));

            var body = InkLayout.ChatBody;

            var speakerTag = overlay.Speaker;
            if (overlay.BondLabel.Length > 0)
                speakerTag += $"　{overlay.BondLabel}";

            var headerRect = InkLayout.ChatHeaderRect;
            var headerY = headerRect.Position.Y;
            var entriesLeft = InkLayout.ChatEntry(0, InkHubModel.ChatEntries.Length).Position.X;

            var portraitMidX = InkLayout.ChatPortrait.GetCenter().X;
            InkDraw.InkLine(ci, new Vector2(portraitMidX - 34f, headerY + 17f),
                new Vector2(portraitMidX - 12f, headerY + 17f), InkStyle.Dim, 0.9f, 0.2f, 7330);
            InkDraw.Jewel(ci, new Vector2(portraitMidX, headerY + 17f), 2.6f, InkStyle.Line, filled: false);
            InkDraw.InkLine(ci, new Vector2(portraitMidX + 12f, headerY + 17f),
                new Vector2(portraitMidX + 34f, headerY + 17f), InkStyle.Dim, 0.9f, 0.2f, 7331);

            if (speakerTag.Length > 0)
            {
                var nameSize = InkDraw.FitSize(speakerTag, headerRect.Size.X - 24f, 26, 16);
                var nameW = InkDraw.Measure(speakerTag, nameSize).X;
                var plate = new Rect2(headerRect.Position.X - 12f, headerY - 4f,
                    nameW + 24f, nameSize + 16f);
                ci.DrawRect(plate, InkStyle.Inset);
                InkDraw.Ink(ci, new[]
                {
                    plate.Position,
                    new Vector2(plate.End.X, plate.Position.Y),
                    plate.End,
                    new Vector2(plate.Position.X, plate.End.Y),
                    plate.Position,
                }, InkStyle.Line, 1.3f, 0.3f, 7320);
                InkDraw.Ink(ci, new[]
                {
                    plate.Position + new Vector2(3f, 3f),
                    new Vector2(plate.End.X - 3f, plate.Position.Y + 3f),
                    plate.End - new Vector2(3f, 3f),
                    new Vector2(plate.Position.X + 3f, plate.End.Y - 3f),
                    plate.Position + new Vector2(3f, 3f),
                }, InkStyle.Dim, 0.9f, 0.22f, 7321);

                InkDraw.TextBounded(ci, new Rect2(headerRect.Position.X, headerY, nameW + 8f, nameSize + 10f),
                    speakerTag, nameSize, 16, InkStyle.Line, "lm");

                InkFrame.HeaderRule(ci, plate.End.X + 14f, entriesLeft - 14f,
                    plate.GetCenter().Y - 2f);
            }
            else
            {
                InkFrame.HeaderRule(ci, headerRect.Position.X, entriesLeft - 14f, headerY + 14f);
            }

            DrawEntries(ci, model);

            var hasChoices = overlay.Choices.Count > 0;
            var textBox = InkLayout.ChatTextRect(hasChoices, overlay.Choices.Count);
            if (!string.IsNullOrEmpty(textToDisplay))
            {
                InkDraw.Wrapped(ci, textBox, textToDisplay, 22, InkStyle.Line, 32f);
            }

            if (hasChoices)
            {
                for (var i = 0; i < overlay.Choices.Count; i++)
                {
                    var choice = overlay.Choices[i];
                    var choiceRect = InkLayout.OverlayChoice(i, overlay.Choices.Count);
                    InkFrame.Button(ci, choiceRect, choice.Label, selected: false, enabled: true, fontSize: 20);
                }
            }

            DrawContinueHint(ci, overlay);
            return;
        }

        if (overlay.Speaker.Length > 0)
        {
            InkDraw.TextBounded(ci, InkLayout.StorySpeakerRect,
                $"【{overlay.Speaker}】", 24, 16, InkStyle.Line, "lm");
        }

        var storyChoicesCount = overlay.Choices.Count;
        var storyTextBox = InkLayout.StoryTextRect(storyChoicesCount);
        if (!string.IsNullOrEmpty(textToDisplay))
        {
            InkDraw.Wrapped(ci, storyTextBox, textToDisplay, 22, InkStyle.Line, 32f);
        }

        for (var i = 0; i < overlay.Choices.Count; i++)
        {
            var choice = overlay.Choices[i];
            var choiceRect = InkLayout.OverlayChoice(i, overlay.Choices.Count);
            InkFrame.Button(ci, choiceRect, choice.Label, selected: false, enabled: true, fontSize: 20);
        }

        DrawContinueHint(ci, overlay);
    }

    private static void DrawPortrait(CanvasItem ci, OverlayView overlay)
    {
        var box = InkLayout.ChatPortrait;
        ci.DrawRect(box, InkStyle.Bg);

        var loaded = false;
        if (overlay.PortraitPath.Length > 0 && ResourceLoader.Exists(overlay.PortraitPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(overlay.PortraitPath);
            if (tex != null)
            {
                var scale = Mathf.Min(box.Size.X / tex.GetWidth(), box.Size.Y / tex.GetHeight());
                var size = new Vector2(tex.GetWidth(), tex.GetHeight()) * scale;
                ci.DrawTextureRect(tex, new Rect2(box.GetCenter() - size / 2f, size), false);
                loaded = true;
            }
        }

        if (!loaded)
        {
            InkDraw.Figure(ci, box.GetCenter().X, box.End.Y - 18f, box.Size.Y * 0.62f,
                InkStyle.Line, 7);
            InkDraw.Text(ci, new Vector2(box.GetCenter().X, box.Position.Y + 18f),
                "（无立绘）", 16, InkStyle.Dim, "cm");
        }

        InkFrame.CardOutline(ci, box, false);
    }

    private static void DrawMeters(CanvasItem ci, OverlayView overlay)
    {
        var box = InkLayout.ChatMeters;

        ci.DrawRect(box, InkStyle.Inset);
        InkDraw.Ink(ci, new[]
        {
            box.Position,
            new Vector2(box.End.X, box.Position.Y),
            box.End,
            new Vector2(box.Position.X, box.End.Y),
            box.Position,
        }, InkStyle.Dim, 1f, 0.25f, 7450);

        var hasFavor = overlay.Favor.HasValue;
        var hasMood = overlay.Mood.HasValue;

        if (hasFavor && hasMood)
        {
            var row0 = InkLayout.ChatMeterRow(0);
            DrawMeterRow(ci, row0, "好感", overlay.Favor!.Value.ToString(), FavorRatio(overlay.Favor.Value));

            var row1 = InkLayout.ChatMeterRow(1);
            DrawMeterRow(ci, row1, "心情", overlay.Mood!.Value.ToString(), Mathf.Clamp(overlay.Mood.Value / 100f, 0f, 1f));
        }
        else if (hasFavor)
        {
            var row0 = InkLayout.ChatMeterRow(0);
            DrawMeterRow(ci, row0, "好感", overlay.Favor!.Value.ToString(), FavorRatio(overlay.Favor.Value));
        }
        else if (hasMood)
        {
            var row0 = InkLayout.ChatMeterRow(0);
            DrawMeterRow(ci, row0, "心情", overlay.Mood!.Value.ToString(), Mathf.Clamp(overlay.Mood.Value / 100f, 0f, 1f));
        }
    }

    private static void DrawMeterRow(CanvasItem ci, Rect2 row, string label, string valText, float ratio)
    {
        var labelRect = InkLayout.ChatMeterLabel(row);
        var valRect = InkLayout.ChatMeterValue(row);
        var barRect = InkLayout.ChatMeterBar(row);

        InkDraw.TextBounded(ci, labelRect, label, 16, 12, InkStyle.Dim, "lm");
        InkDraw.TextBounded(ci, valRect, valText, 16, 12, InkStyle.Line, "rm");
        InkDraw.Meter(ci, barRect, ratio);
    }

    private static float FavorRatio(int favor) =>
        Mathf.Clamp((favor + 1000f) / 2000f, 0f, 1f);

    private static void DrawEntries(CanvasItem ci, InkHubModel model)
    {
        var entries = InkHubModel.ChatEntries;
        for (var i = 0; i < entries.Length; i++)
        {
            var widget = model.Find(InkAction.ChatEntry, i);
            var enabled = widget?.Enabled ?? false;
            InkFrame.Button(ci, InkLayout.ChatEntry(i, entries.Length),
                InkPageModel.Info(entries[i]).Label, false, enabled, 18);
        }
    }

    private static void DrawContinueHint(CanvasItem ci, OverlayView overlay)
    {
        if (overlay.Waiting)
            return;

        var hint = overlay.FullyRevealed ? "▽ 点击继续" : "▽ 点击跳过";
        InkDraw.TextBounded(ci, InkLayout.ChatContinueRect,
            hint, 18, 14, InkStyle.Dim, "rm");
    }
}
