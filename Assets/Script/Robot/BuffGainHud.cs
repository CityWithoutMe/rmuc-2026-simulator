using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

// 左上角列出当前这台车的增益。样式对齐右上角 HeroAttributeHud，排序低于准心和商店。
public class BuffGainHud : MonoBehaviour
{
    const int SortingOrder = 100;
    const string EmptyText = "增益：无";

    Text label;
    RobotAttributeManager stats;
    PlayerMovement move;
    readonly List<string> sourceIds = new List<string>();

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<BuffGainHud>() != null)
            return;

        GameObject host = new GameObject("BuffGainHud");
        host.AddComponent<BuffGainHud>();
    }

    void Awake()
    {
        CreateDisplay();
        Refresh();
    }

    void Update()
    {
        if (stats == null || move == null || !move.isActiveAndEnabled)
            TryBind();
        Refresh();
    }

    void TryBind()
    {
        if (PlayerAttributeBinding.HasRoster
            && PlayerAttributeBinding.TryGetPlayerStats(out RobotAttributeManager roster)
            && roster != null)
        {
            stats = roster;
        }
        else
        {
            PlayerMovement vehicle = FindPlayerVehicle();
            move = vehicle;
            if (vehicle != null)
            {
                if (PlayerAttributeBinding.TryResolveRoster(vehicle.gameObject, out RobotAttributeManager bound) && bound != null)
                    stats = bound;
                else
                {
                    RobotAttributeManager onBody = vehicle.GetComponent<RobotAttributeManager>();
                    if (onBody == null)
                        onBody = vehicle.GetComponentInParent<RobotAttributeManager>();
                    stats = onBody;
                }
            }
        }

        if (move == null || !move.isActiveAndEnabled)
            move = FindPlayerVehicle();
    }

    void Refresh()
    {
        if (label == null)
            return;
        if (stats == null)
        {
            label.text = EmptyText;
            return;
        }

        sourceIds.Clear();
        CollectSources(RobotStat.AttackBuffPercent);
        CollectSources(RobotStat.DefenseBuffPercent);
        CollectSources(RobotStat.CooldownBuffPercent);
        CollectSources(RobotStat.CoolingRate);
        CollectSources(RobotStat.RecoveryRate);

        string body = ProgressFor(stats);
        for (int i = 0; i < sourceIds.Count; i++)
        {
            string id = sourceIds[i];
            string name = BuffName(id);
            if (name == null)
                continue;
            string bonus = BonusText(id);
            if (bonus.Length == 0)
                continue;
            string line = name + " " + bonus + " " + DurationText(id);
            if (body.Length > 0)
                body += "\n";
            body += line;
        }

        float attack = MaxBonus(RobotStat.AttackBuffPercent, false);
        float defense = MaxBonus(RobotStat.DefenseBuffPercent, false);
        float cool = MaxBonus(RobotStat.CoolingRate, true);
        if (body.Length == 0 && attack <= 0.0001f && defense <= 0.0001f && cool <= 0.0001f)
        {
            label.text = EmptyText;
            return;
        }

        string total = "总增益：攻击" + Pct(attack) + " 防御" + Pct(defense) + " 冷却" + Pct(cool);
        label.text = body.Length == 0 ? total : body + "\n" + total;
    }

    // 客户端只展示主机裁定的过程信息，不运行本地占领/跨越判定。
    public static string ProgressFor(RobotAttributeManager attr)
    {
        return LanSession.IsClient ? LanVehicle.ForStats(attr)?.FieldProgress ?? "" : LocalProgress(attr);
    }

    public static string LocalProgress(RobotAttributeManager attr)
    {
        var lines = new List<string>(3);
        if (TerrainCrossBuff.TryGetAttempt(attr, out string crossing)) lines.Add(crossing);
        if (HighlandZoneContest.TryGetChannel(attr, out string highland)) lines.Add(highland);
        if (SideZoneBuff.TryGetChannel(attr, out string side)) lines.Add(side);
        string rebuilding = OutpostHealth.RebuildProgress(attr);
        if (!string.IsNullOrEmpty(rebuilding)) lines.Add(rebuilding);
        return string.Join("\n", lines);
    }

    float MaxBonus(RobotStat stat, bool usePercent)
    {
        float best = 0f;
        IReadOnlyList<StatModifier> list = stats.GetModifiers(stat);
        for (int i = 0; i < list.Count; i++)
        {
            if (BuffName(list[i].sourceId) == null)
                continue;
            float value = usePercent ? list[i].percent : list[i].add;
            if (value > best)
                best = value;
        }

        return best;
    }

    string DurationText(string sourceId)
    {
        float remain = -1f;
        bool found = false;
        found |= Longer(RobotStat.AttackBuffPercent, sourceId, ref remain);
        found |= Longer(RobotStat.DefenseBuffPercent, sourceId, ref remain);
        found |= Longer(RobotStat.CooldownBuffPercent, sourceId, ref remain);
        found |= Longer(RobotStat.CoolingRate, sourceId, ref remain);
        found |= Longer(RobotStat.RecoveryRate, sourceId, ref remain);
        if (!found || remain < 0f)
            return "持续";
        int seconds = Mathf.Max(0, Mathf.CeilToInt(remain));
        return "剩余" + seconds.ToString(CultureInfo.InvariantCulture) + "秒";
    }

    bool Longer(RobotStat stat, string sourceId, ref float remain)
    {
        if (!stats.TryGetModifierTiming(stat, sourceId, out float value))
            return false;
        if (value < 0f)
        {
            remain = -1f;
            return true;
        }

        if (remain < 0f || value > remain)
            remain = value;
        return true;
    }

    void CollectSources(RobotStat stat)
    {
        IReadOnlyList<StatModifier> list = stats.GetModifiers(stat);
        for (int i = 0; i < list.Count; i++)
        {
            string id = list[i].sourceId;
            if (string.IsNullOrEmpty(id) || sourceIds.Contains(id))
                continue;
            if (BuffName(id) == null)
                continue;
            sourceIds.Add(id);
        }
    }

    string BonusText(string sourceId)
    {
        string text = "";
        Append(ref text, "攻击", ModifierAdd(RobotStat.AttackBuffPercent, sourceId));
        Append(ref text, "防御", ModifierAdd(RobotStat.DefenseBuffPercent, sourceId));
        Append(ref text, "冷却", ModifierAdd(RobotStat.CooldownBuffPercent, sourceId));
        Append(ref text, "冷却", ModifierPercent(RobotStat.CoolingRate, sourceId));
        float coolAdd = ModifierAdd(RobotStat.CoolingRate, sourceId);
        float heal = ModifierAdd(RobotStat.RecoveryRate, sourceId);
        if (heal > 0f) text += "回血+" + heal.ToString("0.#", CultureInfo.InvariantCulture) + "/秒";
        if (coolAdd > 0.01f)
        {
            if (text.Length > 0)
                text += " ";
            text += "冷却+" + Mathf.RoundToInt(coolAdd).ToString(CultureInfo.InvariantCulture) + "/秒";
        }
        return text;
    }

    float ModifierPercent(RobotStat stat, string sourceId)
    {
        IReadOnlyList<StatModifier> list = stats.GetModifiers(stat);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].sourceId == sourceId)
                return list[i].percent;
        }

        return 0f;
    }

    float ModifierAdd(RobotStat stat, string sourceId)
    {
        IReadOnlyList<StatModifier> list = stats.GetModifiers(stat);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].sourceId == sourceId)
                return list[i].add;
        }

        return 0f;
    }

    static void Append(ref string text, string labelName, float add)
    {
        if (Mathf.Abs(add) < 0.0001f)
            return;
        if (text.Length > 0)
            text += " ";
        text += labelName + Signed(add);
    }

    static string BuffName(string sourceId)
    {
        if (sourceId.StartsWith("zone_support_supply_", System.StringComparison.Ordinal)) return "补给区";
        if (sourceId.StartsWith("zone_support_outpost_", System.StringComparison.Ordinal)) return "前哨站增益点";
        if (sourceId.StartsWith("zone_support_central_", System.StringComparison.Ordinal)) return "中央高地";
        if (sourceId.StartsWith("zone_highland", System.StringComparison.Ordinal))
            return "梯形高地";
        if (sourceId == TerrainCrossBuff.GroundDefenseId)
            return "地形跨越";
        if (sourceId == TerrainCrossBuff.TunnelDefenseId)
            return "地形跨越（隧道）";
        if (sourceId == TerrainCrossBuff.TunnelCoolingId)
            return "地形跨越（隧道）";
        if (sourceId.StartsWith("zone_base_", System.StringComparison.Ordinal))
            return "基地增益点";
        if (sourceId.StartsWith("zone_fort_def_", System.StringComparison.Ordinal))
            return "堡垒增益点";
        if (sourceId.StartsWith("zone_fort_vuln_", System.StringComparison.Ordinal))
            return "堡垒易伤";
        if (sourceId.StartsWith("rune_small", System.StringComparison.Ordinal))
            return "小能量机关";
        if (sourceId.StartsWith("rune_large", System.StringComparison.Ordinal))
            return "大能量机关";
        return null;
    }

    static string Pct(float unit)
    {
        return Mathf.RoundToInt(unit * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    static string Signed(float unit)
    {
        int value = Mathf.RoundToInt(unit * 100f);
        string sign = value > 0 ? "+" : "";
        return sign + value.ToString(CultureInfo.InvariantCulture) + "%";
    }

    void CreateDisplay()
    {
        GameObject canvasGo = new GameObject("BuffGainCanvas");
        canvasGo.transform.SetParent(transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textGo = new GameObject("BuffText");
        textGo.transform.SetParent(canvasGo.transform, false);

        label = textGo.AddComponent<Text>();
        label.font = LoadBuiltinFont();
        label.fontSize = 24;
        label.alignment = TextAnchor.UpperLeft;
        label.color = Color.white;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        label.supportRichText = false;
        label.text = EmptyText;

        RectTransform rt = label.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(24f, -24f);
        rt.sizeDelta = new Vector2(520f, 360f);
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
            PlayerMovement candidate = all[i];
            if (candidate == null || IsUnderRune(candidate.transform))
                continue;

            bool blue = PlayerMovement.IsBlueHeroVehicle(candidate.gameObject);
            bool red = PlayerMovement.IsRedPlayerVehicle(candidate.gameObject);
            if (!wantInfantry && wantBlue && red)
                continue;
            if (!wantInfantry && !wantBlue && (blue || PlayerMovement.IsBlueSideVehicle(candidate.gameObject)))
                continue;
            if (wantInfantry && !PlayerMovement.IsSelectedPlayerVehicle(candidate.gameObject))
                continue;

            int score = 0;
            if (candidate.enabled && candidate.gameObject.activeInHierarchy)
                score += 2;
            if (wantBlue && blue)
                score += 32;
            if (!wantInfantry && !wantBlue && string.Equals(candidate.gameObject.name, "hero_red", System.StringComparison.OrdinalIgnoreCase))
                score += 32;
            if (PlayerMovement.IsSelectedPlayerVehicle(candidate.gameObject))
                score += 16;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
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
}
