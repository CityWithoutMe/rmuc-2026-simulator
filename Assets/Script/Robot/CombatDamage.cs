using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// 弹丸命中伤害。超热量惩罚不在这里：V2.2.0 第 5.1.3 节改为锁定发射机构，不扣血。
//
// 阵营 Tag 用小写 "red" / "blue"（ProjectSettings/TagManager.asset）。
// Tag 要打在车根上。英雄子网格很多，子弹打在子碰撞体上，从碰撞体往父级找这个 Tag 或 RobotAttributeManager。
public static class CombatDamage
{
    public const string TagRed = "red";
    public const string TagBlue = "blue";

    // 一步步算完的结果，供命中时打日志。防御已是 Clamp01 之后、真正乘进公式的那个数。
    public struct BulletDamage
    {
        public float baseDamage;
        public float attackBuffPercent;
        public float defenseBuffPercent;
        public float finalDamage;
    }

    // 最终伤害 = 基础伤害 × (1 + 攻击方攻击加成) × (1 - 防守方防御加成)
    //
    //   基础伤害：42mm 仍用攻击方 GetCurrent(Damage)（英雄默认 100）。
    //             17mm 按表 5-2 机器人装甲模块为 20（手册不是 10）。
    //             42mm 的 GetCurrent 已经包含贴在 Damage 上的修饰器，这里不再乘第二次。
    //   攻击加成 = 攻击方 GetCurrent(AttackBuffPercent)。0.5 表示 +50%，乘的是 (1 + 0.5)。
    //   防御加成 = Clamp01(防守方 GetCurrent(DefenseBuffPercent))。0.5 表示只承受一半，乘的是 (1 - 0.5)。
    //             和 TakeDamage 一样先夹到 0~1，避免防御超过 100% 打出负数。
    //   攻击加成 ≤ -100% 时 (1 + 攻击加成) 按 0，不再打出负伤害。
    //
    // AddZoneBuff 若把同一个攻击百分比同时写进 Damage 和 AttackBuffPercent，两边都会进本公式。
    // 那是区域 Buff 的现有写法，这里不改它，也不再单独遍历 Damage 的修饰器列表。
    //
    // 例：红方英雄打蓝方英雄，两边加成都是 0
    //   最终 = 100 × (1 + 0) × (1 - 0) = 100，蓝方血量 200 → 100
    //
    // 调用方必须 defender.TakeDamage(最终伤害, ignoreDefense: true, suppressLog: true)。
    // TakeDamage 默认还会再乘一次 (1 - 防御)，防御已经在这里乘过，不能再乘。
    // 详细公式日志由 HandleBulletHit 打，suppressLog 避免同一发再打一行扣血日志。
    // 表 5-2：机器人装甲模块，17mm 弹丸原始伤害 20。42mm 仍走攻击方 Damage（英雄 100）。
    public const float RobotArmorDamage17mm = 20f;

    public static BulletDamage ComputeBulletDamage(RobotAttributeManager attacker, RobotAttributeManager defender, bool is42mm = true)
    {
        float baseDamage = 0f;
        float attackBuff = 0f;
        if (attacker != null)
        {
            baseDamage = is42mm
                ? Mathf.Max(0f, attacker.GetCurrent(RobotStat.Damage))
                : RobotArmorDamage17mm;
            attackBuff = attacker.GetCurrent(RobotStat.AttackBuffPercent);
        }

        float defense = 0f;
        if (defender != null)
            defense = Mathf.Min(1f, defender.GetCurrent(RobotStat.DefenseBuffPercent));

        float attackFactor = Mathf.Max(0f, 1f + attackBuff);
        float defenseFactor = 1f - defense;
        float finalDamage = baseDamage * attackFactor * defenseFactor;

        return new BulletDamage
        {
            baseDamage = baseDamage,
            attackBuffPercent = attackBuff,
            defenseBuffPercent = defense,
            finalDamage = finalDamage
        };
    }

