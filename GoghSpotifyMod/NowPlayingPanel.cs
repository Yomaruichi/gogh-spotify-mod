using System;

using System.Runtime.InteropServices;

using System.Threading;

using BepInEx.Configuration;

using BepInEx.Logging;

using TMPro;

using UnityEngine;

using UnityEngine.Events;

using UnityEngine.EventSystems;

using UnityEngine.UI;

namespace GoghSpotifyMod;

public sealed partial class NowPlayingPanel : MonoBehaviour
{
    internal static ManualLogSource PanelLog;

    internal static ConfigEntry<bool> VisibleSetting;

    internal static ConfigEntry<float> RightMargin;

    internal static ConfigEntry<float> BottomMargin;

    internal static Action<string> SendCommand;

    private static PanelSnapshot latestSnapshot = new PanelSnapshot();

    private static GameObject canvasObject;

    private RectTransform panel;

    private Canvas canvas;

    private TextMeshProUGUI titleLabel;

    private TextMeshProUGUI artistLabel;

    private TextMeshProUGUI statusLabel;

    private TMP_FontAsset panelFont;

    private Image coverImage;

    private Texture2D artworkTexture;

    private Sprite artworkSprite;

    private Sprite roundedSprite;

    private Image toggleIcon;

    private Sprite playSprite;

    private Sprite pauseSprite;

    private Button previousButton;

    private Button toggleButton;

    private Button nextButton;

    private Vector2 dragStart;

    private Vector2 panelStart;

    private string lastArtwork;

    private bool f9WasDown;

    private bool panelFailed;

    private float nextRefreshAt;

    private float nextCreateAttemptAt = 3f;

    private float nextCommandAt;

    private bool dragging;

    public NowPlayingPanel(IntPtr pointer) : base(pointer)
    {
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    internal static void Publish(
        string status,
        string title,
        string artist,
        string playbackStatus,
        string artwork = "",
        bool canPrevious = false,
        bool canToggle = false,
        bool canNext = false,
        bool timelineAvailable = false,
        double positionSeconds = 0,
        double durationSeconds = 0,
        double playbackRate = 1
    )
    {
        // Only managed data crosses the reader thread; Unity work stays in Update.
        Volatile.Write(ref latestSnapshot, new PanelSnapshot
        {
            Status = status,
            Title = title,
            Artist = artist,
            PlaybackStatus = playbackStatus,
            Artwork = artwork ?? "",
            CanPrevious = canPrevious,
            CanToggle = canToggle,
            CanNext = canNext,
            TimelineAvailable = timelineAvailable,
            PositionSeconds = positionSeconds,
            DurationSeconds = durationSeconds,
            PlaybackRate = playbackRate
        });
    }

    public void Update()
    {
        if (panelFailed)
        {
            return;
        }

        try
        {
            bool f9IsDown = (GetAsyncKeyState(0x78) & 0x8000) != 0;

            if (Application.isFocused && f9IsDown && !f9WasDown)
            {
                VisibleSetting.Value = !VisibleSetting.Value;

                // INFO on a focused F9 press; visible is the persisted preference.
                PanelLog.LogInfo($"[INFO] Spotify.Panel toggled; visible={VisibleSetting.Value}");
            }

            f9WasDown = f9IsDown;

            if (canvasObject == null)
            {
                if (Time.unscaledTime < nextCreateAttemptAt)
                {
                    return;
                }

                nextCreateAttemptAt = Time.unscaledTime + 2f;

                if (!TryCreatePanel())
                {
                    return;
                }
            }

            canvasObject.SetActive(VisibleSetting.Value);

            ApplyAppearance();

            ClampPanel();

            UpdateAppearanceMenu();

            if (Time.unscaledTime < nextRefreshAt)
            {
                return;
            }

            nextRefreshAt = Time.unscaledTime + 0.2f;

            RefreshLabels();
        }
        catch (Exception exception)
        {
            panelFailed = true;

            Remove();

            // ERROR once if UI fails; metadata reading remains available.
            PanelLog.LogError($"[ERROR] Spotify.Panel failed; nextAction=disable panel and retain bridge; exception={exception}");
        }
    }

    private bool TryCreatePanel()
    {
        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == null)
            {
                continue;
            }

            panelFont ??= font;

            string name = font.name.ToLowerInvariant();

            if (name.Contains("japan") || name.Contains("notosansjp") || name.Contains("cjk"))
            {
                panelFont = font;

                break;
            }
        }

        if (panelFont == null)
        {
            return false;
        }

        canvasObject = new GameObject("GoghSpotifyNowPlaying");

        UnityEngine.Object.DontDestroyOnLoad(canvasObject);

        canvas = canvasObject.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        canvas.sortingOrder = 30000;

        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

