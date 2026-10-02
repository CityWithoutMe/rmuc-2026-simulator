using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

// 菜单和选车共用的文字与按钮。运行时生成，不改对局场景。
public static class MenuUi
{
    public static Font Font { get; private set; }

    public static Canvas CreateCanvas(string name, int sortingOrder)
    {
        GameObject canvasGo = new GameObject(name);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGo.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        return canvas;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null)
            return;
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
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
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    public static Button CreateButton(Transform parent, string name, string label, UnityAction onClick, bool interactable)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = interactable ? new Color(0.16f, 0.18f, 0.22f, 1f) : new Color(0.12f, 0.12f, 0.12f, 0.7f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.interactable = interactable;
        if (onClick != null)
            button.onClick.AddListener(onClick);

        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.28f, 0.32f, 0.38f, 1f);
        colors.pressedColor = new Color(0.1f, 0.12f, 0.14f, 1f);
        colors.disabledColor = new Color(0.2f, 0.2f, 0.2f, 0.55f);
        button.colors = colors;

        Text text = CreateText(go.transform, "Label", label, 28, TextAnchor.MiddleCenter, interactable ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f));
        Stretch(text.rectTransform);

        return button;
    }

    public static void SetButtonSelected(Button button, bool selected)
    {
        Image image = button.GetComponent<Image>();
        if (image == null)
            return;
        image.color = selected ? new Color(0.55f, 0.16f, 0.16f, 1f) : new Color(0.16f, 0.18f, 0.22f, 1f);
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

    private static Font LoadFont()
    {
        if (Font != null)
            return Font;
        Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (Font == null)
            Font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (Font == null)
            Font = Font.CreateDynamicFontFromOSFont("Arial", 32);
        return Font;
    }
}
