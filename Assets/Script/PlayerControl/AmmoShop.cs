using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 运行时补给商店。挂在 PlayerShooting 那台车上，按 B 开关。
// 单价按常见 RoboMaster 兑换，可按赛季改。README 原先没写死价格，以这里的常量为准。
public class AmmoShop : MonoBehaviour
{
    // 17mm：1 金币/发，按钮一次 10 发（花 10）。42mm：15 金币/发，按钮一次 1 发（花 15）。
    public const int PricePer17mm = 1;
    public const int BuyCount17mm = 10;
    public const int PricePer42mm = 15;
    public const int BuyCount42mm = 1;

    // 金币暂时无限：购买前把存量顶到这个数再 TrySpendCoins，界面不显示它。
    const int InfiniteCoinPool = 1000000;

    public bool IsOpen => open;

    RobotAttributeManager attributes;
    PlayerMovement movement;
    GameObject panel;
    Text ammo17Text;
    Text ammo42Text;
    Text hintText;
    Text buy17Label;
    Text buy42Label;
    Text buyHpLabel;
    Text buyReviveLabel;
    Button buyHpButton;
    Button buyReviveButton;
    bool open;
    static Sprite whiteSprite;

    void Awake()
    {
        EnsureEventSystem();
        CreateUi();
        panel.SetActive(false);
    }

    void Update()
    {
        if (LanSession.Active)
        {
            var vehicle = GetComponent<LanVehicle>();
            if (vehicle == null || !vehicle.IsLocal) return;
        }
        if (Input.GetKeyDown(KeyCode.B))
            SetOpen(!open);

        if (open)
            Refresh();
    }

    void SetOpen(bool value)
    {
        open = value;
        if (panel != null)
            panel.SetActive(value);

        if (movement == null)
        {
            movement = GetComponent<PlayerMovement>();
            if (movement == null)
                movement = GetComponentInParent<PlayerMovement>();
        }

        if (movement != null)
            movement.SetShopOpen(value);
        else if (value)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (value)
            Refresh();
    }

    // HUD 可能稍后才挂上管理器，打开商店和购买时再找。
    RobotAttributeManager ResolveAttributes()
    {
        if (attributes != null)
            return attributes;

        // 有十车总控时和射击同一车位（红英雄是 red_hero）。没有总控时，红方弹药和金币在场景「属性管理器」上。
        attributes = PlayerAttributeBinding.Resolve(gameObject);
        return attributes;
    }

    void Buy17()
    {
        if (LanSession.Active) { LanSession.Instance.Purchase("ammo17"); return; }
        Buy(RobotStat.Ammo17mm, PricePer17mm, BuyCount17mm, true);
    }

    void Buy42()
    {
        if (LanSession.Active) { LanSession.Instance.Purchase("ammo42"); return; }
        Buy(RobotStat.Ammo42mm, PricePer42mm, BuyCount42mm, false);
    }

    void BuyHp()
    {
        if (LanSession.Active) { LanSession.Instance.Purchase("heal"); return; }
        RobotAttributeManager attr = ResolveAttributes();
        if (attr == null)
        {
            hintText.text = "未找到属性";
            return;
        }

        string block = attr.RemoteHealBlockReason();
        if (block != null)
        {
            hintText.text = block;
            Refresh();
            return;
        }

        int price = RobotLevelRules.RemoteHealPrice(MatchRemainingSeconds());
        PayCoins(attr, price);
        attr.BeginRemoteHeal();
        hintText.text = "6秒后增加上限血量的60%（战亡不返还）";
        Refresh();
    }

    void BuyRevive()
    {
        if (LanSession.Active) { LanSession.Instance.Purchase("revive"); return; }
        RobotAttributeManager attr = ResolveAttributes();
        if (attr == null)
        {
            hintText.text = "未找到属性";
            return;
        }

        string block = attr.ImmediateReviveBlockReason();
        if (block != null)
        {
            hintText.text = block;
            Refresh();
            return;
        }

        int price = RobotLevelRules.ImmediateRevivePrice(MatchRemainingSeconds(), attr.Level);
        PayCoins(attr, price);
        if (!attr.BeginImmediateRevive())
        {
            hintText.text = "立即复活失败";
            Refresh();
            return;
        }

        hintText.text = "";
        Refresh();
    }

    static float MatchRemainingSeconds()
    {
        MatchTimer timer = FindAnyObjectByType<MatchTimer>();
        if (timer == null)
            return 420f;
        return timer.RemainingSeconds;
    }

