using System;

using System.Diagnostics;

using TMPro;

using UnityEngine;

using UnityEngine.UI;

namespace GoghSpotifyMod;

public sealed partial class NowPlayingPanel
{
    private const float PlayerWidth = 336f;

    private const float PlayerHeight = 168f;

    private TextMeshProUGUI elapsedLabel;

    private TextMeshProUGUI durationLabel;

    private RectTransform progressFill;

    private void CreateTimeline()
    {
        RectTransform track = CreateRect("Timeline", panel, 304f, 4f, 16f, -96f);

        Image background = track.gameObject.AddComponent<Image>();

        background.sprite = roundedSprite;

        background.type = Image.Type.Sliced;

        background.color = new Color(0.28f, 0.37f, 0.41f);

        background.raycastTarget = false;

        progressFill = CreateRect("Elapsed", track, 0f, 4f, 0f, 0f);

        Image fill = progressFill.gameObject.AddComponent<Image>();

        fill.sprite = roundedSprite;

        fill.type = Image.Type.Sliced;

        fill.color = new Color(0.75f, 0.88f, 0.86f);

        fill.raycastTarget = false;

        elapsedLabel = CreateLabel("ElapsedTime", panel, "--:--", 11f, 16f, 102f, 100f, 18f, new Color(0.76f, 0.82f, 0.84f));

        durationLabel = CreateLabel("Duration", panel, "--:--", 11f, 220f, 102f, 100f, 18f, new Color(0.76f, 0.82f, 0.84f));

        durationLabel.alignment = TextAlignmentOptions.MidlineRight;
    }

    private void RefreshTimeline(PanelSnapshot snapshot, bool ready)
    {
        bool available = ready &&
            snapshot.TimelineAvailable &&
            PlaybackTimeline.Finite(snapshot.DurationSeconds) &&
            snapshot.DurationSeconds > 0 &&
            PlaybackTimeline.Finite(snapshot.PositionSeconds);

        if (!available)
        {
            elapsedLabel.text = "--:--";

            durationLabel.text = "--:--";

            progressFill.sizeDelta = new Vector2(0f, 4f);

            return;
        }

        double sampleAge = (Stopwatch.GetTimestamp() - snapshot.ReceivedAtTicks) / (double)Stopwatch.Frequency;

        double position = PlaybackTimeline.Position(
            snapshot.PositionSeconds,
            snapshot.DurationSeconds,
            sampleAge,
            snapshot.PlaybackStatus == "Playing",
            snapshot.PlaybackRate
        );

        elapsedLabel.text = PlaybackTimeline.Format(position);

        durationLabel.text = PlaybackTimeline.Format(snapshot.DurationSeconds);

        progressFill.sizeDelta = new Vector2((float)(304.0 * position / snapshot.DurationSeconds), 4f);
    }
}
