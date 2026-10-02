using UnityEngine;
using UnityEngine.UI;

// 画面正中、比赛倒计时上方两行：哨塔血量、基地血量。运行时 Canvas，不改场景，不挡点击。
// 倒计时在 MatchTimer 里顶对齐、下移到这两行下面，避免叠在一起挡住准心。
public class OutpostHealthHud : MonoBehaviour
{
    Text label;

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<OutpostHealthHud>() != null)
            return;

        GameObject host = new GameObject("OutpostHealthHud");
        host.AddComponent<OutpostHealthHud>();
    }

    void Awake()
    {
        CreateDisplay();
        Refresh();
    }

    void Update()
    {
        Refresh();
    }

    void CreateDisplay()
    {
        GameObject canvasGo = new GameObject("OutpostHealthCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 110;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textGo = new GameObject("OutpostHpText");
        textGo.transform.SetParent(canvasGo.transform, false);

        label = textGo.AddComponent<Text>();
        label.font = LoadBuiltinFont();
        label.fontSize = 28;
        label.alignment = TextAnchor.UpperCenter;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.supportRichText = false;

        RectTransform rt = label.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -8f);
        rt.sizeDelta = new Vector2(1400f, 72f);
    }

    static Font LoadBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont("Arial", 28);
        return font;
    }

    void Refresh()
    {
        if (label == null)
            return;

        int red = Mathf.RoundToInt(OutpostHealth.HpOf(RobotTeam.Red));
        int blue = Mathf.RoundToInt(OutpostHealth.HpOf(RobotTeam.Blue));
        int max = Mathf.RoundToInt(OutpostHealth.MaxHp);
        int redBase = Mathf.RoundToInt(BaseHealth.HpOf(RobotTeam.Red));
        int blueBase = Mathf.RoundToInt(BaseHealth.HpOf(RobotTeam.Blue));
        int baseMax = Mathf.RoundToInt(BaseHealth.MaxHp);
        int redShield = Mathf.RoundToInt(BaseHealth.ShieldOf(RobotTeam.Red));
        int blueShield = Mathf.RoundToInt(BaseHealth.ShieldOf(RobotTeam.Blue));
        label.text =
            "红方哨塔 " + red + "/" + max + "    蓝方哨塔 " + blue + "/" + max
            + "\n红方基地 " + redBase + "/" + baseMax + " 护盾" + redShield
            + "    蓝方基地 " + blueBase + "/" + baseMax + " 护盾" + blueShield;
    }
}
