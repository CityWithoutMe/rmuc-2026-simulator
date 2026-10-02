using System;
using System.Collections.Generic;
using UnityEngine;

// 基地血量。运行时按名字找「基地_red / 基地_blue」，不改场景，不新建碰撞体。
// 命中该根或其任意子碰撞体都算打中该基地（往父级找到基地根）。
//
// 手册 V2.2.0（20260807）5.5.1：基地血量 5000，开局 150 虚拟护盾。
// 虚拟护盾先扣，护盾扣除不算基地血量损失。
// 5.5.1：当一方前哨站存活，基地处于无敌状态。前哨站血量直接读 OutpostHealth。
// 表 5-2「基地装甲模块」：42mm 200；17mm 分「上方前装甲模块：5」和「其余 5 块装甲模块：20」。
// 任意部位都按可被弹丸攻击的主装甲结算，17mm 取其余 5 块那一档 20。
// 同一行飞镖、撞击为「-」，不实现。基地飞镖检测模块一行的飞镖伤害也不做。
// 不做 5.5.1 的重建，不做 10mm 暴击区。
public class BaseHealth : MonoBehaviour
{
    public const float MaxHp = 5000f;
    public const float InitialShield = 150f;
    public const float Damage42mm = 200f;
    public const float Damage17mm = 20f;

    const float ReasonLogInterval = 2f;

    public static float RedHp { get; private set; } = MaxHp;
    public static float BlueHp { get; private set; } = MaxHp;
    public static float RedShield { get; private set; } = InitialShield;
    public static float BlueShield { get; private set; } = InitialShield;
    public static float RedLowestHp { get; private set; } = MaxHp;
    public static float BlueLowestHp { get; private set; } = MaxHp;
    public static bool RedArmorOpen { get; private set; }
    public static bool BlueArmorOpen { get; private set; }

    struct Root
    {
        public Transform root;
        public RobotTeam team;
    }

    static readonly List<Root> roots = new List<Root>();
    static readonly Dictionary<string, float> reasonLogAt = new Dictionary<string, float>();
    static bool redDestroyedLogged;
    static bool blueDestroyedLogged;

    public static float LowestHp(RobotTeam team)
    {
        if (team == RobotTeam.Red)
            return RedLowestHp;
        if (team == RobotTeam.Blue)
            return BlueLowestHp;
        return 0f;
    }

    public static bool ArmorOpen(RobotTeam team)
    {
        if (team == RobotTeam.Red)
            return RedArmorOpen;
        if (team == RobotTeam.Blue)
            return BlueArmorOpen;
        return false;
    }

    // 5.5.3.9：单次占领对方堡垒计满 20 秒，对方基地护甲展开。不改哨塔转速。
    public static void OpenArmor(RobotTeam team)
    {
        if (team == RobotTeam.Red)
        {
            if (RedArmorOpen)
                return;
            RedArmorOpen = true;
            Debug.Log("红方基地护甲展开");
            return;
        }

        if (team == RobotTeam.Blue)
        {
            if (BlueArmorOpen)
                return;
            BlueArmorOpen = true;
            Debug.Log("蓝方基地护甲展开");
        }
    }

    public static void BindForLoadedMatch()
    {
        RedHp = MaxHp;
        BlueHp = MaxHp;
        RedShield = InitialShield;
        BlueShield = InitialShield;
        RedLowestHp = MaxHp;
        BlueLowestHp = MaxHp;
        RedArmorOpen = false;
        BlueArmorOpen = false;
        redDestroyedLogged = false;
        blueDestroyedLogged = false;
        roots.Clear();
        reasonLogAt.Clear();

        if (FindAnyObjectByType<BaseHealth>() == null)
        {
            GameObject host = new GameObject("BaseHealth");
            host.AddComponent<BaseHealth>();
        }

        CollectRoots();
    }

    static void CollectRoots()
    {
        bool sawRed = false;
        bool sawBlue = false;

        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (candidate == null || !IsBaseRootName(candidate.name))
                continue;
            if (!TryTeam(candidate, out RobotTeam team))
                continue;

            if (team == RobotTeam.Red)
                sawRed = true;
            else
                sawBlue = true;

            roots.Add(new Root { root = candidate, team = team });
            int colliders = candidate.GetComponentsInChildren<Collider>(true).Length;
            Debug.Log(
                "BaseHealth：" + TeamLabel(team) + " " + PathOf(candidate)
                + " 碰撞体 " + colliders
                + " 血量 " + (int)MaxHp + "/" + (int)MaxHp
                + " 护盾 " + (int)InitialShield,
                candidate);
            if (colliders == 0)
                Debug.LogWarning("BaseHealth：" + TeamLabel(team) + "基地根下没有碰撞体，暂时打不中", candidate);
        }

