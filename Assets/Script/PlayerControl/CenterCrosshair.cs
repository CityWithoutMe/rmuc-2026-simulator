using UnityEngine;
using UnityEngine.UI;

// 屏幕正中心一个小圆点，和射击用的视口中心 (0.5, 0.5) 对齐。
// 运行时自己建 Canvas，不改场景。不接收点击。商店打开时藏起来，避免和鼠标抢视线。
public class CenterCrosshair : MonoBehaviour
{
    const int SortingOrder = 150;
    const float DotSize = 10f;

    Image dot;
    AmmoShop shop;

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<CenterCrosshair>() != null)
            return;

        GameObject host = new GameObject("CenterCrosshair");
        host.AddComponent<CenterCrosshair>();
    }

    void Awake()
    {
        CreateDisplay();
    }

    void Update()
    {
        if (dot == null)
            return;

        if (shop == null)
            shop = FindAnyObjectByType<AmmoShop>();

        bool hide = shop != null && shop.IsOpen;
        if (dot.enabled == hide)
            dot.enabled = !hide;
    }

    void CreateDisplay()
    {
        GameObject canvasGo = new GameObject("CenterCrosshairCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject dotGo = new GameObject("Dot");
        dotGo.transform.SetParent(canvasGo.transform, false);

        RectTransform rect = dotGo.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(DotSize, DotSize);

        dot = dotGo.AddComponent<Image>();
        dot.sprite = CreateDotSprite();
        dot.color = Color.white;
        dot.raycastTarget = false;
    }

    static Sprite CreateDotSprite()
    {
        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float center = (size - 1) * 0.5f;
        float radius = center - 1f;
        float outline = radius - 2f;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color ring = new Color(0f, 0f, 0f, 0.85f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                if (dist <= outline)
                    tex.SetPixel(x, y, Color.white);
                else if (dist <= radius)
                    tex.SetPixel(x, y, ring);
                else
                    tex.SetPixel(x, y, clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
