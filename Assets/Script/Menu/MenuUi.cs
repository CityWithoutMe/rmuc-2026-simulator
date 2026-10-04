using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

// 菜单、选车与联机大厅共用的运行时 UI 设计系统。
public static class MenuUi
{
    public enum ButtonTone { Neutral, Primary, Subtle, Danger, RedTeam, BlueTeam }

    public static readonly Color Background = new Color(0.025f, 0.04f, 0.075f, 1f);
    public static readonly Color Surface = new Color(0.055f, 0.085f, 0.14f, 0.96f);
    public static readonly Color SurfaceRaised = new Color(0.075f, 0.115f, 0.185f, 0.98f);
    public static readonly Color Border = new Color(0.32f, 0.48f, 0.68f, 0.28f);
    public static readonly Color Primary = new Color(0.1f, 0.72f, 0.92f, 1f);
    public static readonly Color PrimaryBright = new Color(0.32f, 0.88f, 1f, 1f);
    public static readonly Color TextPrimary = new Color(0.94f, 0.97f, 1f, 1f);
    public static readonly Color TextSecondary = new Color(0.58f, 0.68f, 0.78f, 1f);
    public static readonly Color Warning = new Color(1f, 0.73f, 0.26f, 1f);
    public static readonly Color Red = new Color(1f, 0.28f, 0.3f, 1f);
    public static readonly Color Blue = new Color(0.2f, 0.52f, 1f, 1f);

    public static Font Font { get; private set; }
    static Sprite roundedSprite;
    static Sprite gradientSprite;

    public static Canvas CreateCanvas(string name, int sortingOrder)
    {
        GameObject canvasGo = new GameObject(name);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        return canvas;
    }

    public static void CreateBackdrop(Transform parent, string eyebrow)
    {
        Image background = CreateImage(parent, "Backdrop", Color.white);
        background.sprite = GradientSprite();
        Stretch(background.rectTransform);
        background.transform.SetAsFirstSibling();

        CreateGlow(parent, "TopGlow", new Vector2(0.08f, 0.94f), new Vector2(820f, 210f),
            new Color(0.05f, 0.72f, 0.98f, 0.08f));
        CreateGlow(parent, "BottomGlow", new Vector2(0.92f, 0.04f), new Vector2(720f, 180f),
            new Color(0.2f, 0.35f, 1f, 0.055f));

        Text brand = CreateText(parent, "Brand", eyebrow.ToUpperInvariant(), 18, TextAnchor.MiddleLeft, PrimaryBright);
        brand.fontStyle = FontStyle.Bold;
        Place(brand.rectTransform, new Vector2(0.05f, 0.955f), new Vector2(0.05f, 0.955f), new Vector2(0f, 0.5f));
        brand.rectTransform.sizeDelta = new Vector2(620f, 34f);

        Text version = CreateText(parent, "Version", "RM SIMULATOR  /  2026", 15, TextAnchor.MiddleRight, TextSecondary);
        Place(version.rectTransform, new Vector2(0.95f, 0.955f), new Vector2(0.95f, 0.955f), new Vector2(1f, 0.5f));
        version.rectTransform.sizeDelta = new Vector2(360f, 30f);
    }

    static void CreateGlow(Transform parent, string name, Vector2 anchor, Vector2 size, Color color)
    {
        Image glow = CreateImage(parent, name, color);
        glow.sprite = RoundedSprite();
        glow.type = Image.Type.Sliced;
        Place(glow.rectTransform, anchor, anchor, new Vector2(0.5f, 0.5f));
        glow.rectTransform.sizeDelta = size;
    }

    public static Image CreatePanel(Transform parent, string name, Color color)
    {
        Image image = CreateImage(parent, name, color);
        image.sprite = RoundedSprite();
        image.type = Image.Type.Sliced;
        Outline outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = Border;
        outline.effectDistance = new Vector2(1f, -1f);
        return image;
    }

    public static Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text text = go.AddComponent<Text>();
        text.font = LoadFont();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    public static Button CreateButton(Transform parent, string name, string label, UnityAction onClick, bool interactable,
        ButtonTone tone = ButtonTone.Neutral)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        image.sprite = RoundedSprite();
        image.type = Image.Type.Sliced;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = interactable;
        if (onClick != null) button.onClick.AddListener(onClick);

        Outline outline = go.AddComponent<Outline>();
        outline.effectDistance = new Vector2(1f, -1f);
        ApplyButtonTone(button, tone);