        scaler.referenceResolution = new Vector2(1280f, 720f);

        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

        scaler.matchWidthOrHeight = 0.5f;

        roundedSprite = CreateShape("rounded");

        playSprite = CreateShape("play");

        pauseSprite = CreateShape("pause");

        panel = CreateRect("SpotifyPlayer", canvasObject.transform, PlayerWidth, PlayerHeight, 0f, 0f);

        panel.anchorMin = new Vector2(1f, 0f);

        panel.anchorMax = new Vector2(1f, 0f);

        panel.pivot = new Vector2(1f, 0f);

        panel.anchoredPosition = new Vector2(-RightMargin.Value, BottomMargin.Value);

        Image background = panel.gameObject.AddComponent<Image>();

        panelBackground = background;

        background.sprite = roundedSprite;

        background.type = Image.Type.Sliced;

        background.color = new Color(0.025f, 0.09f, 0.13f, 0.95f);

        background.raycastTarget = true;

        // 336 width = 16 padding + 56 cover + 14 gap + 234 text + 16 padding.
        RectTransform handle = CreateRect("DragHandle", panel, 304f, 30f, 16f, 0f);

        Image handleHitArea = handle.gameObject.AddComponent<Image>();

        handleHitArea.color = Color.clear;

        AddDragEvents(handle.gameObject);

        TextMeshProUGUI spotifyLabel = CreateLabel("Spotify", handle, "Spotify", 12f, 0f, 4f, 130f, 22f, new Color(0.76f, 0.82f, 0.84f));

        statusLabel = CreateLabel("Playback", handle, "Connecting", 11f, 140f, 4f, 164f, 22f, new Color(0.76f, 0.82f, 0.84f));

        statusLabel.alignment = TextAlignmentOptions.MidlineRight;

        RectTransform cover = CreateRect("Cover", panel, 56f, 56f, 16f, -37f);

        Image coverMask = cover.gameObject.AddComponent<Image>();

        coverMask.sprite = roundedSprite;

        coverMask.type = Image.Type.Sliced;

        coverMask.raycastTarget = false;

        Mask mask = cover.gameObject.AddComponent<Mask>();

        mask.showMaskGraphic = false;

        RectTransform coverContent = CreateRect("Artwork", cover, 56f, 56f, 0f, 0f);

        coverImage = coverContent.gameObject.AddComponent<Image>();

        coverImage.color = new Color(0.14f, 0.22f, 0.26f);

        coverImage.preserveAspect = true;

        coverImage.raycastTarget = false;

        titleLabel = CreateLabel("Track", panel, "Connecting to Spotify", 18f, 86f, 32f, 234f, 29f, Color.white);

        artistLabel = CreateLabel("Artist", panel, "Reading local playback", 13f, 86f, 63f, 234f, 23f, new Color(0.78f, 0.83f, 0.85f));

        previousButton = CreateButton("Previous", "previous", 82f, false, "previous");

        toggleButton = CreateButton("PlayPause", "play", 126f, true, "toggle");

        toggleIcon = toggleButton.transform.GetChild(0).GetComponent<Image>();

        nextButton = CreateButton("Next", "next", 170f, false, "next");

        TextMeshProUGUI shortcutLabel = CreateLabel("Shortcut", panel, "F9", 11f, 224f, 130f, 32f, 23f, new Color(0.67f, 0.74f, 0.77f));

        CreateTimeline();

        appearanceLabels = new[] { spotifyLabel, statusLabel, titleLabel, artistLabel, shortcutLabel, elapsedLabel, durationLabel };

        appearanceFontSizes = new[] { 12f, 11f, 18f, 13f, 11f, 11f, 11f };

        CreateTextButton("EditAppearance", panel, "Edit", 266f, 128f, 54f, 28f, ToggleAppearanceMenu);

        CreateAppearanceMenu();

        ApplyAppearance();

        ClampPanel();

        UpdateAppearanceMenu();

        // INFO after all UI and event bindings exist; placement matches the requested lower-right area.
        PanelLog.LogInfo($"[INFO] Spotify.Panel created; version=0.5.0; placement=saved; controls=3; customization=true; font={panelFont.name}");

        RefreshLabels();

