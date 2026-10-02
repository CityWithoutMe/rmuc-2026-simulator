using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// 车与车撞击扣血。手册 V2.2.0（20260807）5.1.1 表 5-2：
//   机器人装甲模块，撞击列原始伤害 2。
//   基地装甲模块、前哨站中部装甲模块的撞击列是「-」，不扣。
// 不走弹丸公式。攻击增益只乘弹丸（表 5-19 注解）。
// 防御增益改变撞击伤害：原始伤害 × (1 - 防御增益)，再 TakeDamage(ignoreDefense: true)，避免乘两次。
// 同阵营不扣，与弹丸一致。5.4.1 里「来源为己方」的经验平分不在这里做。
// 镜头抖仍由 VehicleImpactRelay 转发，这里不改相机。
public class RobotCollisionDamage : MonoBehaviour
{
    // 表 5-2 没有速度分档，只有这一档。低于该相对速度视为轻碰，不扣。
    // 1.5 不是手册数字，只用来挡贴地、蹭一下。
    public const float MinRelativeSpeed = 1.5f;
    public const float RawDamage = 2f;
    public const float PairWindow = 0.3f;

    struct Side
    {
        public Rigidbody body;
        public RobotAttributeManager attr;
        public RobotTeam team;
    }

    struct PairKey : IEquatable<PairKey>
    {
        public int lo;
        public int hi;

        public PairKey(int a, int b)
        {
            if (a < b)
            {
                lo = a;
                hi = b;
            }
            else
            {
                lo = b;
                hi = a;
            }
        }

        public bool Equals(PairKey other)
        {
            return lo == other.lo && hi == other.hi;
        }

        public override bool Equals(object obj)
        {
            return obj is PairKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (lo * 397) ^ hi;
        }
    }