        Text text = CreateText(go.transform, "Label", label, 24, TextAnchor.MiddleCenter,
            interactable ? TextPrimary : new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, 0.55f));
        text.fontStyle = FontStyle.Bold;
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(18f, 0f);
        text.rectTransform.offsetMax = new Vector2(-18f, 0f);
        return button;
    }

    public static void ApplyButtonTone(Button button, ButtonTone tone)
    {
        if (button == null) return;
        Color normal, highlight, pressed, border;
        switch (tone)
        {
            case ButtonTone.Primary:
                normal = new Color(0.04f, 0.5f, 0.68f, 1f); highlight = new Color(0.06f, 0.66f, 0.84f, 1f);
                pressed = new Color(0.03f, 0.36f, 0.5f, 1f); border = new Color(0.35f, 0.9f, 1f, 0.7f); break;
            case ButtonTone.Danger:
                normal = new Color(0.3f, 0.1f, 0.13f, 0.9f); highlight = new Color(0.48f, 0.14f, 0.17f, 1f);
                pressed = new Color(0.22f, 0.07f, 0.09f, 1f); border = new Color(1f, 0.32f, 0.36f, 0.38f); break;
            case ButtonTone.RedTeam:
                normal = new Color(0.35f, 0.09f, 0.13f, 1f); highlight = new Color(0.56f, 0.12f, 0.16f, 1f);
                pressed = new Color(0.25f, 0.06f, 0.09f, 1f); border = new Color(Red.r, Red.g, Red.b, 0.5f); break;
            case ButtonTone.BlueTeam:
                normal = new Color(0.07f, 0.19f, 0.43f, 1f); highlight = new Color(0.1f, 0.31f, 0.68f, 1f);
                pressed = new Color(0.04f, 0.13f, 0.31f, 1f); border = new Color(Blue.r, Blue.g, Blue.b, 0.58f); break;
            case ButtonTone.Subtle:
                normal = new Color(0.07f, 0.1f, 0.15f, 0.65f); highlight = new Color(0.12f, 0.18f, 0.26f, 0.92f);
                pressed = new Color(0.045f, 0.07f, 0.11f, 1f); border = new Color(Border.r, Border.g, Border.b, 0.45f); break;
            default:
                normal = SurfaceRaised; highlight = new Color(0.1f, 0.18f, 0.27f, 1f);
                pressed = new Color(0.045f, 0.08f, 0.13f, 1f); border = Border; break;
        }
        Image image = button.GetComponent<Image>();
        if (image != null) image.color = Color.white;
        Outline outline = button.GetComponent<Outline>();
        if (outline != null) outline.effectColor = border;
        ColorBlock colors = button.colors;
        colors.normalColor = normal;
        colors.highlightedColor = highlight;
        colors.pressedColor = pressed;
        colors.selectedColor = highlight;
        colors.disabledColor = new Color(0.09f, 0.11f, 0.14f, 0.62f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        button.colors = colors;
    }

    public static void SetButtonSelected(Button button, bool selected)
    {
        SetButtonSelected(button, selected, Primary);
    }

    public static void SetButtonSelected(Button button, bool selected, Color accent)
    {
        if (button == null) return;
        ColorBlock colors = button.colors;
        colors.normalColor = selected
            ? new Color(accent.r * 0.42f, accent.g * 0.42f, accent.b * 0.42f, 1f)
            : SurfaceRaised;
        button.colors = colors;
        Outline outline = button.GetComponent<Outline>();
        if (outline != null) outline.effectColor = selected ? new Color(accent.r, accent.g, accent.b, 0.9f) : Border;
    }

    public static void StyleInputField(InputField field)
    {
        if (field == null) return;
        Image image = field.GetComponent<Image>();
        if (image != null)
        {
            image.sprite = RoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = Color.white;
        }
        Outline outline = field.gameObject.AddComponent<Outline>();
        outline.effectColor = Border;
        outline.effectDistance = new Vector2(1f, -1f);
        ColorBlock colors = field.colors;
        colors.normalColor = new Color(0.025f, 0.05f, 0.09f, 0.96f);
        colors.highlightedColor = new Color(0.045f, 0.09f, 0.15f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = colors.highlightedColor;
        field.colors = colors;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    static Sprite RoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;
        const int size = 32;
        const float radius = 9f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "RuntimeRoundedRectangle";
        texture.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
            float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
            float alpha = Mathf.Clamp01(radius + 0.75f - Mathf.Sqrt(dx * dx + dy * dy));
            texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
        texture.Apply();
        roundedSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
            0, SpriteMeshType.FullRect, new Vector4(10f, 10f, 10f, 10f));
        return roundedSprite;
    }

    static Sprite GradientSprite()
    {
        if (gradientSprite != null) return gradientSprite;
        const int height = 128;
        Texture2D texture = new Texture2D(2, height, TextureFormat.RGBA32, false);
        texture.name = "RuntimeMenuGradient";
        texture.wrapMode = TextureWrapMode.Clamp;
        Color bottom = new Color(0.018f, 0.03f, 0.065f, 1f);
        Color top = new Color(0.045f, 0.095f, 0.15f, 1f);
        for (int y = 0; y < height; y++)
        {
            Color color = Color.Lerp(bottom, top, y / (height - 1f));
            texture.SetPixel(0, y, color); texture.SetPixel(1, y, color);
        }
        texture.Apply();
        gradientSprite = Sprite.Create(texture, new Rect(0, 0, 2, height), new Vector2(0.5f, 0.5f), 100f);
        return gradientSprite;
    }

    static Font LoadFont()
    {
        if (Font != null) return Font;
        Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (Font == null) Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (Font == null) Font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 32);
        return Font;
    }
}