    void Buy(RobotStat stat, int unitPrice, int count, bool ammo17)
    {
        RobotAttributeManager attr = ResolveAttributes();
        if (attr == null)
        {
            hintText.text = "未找到属性";
            return;
        }

        int added = ammo17 ? attr.AddAmmo17mm(count) : attr.AddAmmo42mm(count);
        if (added <= 0)
        {
            hintText.text = "已满";
            Refresh();
            return;
        }

        // 只按实际加上的发数计价。超出 Max 的部分 AddAmmo 不会加上。
        PayCoins(attr, unitPrice * added);
        hintText.text = added < count ? "已满" : "";
        Refresh();
    }

    static void PayCoins(RobotAttributeManager attr, int cost)
    {
        if (cost <= 0)
            return;

        // 每次都顶满再扣，避免存量被慢慢扣成小数；界面始终写「无限」，不读这个数。
        attr.SetCurrent(RobotStat.Coins, InfiniteCoinPool);
        if (!attr.TrySpendCoins(cost))
            attr.SetCurrent(RobotStat.Coins, InfiniteCoinPool);
    }

    // 联机购买只由主机执行，沿用现有无限金币和兑换规则。
    public static string ExecuteLanPurchase(RobotAttributeManager attr, string action)
    {
        if (!LanSession.IsHost || attr == null) return "无效购买";
        if (action == "heal")
        {
            string block = attr.RemoteHealBlockReason();
            if (block != null) return block;
            PayCoins(attr, RobotLevelRules.RemoteHealPrice(MatchRemainingSeconds()));
            attr.BeginRemoteHeal();
            return null;
        }
        if (action == "revive")
        {
            string block = attr.ImmediateReviveBlockReason();
            if (block != null) return block;
            PayCoins(attr, RobotLevelRules.ImmediateRevivePrice(MatchRemainingSeconds(), attr.Level));
            return attr.BeginImmediateRevive() ? null : "立即复活失败";
        }
        if (action == "ammo42" && attr.robotType == RobotType.Hero)
        {
            int added = attr.AddAmmo42mm(BuyCount42mm);
            PayCoins(attr, added * PricePer42mm);
            return added > 0 ? null : "已满";
        }
        if (action == "ammo17" && (attr.robotType == RobotType.Hero || attr.robotType == RobotType.Infantry))
        {
            int added = attr.AddAmmo17mm(BuyCount17mm);
            PayCoins(attr, added * PricePer17mm);
            return added > 0 ? null : "已满";
        }
        return "该车辆不能兑换这种弹药";
    }