        return true;
    }

    private Button CreateButton(string name, string shape, float x, bool primary, string command)
    {
        RectTransform rect = CreateRect(name, panel, 36f, 32f, x, -126f);

        Image background = rect.gameObject.AddComponent<Image>();

        background.sprite = roundedSprite;

        background.type = Image.Type.Sliced;

        background.color = primary ? new Color(0.92f, 0.95f, 0.96f) : new Color(0.09f, 0.17f, 0.21f);

        Button button = rect.gameObject.AddComponent<Button>();

        button.targetGraphic = background;

        ColorBlock colors = button.colors;

        colors.highlightedColor = new Color(0.8f, 0.9f, 0.94f);

        colors.pressedColor = new Color(0.6f, 0.76f, 0.82f);

        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        button.colors = colors;

        Navigation navigation = button.navigation;

        navigation.mode = Navigation.Mode.None;

        button.navigation = navigation;

        RectTransform iconRect = CreateRect("Icon", rect, 16f, 16f, 10f, -8f);

        Image icon = iconRect.gameObject.AddComponent<Image>();

        icon.sprite = shape == "play" ? playSprite : CreateShape(shape);

        icon.color = primary ? new Color(0.035f, 0.11f, 0.15f) : Color.white;

        icon.raycastTarget = false;

        button.onClick.AddListener((UnityAction)(() => QueueCommand(command)));

        return button;
    }

    private void QueueCommand(string command)
    {
        if (Time.unscaledTime < nextCommandAt)
        {
            return;
        }

        nextCommandAt = Time.unscaledTime + 0.35f;

        SendCommand?.Invoke(command);
    }

    private void AddDragEvents(GameObject handle)
    {
        EventTrigger trigger = handle.AddComponent<EventTrigger>();

        var begin = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };

        begin.callback.AddListener((UnityAction<BaseEventData>)(data =>
        {
            if (PositionLocked.Value)
            {
                return;
            }

            dragging = true;

            dragStart = data.Cast<PointerEventData>().position;

            panelStart = panel.anchoredPosition;
        }));

        trigger.triggers.Add(begin);

        var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };

        drag.callback.AddListener((UnityAction<BaseEventData>)(data =>
        {
            if (!dragging || PositionLocked.Value)
            {
                return;
            }

            Vector2 current = data.Cast<PointerEventData>().position;

            panel.anchoredPosition = panelStart + (current - dragStart) / canvas.scaleFactor;

            ClampPanel();
        }));

        trigger.triggers.Add(drag);

        var end = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };

        end.callback.AddListener((UnityAction<BaseEventData>)(data =>
        {
            if (!dragging || PositionLocked.Value)
            {
                dragging = false;

                return;
            }

            dragging = false;

            RightMargin.Value = -panel.anchoredPosition.x;

            BottomMargin.Value = panel.anchoredPosition.y;

            // INFO only when dragging finishes; margins describe the saved viewport-relative position.
            PanelLog.LogInfo($"[INFO] Spotify.Panel position saved; right={RightMargin.Value:F0}; bottom={BottomMargin.Value:F0}");
        }));

        trigger.triggers.Add(end);
    }

    private void ClampPanel()
    {
        float width = Screen.width / canvas.scaleFactor;

        float height = Screen.height / canvas.scaleFactor;

        Vector2 position = panel.anchoredPosition;

        float scaledWidth = 336f * panel.localScale.x;

        position.x = Mathf.Clamp(position.x, -Mathf.Max(8f, width - scaledWidth - 8f), -8f);

        float scaledHeight = PlayerHeight * panel.localScale.y;

        position.y = Mathf.Clamp(position.y, 8f, Mathf.Max(8f, height - scaledHeight - 8f));

        panel.anchoredPosition = position;
    }

    private void RefreshLabels()
    {
        PanelSnapshot snapshot = Volatile.Read(ref latestSnapshot);

        bool ready = snapshot.Status == "Ready" && DateTime.UtcNow - snapshot.ReceivedAtUtc < TimeSpan.FromSeconds(12);

        titleLabel.text = ready ? CleanText(snapshot.Title, "Untitled track") : snapshot.Status == "WaitingForSpotify" ? "Open Spotify to start" : "Connecting to Spotify";

        artistLabel.text = ready ? CleanText(snapshot.Artist, "Unknown artist") : "Your music will appear here";

        statusLabel.text = ready ? CleanText(snapshot.PlaybackStatus, "Ready") : "Waiting";

        statusLabel.color = ready && snapshot.PlaybackStatus == "Playing"
            ? new Color(0.64f, 0.88f, 0.73f)
            : new Color(0.76f, 0.82f, 0.84f);

        previousButton.interactable = ready && snapshot.CanPrevious;

        toggleButton.interactable = ready && snapshot.CanToggle;

        nextButton.interactable = ready && snapshot.CanNext;

        toggleIcon.sprite = ready && snapshot.PlaybackStatus == "Playing" ? pauseSprite : playSprite;

        RefreshTimeline(snapshot, ready);

        string artwork = ready ? snapshot.Artwork : "";

        if (artwork != lastArtwork)
        {
            lastArtwork = artwork;

            UpdateArtwork(artwork);
        }
    }

    private void UpdateArtwork(string base64)
    {
        coverImage.sprite = null;

        coverImage.color = new Color(0.14f, 0.22f, 0.26f);

        if (artworkSprite != null)
        {
            UnityEngine.Object.Destroy(artworkSprite);
        }

        if (artworkTexture != null)
        {
            UnityEngine.Object.Destroy(artworkTexture);
        }

        if (string.IsNullOrEmpty(base64) || base64.Length > 1400000)
        {
            return;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(base64);

            artworkTexture = new Texture2D(2, 2);

            if (!ImageConversion.LoadImage(artworkTexture, bytes))
            {
                throw new InvalidOperationException("Unsupported artwork image.");
            }

            artworkSprite = Sprite.Create(
                artworkTexture,
                new Rect(0f, 0f, artworkTexture.width, artworkTexture.height),
                new Vector2(0.5f, 0.5f)
            );

            coverImage.sprite = artworkSprite;

            coverImage.color = Color.white;
        }
        catch (Exception exception)
        {
            // WARN if optional cover decoding fails; the player remains usable with a neutral placeholder.
            PanelLog.LogWarning($"[WARN] Spotify.Panel artwork decode failed; error={exception.GetType().Name}; nextAction=use placeholder");
        }
    }

    private static Sprite CreateShape(string shape)
    {
        Texture2D texture = new Texture2D(48, 48, TextureFormat.RGBA32, false);

        texture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < 48; y++)
        {
            for (int x = 0; x < 48; x++)
            {
                float alpha;

                if (shape == "rounded")
                {
                    float dx = Mathf.Max(Mathf.Abs(x - 23.5f) - 7.5f, 0f);

                    float dy = Mathf.Max(Mathf.Abs(y - 23.5f) - 7.5f, 0f);

                    alpha = Mathf.Clamp01(16f - Mathf.Sqrt(dx * dx + dy * dy));
                }
                else
                {
                    int iconX = shape == "previous" ? 47 - x : x;

                    bool triangle = iconX >= 10 && iconX <= 36 && Mathf.Abs(y - 24) <= (36 - iconX) * 0.7f;

                    bool stopBar = iconX >= 37 && iconX <= 43 && y >= 7 && y <= 41;

                    bool pause = (x >= 10 && x <= 19 || x >= 29 && x <= 38) && y >= 6 && y <= 42;

                    alpha = (shape == "pause" ? pause : triangle || shape != "play" && stopBar) ? 1f : 0f;
                }

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();

        return Sprite.Create(
            texture,
            new Rect(0f, 0f, 48f, 48f),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            shape == "rounded" ? new Vector4(16f, 16f, 16f, 16f) : Vector4.zero
        );
    }

    private static RectTransform CreateRect(string name, Transform parent, float width, float height, float x, float y)
    {
        GameObject child = new GameObject(name);

        RectTransform rect = child.AddComponent<RectTransform>();

        rect.SetParent(parent, false);

        rect.anchorMin = new Vector2(0f, 1f);

        rect.anchorMax = new Vector2(0f, 1f);

        rect.pivot = new Vector2(0f, 1f);

        rect.sizeDelta = new Vector2(width, height);

        rect.anchoredPosition = new Vector2(x, y);

        return rect;
    }

    private TextMeshProUGUI CreateLabel(string name, Transform parent, string text, float size, float x, float y, float width, float height, Color color)
    {
        RectTransform rect = CreateRect(name, parent, width, height, x, -y);

        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();

        label.font = panelFont;

        label.fontSize = size;

        label.color = color;

        label.text = text;

        label.richText = false;

        label.enableWordWrapping = false;

        label.overflowMode = TextOverflowModes.Ellipsis;

        label.alignment = TextAlignmentOptions.MidlineLeft;

        label.raycastTarget = false;

        return label;
    }

    private static string CleanText(string text, string fallback)
    {
        return string.IsNullOrWhiteSpace(text) ? fallback : text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }

    internal static void Remove()
    {
        if (canvasObject != null)
        {
            UnityEngine.Object.Destroy(canvasObject);

            canvasObject = null;
        }
    }

    private sealed class PanelSnapshot
    {
        public string Status = "Connecting";

        public string Title = "";

        public string Artist = "";

        public string PlaybackStatus = "";

        public string Artwork = "";

        public bool CanPrevious;

        public bool CanToggle;

        public bool CanNext;

        public bool TimelineAvailable;

        public double PositionSeconds;

        public double DurationSeconds;

        public double PlaybackRate = 1.0;

        public readonly long ReceivedAtTicks = System.Diagnostics.Stopwatch.GetTimestamp();

        public readonly DateTime ReceivedAtUtc = DateTime.UtcNow;
    }
}
