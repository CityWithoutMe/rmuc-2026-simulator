using System;
using System.Collections.Generic;
using UnityEngine;

// 规则 5.2、5.5.3：复用已保存的 Trigger；中央高地与高地跨越较高处重合。
[DefaultExecutionOrder(-100)]
public sealed class FieldSupportZoneBuff : MonoBehaviour
{
    enum Kind { Supply, Outpost, Central, Base }
    sealed class Zone
    {
        public Transform Root;
        public Kind Type;
        public RobotTeam Side, Controller;
        public string Source;
        public readonly Dictionary<RobotAttributeManager, float> Holders = new Dictionary<RobotAttributeManager, float>();
    }
    static FieldSupportZoneBuff instance;
    readonly List<Zone> zones = new List<Zone>();
    readonly List<RobotAttributeManager> inside = new List<RobotAttributeManager>();
    readonly List<RobotAttributeManager> remove = new List<RobotAttributeManager>();
    RobotAttributeManager[] robots;
    const float ReleaseSeconds = 2f;

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<FieldSupportZoneBuff>() == null)
            new GameObject("FieldSupportZoneBuff").AddComponent<FieldSupportZoneBuff>();
    }

    void Awake()
    {
        instance = this;
        robots = FindObjectsByType<RobotAttributeManager>(FindObjectsSortMode.None);
        GameObject manager = GameObject.Find("场地buff管理器");
        if (manager == null) { Debug.LogWarning("FieldSupportZoneBuff：找不到场地buff管理器"); return; }
        foreach (Transform point in manager.GetComponentsInChildren<Transform>(true))
        {
            string name = point.name.Trim();
            Kind kind;
            RobotTeam side = RobotTeam.Neutral;
            if (name.Contains("补给区增益点")) kind = Kind.Supply;
            else if (name.Contains("前哨站增益点")) kind = Kind.Outpost;
            else if (name.Contains("基地增益点")) kind = Kind.Base;
            else if (name.Contains("中央高地增益点") || (name == "目标点" && point.parent != null
                && point.parent.name.Contains("高地") && !point.parent.name.Contains("梯形"))) kind = Kind.Central;
            else continue;
            if (kind != Kind.Central)
            {
                Transform parent = point.parent;
                while (parent != null && parent != manager.transform)
                {
                    if (parent.name.Equals("red", StringComparison.OrdinalIgnoreCase)) { side = RobotTeam.Red; break; }
                    if (parent.name.Equals("blue", StringComparison.OrdinalIgnoreCase)) { side = RobotTeam.Blue; break; }
                    parent = parent.parent;
                }
                if (side == RobotTeam.Neutral) { Debug.LogWarning("增益点无队伍归属：" + point.name); continue; }
            }
            if (point.GetComponentsInChildren<Collider>(true).Length == 0)
            { Debug.LogWarning("增益点没有碰撞体：" + point.name); continue; }
            zones.Add(new Zone { Root = point, Type = kind, Side = side,
                Source = "zone_support_" + kind.ToString().ToLowerInvariant() + "_" + point.GetInstanceID() });
        }
        Debug.Log("[区域] 补给/前哨/中央高地绑定 " + zones.Count + " 个现有区域");
    }

    static bool GroundRobot(RobotAttributeManager attr) => attr != null && attr.isActiveAndEnabled
        && !attr.IsFoulOut && attr.IsPowered && (attr.robotType == RobotType.Hero
        || attr.robotType == RobotType.Infantry || attr.robotType == RobotType.Engineer || attr.robotType == RobotType.Sentry);

    static bool CanCentral(RobotAttributeManager attr) => GroundRobot(attr) && attr.IsAlive && !attr.IsWeak
        && attr.robotType != RobotType.Engineer;

    static float Elapsed => FindAnyObjectByType<MatchTimer>()?.ElapsedSeconds ?? 0f;

    static bool OutpostEligible(Zone zone, RobotAttributeManager attr)
    {
        if (!GroundRobot(attr)) return false;
        if (attr.team == zone.Side) return OutpostHealth.HpOf(zone.Side) > 0f;
        return (attr.team == RobotTeam.Red || attr.team == RobotTeam.Blue)
            && OutpostHealth.HpOf(attr.team) > 0f && OutpostHealth.HpOf(zone.Side) <= 0f && Elapsed < 300f;
    }

    // 用现有 BoxCollider 的真实旋转/缩放采样，支持开局就在区内和传送入区，不依赖漏掉的 Enter 回调。
    void CollectInside(Zone zone)
    {
        inside.Clear();
        foreach (Collider col in zone.Root.GetComponentsInChildren<Collider>())
        {
            if (!col.enabled || !col.isTrigger) continue;
            if (!(col is BoxCollider box))
            { Debug.LogWarning("区域需 BoxCollider 才能精确采样：" + col.name); continue; }
            Vector3 scale = box.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 half = Vector3.Scale(box.size, scale) * 0.5f;
            foreach (Collider hit in Physics.OverlapBox(box.transform.TransformPoint(box.center), half,
                box.transform.rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                GameObject body = hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
                RobotAttributeManager attr = PlayerAttributeBinding.Resolve(body);
                if (attr != null && !inside.Contains(attr)) inside.Add(attr);
            }
        }
    }

    public static bool InOwnSupply(RobotAttributeManager attr)
    {
        if (instance == null || !GroundRobot(attr)) return false;
        foreach (Zone zone in instance.zones)
        {
            if (zone.Type != Kind.Supply || zone.Side != attr.team) continue;
            instance.CollectInside(zone);
            if (instance.inside.Contains(attr)) return true;
        }
        return false;
    }

    public static bool InOwnOutpost(RobotAttributeManager attr)
    {
        if (instance == null || attr == null) return false;
        foreach (Zone zone in instance.zones)
        {
            if (zone.Type != Kind.Outpost || zone.Side != attr.team) continue;
            instance.CollectInside(zone);
            if (instance.inside.Contains(attr)) return true;
        }
        return false;
    }

    public static bool CanLocalPurchase(RobotAttributeManager attr)
    {
        if (instance == null || !GroundRobot(attr) || !attr.IsAlive || attr.IsWeak) return false;
        foreach (Zone zone in instance.zones)
        {
            bool eligible = zone.Type == Kind.Outpost ? OutpostEligible(zone, attr)
                : (zone.Type == Kind.Supply || zone.Type == Kind.Base) && zone.Side == attr.team;
            if (!eligible) continue;
            instance.CollectInside(zone);
            if (instance.inside.Contains(attr)) return true;
            // 与区域占领一致，离开后 2 秒仍视为占领；死亡/虚弱条件已在入口检查。
            if (zone.Holders.TryGetValue(attr, out float last) && Time.time - last < ReleaseSeconds) return true;
        }
        return false;
    }

    void Update()
    {
        if (!LanSession.CanSimulate || MatchOutcome.Decided) return;
        foreach (Zone zone in zones)
        {
            CollectInside(zone);
            if (zone.Type == Kind.Central) TickCentral(zone);
            else TickSide(zone);
            foreach (RobotAttributeManager attr in robots)
            {
                if (attr == null) continue;
                bool held = zone.Holders.ContainsKey(attr);
                if (zone.Type == Kind.Supply)
                {
                    float fraction = Elapsed >= 240f && attr.IsOutOfCombat ? 0.25f : 0.1f;
                    attr.SetPersistentAdd(RobotStat.RecoveryRate, zone.Source,
                        held ? attr.MaxHp * fraction : 0f);
                }
                else if (zone.Type != Kind.Base)
                    attr.SetPersistentAdd(RobotStat.DefenseBuffPercent, zone.Source, held ? 0.25f : 0f);
            }
        }
    }

    void TickSide(Zone zone)
    {
        foreach (var attr in inside)
        {
            bool card = GroundRobot(attr) && attr.IsAlive && (zone.Type == Kind.Supply || zone.Type == Kind.Base
                ? attr.team == zone.Side : OutpostEligible(zone, attr));
            if (!card) continue;
            attr.NotifyOccupiableBuffCard();
            if (!attr.IsWeak) zone.Holders[attr] = Time.time;
        }
        Prune(zone);
    }

    void TickCentral(Zone zone)
    {
        Prune(zone);
        if (zone.Holders.Count == 0) zone.Controller = RobotTeam.Neutral;
        bool red = false, blue = false;
        foreach (var attr in inside)
        {
            if (!CanCentral(attr)) continue;
            if (attr.team == RobotTeam.Red) red = true;
            if (attr.team == RobotTeam.Blue) blue = true;
        }
        // 无占领方时双方同帧进入，等待其中一方离开；不按对象枚举顺序随机抢占。
        if (zone.Controller == RobotTeam.Neutral && red != blue)
            zone.Controller = red ? RobotTeam.Red : RobotTeam.Blue;
        foreach (var attr in inside)
            if (CanCentral(attr) && attr.team == zone.Controller) zone.Holders[attr] = Time.time;
    }

    void Prune(Zone zone)
    {
        remove.Clear();
        foreach (var pair in zone.Holders)
        {
            var attr = pair.Key;
            bool eligible = GroundRobot(attr) && attr.IsAlive && !attr.IsWeak;
            if (zone.Type == Kind.Outpost) eligible &= OutpostEligible(zone, attr);
            if (!eligible || Time.time - pair.Value >= ReleaseSeconds) remove.Add(attr);
        }
        foreach (var attr in remove) zone.Holders.Remove(attr);
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
