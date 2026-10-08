using System;

// 队伍共享余额；不依赖 Unity，便于验证余额边界和购买原子性。
public sealed class TeamCoinLedger
{
    public int Red { get; private set; }
    public int Blue { get; private set; }

    public int Balance(bool blue) => blue ? Blue : Red;

    public bool Grant(int red, int blue)
    {
        if (red < 0 || blue < 0 || red > int.MaxValue - Red || blue > int.MaxValue - Blue)
            return false;
        Red += red;
        Blue += blue;
        return true;
    }

    public bool Spend(bool blue, int cost)
    {
        if (cost < 0 || cost > Balance(blue)) return false;
        if (blue) Blue -= cost;
        else Red -= cost;
        return true;
    }

    public void Apply(int red, int blue)
    {
        Red = Math.Max(0, red);
        Blue = Math.Max(0, blue);
    }
}
