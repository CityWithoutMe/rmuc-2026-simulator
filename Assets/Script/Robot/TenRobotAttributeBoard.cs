using System;
using UnityEngine;

// 挂到场景物体「属性管理器」上的十车总控。
// Add Component 里搜「十车属性」或「属性管理器」。
// 添加组件时（Reset）在本物体下准备 10 个子物体，名字与 Header 一致，各挂一份 RobotAttributeManager。
// 已有同名子物体就复用，不覆盖已经改过的数值。运行时不在根物体上再挂第二份。
// 降温、过热扣血仍由各子物体自己的 RobotAttributeManager.Update 负责。
[DisallowMultipleComponent]
[AddComponentMenu("属性管理器/十车属性")]
public class TenRobotAttributeBoard : MonoBehaviour
{
    public const int SlotCount = 10;

    // Header 文字必须是这些英文，和子物体名字一致。
    public const string HeaderRedHero = "red_hero";
    public const string HeaderRedInfantry1 = "red_infantry_1";
    public const string HeaderRedInfantry2 = "red_infantry_2";
    public const string HeaderRedInfantry3 = "red_infantry_3";
    public const string HeaderRedEngineer = "red_engineer";
    public const string HeaderBlueHero = "blue_hero";
    public const string HeaderBlueInfantry1 = "blue_infantry_1";
    public const string HeaderBlueInfantry2 = "blue_infantry_2";
    public const string HeaderBlueInfantry3 = "blue_infantry_3";
    public const string HeaderBlueEngineer = "blue_engineer";

    public static readonly string[] SlotHeaders =
    {
        HeaderRedHero,
        HeaderRedInfantry1,
        HeaderRedInfantry2,
        HeaderRedInfantry3,
        HeaderRedEngineer,
        HeaderBlueHero,
        HeaderBlueInfantry1,
        HeaderBlueInfantry2,
        HeaderBlueInfantry3,
        HeaderBlueEngineer
    };

    // 与下面公开字段名一致，给自定义检视器用。
    public static readonly string[] SlotPropertyNames =
    {
        nameof(redHero),
        nameof(redInfantry1),
        nameof(redInfantry2),
        nameof(redInfantry3),
        nameof(redEngineer),
        nameof(blueHero),
        nameof(blueInfantry1),
        nameof(blueInfantry2),
        nameof(blueInfantry3),
        nameof(blueEngineer)
    };

    [Header(HeaderRedHero)]
    [Tooltip("红方英雄。子物体 red_hero 上的那一份属性")]
    public RobotAttributeManager redHero;

    [Header(HeaderRedInfantry1)]
    [Tooltip("红方 1 号步兵")]
    public RobotAttributeManager redInfantry1;

    [Header(HeaderRedInfantry2)]
    [Tooltip("红方 2 号步兵")]
    public RobotAttributeManager redInfantry2;

    [Header(HeaderRedInfantry3)]
    [Tooltip("红方 3 号步兵")]
    public RobotAttributeManager redInfantry3;

    // 红方第 5 台。用户点了英雄和三台步兵，五红五蓝的最后一台用工程。
    [Header(HeaderRedEngineer)]
    [Tooltip("红方工程")]
    public RobotAttributeManager redEngineer;

    [Header(HeaderBlueHero)]
    [Tooltip("蓝方英雄。子物体 blue_hero 上的那一份属性")]
    public RobotAttributeManager blueHero;

    [Header(HeaderBlueInfantry1)]
    [Tooltip("蓝方 1 号步兵")]
    public RobotAttributeManager blueInfantry1;

    [Header(HeaderBlueInfantry2)]
    [Tooltip("蓝方 2 号步兵")]
    public RobotAttributeManager blueInfantry2;

    [Header(HeaderBlueInfantry3)]
    [Tooltip("蓝方 3 号步兵")]
    public RobotAttributeManager blueInfantry3;

    [Header(HeaderBlueEngineer)]
    [Tooltip("蓝方工程")]
    public RobotAttributeManager blueEngineer;

    struct SlotRecipe
    {
        public string header;
        public RobotTeam team;
        public RobotType type;
        public int robotId;