    static readonly Dictionary<PairKey, float> recent = new Dictionary<PairKey, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        recent.Clear();
    }

    public static void BindForLoadedMatch()
    {
        recent.Clear();
        Rigidbody[] bodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody body = bodies[i];
            if (!TryIdentify(body, out Side identified) || identified.attr == null)
                continue;
            if (body.GetComponent<RobotCollisionDamage>() != null)
                continue;
            body.gameObject.AddComponent<RobotCollisionDamage>();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.collider == null)
            return;
        // 墙、地面、哨塔、基地、能量机关、场地道具：没有机器人刚体，或名字是建筑。
        if (IsStructure(collision.collider.transform))
            return;
        if (collision.collider.GetComponent<BullProjectile>() != null)
            return;

        Rigidbody selfBody = GetComponent<Rigidbody>();
        Rigidbody otherBody = collision.collider.attachedRigidbody;
        if (selfBody == null || otherBody == null || selfBody == otherBody)
            return;
        if (IsStructure(otherBody.transform))
            return;

        if (!TryIdentify(selfBody, out Side self))
            return;
        if (!TryIdentify(otherBody, out Side other))
            return;
        if (self.attr == null || other.attr == null || self.attr == other.attr)
            return;
        // 同阵营不扣。双方都要还活着：死车不扣，也不让对方扣。
        if (self.team == other.team)
            return;
        if (!IsSide(self.team) || !IsSide(other.team))
            return;
        if (!self.attr.IsAlive || !other.attr.IsAlive)
            return;

        float speed = RelativeSpeed(collision, selfBody, otherBody);
        if (speed < MinRelativeSpeed)
            return;

        PairKey key = new PairKey(self.attr.GetInstanceID(), other.attr.GetInstanceID());
        float now = Time.time;
        if (recent.TryGetValue(key, out float at) && now - at < PairWindow)
            return;
        recent[key] = now;

        float lostSelf = Strike(self, other);
        float lostOther = Strike(other, self);
        Debug.Log(
            "[撞击] " + Label(self) + " <-> " + Label(other)
            + " 相对速度" + Num(speed)
            + " " + Label(self) + "扣" + Num(lostSelf)
            + " " + Label(other) + "扣" + Num(lostOther),
            this);
    }

    // 受害者扣血。经验只给对方，自己掉血不给自己。打死走现有击毁经验。
    static float Strike(Side victim, Side attacker)
    {
        float defense = Mathf.Min(1f, victim.attr.GetCurrent(RobotStat.DefenseBuffPercent));
        float amount = Mathf.Max(0f, RawDamage * (1f - defense));
        float hpBefore = victim.attr.Hp;
        bool aliveBefore = victim.attr.IsAlive;
        victim.attr.TakeDamage(amount, ignoreDefense: true, suppressLog: true);
        float lost = Mathf.Max(0f, hpBefore - victim.attr.Hp);
        if (lost > 0f)
        {
            MatchOutcome.AddAttackDamage(attacker.team, lost);
            attacker.attr.GrantFlatExperience(
                RobotLevelRules.RobotDamageExperience(lost),
                "撞击机器人 伤害" + Num(lost));
        }

        if (aliveBefore && !victim.attr.IsAlive)
            attacker.attr.GrantKillExperience(victim.attr);
        return lost;
    }

    static float RelativeSpeed(Collision collision, Rigidbody selfBody, Rigidbody otherBody)
    {
        float speed = collision.relativeVelocity.magnitude;
        if (speed < 0.05f && selfBody != null && otherBody != null)
            speed = (selfBody.linearVelocity - otherBody.linearVelocity).magnitude;
        return speed;
    }

    static bool TryIdentify(Rigidbody body, out Side side)
    {
        side = default;
        if (body == null)
            return false;

        GameObject go = body.gameObject;
        if (go.GetComponent<BullProjectile>() != null)
            return false;
        if (PlayerMovement.IsUnderAttributeBoard(go))
            return false;
        if (IsStructure(go.transform))
            return false;

        RobotAttributeManager attr = PlayerAttributeBinding.Resolve(go);
        if (attr == null)
            return false;

        RobotTeam team = CombatDamage.ResolveTeam(go, attr, attr.team);
        if (!IsSide(team))
            return false;
        if (!BelongsToVehicle(go.transform))
            return false;

        side.body = body;
        side.attr = attr;
        side.team = team;
        return true;
    }

    // 车根或父级对得上车位、英雄、步兵、工程。属性管理器上的车位名在上面已经排除。
    static bool BelongsToVehicle(Transform start)
    {
        Transform t = start;
        while (t != null)
        {
            if (PlayerMovement.IsUnderAttributeBoard(t.gameObject))
                return false;
            if (IsStructure(t))
                return false;
            if (TenRobotAttributeBoard.TryMatchSlot(t.gameObject, out int slot) && slot >= 0)
                return true;
            if (CombatDamage.NameContainsHeroRed(t.name) || CombatDamage.NameContainsHeroBlue(t.name))
                return true;
            t = t.parent;
        }

        return false;
    }

    static bool IsStructure(Transform start)
    {
        Transform t = start;
        while (t != null)
        {
            if (t.GetComponent<RotationCenterSpin>() != null)
                return true;
            string n = t.name;
            if (!string.IsNullOrEmpty(n))
            {
                if (n.IndexOf("哨塔", StringComparison.Ordinal) >= 0)
                    return true;
                if (n.IndexOf("能量机关", StringComparison.Ordinal) >= 0)
                    return true;
                // 「基地增益点」是增益区，不是基地。
                if (n.IndexOf("基地", StringComparison.Ordinal) >= 0
                    && n.IndexOf("增益", StringComparison.Ordinal) < 0)
                    return true;
            }

            t = t.parent;
        }

        return false;
    }

    static bool IsSide(RobotTeam team)
    {
        return team == RobotTeam.Red || team == RobotTeam.Blue;
    }

    static string Label(Side side)
    {
        string team = side.team == RobotTeam.Red ? "红方" : "蓝方";
        string type = TypeLabel(side.attr != null ? side.attr.robotType : RobotType.Infantry);
        string name = side.body != null ? side.body.name : "?";
        return team + type + " " + name;
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
            default: return "机器人";
        }
    }

    static string Num(float value)
    {
        float rounded = Mathf.Round(value);
        if (Mathf.Abs(value - rounded) < 0.001f)
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
