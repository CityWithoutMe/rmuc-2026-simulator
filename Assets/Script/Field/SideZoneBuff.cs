using System;
using System.Collections.Generic;
using UnityEngine;

// 场地buff管理器 / red 与 blue 的直接子物体。neutral 不在这里处理。
// 手册 V2.2.0 5.5.3.2 基地增益点、5.5.3.9 堡垒增益点。
// 5.5.3.1：占领失效有 2 秒延迟；占领机器人战亡则增益立刻失效。这两处没有另写占领读条。
public class SideZoneBuff : MonoBehaviour
{
    public const float BaseDefense = 0.5f;
    public const float FortDefense = 0.5f;
    public const float FortVulnerability = -1f;
    public const float ReleaseSeconds = 2f;
    public const float EnemyOpenSeconds = 20f;
    public const float EnemyPauseSeconds = 3f;
    public const float EnemyUnlockSeconds = 180f;
    public const int FortCoolCap = 75;
    public const int FortReserveCap = 500;

    const string ManagerName = "场地buff管理器";
    const string BaseToken = "基地增益点";
    const string FortToken = "堡垒增益点";

    enum Kind
    {
        Base,
        Fort
    }

    class Person
    {
        public float ReleaseLeft;
        public float Progress;
        public float PauseLeft;
        public bool Holding;
    }

    class Zone
    {
        public string Name;
        public Transform Root;
        public RobotTeam Owner;
        public Kind Kind;
        public RobotAttributeManager FortHolder;
        public float FortReleaseLeft;
        public int FortReserveN = -1;
        public readonly Dictionary<RobotAttributeManager, Person> People = new Dictionary<RobotAttributeManager, Person>();
    }

    readonly List<Zone> zones = new List<Zone>();
    readonly List<RobotAttributeManager> inside = new List<RobotAttributeManager>();
    readonly List<RobotAttributeManager> seen = new List<RobotAttributeManager>();
    readonly List<RobotAttributeManager> drop = new List<RobotAttributeManager>();
    bool warnedMissing;
    bool ready;

