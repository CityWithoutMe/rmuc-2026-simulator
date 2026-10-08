using System;

public static class OutpostRules
{
    public const double Speed = .8 * Math.PI;
    public static double AngleAt(double seconds)
    {
        double t = Math.Max(0, seconds), ramp = Math.Min(t, 5);
        return Speed * (ramp * ramp / 10 + Math.Max(0, t - 5));
    }
    public static bool Stop(double elapsed, bool everDestroyed, bool enemyArmorOpen)
        => elapsed >= 180 || everDestroyed || enemyArmorOpen;
}

// 基地实际掉血（不含护盾）每累计 1000 获得一次机会，消耗不会清空余量。
public sealed class OutpostRebuildLedger
{
    double lost;
    int used;
    public int Available => Math.Max(0, (int)(lost / 1000) - used);
    public void RecordDamage(double damage) { if (damage > 0) lost += damage; }
    public bool Consume(double elapsed, bool destroyed)
    {
        if (elapsed >= 300 || !destroyed || Available == 0) return false;
        used++; return true;
    }
}
