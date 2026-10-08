using UnityEngine;

// 金币由主机手动分配，无自动发币；客户端只能请求购买，不能改余额。
[DefaultExecutionOrder(-200)]
public sealed class MatchTeamEconomy : MonoBehaviour
{
    static MatchTeamEconomy instance;
    readonly TeamCoinLedger ledger = new TeamCoinLedger();
    RobotAttributeManager[] robots;
    [Header("主机 T 键分配（无界面）")]
    [Min(0)] public int redGrantAmount = 400;
    [Min(0)] public int blueGrantAmount = 400;
    public static int RedCoins => instance != null ? instance.ledger.Red : 0;
    public static int BlueCoins => instance != null ? instance.ledger.Blue : 0;
    static bool CanManage => (!LanSession.Active || (LanSession.IsHost && LanSession.Instance.Running))
        && !MatchOutcome.Decided;

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<MatchTeamEconomy>() == null)
            new GameObject("MatchTeamEconomy").AddComponent<MatchTeamEconomy>();
    }

    void Awake()
    {
        instance = this;
        robots = FindObjectsByType<RobotAttributeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var robot in robots)
        {
            if (robot.robotType != RobotType.Hero && robot.robotType != RobotType.Infantry) continue;
            robot.baseAmmo17mm = robot.baseAmmo42mm = 0;
            robot.SetBase(RobotStat.Ammo17mm, 0);
            robot.SetBase(RobotStat.Ammo42mm, 0);
        }
        MirrorBalances();
    }

    void Update()
    {
        if (!CanManage) return;
        if (Input.GetKeyDown(KeyCode.T)) GrantConfiguredAmounts();
        MirrorBalances();
    }

    // Inspector 和后续裁判接口均可配置金额；不创建 UI，不改变鼠标/车辆操作状态。
    public bool GrantConfiguredAmounts() => Grant(redGrantAmount, blueGrantAmount);

    public static int Balance(RobotTeam team) => team == RobotTeam.Red ? RedCoins
        : team == RobotTeam.Blue ? BlueCoins : 0;

    public static bool Grant(int red, int blue)
    {
        if (instance == null || !CanManage || !instance.ledger.Grant(red, blue)) return false;
        instance.MirrorBalances();
        Debug.Log("[金币] 主机追加 红方+" + red + " 蓝方+" + blue);
        return true;
    }

    public static bool TrySpend(RobotTeam team, int cost)
    {
        if (instance == null || !CanManage || (team != RobotTeam.Red && team != RobotTeam.Blue)) return false;
        if (!instance.ledger.Spend(team == RobotTeam.Blue, cost)) return false;
        instance.MirrorBalances();
        return true;
    }

    public static void ApplyLanState(int red, int blue)
    {
        if (!LanSession.IsClient || instance == null) return;
        instance.ledger.Apply(red, blue);
    }

    void MirrorBalances()
    {
        foreach (var robot in robots)
            if (robot != null && (robot.team == RobotTeam.Red || robot.team == RobotTeam.Blue))
                robot.SetCurrent(RobotStat.Coins, Balance(robot.team));
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
