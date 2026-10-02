using UnityEngine;

// 数字出处：《RoboMaster 2026 机甲大师超级对抗赛比赛规则手册》V2.2.0（20260807），E:\ 下该 PDF。
// 5.4.1 表 5-12：等级总经验。5.4.1「机器人战亡」：击毁经验。
// 5.4.2：未选类型时英雄默认「远程优先」（表 5-13），步兵底盘默认「血量优先」（表 5-14），
// 步兵发射机构默认「冷却优先」（表 5-15）。表里没有射速列，不改 FireRate。
// 英雄 1 级冷却在表 5-13 远程优先就是 20/秒。项目 1 级保持 20，不改成 40。
// 升级只加「该级相对表内 1 级」的增量，叠到项目里已经写好的 1 级基础值上。
// 5.2.1 远程兑换血量：脱战时确认，6 秒后加此时上限的 60%（不超过上限）。6 秒内战亡则无效且不退金币。
// 表 5-6：血量 50+ROUNDUP((420-剩余秒)/60*20) 金币/次，次数不限。
// 5.2.2：读条 = 10 + (420-剩余秒)/10 + 20×累计立即复活次数，四舍五入。
// 读条复活：10% 上限血量，无敌 30 秒，虚弱锁发射直到增益点卡；卡到时无敌已超过 10 秒则一起解除，否则无敌计满 10 秒。
// 立即复活：100% 血，虚弱和无敌 3 秒，底盘功率上限×2 且不超过 200W、持续 4 秒。
// 表 5-6：立即复活 ROUNDUP((420-剩余秒)/60)*80 + 等级*20 金币/次，次数不限。
// 已进行秒数用 MatchTimer.ElapsedSeconds（默认总长 420 秒时等于 420-剩余秒）。
public static class RobotLevelRules
{
    public const int MaxLevel = 10;

    // 表 5-12。下标 1 起是该级所需总经验，下标 0 不用。
    static readonly int[] TotalExp =
    {
        0,
        0, 550, 1100, 1650, 2200, 2750, 3300, 3850, 4400, 5000
    };

    // 表 5-13 远程优先：上限血量、底盘功率、热量上限、冷却/秒。下标 0 不用。
    static readonly int[] HeroHp = { 0, 200, 220, 240, 260, 280, 300, 320, 340, 360, 400 };
    static readonly int[] HeroPower = { 0, 50, 55, 60, 65, 70, 75, 80, 85, 90, 100 };
    static readonly int[] HeroHeat = { 0, 160, 162, 164, 166, 168, 170, 175, 180, 185, 190 };
    static readonly int[] HeroCool = { 0, 20, 23, 26, 29, 32, 35, 38, 41, 44, 50 };

    // 表 5-14 血量优先：上限血量、底盘功率。
    static readonly int[] InfantryHp = { 0, 200, 225, 250, 275, 300, 325, 350, 375, 400, 400 };
    static readonly int[] InfantryPower = { 0, 45, 50, 55, 60, 65, 70, 75, 80, 90, 100 };

    // 表 5-15 冷却优先：热量上限、冷却/秒。
    static readonly int[] InfantryHeat = { 0, 40, 48, 56, 64, 72, 80, 88, 96, 114, 120 };
    static readonly int[] InfantryCool = { 0, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30 };

    public static bool CanGainExperience(RobotType type)
    {
        return type == RobotType.Hero || type == RobotType.Infantry || type == RobotType.Aerial;
    }

    // 5.4.1 发射弹丸。英雄每发 10，切到 17mm 仍按英雄算。步兵、空中每发 1。工程、哨兵为 0。
    public const float ShotExpHero = 10f;
    public const float ShotExpStandard = 1f;

    // 5.4.1 造成攻击伤害。只乘真正扣掉的血量。
    public const float RobotDamageExpPerHp = 4f;
    public const float OutpostDamageExpPerHp = 2f;

    public static float ShotExperience(RobotType type)
    {
        if (type == RobotType.Hero)
            return ShotExpHero;
        if (type == RobotType.Infantry || type == RobotType.Aerial)
            return ShotExpStandard;
        return 0f;
    }

    public static float RobotDamageExperience(float hpLost)
    {
        if (hpLost <= 0f)
            return 0f;
        return hpLost * RobotDamageExpPerHp;
    }

    public static float OutpostArmorExperience(float hpLost)
    {
        if (hpLost <= 0f)
            return 0f;
        return hpLost * OutpostDamageExpPerHp;
    }

    // 基地装甲：每 2 点伤害 1 经验，奇数向上取整。护盾伤害不要传进来。
    public static float BaseArmorExperience(float hpLost)
    {
        if (hpLost <= 0f)
            return 0f;
        return Mathf.Ceil(hpLost / 2f);
    }