        public SlotRecipe(string header, RobotTeam team, RobotType type, int robotId)
        {
            this.header = header;
            this.team = team;
            this.type = type;
            this.robotId = robotId;
        }
    }

    // 编号互不重复。步兵 2、3 号不能用 ApplyRobotTypeDefaults 的结果：那个函数会把同阵营步兵都写成 3 或 103。
    static readonly SlotRecipe[] Recipes =
    {
        new SlotRecipe(HeaderRedHero, RobotTeam.Red, RobotType.Hero, 1),
        new SlotRecipe(HeaderRedInfantry1, RobotTeam.Red, RobotType.Infantry, 3),
        new SlotRecipe(HeaderRedInfantry2, RobotTeam.Red, RobotType.Infantry, 4),
        new SlotRecipe(HeaderRedInfantry3, RobotTeam.Red, RobotType.Infantry, 5),
        new SlotRecipe(HeaderRedEngineer, RobotTeam.Red, RobotType.Engineer, 2),
        new SlotRecipe(HeaderBlueHero, RobotTeam.Blue, RobotType.Hero, 101),
        new SlotRecipe(HeaderBlueInfantry1, RobotTeam.Blue, RobotType.Infantry, 103),
        new SlotRecipe(HeaderBlueInfantry2, RobotTeam.Blue, RobotType.Infantry, 104),
        new SlotRecipe(HeaderBlueInfantry3, RobotTeam.Blue, RobotType.Infantry, 105),
        new SlotRecipe(HeaderBlueEngineer, RobotTeam.Blue, RobotType.Engineer, 102)
    };

    public RobotAttributeManager GetSlot(int index)
    {
        switch (index)
        {
            case 0: return redHero;
            case 1: return redInfantry1;
            case 2: return redInfantry2;
            case 3: return redInfantry3;
            case 4: return redEngineer;
            case 5: return blueHero;
            case 6: return blueInfantry1;
            case 7: return blueInfantry2;
            case 8: return blueInfantry3;
            case 9: return blueEngineer;
            default: return null;
        }
    }

    public RobotAttributeManager GetByHeader(string header)
    {
        if (string.IsNullOrEmpty(header))
            return null;

        for (int i = 0; i < SlotHeaders.Length; i++)
        {
            if (string.Equals(SlotHeaders[i], header, StringComparison.OrdinalIgnoreCase))
                return GetSlot(i);
        }

        return null;
    }

    // 和 TryResolveVehicle 同一套名字规则，只看这一个物体，不往父级走。
    public static bool TryMatchSlot(GameObject go, out int slot)
    {
        slot = -1;
        if (go == null)
            return false;
        return TryMatchObject(go, out slot);
    }

    // 添加组件、或检视器里点 Reset 时调用。已有同名子物体只把引用接回去。
    void Reset()
    {
        EnsureSlots();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += EnsureSlotsAfterAdd;
#endif
    }

#if UNITY_EDITOR
    void EnsureSlotsAfterAdd()
    {
        if (this == null)
            return;

        EnsureSlots();
        if (Application.isPlaying)
            return;

        UnityEditor.EditorApplication.RepaintHierarchyWindow();
        UnityEditor.ActiveEditorTracker.sharedTracker.ForceRebuild();
    }
#endif

    void EnsureSlots()
    {
        bool changed = false;

        for (int i = 0; i < Recipes.Length; i++)
        {
            // 引用还在就不动数值。检视器 Reset 会先把引用清掉，接着靠同名子物体接回来。
            if (GetSlot(i) != null)
                continue;

            SlotRecipe recipe = Recipes[i];
            Transform child = FindDirectChild(recipe.header);
            if (child == null)
            {
                GameObject go = new GameObject(recipe.header);
                go.transform.SetParent(transform, false);
                child = go.transform;
            }

            RobotAttributeManager ram = child.GetComponent<RobotAttributeManager>();
            if (ram == null)
            {
                ram = child.gameObject.AddComponent<RobotAttributeManager>();
                // 只有刚挂上、还没有用户改过的数时才填兵种默认值。
                ConfigureFresh(ram, recipe);
            }

            SetSlot(i, ram);
            changed = true;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(ram);
                UnityEditor.EditorUtility.SetDirty(child.gameObject);
                if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(ram))
                    UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(ram);
            }
#endif
        }

