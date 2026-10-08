using System;
using System.Collections.Generic;
using UnityEngine;

// 前哨站（场景名「哨塔」）血量。运行时按名字找，不改场景。
//
// 手册 V2.2.0（20260807）5.5.1 基地及前哨站机制：前哨站血量为 1500。
// 表 5-2 攻击伤害或撞击的扣血机制（5.1.1）：前哨站中部装甲模块，
//   42mm 弹丸 200，17mm 弹丸 20，飞镖与撞击对中部装甲为「-」。
// 表中原始伤害乘攻击方增益与本方能量机关防御，再四舍五入。
// 受击点是哨塔下的 target，以及它的全部子物体（含以前的 aiming）。
// 5.5.1 的 10mm×10mm 暴击区（150% 攻击增益）不套到整块 target 上。
// 5.5.1：累计基地掉血获取重建机会；五分钟前连续检测己方前哨点，重建为 750 HP。
public class OutpostHealth : MonoBehaviour
{
    public const float MaxHp = 1500f;
    public const float Damage42mm = 200f;
    public const float Damage17mm = 20f;

    public static float RedHp { get; private set; } = MaxHp;
    public static float BlueHp { get; private set; } = MaxHp;
    public static bool RedEverDestroyed { get; private set; }
    public static bool BlueEverDestroyed { get; private set; }
    static OutpostRebuildLedger redRebuild = new OutpostRebuildLedger(), blueRebuild = new OutpostRebuildLedger();
    static int clientRedRebuild, clientBlueRebuild;
    readonly Dictionary<RobotAttributeManager, float> rebuildProgress = new Dictionary<RobotAttributeManager, float>();
    RobotAttributeManager[] robots;
    public static int RebuildChances(RobotTeam team) => LanSession.IsClient
        ? (team == RobotTeam.Red ? clientRedRebuild : clientBlueRebuild)
        : (team == RobotTeam.Red ? redRebuild.Available : blueRebuild.Available);

    struct Plate
    {
        public Transform target;
        public RobotTeam team;
    }

    static readonly List<Plate> plates = new List<Plate>();
    static bool redDestroyedLogged;
    static bool blueDestroyedLogged;

    public static void BindForLoadedMatch()
    {
        RedHp = MaxHp;
        BlueHp = MaxHp;
        RedEverDestroyed = false;
        BlueEverDestroyed = false;
        redRebuild = new OutpostRebuildLedger(); blueRebuild = new OutpostRebuildLedger();
        redDestroyedLogged = false;
        blueDestroyedLogged = false;
        plates.Clear();

        if (FindAnyObjectByType<OutpostHealth>() == null)
        {
            GameObject host = new GameObject("OutpostHealth");
            host.AddComponent<OutpostHealth>();
        }

        CollectPlates();
        var instance = FindAnyObjectByType<OutpostHealth>();
        instance.rebuildProgress.Clear();
        instance.robots = FindObjectsByType<RobotAttributeManager>(FindObjectsSortMode.None);
    }

    public static void ApplyLanState(LanSnapshot state)
    {
        if (!LanSession.IsClient) return;
        RedHp = state.redOutpost; BlueHp = state.blueOutpost;
        RedEverDestroyed = state.redOutpostDestroyed; BlueEverDestroyed = state.blueOutpostDestroyed;
        clientRedRebuild = state.redRebuildChances; clientBlueRebuild = state.blueRebuildChances;
    }

    public static void RecordBaseDamage(RobotTeam team, float damage)
    {
        if (!LanSession.CanSimulate || MatchOutcome.Decided) return;
        if (team == RobotTeam.Red) redRebuild.RecordDamage(damage);
        else if (team == RobotTeam.Blue) blueRebuild.RecordDamage(damage);
    }

    void Update()
    {
        if (!LanSession.CanSimulate || MatchOutcome.Decided || robots == null) return;
        TickRebuild(Time.deltaTime);
    }

