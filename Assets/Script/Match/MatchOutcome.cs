using UnityEngine;
using UnityEngine.UI;

// 5.8 单局胜负，按编号优先，直到分出结果。不做 BO3/BO5 加赛。
// 1. 时间耗尽，或一方基地被击毁：基地剩余血量高的一方获胜。
// 2. 时间耗尽且基地血量相同，前哨站都没被击毁过：前哨站剩余血量高的一方获胜。
// 3. 时间耗尽且基地血量相同，只有一方前哨站被击毁过：没被击毁过的一方获胜。
// 4. 时间耗尽且基地血量相同，前哨站血量相同或双方都被击毁过：全队攻击伤害高的一方获胜。
// 5. 上述仍相同：全队机器人剩余血量高的一方获胜。
// 6. 仍无法区分：平局。
// 基地被击毁但双方基地血量相同（都是 0）时，第 2～5 条只在时间耗尽时才用，所以直接平局。
public class MatchOutcome : MonoBehaviour
{
    const float Epsilon = 0.05f;

    public static bool Decided { get; private set; }
    public static float RedAttackDamage { get; private set; }
    public static float BlueAttackDamage { get; private set; }

    Text resultText;
    bool judging;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Decided = false;
        RedAttackDamage = 0f;
        BlueAttackDamage = 0f;
    }

    public static void BindForLoadedMatch()
    {
        Decided = false;
        RedAttackDamage = 0f;
        BlueAttackDamage = 0f;

        if (FindAnyObjectByType<MatchOutcome>() != null)
            return;

        GameObject host = new GameObject("MatchOutcome");
        host.AddComponent<MatchOutcome>();
    }

    // 计入对方造成的攻击伤害。基地虚拟护盾也算（5.5.1），同阵营和不扣血的不算。
    public static void AddAttackDamage(RobotTeam attacker, float amount)
    {
        if (Decided || amount <= 0f)
            return;
        if (attacker == RobotTeam.Red)
            RedAttackDamage += amount;
        else if (attacker == RobotTeam.Blue)
            BlueAttackDamage += amount;
    }

    void Awake()
    {
        CreateDisplay();
    }

    void LateUpdate()
    {
        if (Decided || judging)
            return;

        bool baseDestroyed = BaseHealth.HpOf(RobotTeam.Red) <= Epsilon
            || BaseHealth.HpOf(RobotTeam.Blue) <= Epsilon;
        MatchTimer timer = FindAnyObjectByType<MatchTimer>();
        bool timeUp = timer != null && timer.IsFinished;
        if (!baseDestroyed && !timeUp)
            return;

        judging = true;
        if (timer != null)
            timer.Halt();
        Judge(timeUp);
    }

    void Judge(bool timeUp)
    {
        float redBase = BaseHealth.HpOf(RobotTeam.Red);
        float blueBase = BaseHealth.HpOf(RobotTeam.Blue);
        if (redBase > blueBase + Epsilon)
        {
            Finish(RobotTeam.Red, "基地剩余血量更高");
            return;
        }

        if (blueBase > redBase + Epsilon)
        {
            Finish(RobotTeam.Blue, "基地剩余血量更高");
            return;
        }

        if (!timeUp)
        {
            Finish(RobotTeam.Neutral, "双方基地剩余血量相同");
            return;
        }

        bool redEver = OutpostHealth.EverDestroyed(RobotTeam.Red);
        bool blueEver = OutpostHealth.EverDestroyed(RobotTeam.Blue);
        float redOutpost = OutpostHealth.HpOf(RobotTeam.Red);
        float blueOutpost = OutpostHealth.HpOf(RobotTeam.Blue);

        if (!redEver && !blueEver)
        {
            if (redOutpost > blueOutpost + Epsilon)
            {
                Finish(RobotTeam.Red, "前哨站剩余血量更高");
                return;
            }

            if (blueOutpost > redOutpost + Epsilon)
            {
                Finish(RobotTeam.Blue, "前哨站剩余血量更高");
                return;
            }
        }
        else if (redEver != blueEver)
        {
            Finish(redEver ? RobotTeam.Blue : RobotTeam.Red, "前哨站未被击毁过");
            return;
        }

        if (RedAttackDamage > BlueAttackDamage + Epsilon)
        {
            Finish(RobotTeam.Red, "全队攻击伤害更高");
            return;
        }

        if (BlueAttackDamage > RedAttackDamage + Epsilon)
        {
            Finish(RobotTeam.Blue, "全队攻击伤害更高");
            return;
        }

        float redHp = TeamRobotHp(RobotTeam.Red);
        float blueHp = TeamRobotHp(RobotTeam.Blue);
        if (redHp > blueHp + Epsilon)
        {
            Finish(RobotTeam.Red, "全队剩余血量更高");
            return;
        }

        if (blueHp > redHp + Epsilon)
        {
            Finish(RobotTeam.Blue, "全队剩余血量更高");
            return;
        }

        Finish(RobotTeam.Neutral, "各项均相同");
    }

    static float TeamRobotHp(RobotTeam team)
    {
        RobotAttributeManager[] all = FindObjectsByType<RobotAttributeManager>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        TenRobotAttributeBoard roster = FindAnyObjectByType<TenRobotAttributeBoard>();
        Transform board = roster != null ? roster.transform : null;

        float sum = 0f;
        for (int i = 0; i < all.Length; i++)
        {
            RobotAttributeManager attr = all[i];
            if (attr == null || attr.team != team || !CountsForTeamHp(attr.robotType))
                continue;
            if (board != null && !attr.transform.IsChildOf(board) && attr.transform != board)
                continue;
            sum += Mathf.Max(0f, attr.Hp);
        }

        return sum;
    }

    static bool CountsForTeamHp(RobotType type)
    {
        return type == RobotType.Hero
            || type == RobotType.Infantry
            || type == RobotType.Engineer
            || type == RobotType.Sentry
            || type == RobotType.Aerial;
    }

    void Finish(RobotTeam winner, string reason)
    {
        Decided = true;
        string headline = winner == RobotTeam.Red ? "红方获胜"
            : winner == RobotTeam.Blue ? "蓝方获胜"
            : "平局";
        if (resultText != null)
        {
            resultText.text = headline + "\n" + reason;
            resultText.color = winner == RobotTeam.Red ? new Color(1f, 0.45f, 0.4f, 1f)
                : winner == RobotTeam.Blue ? new Color(0.55f, 0.75f, 1f, 1f)
                : Color.white;
        }

        Debug.Log("[胜负] " + headline + "：" + reason
            + " 红方基地" + BaseHealth.HpOf(RobotTeam.Red).ToString("0")
            + " 蓝方基地" + BaseHealth.HpOf(RobotTeam.Blue).ToString("0")
            + " 红方伤害" + RedAttackDamage.ToString("0.#")
            + " 蓝方伤害" + BlueAttackDamage.ToString("0.#"));

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void CreateDisplay()
    {
        GameObject canvasGo = new GameObject("MatchOutcomeCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textGo = new GameObject("ResultText");
        textGo.transform.SetParent(canvasGo.transform, false);
        resultText = textGo.AddComponent<Text>();
        resultText.font = LoadBuiltinFont();
        resultText.fontSize = 64;
        resultText.alignment = TextAnchor.MiddleCenter;
        resultText.color = Color.white;
        resultText.horizontalOverflow = HorizontalWrapMode.Overflow;
        resultText.verticalOverflow = VerticalWrapMode.Overflow;
        resultText.raycastTarget = false;
        resultText.text = "";

        RectTransform rt = resultText.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(900f, 180f);
    }

    static Font LoadBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont("Arial", 64);
        return font;
    }
}
