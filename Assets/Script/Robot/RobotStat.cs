using System;
using UnityEngine;

// 队伍。中立用于场地机关 / 未分配。
public enum RobotTeam
{
    Neutral = 0,
    Red = 1,
    Blue = 2
}

// RM 常见兵种 / 建筑物。数值默认按常见赛季量级，可在 Inspector 改。
public enum RobotType
{
    Infantry = 0, // 步兵
    Hero = 1,     // 英雄
    Sentry = 2,   // 哨兵
    Engineer = 3, // 工程
    Aerial = 4,   // 空中支援
    Radar = 5,    // 雷达站
    Dart = 6,     // 飞镖系统
    Outpost = 7,  // 前哨站
    Base = 8      // 基地
}

// 可走基础值 / 当前值 / 修饰器的数值属性。
// 身份字段（robotId、队伍、兵种）不进这个枚举。
public enum RobotStat
{
    // —— 等级 ——
    Level,
    Exp,

    // —— 生存（装甲板暂不拆，用总血 + IsAlive / IsInvulnerable）——
    Hp,
    MaxHp,
    ShieldHp,
    ReviveCount,
    ReviveRemaining,

    // —— 移动 / 底盘 ——
    MoveSpeed,
    RotateSpeed,
    ChassisPowerLimit,
    ChassisPowerBuffer,
    SpinSpeed,
    CanMove,

    // —— 云台 ——
    GimbalYawSpeed,
    GimbalPitchSpeed,
    CanShoot,

    // —— 射击 / 武器 ——
    Ammo17mm,
    Ammo42mm,
    MaxAmmo17mm,
    MaxAmmo42mm,
    BarrelHeat,
    BarrelHeatLimit,
    CoolingRate,
    FireRate,
    ProjectileSpeed,
    Damage,
    AttackBuffPercent,
    DefenseBuffPercent,
    CooldownBuffPercent,

    // —— 经济 / 裁判 ——
    Coins,
    RemainingFoul,
    IsFoulOut,
    RfidZoneId,
    CurrentZoneFlags,

    // —— 状态 ——
    IsPowered,
    IsIdle,
    IsDefeated,
    IsInvulnerable,
    RecoveryRate
}

// 属性修饰器：加算 + 乘区。区域 Buff 建议 stackable = false，用引用计数处理重叠。
[Serializable]
public class StatModifier
{
    [Tooltip("来源 id，例如 zone_buff_a。同源非叠加时会加引用计数并刷新时间")]
    public string sourceId;

    [Tooltip("加算，先加到基础值上")]
    public float add;

    [Tooltip("乘区。0.5 表示 +50%，公式：(base + Σadd) * (1 + Σpercent)")]
    public float percent;

    [Tooltip("同源是否叠多份。区域 Buff 请保持关闭，用引用计数")]
    public bool stackable;

    [Tooltip("持续时间（秒）。小于 0 表示永久")]
    public float duration = -1f;

    [Tooltip("引用计数。同一 Buff 多个区域重叠时 +1，减到 0 才移除")]
    public int refCount = 1;

    // 运行时剩余时间，永久修饰器为 -1
    [NonSerialized] public float remaining = -1f;

    public bool IsPermanent => duration < 0f;

    public static StatModifier PercentBonus(string sourceId, float percent, float duration = -1f)
    {
        return new StatModifier
        {
            sourceId = sourceId,
            percent = percent,
            duration = duration,
            stackable = false
        };
    }

    public static StatModifier Additive(string sourceId, float add, float duration = -1f)
    {
        return new StatModifier
        {
            sourceId = sourceId,
            add = add,
            duration = duration,
            stackable = false
        };
    }
}
