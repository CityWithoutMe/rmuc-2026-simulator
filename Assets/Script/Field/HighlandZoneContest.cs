using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// 只处理「场地buff管理器 / neutral」下名字带「梯形高地」的区域。
// 进出名单优先读该物体上已有脚本公开的车列表；没有时才在它现成的 Trigger 上补 HighlandZonePresence。
public class HighlandZoneContest : MonoBehaviour
{
    // 《RoboMaster 2026 机甲大师超级对抗赛比赛规则手册》V2.2.0
    // 5.5.3.4 梯形高地增益点机制：占领方获得 50% 防御增益。表里只加这一项。
    // 5.5.3.1：占领状态的失效有 2 秒延迟；占领机器人战亡则增益立刻失效。
    // 5.5.3.4 没有另写占领时长，读条与失效延迟都用这 2 秒。
    public const float HighlandDefensePercent = 0.5f;
    public const float OccupySeconds = 2f;

    const string ManagerName = "场地buff管理器";
    const string NeutralName = "neutral";
    const string HighlandToken = "梯形高地";

    class Zone
    {
        public string Name;
        public Transform Root;
        public RobotTeam Controller;
        public bool HasController;
        public RobotTeam ChannelTeam;
        public float Progress;
        public bool Channeling;
        public float ReleaseLeft;
        public readonly List<RobotAttributeManager> Holders = new List<RobotAttributeManager>();
    }

    readonly List<Zone> zones = new List<Zone>();
    readonly List<RobotAttributeManager> inside = new List<RobotAttributeManager>();
    readonly List<RobotAttributeManager> seen = new List<RobotAttributeManager>();
    bool warnedMissing;
    bool ready;