    void Refresh()
    {
        if (ammo17Text == null)
            return;

        RobotAttributeManager attr = ResolveAttributes();
        if (attr == null)
        {
            ammo17Text.text = "17mm：—    单价 " + PricePer17mm + " 金币/发";
            ammo42Text.text = "42mm：—    单价 " + PricePer42mm + " 金币/发";
            buy17Label.text = "购买 " + BuyCount17mm + " 发（" + (PricePer17mm * BuyCount17mm) + " 金币）";
            buy42Label.text = "购买 " + BuyCount42mm + " 发（" + (PricePer42mm * BuyCount42mm) + " 金币）";
            return;
        }

        int cur17 = Mathf.RoundToInt(attr.GetCurrent(RobotStat.Ammo17mm));
        int max17 = Mathf.RoundToInt(attr.GetCap(RobotStat.Ammo17mm));
        int cur42 = Mathf.RoundToInt(attr.GetCurrent(RobotStat.Ammo42mm));
        int max42 = Mathf.RoundToInt(attr.GetCap(RobotStat.Ammo42mm));

        bool hero = attr.robotType == RobotType.Hero;
        bool infantry = attr.robotType == RobotType.Infantry;
        // 英雄两种都能买（价格不变）。步兵只买 17mm。
        bool show17 = true;
        bool show42 = hero || !infantry;
        if (ammo17Text.transform.parent != null)
            ammo17Text.gameObject.SetActive(show17);
        if (buy17Label != null && buy17Label.transform.parent != null)
            buy17Label.transform.parent.gameObject.SetActive(show17);
        if (ammo42Text != null)
            ammo42Text.gameObject.SetActive(show42);
        if (buy42Label != null && buy42Label.transform.parent != null)
            buy42Label.transform.parent.gameObject.SetActive(show42);

        ammo17Text.text = "17mm：" + cur17 + "/" + max17 + "    单价 " + PricePer17mm + " 金币/发";
        ammo42Text.text = "42mm：" + cur42 + "/" + max42 + "    单价 " + PricePer42mm + " 金币/发";
        buy17Label.text = cur17 >= max17
            ? "已满"
            : "购买 " + BuyCount17mm + " 发（" + (PricePer17mm * BuyCount17mm) + " 金币）";
        buy42Label.text = cur42 >= max42
            ? "已满"
            : "购买 " + BuyCount42mm + " 发（" + (PricePer42mm * BuyCount42mm) + " 金币）";

        float remaining = MatchRemainingSeconds();
        int healPrice = RobotLevelRules.RemoteHealPrice(remaining);
        int revivePrice = RobotLevelRules.ImmediateRevivePrice(remaining, attr.Level);
        string healBlock = attr.RemoteHealBlockReason();
        string reviveBlock = attr.ImmediateReviveBlockReason();

        if (buyHpButton != null)
            buyHpButton.interactable = healBlock == null;
        if (buyReviveButton != null)
            buyReviveButton.interactable = reviveBlock == null;

        buyHpLabel.text = healBlock == null
            ? "买血（" + healPrice + " 金币，+60% 上限）"
            : healBlock;
        buyReviveLabel.text = reviveBlock == null
            ? "立即复活（" + revivePrice + " 金币，满血）"
            : reviveBlock;
    }

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    void CreateUi()
    {
        GameObject canvasGo = new GameObject("AmmoShopCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        canvasGo.AddComponent<GraphicRaycaster>();

        panel = new GameObject("ShopPanel");
        panel.transform.SetParent(canvasGo.transform, false);

        Image bg = panel.AddComponent<Image>();
        bg.sprite = GetWhiteSprite();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.94f);
        bg.raycastTarget = true;

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(560f, 640f);

        CreateText(panel.transform, "Title", "补给商店", 28, TextAnchor.UpperCenter, new Vector2(0f, -16f), new Vector2(520f, 40f));
        CreateText(panel.transform, "Coins", "金币：无限", 24, TextAnchor.UpperCenter, new Vector2(0f, -60f), new Vector2(520f, 36f));

        ammo17Text = CreateText(panel.transform, "Ammo17", "", 24, TextAnchor.UpperCenter, new Vector2(0f, -108f), new Vector2(520f, 36f));
        buy17Label = CreateButton(panel.transform, "Buy17", "购买 10 发（10 金币）", Buy17, new Vector2(0f, -150f), out _);

        ammo42Text = CreateText(panel.transform, "Ammo42", "", 24, TextAnchor.UpperCenter, new Vector2(0f, -214f), new Vector2(520f, 36f));
        buy42Label = CreateButton(panel.transform, "Buy42", "购买 1 发（15 金币）", Buy42, new Vector2(0f, -256f), out _);

        buyHpLabel = CreateButton(panel.transform, "BuyHp", "买血", BuyHp, new Vector2(0f, -314f), out buyHpButton);
        buyReviveLabel = CreateButton(panel.transform, "BuyRevive", "立即复活", BuyRevive, new Vector2(0f, -368f), out buyReviveButton);

        hintText = CreateText(panel.transform, "Hint", "", 22, TextAnchor.UpperCenter, new Vector2(0f, -430f), new Vector2(520f, 64f));
        CreateButton(panel.transform, "Close", "关闭", () => SetOpen(false), new Vector2(0f, -520f), out _);
    }

    Text CreateButton(Transform parent, string name, string caption, UnityEngine.Events.UnityAction onClick, Vector2 pos, out Button button)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.sprite = GetWhiteSprite();
        img.color = new Color(0.22f, 0.24f, 0.3f, 1f);
        img.raycastTarget = true;

        button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(420f, 42f);

        Text label = CreateText(go.transform, "Label", caption, 22, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(420f, 42f));
        RectTransform labelRt = label.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.pivot = new Vector2(0.5f, 0.5f);
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;
        label.raycastTarget = false;
        return label;
    }

    Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor align, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.font = LoadBuiltinFont();
        text.fontSize = fontSize;
        text.alignment = align;
        text.color = Color.white;
        text.text = content;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = false;

        RectTransform rt = text.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return text;
    }

    static Font LoadBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont("Arial", 24);
        return font;
    }

    static Sprite GetWhiteSprite()
    {
        if (whiteSprite != null)
            return whiteSprite;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        return whiteSprite;
    }
}
