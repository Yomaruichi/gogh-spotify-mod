using System;

using BepInEx.Configuration;

using TMPro;

using UnityEngine;

using UnityEngine.Events;

using UnityEngine.UI;

namespace GoghSpotifyMod;

public sealed partial class NowPlayingPanel
{
    internal static ConfigEntry<float> PanelScale;

    internal static ConfigEntry<float> BackgroundOpacity;

    internal static ConfigEntry<float> TextScale;

    internal static ConfigEntry<bool> PositionLocked;

    private Image panelBackground;

    private Image appearanceBackground;

    private RectTransform appearanceMenu;

    private TextMeshProUGUI sizeValueLabel;

    private TextMeshProUGUI opacityValueLabel;

    private TextMeshProUGUI textValueLabel;

    private TextMeshProUGUI lockValueLabel;

    private TextMeshProUGUI[] appearanceLabels;

    private float[] appearanceFontSizes;

    private float appliedTextScale = -1f;

    private bool appearanceMenuOpen;

    private void ApplyAppearance()
    {
        float width = Screen.width / canvas.scaleFactor;

        float height = Screen.height / canvas.scaleFactor;

        float requestedScale = SafeRange(PanelScale.Value, 0.75f, 1.5f, 1f);

        float availableScale = Mathf.Min((width - 16f) / 336f, (height - 16f) / PlayerHeight);

        float effectiveScale = Mathf.Min(requestedScale, Mathf.Max(0.1f, availableScale));

        panel.localScale = new Vector3(effectiveScale, effectiveScale, 1f);

        float opacity = SafeRange(BackgroundOpacity.Value, 0.35f, 1f, 0.95f);

        Color backgroundColor = panelBackground.color;

        backgroundColor.a = opacity;

        panelBackground.color = backgroundColor;

        float textScale = SafeRange(TextScale.Value, 0.85f, 1.25f, 1f);

        if (textScale != appliedTextScale)
        {
            for (int index = 0; index < appearanceLabels.Length; index++)
            {
                appearanceLabels[index].fontSize = appearanceFontSizes[index] * textScale;
            }

            appliedTextScale = textScale;
        }
    }

    private static float SafeRange(float value, float minimum, float maximum, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return Mathf.Clamp(value, minimum, maximum);
    }

    private void ToggleAppearanceMenu()
    {
        appearanceMenuOpen = !appearanceMenuOpen;

        UpdateAppearanceMenu();

        // INFO after an explicit Edit/Done action; open describes menu visibility only.
        PanelLog.LogInfo($"[INFO] Spotify.Appearance menu toggled; open={appearanceMenuOpen}");
    }

    private void CreateAppearanceMenu()
    {
        appearanceMenu = CreateRect("PlayerAppearance", canvasObject.transform, 336f, 232f, 0f, 0f);

        appearanceMenu.anchorMin = new Vector2(1f, 0f);

        appearanceMenu.anchorMax = new Vector2(1f, 0f);

        appearanceMenu.pivot = new Vector2(1f, 0f);

        appearanceBackground = appearanceMenu.gameObject.AddComponent<Image>();

        appearanceBackground.sprite = roundedSprite;

        appearanceBackground.type = Image.Type.Sliced;

        // Keep settings opaque enough to read even while editing the player's transparency.
        appearanceBackground.color = new Color(0.025f, 0.09f, 0.13f, 0.98f);

        CreateLabel("AppearanceHeading", appearanceMenu, "Player appearance", 14f, 16f, 10f, 214f, 26f, Color.white);

        CreateTextButton("Done", appearanceMenu, "Done", 250f, 10f, 70f, 26f, ToggleAppearanceMenu);

        sizeValueLabel = CreateAppearanceRow("Player size", 46f, () => AdjustAppearance("size", -0.05f), () => AdjustAppearance("size", 0.05f));

        opacityValueLabel = CreateAppearanceRow("Background", 82f, () => AdjustAppearance("opacity", -0.05f), () => AdjustAppearance("opacity", 0.05f));

        textValueLabel = CreateAppearanceRow("Text size", 118f, () => AdjustAppearance("text", -0.05f), () => AdjustAppearance("text", 0.05f));

        CreateLabel("LockHeading", appearanceMenu, "Lock position", 13f, 16f, 158f, 170f, 28f, Color.white);

        Button lockButton = CreateTextButton("LockPosition", appearanceMenu, "Unlocked", 210f, 158f, 110f, 28f, TogglePositionLock);

        lockValueLabel = lockButton.transform.GetChild(0).GetComponent<TextMeshProUGUI>();

        CreateTextButton("ResetAppearance", appearanceMenu, "Reset appearance", 16f, 196f, 304f, 26f, ResetAppearance);

        appearanceMenu.gameObject.SetActive(false);
    }

    private TextMeshProUGUI CreateAppearanceRow(string name, float y, Action decrease, Action increase)
    {
        CreateLabel(name + "Heading", appearanceMenu, name, 13f, 16f, y, 140f, 28f, Color.white);

        TextMeshProUGUI value = CreateLabel(name + "Value", appearanceMenu, "", 13f, 154f, y, 62f, 28f, new Color(0.78f, 0.83f, 0.85f));

        value.alignment = TextAlignmentOptions.Center;

        CreateTextButton(name + "Decrease", appearanceMenu, "-", 230f, y, 36f, 28f, decrease);

        CreateTextButton(name + "Increase", appearanceMenu, "+", 284f, y, 36f, 28f, increase);

        return value;
    }

