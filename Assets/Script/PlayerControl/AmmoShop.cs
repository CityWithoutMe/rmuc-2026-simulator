using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 运行时补给商店。挂在 PlayerShooting 那台车上，按 B 开关。
// 2026 5.3.1/5.3.2：现场与远程购弹分开，主机校验位置、脱战、延迟和全队兑换上限。
public class AmmoShop : MonoBehaviour
{
    // 现场：10金币/10发17mm，10金币/1发42mm；远程：150金币/100发17mm或10发42mm。
    public const int PricePer17mm = 1;
    public const int BuyCount17mm = 10;
    public const int PricePer42mm = 10;
    public const int BuyCount42mm = 1;

    // 队伍金币只由主机分配和扣除。
    Text coinsText;

    public bool IsOpen => open;

    RobotAttributeManager attributes;
    PlayerMovement movement;
    GameObject panel;
    Text ammo17Text;
    Text ammo42Text;
    Text hintText;
    Text buy17Label;
    Text buy42Label;
    Text remote17Label, remote42Label;
    Button local17Button, local42Button, remote17Button, remote42Button;
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

    public void CloseForAdministration() { if (open) SetOpen(false); }

    void Buy17() { Purchase("ammo17"); }
    void Buy42() { Purchase("ammo42"); }
    void Remote17() { Purchase("remote17"); }
    void Remote42() { Purchase("remote42"); }
    void BuyHp() { Purchase("heal"); }
    void BuyRevive() { Purchase("revive"); }

    void Purchase(string action)
    {
        if (LanSession.Active) { LanSession.Instance.Purchase(action); return; }
        hintText.text = ExecutePurchase(ResolveAttributes(), action) ?? "兑换成功";
        Refresh();
    }

    static float MatchRemainingSeconds() => FindAnyObjectByType<MatchTimer>()?.RemainingSeconds ?? 420f;

    public static string ExecuteLanPurchase(RobotAttributeManager attr, string action)
    {
        if (!LanSession.IsHost) return "无效购买";
        return ExecutePurchase(attr, action);
    }