    // 5.4.1：工程、哨兵在击毁经验里都按 1 级。
    public static int LevelForKill(RobotAttributeManager robot)
    {
        if (robot == null)
            return 1;
        if (robot.robotType == RobotType.Engineer || robot.robotType == RobotType.Sentry)
            return 1;
        return Mathf.Clamp(Mathf.RoundToInt(robot.GetCurrent(RobotStat.Level)), 1, MaxLevel);
    }

    public static bool IsRobotKillTarget(RobotType type)
    {
        return type == RobotType.Hero
            || type == RobotType.Infantry
            || type == RobotType.Engineer
            || type == RobotType.Sentry
            || type == RobotType.Aerial;
    }

    public static int LevelFromTotalExp(float exp)
    {
        int level = 1;
        for (int lv = MaxLevel; lv >= 1; lv--)
        {
            if (exp >= TotalExp[lv])
            {
                level = lv;
                break;
            }
        }

        return level;
    }

    public static int TotalExpForLevel(int level)
    {
        level = Mathf.Clamp(level, 1, MaxLevel);
        return TotalExp[level];
    }

    // 被击毁者等级 >= 击毁者：50 * 被击毁等级 * (1 + 0.2 * 等级差)。否则：50 * 被击毁等级。
    public static float KillExperience(int victimLevel, int killerLevel)
    {
        victimLevel = Mathf.Clamp(victimLevel, 1, MaxLevel);
        killerLevel = Mathf.Clamp(killerLevel, 1, MaxLevel);
        float exp = victimLevel >= killerLevel
            ? 50f * victimLevel * (1f + 0.2f * (victimLevel - killerLevel))
            : 50f * victimLevel;
        return Mathf.Round(exp * 10f) / 10f;
    }

    public const float OutOfCombatSeconds = 6f;
    public const float RemoteHealDelaySeconds = 6f;
    public const float RemoteHealFraction = 0.6f;
    public const float NaturalReviveHpFraction = 0.1f;
    public const float NaturalInvulnerableSeconds = 30f;
    public const float NaturalInvulnerableFloorSeconds = 10f;
    public const float ImmediateReviveHpFraction = 1f;
    public const float ImmediateWeakSeconds = 3f;
    public const float ImmediateInvulnerableSeconds = 3f;
    public const float ImmediatePowerSeconds = 4f;
    public const float ImmediatePowerCapWatts = 200f;

    // 5.2.2。elapsedSeconds = 420 - 比赛剩余秒。小数四舍五入。
    public static int NaturalReviveSeconds(float elapsedSeconds, int immediateReviveCount)
    {
        float seconds = 10f + Mathf.Max(0f, elapsedSeconds) / 10f + 20f * Mathf.Max(0, immediateReviveCount);
        return Mathf.Max(1, Mathf.RoundToInt(seconds));
    }

    // 表 5-6。ROUNDUP 向上取整到个位。剩余秒超过 420 时按 0 计增量。
    public static int RemoteHealPrice(float remainingSeconds)
    {
        float elapsed = Mathf.Max(0f, 420f - Mathf.Max(0f, remainingSeconds));
        return 50 + Mathf.CeilToInt(elapsed / 60f * 20f);
    }

    public static int ImmediateRevivePrice(float remainingSeconds, int level)
    {
        float elapsed = Mathf.Max(0f, 420f - Mathf.Max(0f, remainingSeconds));
        int minutes = Mathf.CeilToInt(elapsed / 60f);
        level = Mathf.Clamp(level, 1, MaxLevel);
        return minutes * 80 + level * 20;
    }

    public static bool CanRemoteHealType(RobotType type)
    {
        return type == RobotType.Hero || type == RobotType.Infantry || type == RobotType.Sentry;
    }

    public static bool TryGetPerformanceDelta(
        RobotType type,
        int level,
        out float maxHp,
        out float power,
        out float heatLimit,
        out float cooling)
    {
        maxHp = 0f;
        power = 0f;
        heatLimit = 0f;
        cooling = 0f;
        level = Mathf.Clamp(level, 1, MaxLevel);

        if (type == RobotType.Hero)
        {
            maxHp = HeroHp[level] - HeroHp[1];
            power = HeroPower[level] - HeroPower[1];
            heatLimit = HeroHeat[level] - HeroHeat[1];
            cooling = HeroCool[level] - HeroCool[1];
            return true;
        }

        if (type == RobotType.Infantry)
        {
            maxHp = InfantryHp[level] - InfantryHp[1];
            power = InfantryPower[level] - InfantryPower[1];
            heatLimit = InfantryHeat[level] - InfantryHeat[1];
            cooling = InfantryCool[level] - InfantryCool[1];
            return true;
        }

        return false;
    }
}
