using System;

// 2026 5.5.2，单位 rad、s。解析积分避免帧率造成正弦角速度的累计误差。
public static class RuneRotationRules
{
    public const double ConstantSpeed = Math.PI / 3;
    public static double Speed(bool activating, double time, double a, double omega)
        => activating ? a * Math.Sin(omega * time) + 2.090 - a : ConstantSpeed;

    public static double Delta(bool activating, double time, double dt, double a, double omega)
    {
        if (dt <= 0) return 0;
        if (!activating) return ConstantSpeed * dt;
        return a / omega * (Math.Cos(omega * time) - Math.Cos(omega * (time + dt)))
            + (2.090 - a) * dt;
    }
}