    // 子弹碰到碰撞体时调用一次。同阵营、打到自己、打到场地：不扣血、不打日志。敌对才结算并 Debug.Log。
    // is42mm：弹种。前哨站按表 5-2 固定扣血，不走下面的机器人公式。
    public static void HandleBulletHit(Collider hit, GameObject attackerObject, RobotAttributeManager attacker, bool is42mm = false)
    {
        if (attacker == null && attackerObject != null)
            attacker = PlayerAttributeBinding.Resolve(attackerObject);

        RobotTeam hitTeam = ResolveTeam(attackerObject, attacker, attacker != null ? attacker.team : RobotTeam.Neutral);
        // 打在能量机关靶上：按六环结算激活，不走机器人扣血。
        if (PowerRuneActivator.TryHit(hit, hitTeam))
            return;

        // 打在哨塔 target 或其任意子物体上：只有对立阵营扣血。同阵营不扣，调用方仍销毁子弹。
        float outpostRed = OutpostHealth.RedHp;
        float outpostBlue = OutpostHealth.BlueHp;
        if (OutpostHealth.TryAbsorbBullet(hit, hitTeam, is42mm))
        {
            float dropped = HpDropped(outpostRed, outpostBlue, OutpostHealth.RedHp, OutpostHealth.BlueHp);
            MatchOutcome.AddAttackDamage(hitTeam, dropped);
            GrantStructureExperience(attacker, dropped, false);
            return;
        }

        // 打在基地任意碰撞体上：对立阵营且该方前哨站已击毁才扣血。同阵营不扣，调用方仍销毁子弹。
        // 护盾和血量分开记。这里只比较血量，护盾扣完、无敌、同阵营时血量不变，不加经验。
        float baseRed = BaseHealth.RedHp;
        float baseBlue = BaseHealth.BlueHp;
        float shieldRed = BaseHealth.ShieldOf(RobotTeam.Red);
        float shieldBlue = BaseHealth.ShieldOf(RobotTeam.Blue);
        if (BaseHealth.TryAbsorbBullet(hit, hitTeam, is42mm))
        {
            float dropped = HpDropped(baseRed, baseBlue, BaseHealth.RedHp, BaseHealth.BlueHp);
            float shieldDropped = HpDropped(
                shieldRed,
                shieldBlue,
                BaseHealth.ShieldOf(RobotTeam.Red),
                BaseHealth.ShieldOf(RobotTeam.Blue));
            // 5.5.1：护盾扣除计入对方造成的总伤害，但不算基地血量损失，经验仍只看血量。
            MatchOutcome.AddAttackDamage(hitTeam, dropped + shieldDropped);
            GrantStructureExperience(attacker, dropped, true);
            return;
        }

        if (!TryFindDefender(hit, out RobotAttributeManager defender, out RobotTeam defenderTeam))
            return;

        if (IsSelf(hit, attackerObject, attacker, defender))
            return;

        RobotTeam attackerTeam = ResolveTeam(attackerObject, attacker, attacker != null ? attacker.team : RobotTeam.Neutral);
        if (!IsEnemy(attackerTeam, defenderTeam))
            return;

        BulletDamage damage = ComputeBulletDamage(attacker, defender, is42mm);
        float hpBefore = defender.Hp;
        // 防御已在 ComputeBulletDamage 里乘过，这里必须跳过 TakeDamage 的第二次防御。
        // 下面这条公式日志已经写明血量，suppressLog 不再让 TakeDamage 打第二条。
        defender.TakeDamage(damage.finalDamage, ignoreDefense: true, suppressLog: true);
        float hpAfter = defender.Hp;
        float hpLost = hpBefore - hpAfter;
        MatchOutcome.AddAttackDamage(attackerTeam, hpLost);
        if (hpLost > 0f && attacker != null)
        {
            attacker.GrantFlatExperience(
                RobotLevelRules.RobotDamageExperience(hpLost),
                "攻击机器人 伤害" + Num(hpLost));
        }

        if (hpBefore > 0f && !defender.IsAlive && attacker != null)
            attacker.GrantKillExperience(defender);

        Debug.Log(
            "[伤害] " + ActorLabel(attackerTeam, attacker) + " -> " + ActorLabel(defenderTeam, defender)
            + " 基础" + Num(damage.baseDamage)
            + " 攻击加成" + Num(damage.attackBuffPercent)
            + " 防御" + Num(damage.defenseBuffPercent)
            + " 最终" + Num(damage.finalDamage)
            + " 目标血量" + Num(hpBefore) + "->" + Num(hpAfter),
            defender);
    }

    // 发射者阵营：自己、父级、子级上的 red/blue Tag 优先，没有再用属性上的 team。
    public static RobotTeam ResolveTeam(GameObject go, RobotAttributeManager attr, RobotTeam fallback)
    {
        if (go != null && TryGetFactionTeam(go.transform, out RobotTeam tagged))
            return tagged;
        if (attr != null && attr.gameObject != go && TryGetFactionTeam(attr.transform, out tagged))
            return tagged;
        if (go != null && TryNamedHeroTeam(go.transform, out RobotTeam named))
            return named;
        if (attr != null && TryNamedHeroTeam(attr.transform, out named))
            return named;
        if (attr != null && go == null)
            return attr.team;
        return fallback;
    }

