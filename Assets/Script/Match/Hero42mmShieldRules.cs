// 5.3.2：状态属于攻击方英雄，屏蔽作用于对方所有装甲。
public sealed class Hero42mmShieldRules
{
    public bool Blocked { get; private set; }
    double unavailableSince = -1;
    int shotsAfterDeath;
    bool wasDead;

    public void Observe(double now, bool alive, bool online, int allowance)
    {
        if (!alive && !wasDead) shotsAfterDeath = 0;
        wasDead = !alive;
        if (!alive || !online)
        {
            if (unavailableSince < 0) unavailableSince = now;
        }
        if (alive && online && allowance > 0)
        {
            unavailableSince = -1;
            Blocked = false;
        }
        else if (unavailableSince >= 0 && now - unavailableSince >= 3) Blocked = true;
    }

    // 必须传发射前的允许发弹量；合法的最后一发不会被误判为超发。
    public void Fired(double now, bool alive, bool online, int allowanceBefore)
    {
        Observe(now, alive, online, allowanceBefore);
        if (allowanceBefore <= 0) Blocked = true;
        if (!alive && ++shotsAfterDeath >= 3) Blocked = true;
    }
}