    void TickRebuild(float dt)
    {
        float elapsed = FindAnyObjectByType<MatchTimer>()?.ElapsedSeconds ?? 0f;
        foreach (var attr in robots)
        {
            if (attr == null) continue;
            bool type = attr.robotType == RobotType.Hero || attr.robotType == RobotType.Infantry
                || attr.robotType == RobotType.Engineer || attr.robotType == RobotType.Sentry;
            bool eligible = elapsed < 300 && type && attr.isActiveAndEnabled && attr.IsAlive
                && !attr.IsWeak && !attr.IsFoulOut && attr.IsPowered && HpOf(attr.team) <= 0
                && RebuildChances(attr.team) > 0 && FieldSupportZoneBuff.InOwnOutpost(attr);
            if (!eligible) { rebuildProgress.Remove(attr); continue; }
            rebuildProgress.TryGetValue(attr, out float progress);
            progress += dt;
            rebuildProgress[attr] = progress;
            float required = attr.robotType == RobotType.Engineer ? 5f : 10f;
            if (progress < required) continue;
            var ledger = attr.team == RobotTeam.Red ? redRebuild : blueRebuild;
            if (!ledger.Consume(elapsed, HpOf(attr.team) <= 0)) continue;
            if (attr.team == RobotTeam.Red) { RedHp = 750; redDestroyedLogged = false; }
            else { BlueHp = 750; blueDestroyedLogged = false; }
            rebuildProgress.Clear();
            Debug.Log("[前哨] " + TeamLabel(attr.team) + "重建成功，血量 750，剩余机会 " + RebuildChances(attr.team));
        }
    }

    public static string RebuildProgress(RobotAttributeManager attr)
    {
        var instance = FindAnyObjectByType<OutpostHealth>();
        if (instance == null || attr == null || !instance.rebuildProgress.TryGetValue(attr, out float progress)) return null;
        return "前哨重建 " + Mathf.FloorToInt(progress / (attr.robotType == RobotType.Engineer ? 5f : 10f) * 100) + "%";
    }

    static void CollectPlates()
    {
        bool sawRedTower = false;
        bool sawBlueTower = false;
        bool sawRedTarget = false;
        bool sawBlueTarget = false;

        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform tower = all[i];
            if (tower == null || tower.name.IndexOf("哨塔", StringComparison.Ordinal) < 0)
                continue;
            if (!TryTeamFromName(tower.name, out RobotTeam team))
                continue;

            if (team == RobotTeam.Red)
                sawRedTower = true;
            else
                sawBlueTower = true;

            Transform target = FindTarget(tower);
            if (target == null)
                continue;

            if (team == RobotTeam.Red)
                sawRedTarget = true;
            else
                sawBlueTarget = true;

            plates.Add(new Plate { target = target, team = team });
            Debug.Log(
                "OutpostHealth：" + TeamLabel(team) + " " + PathOf(target)
                + " 血量 " + (int)MaxHp + "/" + (int)MaxHp,
                target);
        }

        if (!sawRedTower)
            Debug.LogWarning("OutpostHealth：没有找到名字含「哨塔」且能判断为红方的根物体");
        else if (!sawRedTarget)
            Debug.LogWarning("OutpostHealth：红方哨塔下没有 target，红方哨塔暂时打不中");