    static string ExecutePurchase(RobotAttributeManager attr, string action)
    {
        if (!LanSession.CanSimulate || MatchOutcome.Decided || attr == null) return "无效购买";
        if (attr.IsFoulOut || !attr.IsPowered) return "当前状态不能兑换";
        if (action == "heal" || action == "revive")
        {
            string block = action == "heal" ? attr.RemoteHealBlockReason() : attr.ImmediateReviveBlockReason();
            if (block != null) return block;
            int cost = action == "heal" ? RobotLevelRules.RemoteHealPrice(MatchRemainingSeconds())
                : RobotLevelRules.ImmediateRevivePrice(MatchRemainingSeconds(), attr.Level);
            if (!MatchTeamEconomy.TrySpend(attr.team, cost)) return "队伍金币不足";
            if (action == "heal") attr.BeginRemoteHeal();
            else if (!attr.BeginImmediateRevive())
            {
                MatchTeamEconomy.Grant(attr.team == RobotTeam.Red ? cost : 0,
                    attr.team == RobotTeam.Blue ? cost : 0);
                return "立即复活失败，已退回金币";
            }
            return null;
        }
        if (action == "ammo17" || action == "ammo42" || action == "remote17" || action == "remote42")
            return AmmoExchange.Buy(attr, action == "ammo42" || action == "remote42", action.StartsWith("remote"));
        return "无效兑换项目";
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

        coinsText.text = "队伍金币：" + MatchTeamEconomy.Balance(attr.team);
        int cur17 = Mathf.RoundToInt(attr.GetCurrent(RobotStat.Ammo17mm));
        int max17 = Mathf.RoundToInt(attr.GetCap(RobotStat.Ammo17mm));
        int cur42 = Mathf.RoundToInt(attr.GetCurrent(RobotStat.Ammo42mm));
        int max42 = Mathf.RoundToInt(attr.GetCap(RobotStat.Ammo42mm));

        bool hero = attr.robotType == RobotType.Hero;
        bool infantry = attr.robotType == RobotType.Infantry;
        // 当前 2v2 保留英雄两种弹药入口，步兵只买 17mm。
        bool show17 = hero || infantry;
        bool show42 = hero;
        ammo17Text.gameObject.SetActive(show17);
        buy17Label.transform.parent.gameObject.SetActive(show17);
        remote17Label.transform.parent.gameObject.SetActive(show17);
        ammo42Text.gameObject.SetActive(show42);
        buy42Label.transform.parent.gameObject.SetActive(show42);
        remote42Label.transform.parent.gameObject.SetActive(show42);
        int pending17 = AmmoExchange.Pending(attr, false), pending42 = AmmoExchange.Pending(attr, true);
        ammo17Text.text = "17mm：" + cur17 + "/" + max17 + "    在途：" + pending17;
        ammo42Text.text = "42mm：" + cur42 + "/" + max42 + "    在途：" + pending42;
        bool valid = attr.IsAlive && attr.IsPowered && !attr.IsFoulOut && !MatchOutcome.Decided;
        bool local = valid && FieldSupportZoneBuff.CanLocalPurchase(attr);
        bool remote = valid && attr.IsOutOfCombat;
        int balance = MatchTeamEconomy.Balance(attr.team);
        local17Button.interactable = local && show17 && balance >= 10 && max17 - cur17 - pending17 >= 10;
        local42Button.interactable = local && show42 && balance >= 10 && max42 - cur42 - pending42 >= 1;
        remote17Button.interactable = remote && show17 && balance >= 150 && max17 - cur17 - pending17 >= 100;
        remote42Button.interactable = remote && show42 && balance >= 150 && max42 - cur42 - pending42 >= 10;
        buy17Label.text = "现场购买 10 发 / 10 金币（需占领补给点）";
        buy42Label.text = "现场购买 1 发 / 10 金币（需占领补给点）";
        remote17Label.text = "远程购买 100 发 / 150 金币（脱战，6秒到账）";
        remote42Label.text = "远程购买 10 发 / 150 金币（脱战，6秒到账）";
        float wait = AmmoExchange.WaitingSeconds(attr);
        if (LanSession.Active) hintText.text = LanSession.Instance.Status;
        if (wait > 0) hintText.text = "远程弹量在途，下一笔 " + Mathf.CeilToInt(wait) + " 秒后到账";

        float remaining = MatchRemainingSeconds();
        int healPrice = RobotLevelRules.RemoteHealPrice(remaining);
        int revivePrice = RobotLevelRules.ImmediateRevivePrice(remaining, attr.Level);
        string healBlock = attr.RemoteHealBlockReason();
        string reviveBlock = attr.ImmediateReviveBlockReason();

        if (buyHpButton != null)
            buyHpButton.interactable = healBlock == null && MatchTeamEconomy.Balance(attr.team) >= healPrice;
        if (buyReviveButton != null)
            buyReviveButton.interactable = reviveBlock == null && MatchTeamEconomy.Balance(attr.team) >= revivePrice;

        buyHpLabel.text = healBlock == null
            ? "远程回血（" + healPrice + " 金币，+60% 上限）"
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
        panelRt.sizeDelta = new Vector2(600f, 760f);

        CreateText(panel.transform, "Title", "补给商店", 28, TextAnchor.UpperCenter, new Vector2(0f, -16f), new Vector2(520f, 40f));
        coinsText = CreateText(panel.transform, "Coins", "队伍金币：0", 24, TextAnchor.UpperCenter, new Vector2(0f, -60f), new Vector2(520f, 36f));

        ammo17Text = CreateText(panel.transform, "Ammo17", "", 24, TextAnchor.UpperCenter, new Vector2(0f, -108f), new Vector2(520f, 36f));
        buy17Label = CreateButton(panel.transform, "Buy17", "现场购买 10 发 / 10 金币", Buy17, new Vector2(0f, -150f), out local17Button);
        remote17Label = CreateButton(panel.transform, "Remote17", "远程购买 100 发 / 150 金币", Remote17, new Vector2(0f, -200f), out remote17Button);

        ammo42Text = CreateText(panel.transform, "Ammo42", "", 24, TextAnchor.UpperCenter, new Vector2(0f, -260f), new Vector2(520f, 36f));
        buy42Label = CreateButton(panel.transform, "Buy42", "现场购买 1 发 / 10 金币", Buy42, new Vector2(0f, -300f), out local42Button);
        remote42Label = CreateButton(panel.transform, "Remote42", "远程购买 10 发 / 150 金币", Remote42, new Vector2(0f, -350f), out remote42Button);

        buyHpLabel = CreateButton(panel.transform, "BuyHp", "买血", BuyHp, new Vector2(0f, -418f), out buyHpButton);
        buyReviveLabel = CreateButton(panel.transform, "BuyRevive", "立即复活", BuyRevive, new Vector2(0f, -472f), out buyReviveButton);

        hintText = CreateText(panel.transform, "Hint", "", 22, TextAnchor.UpperCenter, new Vector2(0f, -532f), new Vector2(520f, 64f));
        CreateButton(panel.transform, "Close", "关闭", () => SetOpen(false), new Vector2(0f, -650f), out _);
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
        rt.sizeDelta = new Vector2(550f, 42f);

        Text label = CreateText(go.transform, "Label", caption, 20, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(550f, 42f));
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