    private Button CreateTextButton(string name, Transform parent, string text, float x, float y, float width, float height, Action action)
    {
        RectTransform rect = CreateRect(name, parent, width, height, x, -y);

        Image background = rect.gameObject.AddComponent<Image>();

        background.sprite = roundedSprite;

        background.type = Image.Type.Sliced;

        background.color = new Color(0.10f, 0.19f, 0.23f);

        Button button = rect.gameObject.AddComponent<Button>();

        button.targetGraphic = background;

        ColorBlock colors = button.colors;

        colors.highlightedColor = new Color(0.8f, 0.9f, 0.94f);

        colors.pressedColor = new Color(0.6f, 0.76f, 0.82f);

        button.colors = colors;

        Navigation navigation = button.navigation;

        navigation.mode = Navigation.Mode.None;

        button.navigation = navigation;

        TextMeshProUGUI label = CreateLabel("Label", rect, text, 12f, 4f, 0f, width - 8f, height, Color.white);

        label.alignment = TextAlignmentOptions.Center;

        button.onClick.AddListener((UnityAction)(() => action()));

        return button;
    }

    private void AdjustAppearance(string setting, float step)
    {
        if (setting == "size")
        {
            PanelScale.Value = RoundedStep(PanelScale.Value, step, 0.75f, 1.5f, 1f);
        }
        else if (setting == "opacity")
        {
            BackgroundOpacity.Value = RoundedStep(BackgroundOpacity.Value, step, 0.35f, 1f, 0.95f);
        }
        else if (setting == "text")
        {
            TextScale.Value = RoundedStep(TextScale.Value, step, 0.85f, 1.25f, 1f);
        }

        ApplyAppearance();

        ClampPanel();

        UpdateAppearanceMenu();

        // INFO after a click applies the persisted values; percentages describe appearance and contain no track data.
        PanelLog.LogInfo($"[INFO] Spotify.Appearance changed; size={PanelScale.Value:F2}; opacity={BackgroundOpacity.Value:F2}; text={TextScale.Value:F2}");
    }

    private static float RoundedStep(float value, float step, float minimum, float maximum, float fallback)
    {
        float current = SafeRange(value, minimum, maximum, fallback);

        return Mathf.Clamp(Mathf.Round((current + step) * 100f) / 100f, minimum, maximum);
    }

    private void TogglePositionLock()
    {
        PositionLocked.Value = !PositionLocked.Value;

        dragging = false;

        UpdateAppearanceMenu();

        // INFO after changing the saved lock; playback buttons remain usable in both states.
        PanelLog.LogInfo($"[INFO] Spotify.Appearance position lock changed; locked={PositionLocked.Value}");
    }

    private void ResetAppearance()
    {
        PanelScale.Value = 1f;

        BackgroundOpacity.Value = 0.95f;

        TextScale.Value = 1f;

        PositionLocked.Value = false;

        ApplyAppearance();

        ClampPanel();

        UpdateAppearanceMenu();

        // INFO after restoring appearance defaults; saved right/bottom margins are deliberately retained.
        PanelLog.LogInfo("[INFO] Spotify.Appearance reset; size=1.00; opacity=0.95; text=1.00; locked=false; position=retained");
    }

    private void UpdateAppearanceMenu()
    {
        if (!VisibleSetting.Value)
        {
            appearanceMenuOpen = false;
        }

        appearanceMenu.gameObject.SetActive(appearanceMenuOpen && VisibleSetting.Value);

        if (!appearanceMenuOpen)
        {
            return;
        }

        sizeValueLabel.text = $"{Mathf.RoundToInt(SafeRange(PanelScale.Value, 0.75f, 1.5f, 1f) * 100f)}%";

        opacityValueLabel.text = $"{Mathf.RoundToInt(SafeRange(BackgroundOpacity.Value, 0.35f, 1f, 0.95f) * 100f)}%";

        textValueLabel.text = $"{Mathf.RoundToInt(SafeRange(TextScale.Value, 0.85f, 1.25f, 1f) * 100f)}%";

        lockValueLabel.text = PositionLocked.Value ? "Locked" : "Unlocked";

        float width = Screen.width / canvas.scaleFactor;

        float height = Screen.height / canvas.scaleFactor;

        float menuScale = Mathf.Min(1f, (width - 16f) / 336f, (height - 16f) / 232f);

        menuScale = Mathf.Max(0.1f, menuScale);

        appearanceMenu.localScale = new Vector3(menuScale, menuScale, 1f);

        float menuWidth = 336f * menuScale;

        float menuHeight = 232f * menuScale;

        float playerHeight = PlayerHeight * panel.localScale.y;

        Vector2 position = panel.anchoredPosition;

        if (position.y + playerHeight + 8f + menuHeight <= height - 8f)
        {
            position.y += playerHeight + 8f;
        }
        else
        {
            position.y -= menuHeight + 8f;
        }

        position.x = Mathf.Clamp(position.x, -Mathf.Max(8f, width - menuWidth - 8f), -8f);

        position.y = Mathf.Clamp(position.y, 8f, Mathf.Max(8f, height - menuHeight - 8f));

        appearanceMenu.anchoredPosition = position;
    }
}