    // 已有属性组件时，只按 Tag / hero_blue 名字把 team 对齐，不重刷血量和弹药。
    public static void ApplyTeamFromTags(RobotAttributeManager attr, GameObject hint)
    {
        if (attr == null)
            return;

        GameObject go = hint != null ? hint : attr.gameObject;
        attr.team = ResolveTeam(go, attr, attr.team);
    }

    public static bool NameContainsHeroBlue(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.IndexOf("hero_blue", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool NameContainsHeroRed(string name)
    {
        return !string.IsNullOrEmpty(name)
            && name.IndexOf("hero_red", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // 自己或父级名字能定队伍：hero_blue / hero_red，或步兵名字里的 red / blue。Tag 已经判过的不要再进这里。
    static bool TryNamedHeroTeam(Transform start, out RobotTeam team)
    {
        Transform t = start;
        while (t != null)
        {
            if (TryTeamFromVehicleName(t.name, out team))
                return true;
            t = t.parent;
        }

        team = RobotTeam.Neutral;
        return false;
    }

    static bool TryTeamFromVehicleName(string name, out RobotTeam team)
    {
        team = RobotTeam.Neutral;
        if (string.IsNullOrEmpty(name))
            return false;

        if (NameContainsHeroBlue(name))
        {
            team = RobotTeam.Blue;
            return true;
        }

        if (NameContainsHeroRed(name))
        {
            team = RobotTeam.Red;
            return true;
        }

        bool infantry = name.IndexOf("infantry", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("步兵", StringComparison.Ordinal) >= 0;
        if (!infantry)
            return false;

        bool red = name.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0;
        bool blue = name.IndexOf("blue", StringComparison.OrdinalIgnoreCase) >= 0;
        if (red && !blue)
        {
            team = RobotTeam.Red;
            return true;
        }

        if (blue && !red)
        {
            team = RobotTeam.Blue;
            return true;
        }

        return false;
    }

    // 场景里的蓝方英雄：没有属性组件就补上。不挂玩家控制，也不把能量机关当成车。
    // 由 MatchSceneBind 在 SampleScene 加载后再调用。主菜单是第一个场景时，AfterSceneLoad 只会空跑一次。
    public static void EnsureBlueHeroes()
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var hosts = new HashSet<GameObject>();

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || IsUnderRune(t))
                continue;

            bool taggedBlue = TryReadTag(t.gameObject, out RobotTeam tagTeam) && tagTeam == RobotTeam.Blue;
            bool namedBlue = NameContainsHeroBlue(t.name);
            if (!taggedBlue && !namedBlue)
                continue;

            Transform host = BlueHost(t);
            if (host == null || IsUnderRune(host))
                continue;
            // 根上已经是红方 Tag 时，不要因为子物体名字把它收成蓝方。
            if (TryReadTag(host.gameObject, out RobotTeam hostTag) && hostTag == RobotTeam.Red)
                continue;
            if (!hosts.Add(host.gameObject))
                continue;

            // 十车总控在时，蓝英雄读 blue_hero。不在车上再挂一份，也不用默认值盖掉检视器里的数。
            if (!PlayerAttributeBinding.HasRoster)
            {
                RobotAttributeManager attr = host.GetComponent<RobotAttributeManager>();
                if (attr == null)
                {
                    attr = host.gameObject.AddComponent<RobotAttributeManager>();
                    attr.team = RobotTeam.Blue;
                    attr.robotType = RobotType.Hero;
                    attr.ApplyRobotTypeDefaults();
                }
                else if (taggedBlue || namedBlue)
                {
                    // 已经有组件：只纠正阵营，不动已经写好的血量、热量、弹药。
                    attr.team = RobotTeam.Blue;
                }
            }

            // 蓝车原先 0 个碰撞体，子弹和红车都穿过去。补胶囊 + 动态刚体。
            EnsureBlockingBody(host.gameObject);
        }

        Transform[] roots = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < roots.Length; i++)
        {
            Transform t = roots[i];
            if (t == null || t.parent != null)
                continue;
            if (t.name.IndexOf("hero_red", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            EnsureBlockingBody(t.gameObject);
        }
    }

    // 名字对得上的步兵补碰撞，子弹才能打中。属性仍读车位，不在车上再挂一份。
    public static void EnsureInfantryBodies()
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var hosts = new HashSet<GameObject>();
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || IsUnderRune(t) || PlayerMovement.IsUnderAttributeBoard(t.gameObject))
                continue;
            if (!TenRobotAttributeBoard.TryMatchSlot(t.gameObject, out int slot))
                continue;
            if (slot <= 0 || slot == 4 || slot == 5 || slot == 9)
                continue;

            if (!hosts.Add(t.gameObject))
                continue;

            if (t.gameObject.tag == "Untagged")
            {
                bool blue = slot >= 5;
                t.gameObject.tag = blue ? TagBlue : TagRed;
            }

            EnsureBlockingBody(t.gameObject);
        }
    }

