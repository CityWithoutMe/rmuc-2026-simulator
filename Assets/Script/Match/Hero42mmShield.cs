using UnityEngine;

// 只由主机判断。命中时再次读取英雄状态，避免 Update 顺序导致漏判。
[DefaultExecutionOrder(-100)]
public sealed class Hero42mmShield : MonoBehaviour
{
    static Hero42mmShield instance;
    readonly Hero42mmShieldRules[] rules = { new Hero42mmShieldRules(), new Hero42mmShieldRules() };
    RobotAttributeManager[] heroes = new RobotAttributeManager[2];
    bool clientRed, clientBlue;

    public static void BindForLoadedMatch()
    {
        var existing = FindAnyObjectByType<Hero42mmShield>();
        if (existing != null) Destroy(existing.gameObject);
        new GameObject("Hero42mmShield").AddComponent<Hero42mmShield>();
    }

    void Awake()
    {
        instance = this;
        foreach (var attr in FindObjectsByType<RobotAttributeManager>(FindObjectsSortMode.None))
            if (attr.robotType == RobotType.Hero && Index(attr.team) >= 0)
                heroes[Index(attr.team)] = attr;
    }

    static int Index(RobotTeam team) => team == RobotTeam.Red ? 0 : team == RobotTeam.Blue ? 1 : -1;

    bool Online(RobotAttributeManager hero) => hero != null && hero.isActiveAndEnabled
        && (!LanSession.Active || LanSession.Instance.IsRobotOnline(hero));

    void Observe(int index)
    {
        var hero = heroes[index];
        if (hero == null) return;
        rules[index].Observe(Time.time, hero.IsAlive, Online(hero), Mathf.FloorToInt(hero.GetCurrent(RobotStat.Ammo42mm)));
    }

    void Update()
    {
        if (!LanSession.CanSimulate || MatchOutcome.Decided) return;
        Observe(0); Observe(1);
    }

    public static bool IsBlocked(RobotTeam attackerTeam)
    {
        int index = Index(attackerTeam);
        if (instance == null || index < 0) return false;
        if (LanSession.IsClient) return index == 0 ? instance.clientRed : instance.clientBlue;
        instance.Observe(index);
        return instance.rules[index].Blocked;
    }

    public static void NotifyDeath(RobotAttributeManager attr)
    {
        if (!LanSession.CanSimulate || instance == null || attr == null || attr.robotType != RobotType.Hero) return;
        int index = Index(attr.team);
        if (index >= 0 && instance.heroes[index] == attr) instance.Observe(index);
    }

    public static void Notify42mmFired(RobotAttributeManager attr, int allowanceBefore)
    {
        if (!LanSession.CanSimulate || instance == null || attr == null || attr.robotType != RobotType.Hero) return;
        int index = Index(attr.team);
        if (index >= 0 && instance.heroes[index] == attr)
            instance.rules[index].Fired(Time.time, attr.IsAlive, instance.Online(attr), allowanceBefore);
    }

    public static void ApplyLanState(bool red, bool blue)
    {
        if (!LanSession.IsClient || instance == null) return;
        instance.clientRed = red; instance.clientBlue = blue;
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