        if (!sawBlueTower)
            Debug.LogWarning("OutpostHealth：没有找到名字含「哨塔」且能判断为蓝方的根物体");
        else if (!sawBlueTarget)
            Debug.LogWarning("OutpostHealth：蓝方哨塔下没有 target，蓝方哨塔暂时打不中");
    }

    // 哨塔根下名为 target 的物体。同一座哨塔里若有多块 target，认以前挂着 aiming 的那一块装甲。
    // 命中仍算整块 target 及其子物体。能量机关（挂了 RotationCenterSpin 的层级）整支跳过。
    static Transform FindTarget(Transform tower)
    {
        Transform[] all = tower.GetComponentsInChildren<Transform>(true);
        Transform fallback = null;
        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate == null || candidate == tower || candidate.name != "target")
                continue;
            if (IsUnderEnergyRune(candidate))
                continue;

            if (fallback == null)
                fallback = candidate;
            if (HasDirectChild(candidate, "aiming"))
                return candidate;
        }

        return fallback;
    }

    static bool HasDirectChild(Transform parent, string childName)
    {
        if (parent == null)
            return false;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == childName)
                return true;
        }

        return false;
    }

    static bool IsUnderEnergyRune(Transform t)
    {
        while (t != null)
        {
            if (t.GetComponent<RotationCenterSpin>() != null)
                return true;
            t = t.parent;
        }

        return false;
    }

    // 打在 target 本体或其任意子物体（含以前的 aiming）上时返回 true。调用方仍销毁子弹。
    // 只有对立阵营扣血：红打蓝、蓝打红。同阵营或中立不扣血。
    public static bool TryAbsorbBullet(Collider hit, RobotTeam attackerTeam, bool is42mm, RobotAttributeManager attacker = null)
    {
        if (!TryPlate(hit, out RobotTeam outpostTeam))
            return false;

        if (attackerTeam != RobotTeam.Red && attackerTeam != RobotTeam.Blue)
            return true;
        if (attackerTeam == outpostTeam)
            return true;
        if (is42mm && Hero42mmShield.IsBlocked(attackerTeam)) return true;

        float damage = is42mm ? Damage42mm : Damage17mm;
        if (attacker != null && attacker.team == attackerTeam)
            damage *= Mathf.Max(0f, 1f + attacker.GetCurrent(RobotStat.AttackBuffPercent));
        damage *= 1f - PowerRuneActivator.StructureDefense(outpostTeam);
        damage = Mathf.Floor(damage + .5f);
        ApplyDamage(outpostTeam, damage);
        return true;
    }

    static bool TryPlate(Collider hit, out RobotTeam team)
    {
        team = RobotTeam.Neutral;
        if (hit == null)
            return false;

        Transform cursor = hit.transform;
        while (cursor != null)
        {
            for (int i = 0; i < plates.Count; i++)
            {
                Plate plate = plates[i];
                if (plate.target == null)
                    continue;
                if (cursor != plate.target)
                    continue;
                team = plate.team;
                return true;
            }

            cursor = cursor.parent;
        }

        return false;
    }

    static void ApplyDamage(RobotTeam team, float damage)
    {
        if (damage <= 0f)
            return;

        if (team == RobotTeam.Red)
        {
            if (RedHp <= 0f)
                return;
            RedHp = Mathf.Max(0f, RedHp - damage);
            if (RedHp <= 0f && !redDestroyedLogged)
            {
                redDestroyedLogged = true;
                RedEverDestroyed = true;
                Debug.Log("红方哨塔被击毁");
            }
            return;
        }

        if (BlueHp <= 0f)
            return;
        BlueHp = Mathf.Max(0f, BlueHp - damage);
        if (BlueHp <= 0f && !blueDestroyedLogged)
        {
            blueDestroyedLogged = true;
            BlueEverDestroyed = true;
            Debug.Log("蓝方哨塔被击毁");
        }
    }

    public static bool EverDestroyed(RobotTeam team)
    {
        if (team == RobotTeam.Red)
            return RedEverDestroyed;
        if (team == RobotTeam.Blue)
            return BlueEverDestroyed;
        return false;
    }

    public static float HpOf(RobotTeam team)
    {
        if (team == RobotTeam.Red)
            return RedHp;
        if (team == RobotTeam.Blue)
            return BlueHp;
        return 0f;
    }

    static bool TryTeamFromName(string name, out RobotTeam team)
    {
        team = RobotTeam.Neutral;
        if (string.IsNullOrEmpty(name))
            return false;

        string lower = name.ToLowerInvariant();
        int redAt = MarkIndex(lower, "red");
        int blueAt = MarkIndex(lower, "blue");
        if (redAt < 0 && blueAt < 0)
            return false;
        team = blueAt > redAt ? RobotTeam.Blue : RobotTeam.Red;
        return true;
    }

    static int MarkIndex(string lower, string side)
    {
        int best = -1;
        int marked = lower.LastIndexOf("_" + side, StringComparison.Ordinal);
        if (marked >= 0)
            best = marked;
        if (lower.Equals(side, StringComparison.Ordinal) || lower.EndsWith(side, StringComparison.Ordinal))
        {
            int at = lower.Length - side.Length;
            if (at > best)
                best = at;
        }

        return best;
    }

    static string TeamLabel(RobotTeam team)
    {
        return team == RobotTeam.Blue ? "蓝方" : "红方";
    }

    static string PathOf(Transform t)
    {
        var names = new List<string>();
        while (t != null)
        {
            names.Add(t.name);
            t = t.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }
}