        if (!sawRed)
            Debug.LogWarning("BaseHealth：没有找到名字含「基地」且能判断为红方的根物体，红方基地暂时打不中");
        if (!sawBlue)
            Debug.LogWarning("BaseHealth：没有找到名字含「基地」且能判断为蓝方的根物体，蓝方基地暂时打不中");
    }

    // 「基地增益点」是增益区，不是基地根。
    static bool IsBaseRootName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        if (name.IndexOf("基地", StringComparison.Ordinal) < 0)
            return false;
        if (name.IndexOf("增益", StringComparison.Ordinal) >= 0)
            return false;
        return true;
    }

    // 打在基地根或其子碰撞体上时返回 true。调用方仍销毁子弹。
    // 只有对立阵营、且该方前哨站血量已为 0，才扣护盾或血量。
    public static bool TryAbsorbBullet(Collider hit, RobotTeam attackerTeam, bool is42mm)
    {
        if (!TryBase(hit, out RobotTeam baseTeam))
            return false;

        if (attackerTeam != RobotTeam.Red && attackerTeam != RobotTeam.Blue)
            return true;
        if (attackerTeam == baseTeam)
            return true;

        if (OutpostHealth.HpOf(baseTeam) > 0f)
        {
            LogReason(
                TeamLabel(baseTeam) + "基地处于无敌：该方前哨站仍存活（手册 5.5.1）");
            return true;
        }

        float damage = is42mm ? Damage42mm : Damage17mm;
        ApplyDamage(baseTeam, damage);
        return true;
    }

    static bool TryBase(Collider hit, out RobotTeam team)
    {
        team = RobotTeam.Neutral;
        if (hit == null)
            return false;

        Transform cursor = hit.transform;
        while (cursor != null)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                Root root = roots[i];
                if (root.root == null)
                    continue;
                if (cursor != root.root)
                    continue;
                team = root.team;
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
            if (RedHp <= 0f && RedShield <= 0f)
                return;
            float remain = SpendShield(refRed: true, damage);
            if (remain <= 0f || RedHp <= 0f)
                return;
            RedHp = Mathf.Max(0f, RedHp - remain);
            if (RedHp < RedLowestHp)
                RedLowestHp = RedHp;
            if (RedHp <= 0f && !redDestroyedLogged)
            {
                redDestroyedLogged = true;
                Debug.Log("红方基地被击毁");
            }
            return;
        }

        if (BlueHp <= 0f && BlueShield <= 0f)
            return;
        float left = SpendShield(refRed: false, damage);
        if (left <= 0f || BlueHp <= 0f)
            return;
        BlueHp = Mathf.Max(0f, BlueHp - left);
        if (BlueHp < BlueLowestHp)
            BlueLowestHp = BlueHp;
        if (BlueHp <= 0f && !blueDestroyedLogged)
        {
            blueDestroyedLogged = true;
            Debug.Log("蓝方基地被击毁");
        }
    }

    static float SpendShield(bool refRed, float damage)
    {
        if (refRed)
        {
            if (RedShield <= 0f)
                return damage;
            float take = Mathf.Min(RedShield, damage);
            RedShield -= take;
            return damage - take;
        }

        if (BlueShield <= 0f)
            return damage;
        float used = Mathf.Min(BlueShield, damage);
        BlueShield -= used;
        return damage - used;
    }

    static void LogReason(string reason)
    {
        float now = Time.unscaledTime;
        if (reasonLogAt.TryGetValue(reason, out float last) && now - last < ReasonLogInterval)
            return;
        reasonLogAt[reason] = now;
        Debug.Log(reason);
    }

    public static float HpOf(RobotTeam team)
    {
        if (team == RobotTeam.Red)
            return RedHp;
        if (team == RobotTeam.Blue)
            return BlueHp;
        return 0f;
    }

    public static float ShieldOf(RobotTeam team)
    {
        if (team == RobotTeam.Red)
            return RedShield;
        if (team == RobotTeam.Blue)
            return BlueShield;
        return 0f;
    }

    static bool TryTeam(Transform root, out RobotTeam team)
    {
        if (TryTeamFromName(root.name, out team))
            return true;

        string tag = root.tag;
        if (tag == CombatDamage.TagRed)
        {
            team = RobotTeam.Red;
            return true;
        }

        if (tag == CombatDamage.TagBlue)
        {
            team = RobotTeam.Blue;
            return true;
        }

        team = RobotTeam.Neutral;
        return false;
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