#if UNITY_EDITOR
        if (changed && !Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(gameObject);
            if (gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(this))
                UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }

    static void ConfigureFresh(RobotAttributeManager ram, SlotRecipe recipe)
    {
        ram.team = recipe.team;
        ram.robotType = recipe.type;
        ram.ApplyRobotTypeDefaults();
        // 默认函数会按兵种改写 robotId，步兵会撞号，所以放在它后面。
        ram.robotId = recipe.robotId;
    }

    // Transform.Find 找不到未激活子物体，这里按直接子级名字找，避免再造一个同名车位。
    Transform FindDirectChild(string header)
    {
        int count = transform.childCount;
        for (int i = 0; i < count; i++)
        {
            Transform child = transform.GetChild(i);
            if (child != null && string.Equals(child.name, header, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    void SetSlot(int index, RobotAttributeManager ram)
    {
        switch (index)
        {
            case 0: redHero = ram; break;
            case 1: redInfantry1 = ram; break;
            case 2: redInfantry2 = ram; break;
            case 3: redInfantry3 = ram; break;
            case 4: redEngineer = ram; break;
            case 5: blueHero = ram; break;
            case 6: blueInfantry1 = ram; break;
            case 7: blueInfantry2 = ram; break;
            case 8: blueInfantry3 = ram; break;
            case 9: blueEngineer = ram; break;
        }
    }

    // 对得上某辆车时返回 true。车位引用没接上时 stats 为 null，调用方不要再在车上补一份。
    // 从碰撞体往父级走。车根名字比子网格更具体时用车根。
    public bool TryResolveVehicle(GameObject go, out RobotAttributeManager stats)
    {
        stats = null;
        if (!TryFindSlot(go, out int slot))
            return false;

        stats = GetSlot(slot);
        return true;
    }

    bool TryFindSlot(GameObject go, out int slot)
    {
        slot = -1;
        if (go == null || IsUnderRune(go.transform))
            return false;

        // 子网格和车根都可能对上。同等具体时留更靠近碰撞体的那个，避免上层文件夹把步兵收成英雄。
        int found = -1;
        int foundScore = -1;
        Transform t = go.transform;
        while (t != null)
        {
            if (TryMatchObject(t.gameObject, out int matched))
            {
                int score = MatchScore(t.gameObject);
                if (score > foundScore)
                {
                    foundScore = score;
                    found = matched;
                }
            }

            t = t.parent;
        }

        // 正在操作的红方玩家（hero_red）对上 red_hero。没有步兵、工程的车就不造车，只是这里对不上。
        if (found < 0 && PlayerAttributeBinding.IsRedPlayerHierarchy(go.transform))
            found = 0;

        if (found < 0)
            return false;

        slot = found;
        return true;
    }

    // 带编号的步兵、工程、hero_red / hero_blue、以及车位全名，比单独一个 hero 更具体。
    static int MatchScore(GameObject go)
    {
        string n = go.name;
        if (string.IsNullOrEmpty(n))
            return 0;

        for (int i = 0; i < SlotHeaders.Length; i++)
        {
            if (string.Equals(n, SlotHeaders[i], StringComparison.OrdinalIgnoreCase))
                return 10;
        }

        if (n.IndexOf("hero_red", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("hero_blue", StringComparison.OrdinalIgnoreCase) >= 0)
            return 10;

        if (NameHasInfantry(n) && DetectInfantryIndex(n) >= 1)
            return 10;

        if (n.IndexOf("engineer", StringComparison.OrdinalIgnoreCase) >= 0)
            return 10;

        if (n.IndexOf("hero", StringComparison.OrdinalIgnoreCase) >= 0)
            return 5;

        return 1;
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

    static bool TryMatchObject(GameObject go, out int slot)
    {
        slot = -1;
        string n = go.name;
        if (string.IsNullOrEmpty(n))
            return false;

        for (int i = 0; i < SlotHeaders.Length; i++)
        {
            if (string.Equals(n, SlotHeaders[i], StringComparison.OrdinalIgnoreCase))
            {
                slot = i;
                return true;
            }
        }

        // 场景里的车多半叫 hero_red / hero_blue，和车位名 red_hero / blue_hero 不是同一个字符串。
        if (n.IndexOf("hero_red", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            slot = 0;
            return true;
        }

        if (n.IndexOf("hero_blue", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            slot = 5;
            return true;
        }

        bool infantry = NameHasInfantry(n);
        bool engineer = n.IndexOf("engineer", StringComparison.OrdinalIgnoreCase) >= 0;
        bool hero = n.IndexOf("hero", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!infantry && !engineer && !hero)
            return false;

        if (!TryResolveSide(go, out RobotTeam side))
            return false;

        int baseIndex = side == RobotTeam.Blue ? 5 : 0;

        if (infantry)
        {
            int index = DetectInfantryIndex(n);
            if (index >= 1)
            {
                slot = baseIndex + index;
                return true;
            }
        }

        if (engineer)
        {
            slot = baseIndex + 4;
            return true;
        }

        if (hero)
        {
            slot = baseIndex;
            return true;
        }

        return false;
    }

    // 父级或自己的 red/blue Tag 优先；没有 Tag 时看名字里带不带 red / blue。
    static bool TryResolveSide(GameObject go, out RobotTeam side)
    {
        if (CombatDamage.TryGetFactionTeam(go.transform, out RobotTeam tagged)
            && (tagged == RobotTeam.Red || tagged == RobotTeam.Blue))
        {
            side = tagged;
            return true;
        }

        Transform t = go.transform;
        while (t != null)
        {
            if (NameHasSide(t.name, out side))
                return true;
            t = t.parent;
        }

        side = RobotTeam.Neutral;
        return false;
    }

    // 步兵、Infantry、平衡步兵都算步兵。英雄名字里没有这些字，不会被收成步兵。
    public static bool NameHasInfantry(string n)
    {
        if (string.IsNullOrEmpty(n))
            return false;
        if (n.IndexOf("infantry", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return n.IndexOf("步兵", StringComparison.Ordinal) >= 0;
    }

    static bool NameHasSide(string n, out RobotTeam side)
    {
        side = RobotTeam.Neutral;
        if (string.IsNullOrEmpty(n))
            return false;

        bool red = n.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("红", StringComparison.Ordinal) >= 0;
        bool blue = n.IndexOf("blue", StringComparison.OrdinalIgnoreCase) >= 0
            || n.IndexOf("蓝", StringComparison.Ordinal) >= 0;
        if (red && !blue)
        {
            side = RobotTeam.Red;
            return true;
        }

        if (blue && !red)
        {
            side = RobotTeam.Blue;
            return true;
        }

        return false;
    }

    // 名字里的整数。infantry_1、步兵1 算 1 号；infantry_10 不是 1 号。
    // 没有数字的「步兵」「Infantry」「平衡步兵」当成 1 号：场上每边只有一台时就是这台。
    static int DetectInfantryIndex(string name)
    {
        int best = 0;
        int bestDist = int.MaxValue;
        int role = name.IndexOf("infantry", StringComparison.OrdinalIgnoreCase);
        if (role < 0)
            role = name.IndexOf("步兵", StringComparison.Ordinal);
        bool anyDigit = false;
        int i = 0;
        while (i < name.Length)
        {
            if (!char.IsDigit(name[i]))
            {
                i++;
                continue;
            }

            anyDigit = true;
            int start = i;
            int value = 0;
            while (i < name.Length && char.IsDigit(name[i]))
            {
                value = value * 10 + (name[i] - '0');
                i++;
            }

            if (value < 1 || value > 3)
                continue;

            int dist = role >= 0 ? Mathf.Abs(start - role) : start;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = value;
            }
        }

        if (best >= 1)
            return best;
        if (!anyDigit && NameHasInfantry(name))
            return 1;
        return 0;
    }
}