    public static bool TryGetChannel(RobotAttributeManager attr, out string line)
    {
        line = null;
        if (attr == null)
            return false;
        HighlandZoneContest contest = FindAnyObjectByType<HighlandZoneContest>();
        if (contest == null)
            return false;
        for (int i = 0; i < contest.zones.Count; i++)
        {
            Zone zone = contest.zones[i];
            if (!zone.Channeling || zone.ChannelTeam != attr.team)
                continue;
            if (!contest.IsInside(zone.Root, attr))
                continue;
            int percent = Mathf.Clamp(Mathf.RoundToInt(zone.Progress * 100f), 0, 99);
            line = "梯形高地 占领中 " + percent + "%";
            return true;
        }

        return false;
    }

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<HighlandZoneContest>() != null)
            return;

        GameObject host = new GameObject("HighlandZoneContest");
        host.AddComponent<HighlandZoneContest>();
    }

    void Update()
    {
        if (!ready)
            ready = TryBindZones();
        if (!ready)
            return;

        RefreshControl();
        ApplyBuffs();
    }

    bool TryBindZones()
    {
        Transform manager = FindNamed(ManagerName);
        if (manager == null)
        {
            if (!warnedMissing)
            {
                warnedMissing = true;
                Debug.LogWarning("HighlandZoneContest：未找到「场地buff管理器」。已保存的 SampleScene 里没有这块层级，请在编辑器里保留该物体后再进 Play。");
            }
            return false;
        }

        Transform neutral = FindChildNamed(manager, NeutralName);
        if (neutral == null)
        {
            if (!warnedMissing)
            {
                warnedMissing = true;
                Debug.LogWarning("HighlandZoneContest：场地buff管理器下没有 neutral。");
            }
            return false;
        }

        Transform[] underNeutral = neutral.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < underNeutral.Length; i++)
        {
            Transform child = underNeutral[i];
            if (child == null || child == neutral)
                continue;
            if (child.name.IndexOf(HighlandToken, StringComparison.Ordinal) < 0)
                continue;
            if (HasHighlandAncestor(child, neutral))
                continue;

            EnsureReadable(child);
            zones.Add(new Zone { Name = child.name, Root = child });
        }

        if (zones.Count == 0)
        {
            if (!warnedMissing)
            {
                warnedMissing = true;
                Debug.LogWarning("HighlandZoneContest：neutral 下没有名字包含「梯形高地」的物体。");
            }
            return false;
        }

        return true;
    }

    static void EnsureReadable(Transform zone)
    {
        if (HasUserOccupancy(zone))
            return;

        Collider[] cols = zone.GetComponentsInChildren<Collider>(true);
        bool any = false;
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null || !col.isTrigger)
                continue;
            any = true;
            if (col.GetComponent<HighlandZonePresence>() == null)
                col.gameObject.AddComponent<HighlandZonePresence>();
        }

        if (!any)
            Debug.LogWarning("HighlandZoneContest：" + zone.name + " 上没有 Is Trigger 碰撞体，也无法从已有脚本读到在区内的车。", zone);
    }

    static bool HasUserOccupancy(Transform zone)
    {
        MonoBehaviour[] all = zone.GetComponentsInChildren<MonoBehaviour>(true);
        var scratch = new List<RobotAttributeManager>();
        for (int i = 0; i < all.Length; i++)
        {
            MonoBehaviour mb = all[i];
            if (mb == null || mb is HighlandZonePresence)
                continue;
            scratch.Clear();
            if (TryExtract(mb, scratch, probeOnly: true))
                return true;
        }

        return false;
    }

    void RefreshControl()
    {
        float dt = Time.deltaTime;
        for (int i = 0; i < zones.Count; i++)
        {
            Zone zone = zones[i];
            CollectInside(zone.Root);
            bool red = false;
            bool blue = false;
            bool holderAlive = false;
            bool holderDead = false;
            for (int r = 0; r < inside.Count; r++)
            {
                RobotAttributeManager attr = inside[r];
                if (attr == null)
                    continue;
                if (zone.HasController && attr.team == zone.Controller)
                {
                    if (attr.IsAlive)
                        holderAlive = true;
                    else
                        holderDead = true;
                }

                if (!attr.IsAlive)
                    continue;
                if (attr.team == RobotTeam.Red)
                    red = true;
                else if (attr.team == RobotTeam.Blue)
                    blue = true;
            }

            if (zone.HasController && holderDead && !holderAlive)
            {
                ClearControl(zone);
            }
            else if (red && blue)
            {
                zone.Channeling = zone.Progress > 0f && zone.Progress < 1f;
                zone.ReleaseLeft = 0f;
            }
            else if (red || blue)
            {
                RobotTeam sole = red ? RobotTeam.Red : RobotTeam.Blue;
                if (zone.HasController && zone.Controller == sole)
                {
                    zone.Progress = 1f;
                    zone.Channeling = false;
                    zone.ReleaseLeft = 0f;
                    zone.ChannelTeam = sole;
                    RememberHolders(zone);
                }
                else
                {
                    if (zone.ChannelTeam != sole)
                    {
                        zone.ChannelTeam = sole;
                        zone.Progress = 0f;
                    }

                    zone.Progress += dt / OccupySeconds;
                    zone.Channeling = zone.Progress < 1f;
                    if (zone.Progress >= 1f)
                    {
                        zone.Progress = 1f;
                        zone.Channeling = false;
                        zone.HasController = true;
                        zone.Controller = sole;
                        zone.ReleaseLeft = 0f;
                        RememberHolders(zone);
                    }

                    if (zone.HasController && zone.Controller != sole)
                        TickRelease(zone, dt);
                }
            }
            else if (zone.HasController)
            {
                zone.Channeling = false;
                TickRelease(zone, dt);
            }
            else
            {
                zone.Channeling = false;
                zone.Progress = 0f;
                zone.ReleaseLeft = 0f;
            }
        }
    }

    void RememberHolders(Zone zone)
    {
        zone.Holders.Clear();
        for (int r = 0; r < inside.Count; r++)
        {
            RobotAttributeManager attr = inside[r];
            if (attr != null && attr.IsAlive && attr.team == zone.Controller)
                zone.Holders.Add(attr);
        }
    }

    static void TickRelease(Zone zone, float dt)
    {
        zone.ReleaseLeft += dt;
        if (zone.ReleaseLeft < OccupySeconds)
            return;
        ClearControl(zone);
    }

    static void ClearControl(Zone zone)
    {
        zone.HasController = false;
        zone.Progress = 0f;
        zone.Channeling = false;
        zone.ReleaseLeft = 0f;
        zone.Holders.Clear();
    }

    void ApplyBuffs()
    {
        seen.Clear();
        RobotAttributeManager[] all = FindObjectsByType<RobotAttributeManager>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int i = 0; i < all.Length; i++)
        {
            RobotAttributeManager attr = all[i];
            if (attr == null || seen.Contains(attr))
                continue;
            seen.Add(attr);

            bool heroOrInfantry = attr.robotType == RobotType.Hero || attr.robotType == RobotType.Infantry;
            string keep = null;
            if (heroOrInfantry && attr.IsAlive && (attr.team == RobotTeam.Red || attr.team == RobotTeam.Blue))
            {
                for (int z = 0; z < zones.Count; z++)
                {
                    Zone zone = zones[z];
                    if (!zone.HasController || zone.Controller != attr.team || zone.Progress < 1f)
                        continue;
                    bool staying = IsInside(zone.Root, attr);
                    bool grace = zone.ReleaseLeft > 0f && zone.Holders.Contains(attr);
                    if (!staying && !grace)
                        continue;
                    keep = SourceId(zone.Name, attr.team);
                    break;
                }
            }

            for (int z = 0; z < zones.Count; z++)
            {
                string id = SourceId(zones[z].Name, RobotTeam.Red);
                string idBlue = SourceId(zones[z].Name, RobotTeam.Blue);
                SetDefense(attr, id, id == keep);
                SetDefense(attr, idBlue, idBlue == keep);
            }
        }
    }

    static void SetDefense(RobotAttributeManager attr, string sourceId, bool on)
    {
        bool has = attr.HasModifier(RobotStat.DefenseBuffPercent, sourceId);
        if (on)
        {
            if (!has)
            {
                attr.AddModifier(
                    RobotStat.DefenseBuffPercent,
                    StatModifier.Additive(sourceId, HighlandDefensePercent));
            }
            return;
        }

        if (has)
            attr.RemoveModifier(RobotStat.DefenseBuffPercent, sourceId, true);
    }

    bool IsInside(Transform zone, RobotAttributeManager attr)
    {
        CollectInside(zone);
        return inside.Contains(attr);
    }

    void CollectInside(Transform zone)
    {
        inside.Clear();
        MonoBehaviour[] all = zone.GetComponentsInChildren<MonoBehaviour>(true);
        bool user = false;
        for (int i = 0; i < all.Length; i++)
        {
            MonoBehaviour mb = all[i];
            if (mb == null || mb is HighlandZonePresence)
                continue;
            if (TryExtract(mb, inside, probeOnly: false))
                user = true;
        }

        if (user)
            return;

        HighlandZonePresence[] presences = zone.GetComponentsInChildren<HighlandZonePresence>(true);
        for (int i = 0; i < presences.Length; i++)
        {
            IReadOnlyList<RobotAttributeManager> list = presences[i].Inside;
            for (int n = 0; n < list.Count; n++)
            {
                RobotAttributeManager attr = list[n];
                if (attr != null && !inside.Contains(attr))
                    inside.Add(attr);
            }
        }
    }

    public static string SourceId(string zoneName, RobotTeam team)
    {
        string who = team == RobotTeam.Blue ? "blue" : "red";
        return "zone_highland_" + zoneName + "_" + who;
    }

    static bool TryExtract(MonoBehaviour mb, List<RobotAttributeManager> into, bool probeOnly)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = mb.GetType();
        bool hit = false;

        FieldInfo[] fields = type.GetFields(flags);
        for (int i = 0; i < fields.Length; i++)
        {
            if (!LooksLikeOccupancy(fields[i].Name))
                continue;
            if (Pull(fields[i].GetValue(mb), into, probeOnly))
                hit = true;
        }

        PropertyInfo[] props = type.GetProperties(flags);
        for (int i = 0; i < props.Length; i++)
        {
            if (!props[i].CanRead || props[i].GetIndexParameters().Length != 0)
                continue;
            if (!LooksLikeOccupancy(props[i].Name))
                continue;
            object value;
            try
            {
                value = props[i].GetValue(mb, null);
            }
            catch (Exception)
            {
                continue;
            }

            if (Pull(value, into, probeOnly))
                hit = true;
        }

        return hit;
    }

    static bool LooksLikeOccupancy(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        string n = name.ToLowerInvariant();
        return n.Contains("inside")
            || n.Contains("occupant")
            || n.Contains("robot")
            || n.Contains("vehicle")
            || n.Contains("intruder")
            || name.Contains("区内")
            || name.Contains("在内");
    }

    static bool Pull(object value, List<RobotAttributeManager> into, bool probeOnly)
    {
        if (value == null)
            return false;

        if (value is RobotAttributeManager one)
        {
            if (!probeOnly && one != null && !into.Contains(one))
                into.Add(one);
            return true;
        }

        if (value is GameObject go)
            return PullObject(go, into, probeOnly);

        if (value is Component comp)
            return PullObject(comp.gameObject, into, probeOnly);

        if (value is IEnumerable enumerable && !(value is string))
        {
            bool any = false;
            foreach (object item in enumerable)
            {
                if (item == null)
                    continue;
                if (Pull(item, into, probeOnly))
                    any = true;
            }

            return any || IsOccupancyCollection(value);
        }

        return false;
    }

    static bool IsOccupancyCollection(object value)
    {
        Type type = value.GetType();
        if (!type.IsGenericType)
            return false;
        Type[] args = type.GetGenericArguments();
        if (args.Length != 1)
            return false;
        Type arg = args[0];
        return arg == typeof(RobotAttributeManager)
            || arg == typeof(GameObject)
            || arg == typeof(Collider)
            || arg == typeof(Transform)
            || typeof(Component).IsAssignableFrom(arg);
    }

    static bool PullObject(GameObject go, List<RobotAttributeManager> into, bool probeOnly)
    {
        if (go == null)
            return false;

        if (PlayerAttributeBinding.TryResolveRoster(go, out RobotAttributeManager roster) && roster != null)
        {
            if (!probeOnly && !into.Contains(roster))
                into.Add(roster);
            return true;
        }

        RobotAttributeManager attr = go.GetComponent<RobotAttributeManager>();
        if (attr == null)
            attr = go.GetComponentInParent<RobotAttributeManager>();
        if (attr == null)
            return false;
        if (!probeOnly && !into.Contains(attr))
            into.Add(attr);
        return true;
    }

    static bool HasHighlandAncestor(Transform child, Transform neutral)
    {
        Transform parent = child.parent;
        while (parent != null && parent != neutral)
        {
            if (parent.name.IndexOf(HighlandToken, StringComparison.Ordinal) >= 0)
                return true;
            parent = parent.parent;
        }

        return false;
    }

    static Transform FindNamed(string name)
    {
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && string.Equals(all[i].name, name, StringComparison.Ordinal))
                return all[i];
        }

        return null;
    }

    static Transform FindChildNamed(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != root && string.Equals(all[i].name, name, StringComparison.OrdinalIgnoreCase))
                return all[i];
        }

        return null;
    }
}
