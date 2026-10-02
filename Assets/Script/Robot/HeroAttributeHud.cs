using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

// 右上角显示红方玩家当前属性。找到场景「属性管理器」时读那一份，不再往红车上挂第二份。
// 只读已有 API，不结算伤害、热量、弹药，也不改移动。
public class HeroAttributeHud : MonoBehaviour
{
    const string MissingText = "未找到车辆";

    Text label;
    Text reviveLabel;
    RobotAttributeManager stats;
    PlayerMovement move;
    bool warnedMissing;

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<HeroAttributeHud>() != null)
            return;

        GameObject host = new GameObject("HeroAttributeHud");
        host.AddComponent<HeroAttributeHud>();
    }

    void Awake()
    {
        CreateDisplay();
        Refresh();
    }

    void Start()
    {
        // Start 时 PlayerMovement 已把控制从 Cube 迁到选中的英雄。
        CombatDamage.EnsureBlueHeroes();
        TryBind();
        Refresh();
    }

    void Update()
    {
        if (stats == null)
            TryBind();
        if (move == null || !move.isActiveAndEnabled)
            move = FindPlayerVehicle();
        Refresh();
    }

    void TryBind()
    {
        if (stats != null)
            return;

        // 十车总控在时，右上角读当前车位（英雄 red_hero / blue_hero，步兵 red_infantry_1 等）。
        if (PlayerAttributeBinding.HasRoster)
        {
            if (PlayerAttributeBinding.TryGetPlayerStats(out RobotAttributeManager rosterStats) && rosterStats != null)
                stats = rosterStats;
            return;
        }

        // 没有总控、但场景里有「属性管理器」时，红方血量、热量、弹药都读根上那一份。
        if (PlayerAttributeBinding.TryGetPlayerStats(out RobotAttributeManager sceneStats) && sceneStats != null)
        {
            stats = sceneStats;
            return;
        }

        PlayerMovement vehicle = FindPlayerVehicle();
        if (vehicle == null)
        {
            if (!warnedMissing)
            {
                warnedMissing = true;
                Debug.LogWarning("HeroAttributeHud：未找到当前车辆");
            }
            return;
        }

        // 没有场景物体时，绑当前正在操作的英雄。蓝方被选中时读蓝车，不改成红方。
        RobotAttributeManager existing = vehicle.GetComponent<RobotAttributeManager>();
        if (existing == null)
            existing = vehicle.GetComponentInParent<RobotAttributeManager>();
        if (existing != null)
        {
            CombatDamage.ApplyTeamFromTags(existing, vehicle.gameObject);
            stats = existing;
            return;
        }

        stats = vehicle.gameObject.AddComponent<RobotAttributeManager>();
        if (MatchLaunchSelection.PlaysInfantry)
        {
            stats.robotType = RobotType.Infantry;
            stats.team = MatchLaunchSelection.Team;
        }
        else
        {
            stats.robotType = RobotType.Hero;
            stats.team = MatchLaunchSelection.PlaysBlueHero ? RobotTeam.Blue : RobotTeam.Red;
        }
        stats.team = CombatDamage.ResolveTeam(vehicle.gameObject, null, stats.team);
        stats.ApplyRobotTypeDefaults();
    }

    // 只绑定当前被操作的那一台。选步兵找对应步兵，选蓝方英雄找 hero_blue，否则找 hero_red。
    static PlayerMovement FindPlayerVehicle()
    {
        bool wantBlue = MatchLaunchSelection.PlaysBlueHero;
        bool wantInfantry = MatchLaunchSelection.PlaysInfantry;
        PlayerMovement[] all = FindObjectsByType<PlayerMovement>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        PlayerMovement best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < all.Length; i++)
        {
            PlayerMovement move = all[i];
            if (move == null || IsUnderRune(move.transform))
                continue;

            bool blue = PlayerMovement.IsBlueHeroVehicle(move.gameObject);
            bool red = PlayerMovement.IsRedPlayerVehicle(move.gameObject);
            if (!wantInfantry && wantBlue && red)
                continue;
            if (!wantInfantry && !wantBlue && (blue || PlayerMovement.IsBlueSideVehicle(move.gameObject)))
                continue;
            if (wantInfantry && !PlayerMovement.IsSelectedPlayerVehicle(move.gameObject))
                continue;

            int score = 0;
            if (move.enabled && move.gameObject.activeInHierarchy)
                score += 2;
            if (wantBlue && blue)
                score += 32;
            if (!wantInfantry && !wantBlue && string.Equals(move.gameObject.name, "hero_red", System.StringComparison.OrdinalIgnoreCase))
                score += 32;
            if (PlayerMovement.IsSelectedPlayerVehicle(move.gameObject))
                score += 16;

            if (score > bestScore)
            {
                bestScore = score;
                best = move;
            }
        }

        return best;
    }

    static bool IsUnderRune(Transform t)
    {
        while (t != null)
        {
            if (t.GetComponent<RotationCenterSpin>() != null)
                return true;
            t = t.parent;
        }

        return false;
    }

    void CreateDisplay()
    {
        GameObject canvasGo = new GameObject("HeroAttributeCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textGo = new GameObject("AttributeText");
        textGo.transform.SetParent(canvasGo.transform, false);

        label = textGo.AddComponent<Text>();
        label.font = LoadBuiltinFont();
        label.fontSize = 24;
        label.alignment = TextAnchor.UpperRight;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.supportRichText = false;

        RectTransform rt = label.rectTransform;
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-24f, -24f);
        rt.sizeDelta = new Vector2(560f, 640f);

        GameObject reviveGo = new GameObject("ReviveText");
        reviveGo.transform.SetParent(canvasGo.transform, false);
        reviveLabel = reviveGo.AddComponent<Text>();
        reviveLabel.font = label.font;
        reviveLabel.fontSize = 42;
        reviveLabel.alignment = TextAnchor.MiddleCenter;
        reviveLabel.color = new Color(1f, 0.85f, 0.35f, 1f);
        reviveLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        reviveLabel.verticalOverflow = VerticalWrapMode.Overflow;
        reviveLabel.raycastTarget = false;
        reviveLabel.text = "";

        RectTransform reviveRt = reviveLabel.rectTransform;
        reviveRt.anchorMin = new Vector2(0.5f, 0.5f);
        reviveRt.anchorMax = new Vector2(0.5f, 0.5f);
        reviveRt.pivot = new Vector2(0.5f, 0.5f);
        reviveRt.anchoredPosition = new Vector2(0f, 80f);
        reviveRt.sizeDelta = new Vector2(800f, 180f);
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

    void Refresh()
    {
        if (label == null)
            return;

        if (stats == null)
        {
            label.text = MissingText;
            if (reviveLabel != null)
                reviveLabel.text = "";
            return;
        }

        int nextExp = stats.Level >= RobotLevelRules.MaxLevel
            ? RobotLevelRules.TotalExpForLevel(RobotLevelRules.MaxLevel)
            : RobotLevelRules.TotalExpForLevel(stats.Level + 1);
        label.text =
            "兵种：" + TypeLabel(stats.robotType) + "\n" +
            "队伍：" + TeamLabel(stats.team) + "\n" +
            "等级：" + stats.Level + "\n" +
            "经验：" + N(stats.Experience) + "/" + nextExp + "\n" +
            "血量：" + N(stats.Hp) + "/" + N(stats.MaxHp) + "\n" +
            "护盾：" + N(stats.GetCurrent(RobotStat.ShieldHp)) + "\n" +
            "存活：" + YesNo(stats.IsAlive) + "\n" +
            "当前弹种：" + CurrentCaliberLine() + "\n" +
            "17mm弹药：" + N(stats.GetCurrent(RobotStat.Ammo17mm)) + "\n" +
            "42mm弹药：" + N(stats.GetCurrent(RobotStat.Ammo42mm)) + "\n" +
            "枪口热量：" + N(stats.GetCurrent(RobotStat.BarrelHeat)) + "/" + N(stats.GetCurrent(RobotStat.BarrelHeatLimit)) + "\n" +
            "过热：" + YesNo(stats.IsOverheated) + "\n" +
            "发射锁定：" + BarrelLockLabel() + "\n" +
            "底盘功率上限：" + N(stats.GetCurrent(RobotStat.ChassisPowerLimit)) + "\n" +
            "电容能量：" + N(stats.GetCurrent(RobotStat.ChassisPowerBuffer)) + "\n" +
            "移速：" + N(stats.MoveSpeed) + "\n" +
            "速度：" + F1(CurrentHorizontalSpeed()) + "\n" +
            "伤害：" + N(CurrentBulletBaseDamage());

        if (reviveLabel != null)
            reviveLabel.text = ReviveStatusText();
    }

    string ReviveStatusText()
    {
        string text = "";
        if (!stats.IsAlive && stats.ReviveSecondsRemaining > 0f)
            text = "复活倒计时：" + Mathf.CeilToInt(stats.ReviveSecondsRemaining);

        if (stats.IsWeak)
        {
            string weak = "虚弱：发射锁定";
            if (stats.WeakSecondsRemaining > 0f)
                weak += " " + Mathf.CeilToInt(stats.WeakSecondsRemaining) + " 秒";
            text = text.Length == 0 ? weak : text + "\n" + weak;
        }

        if (stats.InvulnerableSecondsRemaining > 0f)
        {
            string inv = "无敌剩余：" + Mathf.CeilToInt(stats.InvulnerableSecondsRemaining) + " 秒";
            text = text.Length == 0 ? inv : text + "\n" + inv;
        }

        return text;
    }

    float CurrentBulletBaseDamage()
    {
        PlayerShooting shooting = CurrentShooting();
        bool use42 = shooting != null
            ? shooting.SelectedIs42mm
            : stats.robotType == RobotType.Hero;
        if (!use42)
            return CombatDamage.RobotArmorDamage17mm;
        return stats.Damage;
    }

    string CurrentCaliberLine()
    {
        PlayerShooting shooting = CurrentShooting();
        bool use42 = shooting != null
            ? shooting.SelectedIs42mm
            : stats != null && stats.robotType == RobotType.Hero;
        float ammo = use42
            ? stats.GetCurrent(RobotStat.Ammo42mm)
            : stats.GetCurrent(RobotStat.Ammo17mm);
        return (use42 ? "42mm" : "17mm") + "  " + N(ammo);
    }

    string BarrelLockLabel()
    {
        if (stats.IsBarrelLockedForMatch)
            return "本局永久锁定";
        if (stats.IsBarrelLocked)
            return "锁定（热量归零后解锁）";
        return "否";
    }

    PlayerShooting CurrentShooting()
    {
        if (move == null || !move.isActiveAndEnabled)
            move = FindPlayerVehicle();
        if (move == null)
            return null;
        return move.GetComponent<PlayerShooting>();
    }

    float CurrentHorizontalSpeed()
    {
        if (move == null || !move.isActiveAndEnabled)
            move = FindPlayerVehicle();
        if (move == null)
            return 0f;
        return move.CurrentHorizontalSpeed;
    }

    static int N(float v)
    {
        return Mathf.RoundToInt(v);
    }

    static string F1(float v)
    {
        return v.ToString("0.0", CultureInfo.InvariantCulture);
    }

    static string YesNo(bool value)
    {
        return value ? "是" : "否";
    }

    static string TeamLabel(RobotTeam team)
    {
        switch (team)
        {
            case RobotTeam.Red: return "红方";
            case RobotTeam.Blue: return "蓝方";
            default: return "中立";
        }
    }

    static string TypeLabel(RobotType type)
    {
        switch (type)
        {
            case RobotType.Hero: return "英雄";
            case RobotType.Infantry: return "步兵";
            case RobotType.Sentry: return "哨兵";
            case RobotType.Engineer: return "工程";
            case RobotType.Aerial: return "空中";
            case RobotType.Radar: return "雷达";
            case RobotType.Dart: return "飞镖";
            case RobotType.Outpost: return "前哨站";
            case RobotType.Base: return "基地";
            default: return type.ToString();
        }
    }
}
