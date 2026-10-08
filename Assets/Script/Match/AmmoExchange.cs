using System.Collections.Generic;
using UnityEngine;

// 所有现场/远程购弹均由主机校验。已兑换及在途弹量共同占用队伍总兑换上限。
[DefaultExecutionOrder(-150)]
public sealed class AmmoExchange : MonoBehaviour
{
    sealed class Order { public RobotAttributeManager Robot; public bool Is42; public int Count; public float Due; }
    static AmmoExchange instance;
    readonly List<Order> orders = new List<Order>();
    readonly int[,] used = new int[2, 2];
    readonly Dictionary<RobotAttributeManager, LanRobotState> clientPending = new Dictionary<RobotAttributeManager, LanRobotState>();

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<AmmoExchange>() == null) new GameObject("AmmoExchange").AddComponent<AmmoExchange>();
    }
    void Awake() { instance = this; }
    void OnDestroy() { if (instance == this) instance = null; }

    public static int Pending(RobotAttributeManager attr, bool is42)
    {
        if (instance == null || attr == null) return 0;
        if (LanSession.IsClient)
            return instance.clientPending.TryGetValue(attr, out var state) ? (is42 ? state.pendingAmmo42 : state.pendingAmmo17) : 0;
        int total = 0;
        foreach (var order in instance.orders) if (order.Robot == attr && order.Is42 == is42) total += order.Count;
        return total;
    }
    public static float WaitingSeconds(RobotAttributeManager attr)
    {
        if (instance == null || attr == null) return 0;
        if (LanSession.IsClient)
            return instance.clientPending.TryGetValue(attr, out var state) ? state.ammoDelivery : 0;
        float nearest = float.PositiveInfinity;
        foreach (var order in instance.orders) if (order.Robot == attr) nearest = Mathf.Min(nearest, order.Due - Time.time);
        return float.IsPositiveInfinity(nearest) ? 0 : Mathf.Max(0, nearest);
    }
    public static void ApplyClientPending(RobotAttributeManager attr, LanRobotState state)
    {
        if (LanSession.IsClient && instance != null) instance.clientPending[attr] = state;
    }
    public static string Buy(RobotAttributeManager attr, bool is42, bool remote)
    {
        if (instance == null || !LanSession.CanSimulate || MatchOutcome.Decided || attr == null) return "无效兑换";
        if (!attr.IsAlive || !attr.IsPowered || attr.IsFoulOut) return "当前状态不能兑换弹药";
        if (attr.team != RobotTeam.Red && attr.team != RobotTeam.Blue) return "无效队伍";
        if (is42 ? attr.robotType != RobotType.Hero : attr.robotType != RobotType.Hero && attr.robotType != RobotType.Infantry)
            return "该车辆不能兑换这种弹药";
        if (remote && !attr.IsOutOfCombat) return "远程购弹需脱战（连续 6 秒未发射且未扣血）";
        if (!remote && !FieldSupportZoneBuff.CanLocalPurchase(attr)) return "现场购弹需占领可用补给区、基地或前哨站增益点";
        int count = AmmoExchangeRules.Count(is42, remote), price = AmmoExchangeRules.Price(is42, remote);
        RobotStat stat = is42 ? RobotStat.Ammo42mm : RobotStat.Ammo17mm;
        if (attr.GetCap(stat) - attr.GetCurrent(stat) - Pending(attr, is42) < count) return "剩余弹量容量不足一个兑换单位（含在途弹量）";
        int team = attr.team == RobotTeam.Blue ? 1 : 0, type = is42 ? 1 : 0;
        if (instance.used[team, type] + count > AmmoExchangeRules.Limit(is42)) return "本队已达到该弹种的整局兑换上限";
        if (!MatchTeamEconomy.TrySpend(attr.team, price)) return "队伍金币不足";
        instance.used[team, type] += count;
        if (remote) instance.orders.Add(new Order { Robot = attr, Is42 = is42, Count = count, Due = Time.time + AmmoExchangeRules.Delay });
        else if (is42) attr.AddAmmo42mm(count);
        else attr.AddAmmo17mm(count);
        return null;
    }
    void Update()
    {
        if (!LanSession.CanSimulate || MatchOutcome.Decided) return;
        for (int i = orders.Count - 1; i >= 0; i--)
        {
            var order = orders[i];
            if (Time.time < order.Due) continue;
            if (order.Robot != null)
            {
                if (order.Is42) order.Robot.AddAmmo42mm(order.Count);
                else order.Robot.AddAmmo17mm(order.Count);
            }
            orders.RemoveAt(i);
        }
    }
}