    // 车和车、子弹和车都靠根上的盒子/胶囊。非凸网格互不相撞，蓝车之前一个碰撞体都没有。
    static void EnsureBlockingBody(GameObject hero)
    {
        if (hero == null)
            return;

        Collider[] cols = hero.GetComponentsInChildren<Collider>(true);
        bool hasPrimitive = false;
        for (int i = 0; i < cols.Length; i++)
        {
            Collider c = cols[i];
            if (c == null || !c.enabled || c.isTrigger)
                continue;
            if (c is BoxCollider || c is CapsuleCollider || c is SphereCollider)
                hasPrimitive = true;
        }

        if (!hasPrimitive)
        {
            Bounds b = new Bounds(hero.transform.position, Vector3.one * 0.5f);
            bool hasBound = false;
            Renderer[] rends = hero.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null || !rends[i].enabled)
                    continue;
                if (!hasBound)
                {
                    b = rends[i].bounds;
                    hasBound = true;
                }
                else
                {
                    b.Encapsulate(rends[i].bounds);
                }
            }

            CapsuleCollider cap = hero.AddComponent<CapsuleCollider>();
            cap.isTrigger = false;
            Vector3 localCenter = hero.transform.InverseTransformPoint(b.center);
            cap.center = localCenter;
            float scaleY = Mathf.Max(Mathf.Abs(hero.transform.lossyScale.y), 0.0001f);
            float scaleX = Mathf.Max(Mathf.Abs(hero.transform.lossyScale.x), 0.0001f);
            cap.height = Mathf.Max(0.4f, b.size.y / scaleY);
            float radius = 0.5f * Mathf.Max(b.size.x, b.size.z) / scaleX;
            cap.radius = Mathf.Max(0.15f, radius * 0.45f);
            if (cap.height < cap.radius * 2f)
                cap.height = cap.radius * 2f;
        }

        Rigidbody rb = hero.GetComponent<Rigidbody>();
        if (rb == null)
            rb = hero.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        if (hero.GetComponent<RobotCollisionDamage>() == null)
            hero.AddComponent<RobotCollisionDamage>();
    }

    // 自己和父级的 Tag 优先；车根没打 Tag、只打在子网格上时，再看子级。红蓝同时出现则不猜。
    public static bool TryGetFactionTeam(Transform start, out RobotTeam team)
    {
        Transform t = start;
        while (t != null)
        {
            if (TryReadTag(t.gameObject, out team))
                return true;
            t = t.parent;
        }

        if (start == null)
        {
            team = RobotTeam.Neutral;
            return false;
        }

        bool sawRed = false;
        bool sawBlue = false;
        Transform[] kids = start.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < kids.Length; i++)
        {
            if (kids[i] == start)
                continue;
            if (!TryReadTag(kids[i].gameObject, out RobotTeam childTeam))
                continue;
            if (childTeam == RobotTeam.Red)
                sawRed = true;
            else if (childTeam == RobotTeam.Blue)
                sawBlue = true;
        }

        if (sawRed && !sawBlue)
        {
            team = RobotTeam.Red;
            return true;
        }

        if (sawBlue && !sawRed)
        {
            team = RobotTeam.Blue;
            return true;
        }

        team = RobotTeam.Neutral;
        return false;
    }

    static bool TryFindDefender(Collider col, out RobotAttributeManager defender, out RobotTeam team)
    {
        defender = null;
        team = RobotTeam.Neutral;
        if (col == null)
            return false;

        bool foundTag = false;
        RobotTeam tagTeam = RobotTeam.Neutral;
        Transform t = col.transform;
        while (t != null)
        {
            if (!foundTag && TryReadTag(t.gameObject, out RobotTeam tt))
            {
                foundTag = true;
                tagTeam = tt;
            }

            if (!foundTag && TryTeamFromVehicleName(t.name, out RobotTeam namedTeam))
            {
                foundTag = true;
                tagTeam = namedTeam;
            }

            if (defender == null)
            {
                RobotAttributeManager attr = t.GetComponent<RobotAttributeManager>();
                if (attr != null)
                    defender = attr;
            }

            t = t.parent;
        }

        // 十车总控在时，红蓝英雄、对得上名字的步兵和工程都读车位。没有这种车就不造。
        if (PlayerAttributeBinding.TryResolveRoster(col.gameObject, out RobotAttributeManager rosterStats))
        {
            if (rosterStats == null)
                return false;

            defender = rosterStats;
            team = foundTag ? tagTeam : defender.team;
            return true;
        }

        // 没有总控时：红方玩家的血在场景「属性管理器」上。蓝车仍用自己身上的组件。
        if (PlayerAttributeBinding.IsRedPlayerHierarchy(col.transform))
        {
            RobotAttributeManager playerStats = PlayerAttributeBinding.Resolve(col.gameObject);
            if (playerStats != null)
                defender = playerStats;
        }

        if (defender == null)
            return false;

        // 被击中物体：父链上的 red/blue 优先，没有再用属性里的 team。
        team = foundTag ? tagTeam : defender.team;
        return true;
    }

    static bool IsSelf(Collider hit, GameObject attackerObject, RobotAttributeManager attacker, RobotAttributeManager defender)
    {
        if (attacker != null && defender == attacker)
            return true;

        Transform hitTransform = hit != null ? hit.transform : null;
        if (hitTransform == null)
            return false;

        if (SharesHierarchy(hitTransform, attackerObject != null ? attackerObject.transform : null))
            return true;
        if (attacker != null && SharesHierarchy(hitTransform, attacker.transform))
            return true;
        return false;
    }

    static bool SharesHierarchy(Transform a, Transform b)
    {
        if (a == null || b == null)
            return false;
        return a == b || a.IsChildOf(b) || b.IsChildOf(a);
    }

    static bool IsEnemy(RobotTeam attacker, RobotTeam defender)
    {
        if (attacker == defender)
            return false;
        bool attackerSide = attacker == RobotTeam.Red || attacker == RobotTeam.Blue;
        bool defenderSide = defender == RobotTeam.Red || defender == RobotTeam.Blue;
        return attackerSide && defenderSide;
    }

    // 蓝 Tag 或名字沿父级收到车根。父级既不是蓝 Tag 也不是 hero_blue 就停，避免把整块场地收成一辆车。
    static Transform BlueHost(Transform start)
    {
        Transform host = start;
        Transform parent = start.parent;
        while (parent != null && !IsUnderRune(parent))
        {
            bool parentBlue = TryReadTag(parent.gameObject, out RobotTeam team) && team == RobotTeam.Blue;
            if (!parentBlue && !NameContainsHeroBlue(parent.name))
                break;
            host = parent;
            parent = parent.parent;
        }

        return host;
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

    static bool TryReadTag(GameObject go, out RobotTeam team)
    {
        team = RobotTeam.Neutral;
        if (go == null)
            return false;

        // 读 tag 字符串，不调用 CompareTag（标签未登记时 CompareTag 会抛异常）。
        string tag = go.tag;
        if (tag == TagRed)
        {
            team = RobotTeam.Red;
            return true;
        }

        if (tag == TagBlue)
        {
            team = RobotTeam.Blue;
            return true;
        }

        return false;
    }

    static string ActorLabel(RobotTeam team, RobotAttributeManager attr)
    {
        return TeamLabel(team) + TypeLabel(attr != null ? attr.robotType : RobotType.Infantry, attr == null);
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

    static string TypeLabel(RobotType type, bool unknown)
    {
        if (unknown)
            return "单位";

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
            default: return "单位";
        }
    }

    static float HpDropped(float redBefore, float blueBefore, float redAfter, float blueAfter)
    {
        return Mathf.Max(0f, redBefore - redAfter) + Mathf.Max(0f, blueBefore - blueAfter);
    }

    static void GrantStructureExperience(RobotAttributeManager attacker, float hpLost, bool isBase)
    {
        if (attacker == null || hpLost <= 0f)
            return;

        float gain = isBase
            ? RobotLevelRules.BaseArmorExperience(hpLost)
            : RobotLevelRules.OutpostArmorExperience(hpLost);
        string source = isBase ? "攻击基地" : "攻击前哨站";
        attacker.GrantFlatExperience(gain, source + " 伤害" + Num(hpLost));
    }

    static string Num(float value)
    {
        float rounded = Mathf.Round(value);
        if (Mathf.Abs(value - rounded) < 0.001f)
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
