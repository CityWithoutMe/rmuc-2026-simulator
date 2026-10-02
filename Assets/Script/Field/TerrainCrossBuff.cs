using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// 《RoboMaster 2026》V2.2.0 5.5.3.5
// 只处理「场地buff管理器 / neutral / 地形跨越」下已经摆好的路线。
// 每条路线用子物体「入口」「目标点」上的触发器：先碰到入口，再在 X 秒内碰到目标点。
// 途中碰到别的路线的入口或目标点，这次不算。
public class TerrainCrossBuff : MonoBehaviour
{
    public const string GroundDefenseId = "terrain_cross_ground";
    public const string TunnelDefenseId = "terrain_tunnel_def";
    public const string TunnelCoolingId = "terrain_tunnel_cool";

    const string ManagerName = "场地buff管理器";
    const string NeutralName = "neutral";
    const string FolderToken = "地形跨越";
    const string EntranceName = "入口";
    const string TargetName = "目标点";
    const float FirstExp = 300f;
    const float RoadLockSeconds = 15f;

    enum Kind
    {
        Slope,
        Highland,
        Road,
        Tunnel
    }

    class Route
    {
        public string Name;
        public Kind Kind;
        public float Window;
        public HighlandZonePresence Entrance;
        public HighlandZonePresence Target;
    }

    class Attempt
    {
        public Route Route;
        public float Deadline;
    }

    readonly List<Route> routes = new List<Route>();
    readonly Dictionary<RobotAttributeManager, Attempt> attempts = new Dictionary<RobotAttributeManager, Attempt>();
    readonly Dictionary<RobotAttributeManager, float> roadUnlockAt = new Dictionary<RobotAttributeManager, float>();
    readonly HashSet<string> firstExp = new HashSet<string>();
    readonly List<RobotAttributeManager> scratch = new List<RobotAttributeManager>();
    readonly Dictionary<HighlandZonePresence, HashSet<RobotAttributeManager>> touched = new Dictionary<HighlandZonePresence, HashSet<RobotAttributeManager>>();
    bool warned;
    bool ready;

