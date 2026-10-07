using System.Collections.Generic;
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

        // 演出中插画作底：聊天层转半透明，画从底下透出来；平时照旧近乎不透明。
        var backdrop = model.SceneMode ? 0.6f : 0.97f;
        ci.DrawRect(rect, new Color(InkStyle.Panel, backdrop));
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

        if (overlay.Kind == OverlayKind.Illustration)
        {
            InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 40f, rect.GetCenter().Y - 20f, rect.Size.X - 80f, 40f),
                $"［插画：{overlay.IllustrationId}］", 26, 18, InkStyle.Line, "cm");
            if (overlay.Text.Length > 0)
                InkDraw.TextBounded(ci, new Rect2(rect.Position.X + 40f, rect.End.Y - 60f, rect.Size.X - 80f, 30f),
                    overlay.Text, 20, 14, InkStyle.Dim, "cm");
            return;
        }

        // 段落累积：同段之前的句子整句保留，末句跟随打字机逐字；
        // 各句独立折行，超出文本框容量时保留最新的行。
        var sentences = new List<string>(overlay.OverlayLines);
        if (sentences.Count > 0 && overlay.Revealed < overlay.Text.Length)
            sentences[^1] = overlay.Text[..overlay.Revealed];

        if (overlay.Kind == OverlayKind.Dialogue)
        {
            DrawPortrait(ci, overlay, model.SceneMode);
            DrawMeters(ci, overlay);

            var body = InkLayout.ChatBody;

            var speakerTag = overlay.Speaker;
            if (overlay.BondLabel.Length > 0)
                speakerTag += $"　{overlay.BondLabel}";

            var headerRect = InkLayout.ChatHeaderRect;
            var headerY = headerRect.Position.Y;
            var entriesLeft = InkLayout.ChatEntry(0, InkHubModel.ChatEntries.Length).Position.X;

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

            }
            else
            {
            }

            DrawEntries(ci, model);

            var hasChoices = overlay.Choices.Count > 0;
            var textBox = InkLayout.ChatTextRect(hasChoices, overlay.Choices.Count);

            var bodyLines = new List<(string Text, bool Dim)>();
            for (var s = 0; s < sentences.Count; s++)
            {
                var sentence = sentences[s];
                var isDim = sentence.StartsWith("［判定");
                foreach (var line in InkDraw.WrapLines(sentence, textBox.Size.X, 22))
                    bodyLines.Add((line, isDim));
            }
            const float lineHeight = 32f;
            var capacity = Mathf.Max(1, (int)((textBox.Size.Y + 8f) / lineHeight));
            var skip = Mathf.Max(0, bodyLines.Count - capacity);
            for (var i = skip; i < bodyLines.Count; i++)
            {
                var color = bodyLines[i].Dim ? InkStyle.Dim : InkStyle.Line;
                InkDraw.Text(ci,
                    new Vector2(textBox.Position.X, textBox.Position.Y + (i - skip) * lineHeight),
                    bodyLines[i].Text, 22, color);
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
        // 旁白段保持旧行为：单句跟随打字机。
        var storyText = overlay.Revealed <= 0
            ? ""
            : overlay.Revealed < overlay.Text.Length
                ? overlay.Text[..overlay.Revealed]
                : overlay.Text;
        if (!string.IsNullOrEmpty(storyText))
        {
            InkDraw.Wrapped(ci, storyTextBox, storyText, 22, InkStyle.Line, 32f);
        }

        for (var i = 0; i < overlay.Choices.Count; i++)
        {
            var choice = overlay.Choices[i];
            var choiceRect = InkLayout.OverlayChoice(i, overlay.Choices.Count);
            InkFrame.Button(ci, choiceRect, choice.Label, selected: false, enabled: true, fontSize: 20);
        }

        DrawContinueHint(ci, overlay);
    }

    private static void DrawPortrait(CanvasItem ci, OverlayView overlay, bool overIllustration)
    {
        var box = InkLayout.ChatPortrait;
        // 演出中这块底下是插画：底色转半透明，不掏出一块死黑。
        ci.DrawRect(box, overIllustration ? new Color(InkStyle.Bg, 0.6f) : InkStyle.Bg);

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

        InkDraw.TextBounded(ci, labelRect, label, 26, 26, InkStyle.Dim, "lm");
        InkDraw.TextBounded(ci, valRect, valText, 26, 26, InkStyle.Line, "rm");
        InkDynamicMeter.Draw(ci, $"chat_meter_{label}", barRect, ratio, isHp: false);
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
                InkPageModel.Info(entries[i]).Label, false, enabled, 26);
        }
    }

    private static void DrawContinueHint(CanvasItem ci, OverlayView overlay)
    {
        if (overlay.Waiting)
            return;

        var hint = "▽";
        InkDraw.TextBounded(ci, InkLayout.ChatContinueRect,
            hint, 26, 26, InkStyle.Dim, "rm");
    }
}