    public static bool TryGetChannel(RobotAttributeManager attr, out string line)
    {
        line = null;
        if (attr == null)
            return false;
        SideZoneBuff buff = FindAnyObjectByType<SideZoneBuff>();
        if (buff == null)
            return false;
        for (int i = 0; i < buff.zones.Count; i++)
        {
            Zone zone = buff.zones[i];
            if (zone.Kind != Kind.Fort)
                continue;
            if (!zone.People.TryGetValue(attr, out Person person) || !person.Holding)
                continue;
            if (person.Progress >= EnemyOpenSeconds)
                continue;
            int percent = Mathf.Clamp(Mathf.RoundToInt(person.Progress / EnemyOpenSeconds * 100f), 0, 99);
            line = "堡垒增益点 占领中 " + percent + "%";
            return true;
        }

        return false;
    }

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<SideZoneBuff>() != null)
            return;
        GameObject host = new GameObject("SideZoneBuff");
        host.AddComponent<SideZoneBuff>();
    }

    void Update()
    {
        if (!LanSession.CanSimulate) return;
        if (!ready)
            ready = TryBind();
        if (!ready)
            return;
        float dt = Time.deltaTime;
        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].Kind == Kind.Base)
                TickBase(zones[i], dt);
            else
                TickFort(zones[i], dt);
        }

        ApplyBuffs();
    }

    bool TryBind()
    {
        Transform manager = FindNamed(ManagerName);
        if (manager == null)
        {
            WarnOnce("未找到「场地buff管理器」。");
            return false;
        }

        BindFolder(manager, "red", RobotTeam.Red);
        BindFolder(manager, "blue", RobotTeam.Blue);
        if (zones.Count == 0)
        {
            WarnOnce("red / blue 下没有基地增益点或堡垒增益点。");
            return false;
        }

        return true;
    }

    void BindFolder(Transform manager, string folderName, RobotTeam owner)
    {
        Transform folder = null;
        for (int i = 0; i < manager.childCount; i++)
        {
            Transform child = manager.GetChild(i);
            if (child != null && string.Equals(child.name, folderName, StringComparison.OrdinalIgnoreCase))
            {
                folder = child;
                break;
            }
        }

        if (folder == null)
        {
            WarnOnce("场地buff管理器下没有 " + folderName + "。");
            return;
        }

        for (int i = 0; i < folder.childCount; i++)
        {
            Transform point = folder.GetChild(i);
            if (point == null)
                continue;
            string name = point.name.Trim();
            if (name.IndexOf(BaseToken, StringComparison.Ordinal) >= 0)
            {
                EnsurePresence(point);
                zones.Add(new Zone { Name = name, Root = point, Owner = owner, Kind = Kind.Base });
            }
            else if (name.IndexOf(FortToken, StringComparison.Ordinal) >= 0)
            {
                EnsurePresence(point);
                zones.Add(new Zone { Name = name, Root = point, Owner = owner, Kind = Kind.Fort });
            }
            else
            {
                Debug.LogWarning("SideZoneBuff：" + point.name + " 不在手册 5.5.3 的增益点里，未套增益。", point);
            }
        }
    }

    static void EnsurePresence(Transform point)
    {
        Collider[] cols = point.GetComponentsInChildren<Collider>(true);
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
            Debug.LogWarning("SideZoneBuff：" + point.name + " 上没有 Is Trigger 碰撞体。", point);
    }

    void TickBase(Zone zone, float dt)
    {
        CollectInside(zone.Root);
        for (int i = 0; i < inside.Count; i++)
        {
            RobotAttributeManager attr = inside[i];
            if (!CanBase(attr, zone.Owner))
                continue;
            Person person = PersonOf(zone, attr);
            person.Holding = true;
            person.ReleaseLeft = 0f;
            attr.NotifyOccupiableBuffCard();
        }

        drop.Clear();
        foreach (KeyValuePair<RobotAttributeManager, Person> pair in zone.People)
        {
            RobotAttributeManager attr = pair.Key;
            Person person = pair.Value;
            if (attr == null)
            {
                drop.Add(attr);
                continue;
            }

            bool stayed = person.Holding && inside.Contains(attr) && CanBase(attr, zone.Owner);
            if (stayed)
                continue;
            if (!attr.IsAlive)
            {
                person.Holding = false;
                person.ReleaseLeft = ReleaseSeconds;
                continue;
            }

            if (!person.Holding)
                continue;
            person.ReleaseLeft += dt;
            if (person.ReleaseLeft >= ReleaseSeconds)
                person.Holding = false;
        }

        for (int i = 0; i < drop.Count; i++)
            zone.People.Remove(drop[i]);
    }

    void TickFort(Zone zone, float dt)
    {
        CollectInside(zone.Root);
        bool ownLive = OutpostHealth.EverDestroyed(zone.Owner);
        TickFortOwner(zone, dt, ownLive);
        TickFortEnemy(zone, dt);
    }

    void TickFortOwner(Zone zone, float dt, bool ownLive)
    {
        RobotAttributeManager holder = zone.FortHolder;
        bool insideHolder = holder != null && holder.IsAlive && ownLive && InsideEligible(holder, zone.Owner, true);
        if (holder != null && !holder.IsAlive)
        {
            holder.ClearFortReserve();
            zone.FortHolder = null;
            zone.FortReleaseLeft = 0f;
            zone.FortReserveN = -1;
        }
        else if (!ownLive)
        {
            if (holder != null)
                holder.ClearFortReserve();
            zone.FortHolder = null;
            zone.FortReleaseLeft = 0f;
            zone.FortReserveN = -1;
        }
        else if (insideHolder)
        {
            zone.FortReleaseLeft = 0f;
        }
        else if (holder != null)
        {
            zone.FortReleaseLeft += dt;
            if (zone.FortReleaseLeft >= ReleaseSeconds)
            {
                holder.ClearFortReserve();
                zone.FortHolder = null;
                zone.FortReleaseLeft = 0f;
                zone.FortReserveN = -1;
            }
        }

        if (zone.FortHolder == null && ownLive)
        {
            for (int i = 0; i < inside.Count; i++)
            {
                RobotAttributeManager attr = inside[i];
                if (!CanFort(attr) || attr.team != zone.Owner || !attr.IsAlive)
                    continue;
                zone.FortHolder = attr;
                zone.FortReleaseLeft = 0f;
                break;
            }
        }

        if (zone.FortHolder != null && zone.FortReleaseLeft < ReleaseSeconds)
            FillReserve(zone);
    }

    static void FillReserve(Zone zone)
    {
        int n = ReserveCount(zone.Owner);
        if (zone.FortReserveN == n)
            return;
        zone.FortReserveN = n;
        zone.FortHolder.SetFortReserve(n);
    }

    void TickFortEnemy(Zone zone, float dt)
    {
        bool gate = MatchElapsed() >= EnemyUnlockSeconds && OutpostHealth.HpOf(zone.Owner) <= 0f;
        bool armor = BaseHealth.ArmorOpen(zone.Owner);
        for (int i = 0; i < inside.Count; i++)
        {
            RobotAttributeManager attr = inside[i];
            if (!CanFort(attr) || attr.team == zone.Owner || !attr.IsAlive)
                continue;
            if (!gate)
                continue;
            Person person = PersonOf(zone, attr);
            person.Holding = true;
            person.ReleaseLeft = 0f;
            person.PauseLeft = 0f;
            if (!armor)
            {
                person.Progress += dt;
                if (person.Progress >= EnemyOpenSeconds)
                    BaseHealth.OpenArmor(zone.Owner);
            }
        }

        drop.Clear();
        foreach (KeyValuePair<RobotAttributeManager, Person> pair in zone.People)
        {
            RobotAttributeManager attr = pair.Key;
            Person person = pair.Value;
            if (attr == null)
            {
                drop.Add(attr);
                continue;
            }

            if (attr.team == zone.Owner)
                continue;
            bool stayed = person.Holding && gate && inside.Contains(attr) && attr.IsAlive && CanFort(attr);
            if (stayed)
                continue;
            if (!attr.IsAlive)
            {
                person.Holding = false;
                person.ReleaseLeft = ReleaseSeconds;
                if (person.Progress > 0f && person.PauseLeft <= 0f)
                    person.PauseLeft = EnemyPauseSeconds;
                continue;
            }

            if (person.Holding)
            {
                person.ReleaseLeft += dt;
                if (!armor && gate)
                {
                    person.Progress += dt;
                    if (person.Progress >= EnemyOpenSeconds)
                        BaseHealth.OpenArmor(zone.Owner);
                }

                if (person.ReleaseLeft >= ReleaseSeconds)
                {
                    person.Holding = false;
                    if (person.Progress > 0f)
                        person.PauseLeft = EnemyPauseSeconds;
                }

                continue;
            }

            if (person.PauseLeft > 0f)
            {
                person.PauseLeft -= dt;
                if (person.PauseLeft <= 0f)
                {
                    person.PauseLeft = 0f;
                    person.Progress = 0f;
                }
            }
        }

        for (int i = 0; i < drop.Count; i++)
            zone.People.Remove(drop[i]);
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
            for (int z = 0; z < zones.Count; z++)
            {
                Zone zone = zones[z];
                if (zone.Kind == Kind.Base)
                    ApplyBase(attr, zone);
                else
                    ApplyFort(attr, zone);
            }
        }
    }

    static void ApplyBase(RobotAttributeManager attr, Zone zone)
    {
        bool on = false;
        if (zone.People.TryGetValue(attr, out Person person))
            on = person.Holding && attr.IsAlive && person.ReleaseLeft < ReleaseSeconds;
        SetDefense(attr, BaseId(zone), on, BaseDefense);
    }

    void ApplyFort(RobotAttributeManager attr, Zone zone)
    {
        bool own = zone.FortHolder == attr && attr.IsAlive && zone.FortReleaseLeft < ReleaseSeconds
            && OutpostHealth.EverDestroyed(zone.Owner);
        SetDefense(attr, FortDefId(zone), own, FortDefense);
        if (own)
            SetFortCool(attr, zone);
        else
            attr.SetPersistentAdd(RobotStat.CoolingRate, FortDefId(zone), 0f);

        bool vuln = false;
        if (zone.People.TryGetValue(attr, out Person person))
        {
            vuln = person.Holding && attr.IsAlive && person.ReleaseLeft < ReleaseSeconds
                && attr.team != zone.Owner && !BaseHealth.ArmorOpen(zone.Owner);
        }

        SetDefense(attr, FortVulnId(zone), vuln, FortVulnerability);
    }

    static void SetFortCool(RobotAttributeManager attr, Zone zone)
    {
        int w = CoolW(zone.Owner);
        string id = FortDefId(zone);
        float b = attr.GetBase(RobotStat.CoolingRate);
        float pct = 0f;
        float other = 0f;
        IReadOnlyList<StatModifier> list = attr.GetModifiers(RobotStat.CoolingRate);
        for (int i = 0; i < list.Count; i++)
        {
            StatModifier mod = list[i];
            if (mod.sourceId == id)
                continue;
            other += mod.add;
            if (mod.percent > pct)
                pct = mod.percent;
        }

        float without = (b + other) * (1f + pct);
        float flat = b + w;
        float need = 0f;
        if (flat > without + 0.01f)
            need = flat / Mathf.Max(0.0001f, 1f + pct) - b - other;
        attr.SetPersistentAdd(RobotStat.CoolingRate, FortDefId(zone), need);
    }

    static void SetDefense(RobotAttributeManager attr, string sourceId, bool on, float add)
    {
        bool has = attr.HasModifier(RobotStat.DefenseBuffPercent, sourceId);
        if (on)
        {
            if (!has)
                attr.AddModifier(RobotStat.DefenseBuffPercent, StatModifier.Additive(sourceId, add));
            return;
        }

        if (has)
            attr.RemoveModifier(RobotStat.DefenseBuffPercent, sourceId, true);
    }

    static int CoolW(RobotTeam team)
    {
        float delta = Mathf.Max(0f, BaseHealth.MaxHp - BaseHealth.LowestHp(team));
        int w = Mathf.FloorToInt(delta / 40f);
        return Mathf.Min(FortCoolCap, w);
    }

    static int ReserveCount(RobotTeam team)
    {
        float delta = Mathf.Max(0f, BaseHealth.MaxHp - BaseHealth.LowestHp(team));
        int n = 100 + 2 * Mathf.FloorToInt(delta / 15f);
        return Mathf.Min(FortReserveCap, n);
    }

    static bool CanBase(RobotAttributeManager attr, RobotTeam owner)
    {
        if (attr == null || !attr.IsAlive || attr.team != owner)
            return false;
        return attr.robotType == RobotType.Hero
            || attr.robotType == RobotType.Infantry
            || attr.robotType == RobotType.Sentry
            || attr.robotType == RobotType.Engineer;
    }

    static bool CanFort(RobotAttributeManager attr)
    {
        if (attr == null || !attr.IsAlive)
            return false;
        return attr.robotType == RobotType.Hero
            || attr.robotType == RobotType.Infantry
            || attr.robotType == RobotType.Sentry;
    }

    bool InsideEligible(RobotAttributeManager attr, RobotTeam owner, bool ownSide)
    {
        if (!CanFort(attr))
            return false;
        if (ownSide)
            return attr.team == owner && inside.Contains(attr);
        return attr.team != owner && inside.Contains(attr);
    }

    void CollectInside(Transform zone)
    {
        inside.Clear();
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

    static Person PersonOf(Zone zone, RobotAttributeManager attr)
    {
        if (!zone.People.TryGetValue(attr, out Person person))
        {
            person = new Person();
            zone.People[attr] = person;
        }

        return person;
    }

    static float MatchElapsed()
    {
        MatchTimer timer = FindAnyObjectByType<MatchTimer>();
        return timer != null ? timer.ElapsedSeconds : 0f;
    }

    static string BaseId(Zone zone)
    {
        return "zone_base_" + zone.Name + "_" + Side(zone.Owner);
    }

    static string FortDefId(Zone zone)
    {
        return "zone_fort_def_" + zone.Name + "_" + Side(zone.Owner);
    }

    static string FortVulnId(Zone zone)
    {
        return "zone_fort_vuln_" + zone.Name + "_" + Side(zone.Owner);
    }

    static string Side(RobotTeam team)
    {
        return team == RobotTeam.Blue ? "blue" : "red";
    }

    void WarnOnce(string message)
    {
        if (warnedMissing)
            return;
        warnedMissing = true;
        Debug.LogWarning("SideZoneBuff：" + message);
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
}