    public static bool TryGetAttempt(RobotAttributeManager attr, out string line)
    {
        line = null;
        if (attr == null)
            return false;
        TerrainCrossBuff buff = FindAnyObjectByType<TerrainCrossBuff>();
        if (buff == null)
            return false;
        if (!buff.attempts.TryGetValue(attr, out Attempt attempt) || attempt == null || attempt.Route == null)
            return false;
        float remain = attempt.Deadline - Time.time;
        if (remain < 0f)
            return false;
        int seconds = Mathf.CeilToInt(remain);
        line = "进入（" + attempt.Route.Name + "）入口 剩余" + seconds.ToString(CultureInfo.InvariantCulture) + "秒";
        return true;
    }

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<TerrainCrossBuff>() != null)
            return;
        GameObject host = new GameObject("TerrainCrossBuff");
        host.AddComponent<TerrainCrossBuff>();
    }

    void Update()
    {
        if (!ready)
            ready = TryBind();
        if (!ready)
            return;

        PollEntrances();
        PollTargets();
        ClearIfDead();
    }

    bool TryBind()
    {
        Transform manager = GameObject.Find(ManagerName) != null ? GameObject.Find(ManagerName).transform : null;
        if (manager == null)
        {
            WarnOnce("未找到「场地buff管理器」。");
            return false;
        }

        Transform neutral = FindChild(manager, NeutralName);
        if (neutral == null)
        {
            WarnOnce("场地buff管理器下没有 neutral。");
            return false;
        }

        Transform folder = null;
        for (int i = 0; i < neutral.childCount; i++)
        {
            Transform child = neutral.GetChild(i);
            if (child.name.IndexOf(FolderToken, StringComparison.Ordinal) >= 0)
            {
                folder = child;
                break;
            }
        }

        if (folder == null)
        {
            WarnOnce("neutral 下没有「地形跨越」。请保存场景后再进 Play。");
            return false;
        }

        for (int i = 0; i < folder.childCount; i++)
        {
            Transform routeRoot = folder.GetChild(i);
            if (!TryKind(routeRoot.name, out Kind kind, out float window))
            {
                Debug.LogWarning("TerrainCrossBuff：无法从名字判断类型（需要含 飞坡 / 高地 / 公路 / 隧道）：" + routeRoot.name, routeRoot);
                continue;
            }

            Transform entrance = FindNamedChild(routeRoot, EntranceName);
            Transform target = FindNamedChild(routeRoot, TargetName);
            if (entrance == null || target == null)
            {
                Debug.LogWarning("TerrainCrossBuff：" + routeRoot.name + " 下需要子物体「入口」和「目标点」。", routeRoot);
                continue;
            }

            routes.Add(new Route
            {
                Name = routeRoot.name,
                Kind = kind,
                Window = window,
                Entrance = EnsurePresence(entrance),
                Target = EnsurePresence(target)
            });
        }

        if (routes.Count == 0)
        {
            WarnOnce("地形跨越下没有可用路线。");
            return false;
        }

        return true;
    }

    void PollEntrances()
    {
        for (int i = 0; i < routes.Count; i++)
        {
            Route route = routes[i];
            CopyInside(route.Entrance);
            for (int r = 0; r < scratch.Count; r++)
            {
                if (NewlyTouched(route.Entrance, scratch[r]))
                    OnEntrance(scratch[r], route);
            }
            RememberTouched(route.Entrance);
        }
    }

    void PollTargets()
    {
        for (int i = 0; i < routes.Count; i++)
        {
            Route route = routes[i];
            CopyInside(route.Target);
            for (int r = 0; r < scratch.Count; r++)
            {
                if (NewlyTouched(route.Target, scratch[r]))
                    OnTarget(scratch[r], route);
            }
            RememberTouched(route.Target);
        }
    }

    bool NewlyTouched(HighlandZonePresence presence, RobotAttributeManager attr)
    {
        if (!touched.TryGetValue(presence, out HashSet<RobotAttributeManager> set))
            return true;
        return !set.Contains(attr);
    }

    void RememberTouched(HighlandZonePresence presence)
    {
        if (!touched.TryGetValue(presence, out HashSet<RobotAttributeManager> set))
        {
            set = new HashSet<RobotAttributeManager>();
            touched[presence] = set;
        }

        set.Clear();
        for (int i = 0; i < scratch.Count; i++)
            set.Add(scratch[i]);
    }

    void OnEntrance(RobotAttributeManager attr, Route route)
    {
        if (!CanCross(attr))
            return;

        if (attempts.TryGetValue(attr, out Attempt attempt) && attempt.Route != route)
            attempts.Remove(attr);

        attempts[attr] = new Attempt
        {
            Route = route,
            Deadline = Time.time + route.Window
        };
    }

    void OnTarget(RobotAttributeManager attr, Route route)
    {
        if (!CanCross(attr))
            return;
        if (!attempts.TryGetValue(attr, out Attempt attempt))
            return;

        if (attempt.Route != route)
        {
            attempts.Remove(attr);
            return;
        }

        if (Time.time > attempt.Deadline)
        {
            attempts.Remove(attr);
            return;
        }

        attempts.Remove(attr);
        Grant(attr, route);
    }

    void Grant(RobotAttributeManager attr, Route route)
    {
        if (route.Kind == Kind.Road)
        {
            if (roadUnlockAt.TryGetValue(attr, out float unlock) && Time.time < unlock)
                return;
            roadUnlockAt[attr] = Time.time + RoadLockSeconds;
        }

        if (route.Kind == Kind.Tunnel)
            GrantTunnel(attr);
        else
            GrantGround(attr, route.Kind);

        string key = attr.GetInstanceID() + ":" + route.Kind;
        if (firstExp.Add(key))
            attr.GrantFlatExperience(FirstExp, "首次地形跨越（" + KindLabel(route.Kind) + "）");

        Debug.Log("[地形跨越] " + attr.name + " 完成 " + route.Name, attr);
    }

    static void GrantGround(RobotAttributeManager attr, Kind kind)
    {
        float fresh = kind == Kind.Road ? 5f : 30f;
        float add = 0.25f;
        float duration = fresh;
        if (attr.TryGetModifierTiming(RobotStat.DefenseBuffPercent, GroundDefenseId, out float remain) && remain > 0f)
        {
            add = 0.5f;
            duration = Mathf.Max(remain, fresh);
            attr.RemoveModifier(RobotStat.DefenseBuffPercent, GroundDefenseId, true);
        }

        attr.AddModifier(
            RobotStat.DefenseBuffPercent,
            StatModifier.Additive(GroundDefenseId, add, duration));
    }

    static void GrantTunnel(RobotAttributeManager attr)
    {
        attr.RemoveModifier(RobotStat.DefenseBuffPercent, TunnelDefenseId, true);
        attr.RemoveModifier(RobotStat.CoolingRate, TunnelCoolingId, true);
        attr.AddModifier(RobotStat.DefenseBuffPercent, StatModifier.Additive(TunnelDefenseId, 0.5f, 10f));
        attr.AddModifier(RobotStat.CoolingRate, StatModifier.PercentBonus(TunnelCoolingId, 1f, 120f));
    }

    void ClearIfDead()
    {
        RobotAttributeManager[] all = FindObjectsByType<RobotAttributeManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RobotAttributeManager attr = all[i];
            if (attr == null || attr.IsAlive)
                continue;
            attempts.Remove(attr);
            attr.RemoveModifiersFromSource(GroundDefenseId, true);
            attr.RemoveModifiersFromSource(TunnelDefenseId, true);
            attr.RemoveModifiersFromSource(TunnelCoolingId, true);
        }
    }

    void CopyInside(HighlandZonePresence presence)
    {
        scratch.Clear();
        if (presence == null)
            return;
        IReadOnlyList<RobotAttributeManager> inside = presence.Inside;
        for (int i = 0; i < inside.Count; i++)
        {
            if (inside[i] != null)
                scratch.Add(inside[i]);
        }
    }

    static bool CanCross(RobotAttributeManager attr)
    {
        if (attr == null || !attr.IsAlive)
            return false;
        return attr.robotType == RobotType.Hero
            || attr.robotType == RobotType.Infantry
            || attr.robotType == RobotType.Engineer
            || attr.robotType == RobotType.Sentry;
    }

    static bool TryKind(string name, out Kind kind, out float window)
    {
        kind = Kind.Road;
        window = 3f;
        if (name.IndexOf("飞坡", StringComparison.Ordinal) >= 0)
        {
            kind = Kind.Slope;
            window = 10f;
            return true;
        }

        if (name.IndexOf("隧道", StringComparison.Ordinal) >= 0)
        {
            kind = Kind.Tunnel;
            window = 3f;
            return true;
        }

        if (name.IndexOf("公路", StringComparison.Ordinal) >= 0)
        {
            kind = Kind.Road;
            window = 3f;
            return true;
        }

        if (name.IndexOf("高地", StringComparison.Ordinal) >= 0)
        {
            kind = Kind.Highland;
            window = 5f;
            return true;
        }

        return false;
    }

    static string KindLabel(Kind kind)
    {
        switch (kind)
        {
            case Kind.Slope: return "飞坡";
            case Kind.Highland: return "高地";
            case Kind.Tunnel: return "隧道";
            default: return "公路";
        }
    }

    static HighlandZonePresence EnsurePresence(Transform point)
    {
        HighlandZonePresence presence = point.GetComponent<HighlandZonePresence>();
        if (presence == null)
            presence = point.gameObject.AddComponent<HighlandZonePresence>();

        Collider[] cols = point.GetComponents<Collider>();
        bool trigger = false;
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null && cols[i].isTrigger)
                trigger = true;
        }

        if (!trigger)
            Debug.LogWarning("TerrainCrossBuff：" + point.name + " 上没有 Is Trigger 碰撞体。", point);
        return presence;
    }

    static Transform FindChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name)
                return child;
        }

        return null;
    }

    static Transform FindNamedChild(Transform root, string name)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != root && all[i].name == name)
                return all[i];
        }

        return null;
    }

    void WarnOnce(string message)
    {
        if (warned)
            return;
        warned = true;
        Debug.LogWarning("TerrainCrossBuff：" + message);
    }
}
