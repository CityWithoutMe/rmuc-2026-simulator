using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;

// 挂到机器人上的属性管理器。当前值 = 基础值经修饰器计算；资源型属性单独存量并 Clamp。
// 后续区域 Buff / 裁判 / 移动直接 GetCurrent、AddModifier、TakeDamage 即可。
public class RobotAttributeManager : MonoBehaviour
{
    public const string DeadSourceId = "_internal_dead";
    public const string FoulOutSourceId = "_internal_foul";
    public const string WeakSourceId = "_revive_weak";
    public const string ImmediatePowerSourceId = "_immediate_power";

    static readonly int StatCount = Enum.GetValues(typeof(RobotStat)).Length;
    float[] lanValues;

    // —— 身份（不是修饰器目标）——
    [Header("身份")]
    public int robotId = 3;
    public RobotTeam team = RobotTeam.Red;
    public RobotType robotType = RobotType.Infantry;

    [Header("等级（步兵/英雄升级，可按赛季改）")]
    public int baseLevel = 1;
    public float baseExp = 0f;

    [Header("生存（一级血量优先步兵 / 远程优先英雄均为 200HP）")]
    [Tooltip("开局血量，通常等于最大血量")]
    public float baseHp = 200f;
    [Tooltip("2026 一级默认：血量优先步兵 200，远程优先英雄 200")]
    public float baseMaxHp = 200f;
    [Tooltip("基地护盾等。普通机器人默认 0")]
    public float baseShieldHp = 0f;
    [Tooltip("已复活次数的开局值，一般是 0")]
    public int baseReviveCount = 0;
    [Tooltip("剩余复活次数。小于 0 表示不限制；建筑物建议 0")]
    public int baseReviveRemaining = -1;

    [Header("移动 / 底盘（单位：m/s、deg/s、W）")]
    [Tooltip("平移速度 m/s，后续起伏地面移动可读这个当前值")]
    public float baseMoveSpeed = 5f;
    [Tooltip("底盘转向速度 deg/s")]
    public float baseRotateSpeed = 180f;
    [Tooltip("2026 一级默认：血量优先步兵 45W，远程优先英雄 50W")]
    public float baseChassisPowerLimit = 45f;
    [Tooltip("超级电容 / 缓冲能量，开局视为满")]
    public float baseChassisPowerBuffer = 60f;
    [Tooltip("小陀螺转速 deg/s")]
    public float baseSpinSpeed = 360f;
    public bool baseCanMove = true;

    [Header("云台（deg/s）")]
    public float baseGimbalYawSpeed = 300f;
    public float baseGimbalPitchSpeed = 300f;
    public bool baseCanShoot = true;

    [Header("射击 / 武器（弹药上限可按赛季改）")]
    [Tooltip("17mm 开局弹药")]
    public int baseAmmo17mm = 0;
    [Tooltip("42mm 开局弹药，英雄与步兵均为 0，由队伍金币购买")]
    public int baseAmmo42mm = 0;
    public int baseMaxAmmo17mm = 500;
    public int baseMaxAmmo42mm = 0;
    public float baseBarrelHeat = 0f;
    [Tooltip("2026 一级默认：冷却优先步兵 40，远程优先英雄 160")]
    public float baseBarrelHeatLimit = 40f;
    [Tooltip("冷却速度，热量/秒")]
    public float baseCoolingRate = 12f;
    [Tooltip("射速，发/秒")]
    public float baseFireRate = 10f;
    [Tooltip("弹速 m/s")]
    public float baseProjectileSpeed = 30f;
    [Tooltip("自定义伤害属性。规则机器人装甲弹丸伤害按弹种固定为 17mm 20、42mm 200，再计算攻防增益")]
    public float baseDamage = 10f;
    [Tooltip("攻击加成。0.5 = +50%。弹丸最终伤害 = 基础伤害 * (1 + 攻击加成) * (1 - 对方防御)")]
    public float baseAttackBuffPercent = 0f;
    [Tooltip("防御加成。0.5 = 承伤 50%。弹丸结算已乘 (1 - Clamp01(防御))；TakeDamage 默认还会再乘一次，所以弹丸要传 ignoreDefense")]
    public float baseDefenseBuffPercent = 0f;
    [Tooltip("冷却加成显示值。实际冷却乘区请用修饰器贴到 CoolingRate")]
    public float baseCooldownBuffPercent = 0f;

    [Header("经济 / 裁判")]
    public int baseCoins = 0;
    [Tooltip("剩余犯规余地，可按赛季改")]
    public int baseRemainingFoul = 4;
    public bool baseIsFoulOut = false;
    [Tooltip("当前 RFID / 功能区 id，0 表示不在区")]
    public int baseRfidZoneId = 0;
    [Tooltip("功能区 bitmask，供区域管理器使用")]
    public int baseCurrentZoneFlags = 0;

    [Header("状态")]
    public bool baseIsPowered = true;
    public bool baseIsIdle = false;
    public bool baseIsDefeated = false;
    public bool baseIsInvulnerable = false;
    [Tooltip("回血速度 HP/秒。回血区用修饰器叠到这个属性")]
    public float baseRecoveryRate = 0f;

    [Header("自动结算")]
    [Tooltip("每帧按 CoolingRate 降温")]
    public bool autoCoolBarrel = true;
    [Tooltip("每帧按 RecoveryRate 回血")]
    public bool autoRecoverHp = true;

    [Header("运行时查看（每帧刷新。要改对局数值请改上面的基础值）")]
    [SerializeField] float viewHp;
    [SerializeField] float viewMaxHp;
    [SerializeField] float viewMoveSpeed;
    [SerializeField] float viewDamage;
    [SerializeField] float viewBarrelHeat;
    [SerializeField] float viewAmmo17mm;
    [SerializeField] float viewAmmo42mm;
    [SerializeField] bool viewAlive;
    [SerializeField] bool viewCanMove;
    [SerializeField] bool viewCanShoot;

    [Header("事件（也可代码订阅 StatChanged / Died / Revived）")]
    public UnityEvent onDeath = new UnityEvent();
    public UnityEvent onRevive = new UnityEvent();

    // stat, oldValue, newValue
    public event Action<RobotStat, float, float> StatChanged;
    public event Action<RobotAttributeManager> Died;
    public event Action<RobotAttributeManager> Revived;

    float[] runtimeBase;
    float[] stored; // 资源型当前存量
    List<StatModifier>[] mods;
    bool inited;
    bool deathEventFired;
    bool applyingInspector;

    // 检视器公开值上次写进运行时的快照。只在有变化时写回，不在 Update 里用默认值盖掉存量。
    float[] inspectorSynced;
    float[] inspectorScratch;

    // 规则常量。出处：V2.2.0 第 5.1.3 节「射击热量超限和冷却」。
    // 每发 17mm 热量 +10，每发 42mm 热量 +100，与初速度无关。
    // 超限不再按 (热量-上限)/250×最大血量 扣血。改为锁定发射机构，见 TickBarrelHeatLock。
    public const float HeatPer17mmRound = 10f;
    public const float HeatPer42mmRound = 100f;
    // 步兵默认射速（发/秒）。英雄切到 17mm 时读这个数，不改英雄自己的 FireRate。
    public const float InfantryBaseFireRate = 10f;
    // 17mm 发射机构 Q2 = Q0 + 100；42mm 发射机构 Q2 = Q0 + 200。超过 Q2 则本局永久锁定。
    public const float SevereExcess17mm = 100f;
    public const float SevereExcess42mm = 200f;

    // Q1 曾经超过 Q0 后保持锁定，直到热量回到 0 才解锁。超过 Q2 后本局不再解锁。
    bool barrelLockedUntilZero;
    bool barrelLockedForMatch;

    // 项目 1 级基础值。升级只加规则表相对 1 级的增量，不改写这些开局数。
    bool levelBaselineReady;
    int appliedPerformanceLevel = -1;
    float level1MaxHp;
    float level1Power;
    float level1Heat;
    float level1Cool;
    float reviveSecondsRemaining;
    float invulnerableSecondsRemaining;
    float weakSecondsRemaining;
    float naturalInvulnerableElapsed;
    float remoteHealSecondsRemaining;
    float secondsSinceCombat = RobotLevelRules.OutOfCombatSeconds;
    int immediateReviveCount;
    bool weakUntilBuffCard;
    bool naturalInvulnerableActive;

    public int Level => Mathf.Clamp(Mathf.RoundToInt(GetCurrent(RobotStat.Level)), 1, RobotLevelRules.MaxLevel);
    public float Experience => GetCurrent(RobotStat.Exp);
    public float ReviveSecondsRemaining => reviveSecondsRemaining;
    public float InvulnerableSecondsRemaining => invulnerableSecondsRemaining;
    public float WeakSecondsRemaining => weakSecondsRemaining;
    public float RemoteHealSecondsRemaining => remoteHealSecondsRemaining;
    public int ImmediateReviveCount => immediateReviveCount;
    public bool IsWeak => HasModifier(RobotStat.CanShoot, WeakSourceId) || weakUntilBuffCard || weakSecondsRemaining > 0f;
    // 5.2 文首：存活且连续 6 秒未发射、未扣血。开局默认脱战。
    public bool IsOutOfCombat => IsAlive && secondsSinceCombat >= RobotLevelRules.OutOfCombatSeconds;

    public bool IsAlive => GetCurrent(RobotStat.Hp) > 0f && GetCurrent(RobotStat.IsDefeated) < 0.5f;
    public bool IsInvulnerable => GetCurrent(RobotStat.IsInvulnerable) > 0.5f;
    public bool IsPowered => GetCurrent(RobotStat.IsPowered) > 0.5f;
    public bool IsIdle => GetCurrent(RobotStat.IsIdle) > 0.5f;
    public bool IsDefeated => GetCurrent(RobotStat.IsDefeated) > 0.5f;
    public bool IsFoulOut => GetCurrent(RobotStat.IsFoulOut) > 0.5f;
    // 当前热量 Q1 严格大于上限 Q0。等于上限仍不算超限。
    public bool IsOverheated => GetCurrent(RobotStat.BarrelHeat) > GetCurrent(RobotStat.BarrelHeatLimit) + 0.0001f;

    // 5.1.3：一旦 Q1 > Q0 就锁定，直到 Q1 回到 0 才解锁。Q1 > Q2 则本局永久锁定。
    public bool IsBarrelLocked => barrelLockedForMatch || barrelLockedUntilZero;
    public bool IsBarrelLockedForMatch => barrelLockedForMatch;

    // 给移动 / 射击用：自身开关 + 存活 + 上电 + 发射机构未锁定
    public bool CanMove => IsAlive && IsPowered && !IsFoulOut && GetCurrent(RobotStat.CanMove) > 0.5f;
    public bool CanShoot => IsAlive && IsPowered && !IsFoulOut && GetCurrent(RobotStat.CanShoot) > 0.5f && !IsBarrelLocked;

    public float Hp => GetCurrent(RobotStat.Hp);
    public float MaxHp => GetCurrent(RobotStat.MaxHp);
    public float MoveSpeed => GetCurrent(RobotStat.MoveSpeed);
    public float RotateSpeed => GetCurrent(RobotStat.RotateSpeed);
    public float Damage => GetCurrent(RobotStat.Damage);

    void Awake()
    {
        EnsureInit();
        RefreshView();
    }

    void Update()
    {
        if (!inited) return;
        if (!LanSession.CanSimulate) { RefreshView(); return; }

        // 公开基础值相对上次快照有变化才写回存量。没改过的血量、热量、弹药保持原值，不用默认数覆盖。
        if (!applyingInspector)
        {
            applyingInspector = true;
            ApplyInspectorDeltas();
            applyingInspector = false;
        }

        float dt = Time.deltaTime;
        TickModifiers(dt);

        // 5.1.3：冷却按每秒冷却值连续扣，英雄仍是 20/秒。10 Hz 结算（每周期扣 每秒冷却值/10）与这个速率相同。
        if (autoCoolBarrel)
            AddBarrelHeat(-GetCurrent(RobotStat.CoolingRate) * dt);

        TickBarrelHeatLock();

        if (autoRecoverHp && IsAlive)
        {
            float regen = GetCurrent(RobotStat.RecoveryRate);
            if (regen > 0f)
                Heal(regen * dt);
        }

        TickLevelPerformance();
        TickNaturalRevive(dt);
        TickReviveAftermath(dt);
        TickRemoteHeal(dt);
        if (IsAlive)
            secondsSinceCombat += dt;

        RefreshView();
    }

    // Play 模式在检视器里改公开基础值时，写回正在使用的存量或基础值。
    // 血量、热量、弹药会变当前存量；上限、射速、冷却、伤害等会变当前计算值。
    void OnValidate()
    {
        if (!Application.isPlaying || !inited || applyingInspector)
            return;

        applyingInspector = true;
        ApplyInspectorDeltas();
        RefreshView();
        applyingInspector = false;
    }

    [ContextMenu("按兵种填入默认基础值（可按赛季改）")]
    public void ApplyRobotTypeDefaults()
    {
        // 先恢复成“不能打、没有弹药”的机关默认，再按兵种补
        baseLevel = 1;
        baseExp = 0f;
        baseShieldHp = 0f;
        baseReviveCount = 0;
        baseChassisPowerBuffer = 60f;
        baseSpinSpeed = 360f;
        baseCanMove = true;
        baseGimbalYawSpeed = 300f;
        baseGimbalPitchSpeed = 300f;
        baseCanShoot = true;
        baseBarrelHeat = 0f;
        baseAttackBuffPercent = 0f;
        baseDefenseBuffPercent = 0f;
        baseCooldownBuffPercent = 0f;
        baseCoins = 0;
        baseRemainingFoul = 4;
        baseIsFoulOut = false;
        baseRfidZoneId = 0;
        baseCurrentZoneFlags = 0;
        baseIsPowered = true;
        baseIsIdle = false;
        baseIsDefeated = false;
        baseIsInvulnerable = false;
        baseRecoveryRate = 0f;

        switch (robotType)
        {
            case RobotType.Hero:
                robotId = team == RobotTeam.Blue ? 101 : 1;
                baseHp = 200f;
                baseMaxHp = 200f;
                baseReviveRemaining = -1;
                baseMoveSpeed = 4.5f;
                baseRotateSpeed = 160f;
                baseChassisPowerLimit = 50f;
                baseAmmo17mm = 0;
                baseAmmo42mm = 0;
                baseMaxAmmo17mm = 200;
                baseMaxAmmo42mm = 16;
                // V2.2.0 表 5-13：一级远程优先，热量上限 160、冷却 20/秒。
                baseBarrelHeatLimit = 160f;
                baseCoolingRate = 20f;
                baseFireRate = 2f;
                baseProjectileSpeed = 16f;
                baseDamage = CombatDamage.RobotArmorDamage42mm;
                break;

            case RobotType.Sentry:
                robotId = team == RobotTeam.Blue ? 107 : 7;
                baseHp = 600f;
                baseMaxHp = 600f;
                baseReviveRemaining = 0;
                baseMoveSpeed = 3.5f;
                baseRotateSpeed = 200f;
                baseChassisPowerLimit = 20f;
                baseSpinSpeed = 480f;
                baseAmmo17mm = 750;
                baseAmmo42mm = 0;
                baseMaxAmmo17mm = 750;
                baseMaxAmmo42mm = 0;
                baseBarrelHeatLimit = 240f;
                baseCoolingRate = 12f;
                baseFireRate = 20f;
                baseProjectileSpeed = 30f;
                baseDamage = 10f;
                break;

            case RobotType.Engineer:
                robotId = team == RobotTeam.Blue ? 102 : 2;
                baseHp = 250f;
                baseMaxHp = 250f;
                baseReviveRemaining = -1;
                baseMoveSpeed = 4f;
                baseChassisPowerLimit = 80f;
                baseCanShoot = false;
                baseAmmo17mm = 0;
                baseAmmo42mm = 0;
                baseMaxAmmo17mm = 0;
                baseMaxAmmo42mm = 0;
                baseBarrelHeatLimit = 0f;
                baseCoolingRate = 0f;
                baseFireRate = 0f;
                baseProjectileSpeed = 0f;
                baseDamage = 0f;
                break;

            case RobotType.Aerial:
                robotId = team == RobotTeam.Blue ? 106 : 6;
                baseHp = 100f;
                baseMaxHp = 100f;
                baseReviveRemaining = 0;
                baseMoveSpeed = 6f;
                baseChassisPowerLimit = 0f;
                baseChassisPowerBuffer = 0f;
                baseSpinSpeed = 0f;
                baseAmmo17mm = 500;
                baseAmmo42mm = 0;
                baseMaxAmmo17mm = 500;
                baseMaxAmmo42mm = 0;
                baseBarrelHeatLimit = 240f;
                baseCoolingRate = 12f;
                baseFireRate = 10f;
                baseProjectileSpeed = 30f;
                baseDamage = 10f;
                break;

            case RobotType.Radar:
                robotId = team == RobotTeam.Blue ? 109 : 9;
                baseHp = 100f;
                baseMaxHp = 100f;
                baseReviveRemaining = 0;
                baseCanMove = false;
                baseCanShoot = false;
                baseMoveSpeed = 0f;
                baseRotateSpeed = 0f;
                baseChassisPowerLimit = 0f;
                baseChassisPowerBuffer = 0f;
                baseSpinSpeed = 0f;
                ZeroWeaponBases();
                break;

            case RobotType.Dart:
                robotId = team == RobotTeam.Blue ? 108 : 8;
                baseHp = 500f;
                baseMaxHp = 500f;
                baseReviveRemaining = 0;
                baseCanMove = false;
                baseMoveSpeed = 0f;
                baseRotateSpeed = 0f;
                baseChassisPowerLimit = 0f;
                baseChassisPowerBuffer = 0f;
                baseSpinSpeed = 0f;
                baseAmmo17mm = 0;
                baseAmmo42mm = 0;
                baseMaxAmmo17mm = 0;
                baseMaxAmmo42mm = 0;
                baseBarrelHeatLimit = 0f;
                baseCoolingRate = 0f;
                baseFireRate = 1f;
                baseProjectileSpeed = 18f;
                baseDamage = 1000f;
                break;

            case RobotType.Outpost:
                robotId = team == RobotTeam.Blue ? 110 : 10;
                baseHp = 1500f;
                baseMaxHp = 1500f;
                baseReviveRemaining = 0;
                baseCanMove = false;
                baseCanShoot = false;
                baseMoveSpeed = 0f;
                baseRotateSpeed = 0f;
                baseChassisPowerLimit = 0f;
                baseChassisPowerBuffer = 0f;
                baseSpinSpeed = 0f;
                ZeroWeaponBases();
                break;

            case RobotType.Base:
                robotId = team == RobotTeam.Blue ? 111 : 11;
                baseHp = 5000f;
                baseMaxHp = 5000f;
                baseShieldHp = 500f;
                baseReviveRemaining = 0;
                baseCanMove = false;
                baseCanShoot = false;
                baseMoveSpeed = 0f;
                baseRotateSpeed = 0f;
                baseChassisPowerLimit = 0f;
                baseChassisPowerBuffer = 0f;
                baseSpinSpeed = 0f;
                ZeroWeaponBases();
                break;

            default: // Infantry
                robotId = team == RobotTeam.Blue ? 103 : 3;
                baseHp = 200f;
                baseMaxHp = 200f;
                baseReviveRemaining = -1;
                baseMoveSpeed = 5f;
                baseRotateSpeed = 180f;
                baseChassisPowerLimit = 45f;
                baseAmmo17mm = 0;
                baseAmmo42mm = 0;
                baseMaxAmmo17mm = 500;
                baseMaxAmmo42mm = 0;
                baseBarrelHeatLimit = 40f;
                baseCoolingRate = 12f;
                baseFireRate = InfantryBaseFireRate;
                baseProjectileSpeed = 30f;
                baseDamage = 10f;
                break;
        }

        if (Application.isPlaying)
            ResetToBase();
    }

    void ZeroWeaponBases()
    {
        baseAmmo17mm = 0;
        baseAmmo42mm = 0;
        baseMaxAmmo17mm = 0;
        baseMaxAmmo42mm = 0;
        baseBarrelHeatLimit = 0f;
        baseCoolingRate = 0f;
        baseFireRate = 0f;
        baseProjectileSpeed = 0f;
        baseDamage = 0f;
        baseGimbalYawSpeed = 0f;
        baseGimbalPitchSpeed = 0f;
    }

    // —— 基础值 / 当前值 ——

    public float GetBase(RobotStat stat)
    {
        EnsureInit();
        return runtimeBase[(int)stat];
    }

    public void SetBase(RobotStat stat, float value)
    {
        EnsureInit();
        float old = GetCurrent(stat);
        runtimeBase[(int)stat] = value;

        if (stat == RobotStat.ChassisPowerBuffer)
            SetStored(stat, stored[(int)stat], true); // 电容：基础值是容量，只夹紧当前能量
        else if (IsResource(stat))
            SetStored(stat, value, true);
        else
            ClampResourcesAffectedBy(stat);

        float now = GetCurrent(stat);
        RaiseChanged(stat, old, now);
    }

    public float GetCurrent(RobotStat stat)
    {
        EnsureInit();
        if (LanSession.IsClient && lanValues != null) return lanValues[(int)stat];
        if (IsResource(stat))
            return stored[(int)stat];
        return ComputeModified(stat);
    }

    // 资源型可直接写当前存量（会 Clamp）。派生属性请用修饰器，不要 SetCurrent。
    public void SetCurrent(RobotStat stat, float value)
    {
        EnsureInit();
        if (!IsResource(stat))
        {
            Debug.LogWarning("SetCurrent 只用于资源型属性（血量/弹药/热量等），派生属性请用 SetBase 或 AddModifier: " + stat, this);
            return;
        }

        SetStored(stat, value, false);
    }

    public float GetCap(RobotStat stat)
    {
        EnsureInit();
        switch (stat)
        {
            case RobotStat.Hp: return ComputeModified(RobotStat.MaxHp);
            case RobotStat.Ammo17mm: return ComputeModified(RobotStat.MaxAmmo17mm);
            case RobotStat.Ammo42mm: return ComputeModified(RobotStat.MaxAmmo42mm);
            case RobotStat.BarrelHeat: return ComputeModified(RobotStat.BarrelHeatLimit);
            case RobotStat.ChassisPowerBuffer: return Mathf.Max(0f, ComputeModified(RobotStat.ChassisPowerBuffer));
            case RobotStat.ShieldHp: return Mathf.Max(0f, ComputeModified(RobotStat.ShieldHp));
            default: return ComputeModified(stat);
        }
    }

    // —— 修饰器 ——

    public StatModifier AddModifier(RobotStat stat, StatModifier modifier)
    {
        EnsureInit();
        if (modifier == null) return null;

        if (string.IsNullOrEmpty(modifier.sourceId))
            modifier.sourceId = "_anon_" + Guid.NewGuid().ToString("N");

        var list = mods[(int)stat];
        float old = GetCurrent(stat);

        // 同源且不可叠加：加引用计数，并刷新持续时间
        if (!modifier.stackable)
        {
            StatModifier existing = FindModifier(stat, modifier.sourceId);
            if (existing != null)
            {
                existing.refCount++;
                existing.add = modifier.add;
                existing.percent = modifier.percent;
                existing.duration = modifier.duration;
                existing.remaining = modifier.duration;
                AfterModifierChanged(stat, old);
                return existing;
            }
        }

        var copy = CloneModifier(modifier);
        list.Add(copy);
        AfterModifierChanged(stat, old);
        return copy;
    }

    // 同源修饰器只改 add，不加引用计数。add 接近 0 时摘掉。
    public void SetPersistentAdd(RobotStat stat, string sourceId, float add)
    {
        EnsureInit();
        StatModifier existing = FindModifier(stat, sourceId);
        if (Mathf.Abs(add) < 0.0001f)
        {
            if (existing != null)
                RemoveModifier(stat, sourceId, true);
            return;
        }

        if (existing != null)
        {
            if (Mathf.Approximately(existing.add, add))
                return;
            float old = GetCurrent(stat);
            existing.add = add;
            existing.duration = -1f;
            existing.remaining = -1f;
            AfterModifierChanged(stat, old);
            return;
        }

        AddModifier(stat, StatModifier.Additive(sourceId, add));
    }

    // 5.5.3.9 堡垒「储备允许发弹量」。发射时优先扣这里，耗尽后再扣弹仓。
    int fortReserve;

    public int FortReserve => fortReserve;

    public void SetFortReserve(int amount)
    {
        fortReserve = Mathf.Max(0, amount);
    }

    public void ClearFortReserve()
    {
        fortReserve = 0;
    }

    public bool RemoveModifier(StatModifier modifier)
    {
        if (modifier == null) return false;
        EnsureInit();

        bool removed = false;
        for (int s = 0; s < StatCount; s++)
        {
            var list = mods[s];
            int idx = list.IndexOf(modifier);
            if (idx < 0) continue;

            var stat = (RobotStat)s;
            float old = GetCurrent(stat);
            list.RemoveAt(idx);
            AfterModifierChanged(stat, old);
            removed = true;
        }

        return removed;
    }

    // 对该属性上来源减一次引用；减到 0 移除。force 则直接摘掉。
    public bool RemoveModifier(RobotStat stat, string sourceId, bool force = false)
    {
        EnsureInit();
        if (string.IsNullOrEmpty(sourceId)) return false;

        var list = mods[(int)stat];
        float old = GetCurrent(stat);
        bool changed = ReleaseList(list, sourceId, force);
        if (changed)
            AfterModifierChanged(stat, old);
        return changed;
    }

    public void RemoveModifiersFromSource(string sourceId, bool force = false)
    {
        EnsureInit();
        if (string.IsNullOrEmpty(sourceId)) return;

        for (int s = 0; s < StatCount; s++)
        {
            var list = mods[s];
            if (list.Count == 0) continue;
            float old = GetCurrent((RobotStat)s);
            if (ReleaseList(list, sourceId, force))
                AfterModifierChanged((RobotStat)s, old);
        }
    }

    public bool HasModifier(string sourceId)
    {
        EnsureInit();
        if (string.IsNullOrEmpty(sourceId)) return false;
        for (int s = 0; s < StatCount; s++)
        {
            if (FindModifier((RobotStat)s, sourceId) != null)
                return true;
        }

        return false;
    }

    public bool HasModifier(RobotStat stat, string sourceId)
    {
        EnsureInit();
        return FindModifier(stat, sourceId) != null;
    }

    public int GetModifierRefCount(RobotStat stat, string sourceId)
    {
        EnsureInit();
        var m = FindModifier(stat, sourceId);
        return m != null ? m.refCount : 0;
    }

    public IReadOnlyList<StatModifier> GetModifiers(RobotStat stat)
    {
        EnsureInit();
        return mods[(int)stat];
    }

    public void ClearAllModifiers()
    {
        EnsureInit();
        for (int s = 0; s < StatCount; s++)
        {
            if (mods[s].Count == 0) continue;
            float old = GetCurrent((RobotStat)s);
            mods[s].Clear();
            AfterModifierChanged((RobotStat)s, old);
        }
    }

    // 功能区便捷接口：把乘区贴到伤害/移动/冷却上，同时写入裁判显示用百分比。
    public void AddZoneBuff(string sourceId, float attackPercent = 0f, float defensePercent = 0f, float cooldownPercent = 0f, float speedPercent = 0f)
    {
        if (attackPercent != 0f)
        {
            AddModifier(RobotStat.Damage, StatModifier.PercentBonus(sourceId, attackPercent));
            AddModifier(RobotStat.AttackBuffPercent, StatModifier.Additive(sourceId, attackPercent));
        }

        if (defensePercent != 0f)
            AddModifier(RobotStat.DefenseBuffPercent, StatModifier.Additive(sourceId, defensePercent));

        if (cooldownPercent != 0f)
        {
            AddModifier(RobotStat.CoolingRate, StatModifier.PercentBonus(sourceId, cooldownPercent));
            AddModifier(RobotStat.CooldownBuffPercent, StatModifier.Additive(sourceId, cooldownPercent));
        }

        if (speedPercent != 0f)
            AddModifier(RobotStat.MoveSpeed, StatModifier.PercentBonus(sourceId, speedPercent));
    }

    public void RemoveZoneBuff(string sourceId)
    {
        RemoveModifiersFromSource(sourceId);
    }

    // —— 资源操作 ——

    // ignoreDefense 为 false 时，这里再乘一次 (1 - Clamp01(防御))。
    // 弹丸伤害已在 CombatDamage.ComputeBulletDamage 里乘过防御，必须传 true，避免减两次。
    // suppressLog 默认 false。子弹命中已在 HandleBulletHit 打过详细公式，只那里传 true，避免同一发再打一行。
    // fromOverheat 保留参数。2026 手册 5.1.3 不再因超热量扣血，正常路径不会传 true。
    // 金额小于等于 0、已死、无敌、扣完血量没变（例如只打掉护盾）都不打印。
    public float TakeDamage(float amount, bool ignoreDefense = false, bool suppressLog = false, bool fromOverheat = false)
    {
        EnsureInit();
        if (amount <= 0f || !IsAlive || IsInvulnerable)
            return 0f;

        if (!ignoreDefense)
        {
            float def = Mathf.Min(1f, GetCurrent(RobotStat.DefenseBuffPercent));
            amount *= 1f - def;
        }

        if (amount <= 0f) return 0f;

        float hpBefore = stored[(int)RobotStat.Hp];
        float dealt = 0f;
        float shield = stored[(int)RobotStat.ShieldHp];
        if (shield > 0f)
        {
            float absorbed = Mathf.Min(shield, amount);
            SetStored(RobotStat.ShieldHp, shield - absorbed, false);
            amount -= absorbed;
            dealt += absorbed;
        }

        if (amount > 0f)
        {
            float hp = stored[(int)RobotStat.Hp];
            float absorbed = Mathf.Min(hp, amount);
            SetStored(RobotStat.Hp, hp - absorbed, false);
            dealt += absorbed;
        }

        if (stored[(int)RobotStat.Hp] <= 0f)
            Die();

        float hpAfter = stored[(int)RobotStat.Hp];
        if (hpAfter < hpBefore)
        {
            secondsSinceCombat = 0f;
            LogHpLoss(hpBefore - hpAfter, hpBefore, hpAfter, suppressLog, fromOverheat);
        }

        return dealt;
    }

    // 血量真正下降才到这里。子弹 suppressLog 时直接返回；过热单独标明来源。
    void LogHpLoss(float lost, float hpBefore, float hpAfter, bool suppressLog, bool fromOverheat)
    {
        if (suppressLog || lost <= 0f)
            return;

        string who = TeamLabel(team) + " " + gameObject.name;
        string numbers = "扣血 " + FormatNumber(lost)
            + " 血量 " + FormatNumber(hpBefore) + "->" + FormatNumber(hpAfter);
        if (fromOverheat)
            Debug.Log("[伤害] 过热 " + who + " " + numbers, this);
        else
            Debug.Log("[伤害] " + who + " " + numbers, this);
    }

    static string TeamLabel(RobotTeam side)
    {
        switch (side)
        {
            case RobotTeam.Red: return "红方";
            case RobotTeam.Blue: return "蓝方";
            default: return "中立";
        }
    }

    static string FormatNumber(float value)
    {
        float rounded = Mathf.Round(value);
        if (Mathf.Abs(value - rounded) < 0.001f)
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public float Heal(float amount)
    {
        EnsureInit();
        if (amount <= 0f || !IsAlive) return 0f;

        float hp = stored[(int)RobotStat.Hp];
        float cap = GetCap(RobotStat.Hp);
        float next = Mathf.Min(cap, hp + amount);
        float healed = next - hp;
        SetStored(RobotStat.Hp, next, false);
        return healed;
    }

    public bool TryConsumeAmmo17mm(int count = 1)
    {
        return TryConsumeAmmo(RobotStat.Ammo17mm, count);
    }

    public bool TryConsumeAmmo42mm(int count = 1)
    {
        return TryConsumeAmmo(RobotStat.Ammo42mm, count);
    }

    public int AddAmmo17mm(int count)
    {
        return AddAmmo(RobotStat.Ammo17mm, count);
    }

    public int AddAmmo42mm(int count)
    {
        return AddAmmo(RobotStat.Ammo42mm, count);
    }

    // 加热。正数升温，负数降温。返回是否处于过热。
    public bool AddBarrelHeat(float heat)
    {
        EnsureInit();
        float cur = stored[(int)RobotStat.BarrelHeat];
        SetStored(RobotStat.BarrelHeat, cur + heat, false);
        return IsOverheated;
    }

    public bool TryAddBarrelHeat(float heat)
    {
        EnsureInit();
        if (heat > 0f && stored[(int)RobotStat.BarrelHeat] + heat > GetCap(RobotStat.BarrelHeat) + 0.0001f)
            return false;
        AddBarrelHeat(heat);
        return true;
    }

    public float AddChassisPowerBuffer(float energy)
    {
        EnsureInit();
        float cur = stored[(int)RobotStat.ChassisPowerBuffer];
        SetStored(RobotStat.ChassisPowerBuffer, cur + energy, false);
        return stored[(int)RobotStat.ChassisPowerBuffer] - cur;
    }

    public int AddCoins(int delta)
    {
        EnsureInit();
        int cur = Mathf.RoundToInt(stored[(int)RobotStat.Coins]);
        SetStored(RobotStat.Coins, Mathf.Max(0, cur + delta), false);
        return Mathf.RoundToInt(stored[(int)RobotStat.Coins]) - cur;
    }

    public bool TrySpendCoins(int cost)
    {
        EnsureInit();
        if (cost <= 0) return true;
        if (stored[(int)RobotStat.Coins] + 0.0001f < cost) return false;
        AddCoins(-cost);
        return true;
    }

    public bool AddFoul(int count = 1)
    {
        EnsureInit();
        int left = Mathf.RoundToInt(stored[(int)RobotStat.RemainingFoul]) - count;
        SetStored(RobotStat.RemainingFoul, left, false);
        if (left > 0) return false;

        SetBase(RobotStat.IsFoulOut, 1f);
        if (!HasModifier(FoulOutSourceId))
        {
            AddModifier(RobotStat.CanMove, StatModifier.Additive(FoulOutSourceId, -1f));
            AddModifier(RobotStat.CanShoot, StatModifier.Additive(FoulOutSourceId, -1f));
        }

        return true;
    }

    public void SetRfidZone(int zoneId)
    {
        SetCurrent(RobotStat.RfidZoneId, zoneId);
    }

    public void AddZoneFlag(int flag)
    {
        int flags = Mathf.RoundToInt(GetCurrent(RobotStat.CurrentZoneFlags)) | flag;
        SetCurrent(RobotStat.CurrentZoneFlags, flags);
    }

    public void RemoveZoneFlag(int flag)
    {
        int flags = Mathf.RoundToInt(GetCurrent(RobotStat.CurrentZoneFlags)) & ~flag;
        SetCurrent(RobotStat.CurrentZoneFlags, flags);
    }

    public void Die()
    {
        EnsureInit();
        if (deathEventFired && !IsAlive) return;

        SetStored(RobotStat.Hp, 0f, false);
        SetStored(RobotStat.BarrelHeat, 0f, false);
        SetStored(RobotStat.ChassisPowerBuffer, GetCap(RobotStat.ChassisPowerBuffer), false);
        barrelLockedUntilZero = false; // 本局永久锁定标记保留。
        SetBase(RobotStat.IsDefeated, 1f);
        Hero42mmShield.NotifyDeath(this);
        SetBase(RobotStat.IsIdle, 0f);

        if (!HasModifier(RobotStat.CanMove, DeadSourceId))
            AddModifier(RobotStat.CanMove, StatModifier.Additive(DeadSourceId, -1f));
        if (!HasModifier(RobotStat.CanShoot, DeadSourceId))
            AddModifier(RobotStat.CanShoot, StatModifier.Additive(DeadSourceId, -1f));

        if (deathEventFired) return;
        deathEventFired = true;
        CancelRemoteHeal();
        ClearReviveAftermath();
        Debug.Log("[死亡] " + ActorLabel(this), this);
        BeginNaturalRevive();
        Died?.Invoke(this);
        onDeath?.Invoke();
    }

    public bool Revive(float hpPercent = 1f)
    {
        EnsureInit();
        if (IsAlive) return false;

        int remaining = Mathf.RoundToInt(stored[(int)RobotStat.ReviveRemaining]);
        if (remaining == 0) return false;

        if (remaining > 0)
            SetStored(RobotStat.ReviveRemaining, remaining - 1, false);

        SetStored(RobotStat.ReviveCount, stored[(int)RobotStat.ReviveCount] + 1f, false);
        SetBase(RobotStat.IsDefeated, 0f);
        RemoveModifiersFromSource(DeadSourceId, true);

        float cap = GetCap(RobotStat.Hp);
        SetStored(RobotStat.Hp, Mathf.Clamp(cap * Mathf.Clamp01(hpPercent), 1f, cap), false);
        deathEventFired = false;
        reviveSecondsRemaining = 0f;

        Revived?.Invoke(this);
        onRevive?.Invoke();
        return true;
    }

    [ContextMenu("比赛重置 ResetToBase")]
    public void ResetToBase()
    {
        EnsureInit();
        for (int s = 0; s < StatCount; s++)
            mods[s].Clear();

        CopyInspectorToRuntimeBases();
        FillResourcesFromBases();
        deathEventFired = false;
        barrelLockedUntilZero = false;
        barrelLockedForMatch = false;
        reviveSecondsRemaining = 0f;
        immediateReviveCount = 0;
        secondsSinceCombat = RobotLevelRules.OutOfCombatSeconds;
        CancelRemoteHeal();
        ClearReviveAftermath();
        levelBaselineReady = false;
        appliedPerformanceLevel = -1;
        SyncInspectorSnapshot();
        TickLevelPerformance();
        RefreshView();
    }

    // V2.2.0 第 5.1.3 节。Q0 为热量上限，Q1 为当前热量。
    // 17mm 发射机构 Q2 = Q0 + 100；42mm 发射机构（英雄，表 3-3）Q2 = Q0 + 200。
    // Q2 > Q1 > Q0：锁定发射机构，直到 Q1 回到 0 才解锁。
    // Q1 > Q2：本局后续时间永久锁定，不再解锁。
    // 不再按旧公式 (Q1-Q0)/250×最大血量、10 Hz 扣血。冷却仍在 Update 里按每秒冷却值进行。
    void TickBarrelHeatLock()
    {
        if (barrelLockedForMatch)
            return;

        float q1 = GetCurrent(RobotStat.BarrelHeat);
        float q0 = GetCurrent(RobotStat.BarrelHeatLimit);
        float q2 = q0 + (Uses42mmLauncher ? SevereExcess42mm : SevereExcess17mm);

        if (q1 > q2 + 0.0001f)
        {
            barrelLockedForMatch = true;
            barrelLockedUntilZero = true;
            return;
        }

        if (q1 > q0 + 0.0001f)
            barrelLockedUntilZero = true;

        if (barrelLockedUntilZero && q1 <= 0.0001f)
            barrelLockedUntilZero = false;
    }

    // 英雄只有 42mm 发射机构。步兵等使用 17mm 发射机构。切换弹种不改变机构类型。
    public bool Uses42mmLauncher => robotType == RobotType.Hero;

    // —— 内部 ——

    void EnsureInit()
    {
        if (inited) return;
        inited = true;

        runtimeBase = new float[StatCount];
        stored = new float[StatCount];
        mods = new List<StatModifier>[StatCount];
        for (int i = 0; i < StatCount; i++)
            mods[i] = new List<StatModifier>(4);

        CopyInspectorToRuntimeBases();
        FillResourcesFromBases();
        SyncInspectorSnapshot();
    }

    // 刚 AddComponent、还没被改过时为 true。用来决定要不要把车上的存量迁过来。
    public bool IsFreshInspectorDefault()
    {
        return robotType == RobotType.Infantry
            && team == RobotTeam.Red
            && robotId == 3
            && baseLevel == 1
            && Mathf.Approximately(baseExp, 0f)
            && Mathf.Approximately(baseHp, 200f)
            && Mathf.Approximately(baseMaxHp, 200f)
            && Mathf.Approximately(baseShieldHp, 0f)
            && baseAmmo17mm == 0
            && baseAmmo42mm == 0
            && baseMaxAmmo17mm == 500
            && baseMaxAmmo42mm == 0
            && Mathf.Approximately(baseBarrelHeat, 0f)
            && Mathf.Approximately(baseBarrelHeatLimit, 40f)
            && Mathf.Approximately(baseFireRate, 10f)
            && Mathf.Approximately(baseDamage, 10f);
    }

    // 把另一份已经跑起来的基础值和存量接过来。调用方只在场景组件刚加上且仍是默认值时使用。
    public void CopyLiveStateFrom(RobotAttributeManager source)
    {
        if (source == null || source == this)
            return;

        source.EnsureInit();
        EnsureInit();

        robotId = source.robotId;
        team = source.team;
        robotType = source.robotType;
        autoCoolBarrel = source.autoCoolBarrel;
        autoRecoverHp = source.autoRecoverHp;
        CopyBaseFieldsFrom(source);

        ResetToBase();

        for (int s = 0; s < StatCount; s++)
        {
            if (!IsResource((RobotStat)s))
                continue;
            SetStored((RobotStat)s, source.stored[s], true);
        }

        for (int s = 0; s < StatCount; s++)
        {
            mods[s].Clear();
            List<StatModifier> from = source.mods[s];
            for (int i = 0; i < from.Count; i++)
                mods[s].Add(CloneModifier(from[i]));
        }

        deathEventFired = source.deathEventFired;
        barrelLockedUntilZero = source.barrelLockedUntilZero;
        barrelLockedForMatch = source.barrelLockedForMatch;
        WriteStoredResourcesToInspector();
        SyncInspectorSnapshot();
        RefreshView();
    }

    void CopyBaseFieldsFrom(RobotAttributeManager source)
    {
        baseLevel = source.baseLevel;
        baseExp = source.baseExp;
        baseHp = source.baseHp;
        baseMaxHp = source.baseMaxHp;
        baseShieldHp = source.baseShieldHp;
        baseReviveCount = source.baseReviveCount;
        baseReviveRemaining = source.baseReviveRemaining;
        baseMoveSpeed = source.baseMoveSpeed;
        baseRotateSpeed = source.baseRotateSpeed;
        baseChassisPowerLimit = source.baseChassisPowerLimit;
        baseChassisPowerBuffer = source.baseChassisPowerBuffer;
        baseSpinSpeed = source.baseSpinSpeed;
        baseCanMove = source.baseCanMove;
        baseGimbalYawSpeed = source.baseGimbalYawSpeed;
        baseGimbalPitchSpeed = source.baseGimbalPitchSpeed;
        baseCanShoot = source.baseCanShoot;
        baseAmmo17mm = source.baseAmmo17mm;
        baseAmmo42mm = source.baseAmmo42mm;
        baseMaxAmmo17mm = source.baseMaxAmmo17mm;
        baseMaxAmmo42mm = source.baseMaxAmmo42mm;
        baseBarrelHeat = source.baseBarrelHeat;
        baseBarrelHeatLimit = source.baseBarrelHeatLimit;
        baseCoolingRate = source.baseCoolingRate;
        baseFireRate = source.baseFireRate;
        baseProjectileSpeed = source.baseProjectileSpeed;
        baseDamage = source.baseDamage;
        baseAttackBuffPercent = source.baseAttackBuffPercent;
        baseDefenseBuffPercent = source.baseDefenseBuffPercent;
        baseCooldownBuffPercent = source.baseCooldownBuffPercent;
        baseCoins = source.baseCoins;
        baseRemainingFoul = source.baseRemainingFoul;
        baseIsFoulOut = source.baseIsFoulOut;
        baseRfidZoneId = source.baseRfidZoneId;
        baseCurrentZoneFlags = source.baseCurrentZoneFlags;
        baseIsPowered = source.baseIsPowered;
        baseIsIdle = source.baseIsIdle;
        baseIsDefeated = source.baseIsDefeated;
        baseIsInvulnerable = source.baseIsInvulnerable;
        baseRecoveryRate = source.baseRecoveryRate;
    }

    void CopyInspectorToRuntimeBases()
    {
        WriteInspectorBases(runtimeBase);
    }

    void WriteInspectorBases(float[] dst)
    {
        dst[(int)RobotStat.Level] = baseLevel;
        dst[(int)RobotStat.Exp] = baseExp;
        dst[(int)RobotStat.Hp] = baseHp;
        dst[(int)RobotStat.MaxHp] = baseMaxHp;
        dst[(int)RobotStat.ShieldHp] = baseShieldHp;
        dst[(int)RobotStat.ReviveCount] = baseReviveCount;
        dst[(int)RobotStat.ReviveRemaining] = baseReviveRemaining;
        dst[(int)RobotStat.MoveSpeed] = baseMoveSpeed;
        dst[(int)RobotStat.RotateSpeed] = baseRotateSpeed;
        dst[(int)RobotStat.ChassisPowerLimit] = baseChassisPowerLimit;
        dst[(int)RobotStat.ChassisPowerBuffer] = baseChassisPowerBuffer;
        dst[(int)RobotStat.SpinSpeed] = baseSpinSpeed;
        dst[(int)RobotStat.CanMove] = baseCanMove ? 1f : 0f;
        dst[(int)RobotStat.GimbalYawSpeed] = baseGimbalYawSpeed;
        dst[(int)RobotStat.GimbalPitchSpeed] = baseGimbalPitchSpeed;
        dst[(int)RobotStat.CanShoot] = baseCanShoot ? 1f : 0f;
        dst[(int)RobotStat.Ammo17mm] = baseAmmo17mm;
        dst[(int)RobotStat.Ammo42mm] = baseAmmo42mm;
        dst[(int)RobotStat.MaxAmmo17mm] = baseMaxAmmo17mm;
        dst[(int)RobotStat.MaxAmmo42mm] = baseMaxAmmo42mm;
        dst[(int)RobotStat.BarrelHeat] = baseBarrelHeat;
        dst[(int)RobotStat.BarrelHeatLimit] = baseBarrelHeatLimit;
        dst[(int)RobotStat.CoolingRate] = baseCoolingRate;
        dst[(int)RobotStat.FireRate] = baseFireRate;
        dst[(int)RobotStat.ProjectileSpeed] = baseProjectileSpeed;
        dst[(int)RobotStat.Damage] = baseDamage;
        dst[(int)RobotStat.AttackBuffPercent] = baseAttackBuffPercent;
        dst[(int)RobotStat.DefenseBuffPercent] = baseDefenseBuffPercent;
        dst[(int)RobotStat.CooldownBuffPercent] = baseCooldownBuffPercent;
        dst[(int)RobotStat.Coins] = baseCoins;
        dst[(int)RobotStat.RemainingFoul] = baseRemainingFoul;
        dst[(int)RobotStat.IsFoulOut] = baseIsFoulOut ? 1f : 0f;
        dst[(int)RobotStat.RfidZoneId] = baseRfidZoneId;
        dst[(int)RobotStat.CurrentZoneFlags] = baseCurrentZoneFlags;
        dst[(int)RobotStat.IsPowered] = baseIsPowered ? 1f : 0f;
        dst[(int)RobotStat.IsIdle] = baseIsIdle ? 1f : 0f;
        dst[(int)RobotStat.IsDefeated] = baseIsDefeated ? 1f : 0f;
        dst[(int)RobotStat.IsInvulnerable] = baseIsInvulnerable ? 1f : 0f;
        dst[(int)RobotStat.RecoveryRate] = baseRecoveryRate;
    }

    void SyncInspectorSnapshot()
    {
        if (inspectorSynced == null || inspectorSynced.Length != StatCount)
            inspectorSynced = new float[StatCount];
        WriteInspectorBases(inspectorSynced);
    }

    // 上限、射速先写，血量弹药热量后写，避免先把血量夹进旧上限。
    void ApplyInspectorDeltas()
    {
        if (inspectorSynced == null)
        {
            SyncInspectorSnapshot();
            return;
        }

        if (inspectorScratch == null || inspectorScratch.Length != StatCount)
            inspectorScratch = new float[StatCount];
        WriteInspectorBases(inspectorScratch);

        for (int pass = 0; pass < 2; pass++)
        {
            for (int s = 0; s < StatCount; s++)
            {
                if (Mathf.Approximately(inspectorSynced[s], inspectorScratch[s]))
                    continue;

                var stat = (RobotStat)s;
                bool resource = IsResource(stat);
                if (pass == 0 && resource)
                    continue;
                if (pass == 1 && !resource)
                    continue;

                // 电容这一个数既是容量也是当前能量，检视器改它时两边一起写。
                if (stat == RobotStat.ChassisPowerBuffer)
                {
                    SetBase(stat, inspectorScratch[s]);
                    SetCurrent(stat, inspectorScratch[s]);
                }
                else
                    SetBase(stat, inspectorScratch[s]);
            }
        }

        for (int s = 0; s < StatCount; s++)
            inspectorSynced[s] = inspectorScratch[s];
    }

    void WriteStoredResourcesToInspector()
    {
        baseLevel = Mathf.RoundToInt(stored[(int)RobotStat.Level]);
        baseExp = stored[(int)RobotStat.Exp];
        baseHp = stored[(int)RobotStat.Hp];
        baseShieldHp = stored[(int)RobotStat.ShieldHp];
        baseReviveCount = Mathf.RoundToInt(stored[(int)RobotStat.ReviveCount]);
        baseReviveRemaining = Mathf.RoundToInt(stored[(int)RobotStat.ReviveRemaining]);
        baseAmmo17mm = Mathf.RoundToInt(stored[(int)RobotStat.Ammo17mm]);
        baseAmmo42mm = Mathf.RoundToInt(stored[(int)RobotStat.Ammo42mm]);
        baseBarrelHeat = stored[(int)RobotStat.BarrelHeat];
        baseChassisPowerBuffer = stored[(int)RobotStat.ChassisPowerBuffer];
        baseCoins = Mathf.RoundToInt(stored[(int)RobotStat.Coins]);
        baseRemainingFoul = Mathf.RoundToInt(stored[(int)RobotStat.RemainingFoul]);
        baseRfidZoneId = Mathf.RoundToInt(stored[(int)RobotStat.RfidZoneId]);
        baseCurrentZoneFlags = Mathf.RoundToInt(stored[(int)RobotStat.CurrentZoneFlags]);
    }

    void FillResourcesFromBases()
    {
        for (int s = 0; s < StatCount; s++)
        {
            var stat = (RobotStat)s;
            if (!IsResource(stat)) continue;
            stored[s] = ClampResource(stat, runtimeBase[s]);
        }

        // 开局血量不超过当前最大血量
        stored[(int)RobotStat.Hp] = Mathf.Clamp(runtimeBase[(int)RobotStat.Hp], 0f, ComputeModified(RobotStat.MaxHp));
        stored[(int)RobotStat.ShieldHp] = Mathf.Max(0f, runtimeBase[(int)RobotStat.ShieldHp]);
    }

    float ComputeModified(RobotStat stat)
    {
        if (LanSession.IsClient && lanValues != null) return lanValues[(int)stat];
        float b = runtimeBase[(int)stat];
        float add = 0f;
        float pct = 0f;
        var list = mods[(int)stat];
        bool takeMax = stat == RobotStat.AttackBuffPercent
            || stat == RobotStat.DefenseBuffPercent
            || stat == RobotStat.RecoveryRate
            || stat == RobotStat.CooldownBuffPercent;
        // 防御正增益仍取最大。易伤是负防御，取最负的一条，再和正增益相加。
        // 手册 5.5.3 示例：25% 防御再占对方堡垒（100% 易伤）得到 75% 易伤；再叠 15% 易伤仍是 75%。
        bool defenseMix = stat == RobotStat.DefenseBuffPercent;
        float posAdd = 0f;
        float negAdd = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            if (takeMax)
            {
                if (defenseMix)
                {
                    if (list[i].add > posAdd)
                        posAdd = list[i].add;
                    if (list[i].add < negAdd)
                        negAdd = list[i].add;
                }
                else if (list[i].add > add)
                    add = list[i].add;
                if (list[i].percent > pct)
                    pct = list[i].percent;
            }
            else if (stat == RobotStat.CoolingRate)
            {
                add += list[i].add;
                if (list[i].percent > pct)
                    pct = list[i].percent;
            }
            else
            {
                add += list[i].add;
                pct += list[i].percent;
            }
        }

        if (defenseMix)
            add = posAdd + negAdd;

        float v = (b + add) * (1f + pct);
        return PostProcess(stat, v);
    }

    static float PostProcess(RobotStat stat, float v)
    {
        switch (stat)
        {
            case RobotStat.CanMove:
            case RobotStat.CanShoot:
            case RobotStat.IsPowered:
            case RobotStat.IsIdle:
            case RobotStat.IsDefeated:
            case RobotStat.IsFoulOut:
            case RobotStat.IsInvulnerable:
                return v > 0.5f ? 1f : 0f;
            case RobotStat.MaxHp:
            case RobotStat.MoveSpeed:
            case RobotStat.RotateSpeed:
            case RobotStat.SpinSpeed:
            case RobotStat.GimbalYawSpeed:
            case RobotStat.GimbalPitchSpeed:
            case RobotStat.ChassisPowerLimit:
            case RobotStat.CoolingRate:
            case RobotStat.FireRate:
            case RobotStat.ProjectileSpeed:
            case RobotStat.Damage:
            case RobotStat.MaxAmmo17mm:
            case RobotStat.MaxAmmo42mm:
            case RobotStat.BarrelHeatLimit:
            case RobotStat.RecoveryRate:
                return Mathf.Max(0f, v);
            default:
                return v;
        }
    }

    static bool IsResource(RobotStat stat)
    {
        switch (stat)
        {
            case RobotStat.Hp:
            case RobotStat.ShieldHp:
            case RobotStat.Exp:
            case RobotStat.Level:
            case RobotStat.ReviveCount:
            case RobotStat.ReviveRemaining:
            case RobotStat.Ammo17mm:
            case RobotStat.Ammo42mm:
            case RobotStat.BarrelHeat:
            case RobotStat.ChassisPowerBuffer:
            case RobotStat.Coins:
            case RobotStat.RemainingFoul:
            case RobotStat.RfidZoneId:
            case RobotStat.CurrentZoneFlags:
                return true;
            default:
                return false;
        }
    }

    void SetStored(RobotStat stat, float value, bool silent)
    {
        float old = stored[(int)stat];
        float next = ClampResource(stat, value);
        if (Mathf.Approximately(old, next)) return;
        stored[(int)stat] = next;
        if (!silent)
            RaiseChanged(stat, old, next);
    }

    float ClampResource(RobotStat stat, float value)
    {
        switch (stat)
        {
            case RobotStat.Hp:
                return Mathf.Clamp(value, 0f, ComputeModified(RobotStat.MaxHp));
            case RobotStat.ShieldHp:
                return Mathf.Max(0f, value);
            case RobotStat.Ammo17mm:
                return Mathf.Clamp(value, 0f, ComputeModified(RobotStat.MaxAmmo17mm));
            case RobotStat.Ammo42mm:
                return Mathf.Clamp(value, 0f, ComputeModified(RobotStat.MaxAmmo42mm));
            case RobotStat.BarrelHeat:
                // 必须允许高于上限，否则 (热量 - 上限) 永远 ≤ 0，过热扣血不会发生。只禁止负热量。
                return Mathf.Max(0f, value);
            case RobotStat.ChassisPowerBuffer:
                return Mathf.Clamp(value, 0f, Mathf.Max(0f, ComputeModified(RobotStat.ChassisPowerBuffer)));
            case RobotStat.Coins:
            case RobotStat.Exp:
            case RobotStat.Level:
                return Mathf.Max(0f, value);
            case RobotStat.ReviveCount:
                return Mathf.Max(0f, value);
            case RobotStat.RemainingFoul:
                return value; // 可为负，方便判定罚下
            default:
                return value;
        }
    }

    void ClampResourcesAffectedBy(RobotStat stat)
    {
        switch (stat)
        {
            case RobotStat.MaxHp:
                SetStored(RobotStat.Hp, stored[(int)RobotStat.Hp], true);
                break;
            case RobotStat.MaxAmmo17mm:
                SetStored(RobotStat.Ammo17mm, stored[(int)RobotStat.Ammo17mm], true);
                break;
            case RobotStat.MaxAmmo42mm:
                SetStored(RobotStat.Ammo42mm, stored[(int)RobotStat.Ammo42mm], true);
                break;
            case RobotStat.BarrelHeatLimit:
                SetStored(RobotStat.BarrelHeat, stored[(int)RobotStat.BarrelHeat], true);
                break;
            case RobotStat.ChassisPowerBuffer:
                SetStored(RobotStat.ChassisPowerBuffer, stored[(int)RobotStat.ChassisPowerBuffer], true);
                break;
        }
    }

    void AfterModifierChanged(RobotStat stat, float oldCurrent)
    {
        ClampResourcesAffectedBy(stat);
        float now = GetCurrent(stat);
        RaiseChanged(stat, oldCurrent, now);
    }

    void RaiseChanged(RobotStat stat, float oldValue, float newValue)
    {
        if (Mathf.Approximately(oldValue, newValue)) return;
        StatChanged?.Invoke(stat, oldValue, newValue);
    }

    StatModifier FindModifier(RobotStat stat, string sourceId)
    {
        var list = mods[(int)stat];
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].sourceId == sourceId)
                return list[i];
        }

        return null;
    }

    static bool ReleaseList(List<StatModifier> list, string sourceId, bool force)
    {
        bool changed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].sourceId != sourceId) continue;
            if (force)
            {
                list.RemoveAt(i);
                changed = true;
                continue;
            }

            list[i].refCount--;
            if (list[i].refCount <= 0)
                list.RemoveAt(i);
            return true; // 一次退出只减一个来源
        }

        return changed;
    }

    static StatModifier CloneModifier(StatModifier m)
    {
        return new StatModifier
        {
            sourceId = m.sourceId,
            add = m.add,
            percent = m.percent,
            stackable = m.stackable,
            duration = m.duration,
            refCount = Mathf.Max(1, m.refCount),
            remaining = m.duration
        };
    }

    void TickModifiers(float dt)
    {
        for (int s = 0; s < StatCount; s++)
        {
            var list = mods[s];
            if (list.Count == 0) continue;

            float old = 0f;
            bool expired = false;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var m = list[i];
                if (m.duration < 0f) continue;
                m.remaining -= dt;
                if (m.remaining > 0f) continue;
                if (!expired)
                    old = GetCurrent((RobotStat)s);
                list.RemoveAt(i);
                expired = true;
            }

            if (expired)
                AfterModifierChanged((RobotStat)s, old);
        }
    }

    bool TryConsumeAmmo(RobotStat stat, int count)
    {
        EnsureInit();
        if (count <= 0) return true;
        if (stat == RobotStat.Ammo17mm || stat == RobotStat.Ammo42mm)
        {
            int reserveCost = stat == RobotStat.Ammo42mm ? count * 10 : count;
            if (fortReserve >= reserveCost)
            {
                fortReserve -= reserveCost;
                return true;
            }
        }

        if (stored[(int)stat] + 0.0001f < count) return false;
        SetStored(stat, stored[(int)stat] - count, false);
        return true;
    }

    int AddAmmo(RobotStat stat, int count)
    {
        EnsureInit();
        float old = stored[(int)stat];
        SetStored(stat, old + count, false);
        return Mathf.RoundToInt(stored[(int)stat] - old);
    }

    // 子弹击毁敌方机器人时由 CombatDamage 调用。同队不给。满级不再获得经验。
    public void GrantKillExperience(RobotAttributeManager victim)
    {
        if (victim == null || victim == this)
            return;
        if (team == victim.team)
            return;
        if (!RobotLevelRules.CanGainExperience(robotType))
            return;
        if (!RobotLevelRules.IsRobotKillTarget(victim.robotType))
            return;

        int killerLevel = Level;
        if (killerLevel >= RobotLevelRules.MaxLevel)
        {
            Debug.Log("[经验] " + ActorLabel(this) + " 已满级，击毁 " + ActorLabel(victim) + " 不再获得经验", this);
            return;
        }

        float gain = RobotLevelRules.KillExperience(RobotLevelRules.LevelForKill(victim), killerLevel);
        CommitExperience(gain, "击毁 " + ActorLabel(victim), true);
    }

    // 地形跨越、发射弹丸、攻击伤害等固定经验。满级不再增加。小符额外在 CommitExperience 里加。
    public void GrantFlatExperience(float gain, string reason)
    {
        CommitExperience(gain, reason, false);
    }

    // 先记原经验，小符增益期间再按同样数额加一笔，并受本方共享 1200 限制。满级剩余格子不够时，额外部分少加。
    void CommitExperience(float gain, string reason, bool includeLevel)
    {
        EnsureInit();
        if (gain <= 0f || !RobotLevelRules.CanGainExperience(robotType))
            return;
        if (Level >= RobotLevelRules.MaxLevel)
            return;

        float room = RobotLevelRules.TotalExpForLevel(RobotLevelRules.MaxLevel) - Experience;
        if (room <= 0f)
            return;

        float baseApplied = Mathf.Min(gain, room);
        float extraRoom = room - baseApplied;
        float extra = 0f;
        if (extraRoom > 0f)
            extra = PowerRuneActivator.ConsumeSmallRuneExtra(team, Mathf.Min(baseApplied, extraRoom));

        int oldLevel = Level;
        float nextExp = Experience + baseApplied + extra;
        int newLevel = RobotLevelRules.LevelFromTotalExp(nextExp);
        if (newLevel >= RobotLevelRules.MaxLevel)
        {
            newLevel = RobotLevelRules.MaxLevel;
            nextExp = Mathf.Min(nextExp, RobotLevelRules.TotalExpForLevel(RobotLevelRules.MaxLevel));
        }

        SetCurrent(RobotStat.Exp, nextExp);
        SetCurrent(RobotStat.Level, newLevel);
        RememberResourceInspector(RobotStat.Exp, nextExp);
        RememberResourceInspector(RobotStat.Level, newLevel);
        TickLevelPerformance();

        string line = "[经验] " + ActorLabel(this) + " " + reason
            + " 经验+" + baseApplied.ToString("0.#");
        if (extra > 0f)
            line += " 小符额外+" + extra.ToString("0.#");
        line += " 当前经验 " + Experience.ToString("0.#");
        if (includeLevel)
            line += " 等级 " + Level;
        Debug.Log(line, this);

        if (newLevel > oldLevel)
            Debug.Log("[升级] " + ActorLabel(this) + " " + oldLevel + " -> " + newLevel, this);
    }

    public bool TryGetModifierTiming(RobotStat stat, string sourceId, out float remaining)
    {
        remaining = 0f;
        StatModifier found = FindModifier(stat, sourceId);
        if (found == null)
            return false;
        remaining = found.remaining;
        return true;
    }

    void BeginNaturalRevive()
    {
        reviveSecondsRemaining = 0f;
        int charges = Mathf.RoundToInt(GetCurrent(RobotStat.ReviveRemaining));
        if (charges == 0)
            return;

        float elapsed = 0f;
        MatchTimer timer = FindAnyObjectByType<MatchTimer>();
        if (timer != null)
            elapsed = timer.ElapsedSeconds;

        reviveSecondsRemaining = RobotLevelRules.NaturalReviveSeconds(elapsed, immediateReviveCount);
    }

    void TickNaturalRevive(float dt)
    {
        if (IsAlive || IsFoulOut || !IsPowered || MatchOutcome.Decided || reviveSecondsRemaining <= 0f)
            return;

        bool accelerated = FieldSupportZoneBuff.InOwnSupply(this)
            || BaseHealth.HpOf(team) < 2000f;
        reviveSecondsRemaining -= dt * (accelerated ? 4f : 1f);
        if (reviveSecondsRemaining > 0f)
            return;

        reviveSecondsRemaining = 0f;
        if (Revive(RobotLevelRules.NaturalReviveHpFraction))
        {
            BeginNaturalAftermath();
            Debug.Log("[复活] " + ActorLabel(this) + " 读条结束，血量 10%，虚弱锁发射，无敌 30 秒", this);
        }
    }

    public void NotifyRoundFired()
    {
        secondsSinceCombat = 0f;
    }

    // 5.2.1：英雄、步兵、哨兵，且处于脱战。返回 null 表示可以买。
    public string RemoteHealBlockReason()
    {
        EnsureInit();
        if (!RobotLevelRules.CanRemoteHealType(robotType))
            return "该兵种不能远程兑换血量";
        if (!IsAlive)
            return "仅存活且脱战时可以兑换";
        if (!IsOutOfCombat)
            return "需脱战（连续6秒未发射且未扣血）";
        if (remoteHealSecondsRemaining > 0f)
            return "血量 " + Mathf.CeilToInt(remoteHealSecondsRemaining) + " 秒后到账";
        return null;
    }

    public void BeginRemoteHeal()
    {
        remoteHealSecondsRemaining = RobotLevelRules.RemoteHealDelaySeconds;
    }

    // 5.2.2：只在复活读条进行中兑换。返回 null 表示可以买。
    public string ImmediateReviveBlockReason()
    {
        EnsureInit();
        if (IsAlive)
            return "仅战亡读条期间可以兑换";
        if (reviveSecondsRemaining <= 0f)
            return "当前没有复活读条";
        return null;
    }

    public bool BeginImmediateRevive()
    {
        if (ImmediateReviveBlockReason() != null)
            return false;
        if (!Revive(RobotLevelRules.ImmediateReviveHpFraction))
            return false;

        immediateReviveCount++;
        BeginImmediateAftermath();
        Debug.Log("[复活] " + ActorLabel(this) + " 立即复活，满血，虚弱与无敌 3 秒，累计 " + immediateReviveCount + " 次", this);
        return true;
    }

    // 5.2.2：检测到可占领的前哨站、基地或补给区增益点卡。自然复活会按 10 秒门槛收束无敌。
    public void NotifyOccupiableBuffCard()
    {
        if (!IsWeak && !naturalInvulnerableActive)
            return;

        bool wasNatural = naturalInvulnerableActive;
        float elapsed = naturalInvulnerableElapsed;
        EndWeak();
        if (!wasNatural)
            return;

        if (elapsed > RobotLevelRules.NaturalInvulnerableFloorSeconds)
            EndInvulnerable();
        else
        {
            invulnerableSecondsRemaining = RobotLevelRules.NaturalInvulnerableFloorSeconds - elapsed;
            if (invulnerableSecondsRemaining <= 0f)
                EndInvulnerable();
        }
    }

    void BeginNaturalAftermath()
    {
        EndWeak();
        weakUntilBuffCard = true;
        weakSecondsRemaining = 0f;
        LockShootingForWeak();
        naturalInvulnerableActive = true;
        naturalInvulnerableElapsed = 0f;
        invulnerableSecondsRemaining = RobotLevelRules.NaturalInvulnerableSeconds;
        SetBase(RobotStat.IsInvulnerable, 1f);
    }

    void BeginImmediateAftermath()
    {
        EndWeak();
        naturalInvulnerableActive = false;
        naturalInvulnerableElapsed = 0f;
        weakUntilBuffCard = false;
        weakSecondsRemaining = RobotLevelRules.ImmediateWeakSeconds;
        LockShootingForWeak();
        invulnerableSecondsRemaining = RobotLevelRules.ImmediateInvulnerableSeconds;
        SetBase(RobotStat.IsInvulnerable, 1f);

        float cur = GetCurrent(RobotStat.ChassisPowerLimit);
        float target = Mathf.Min(cur * 2f, RobotLevelRules.ImmediatePowerCapWatts);
        float add = target - cur;
        if (add > 0.01f)
        {
            AddModifier(
                RobotStat.ChassisPowerLimit,
                StatModifier.Additive(ImmediatePowerSourceId, add, RobotLevelRules.ImmediatePowerSeconds));
        }
    }

    void LockShootingForWeak()
    {
        if (!HasModifier(RobotStat.CanShoot, WeakSourceId))
            AddModifier(RobotStat.CanShoot, StatModifier.Additive(WeakSourceId, -1f));
    }

    void TickReviveAftermath(float dt)
    {
        if (weakSecondsRemaining > 0f)
        {
            weakSecondsRemaining -= dt;
            if (weakSecondsRemaining <= 0f)
            {
                weakSecondsRemaining = 0f;
                if (!weakUntilBuffCard)
                    EndWeak();
            }
        }

        if (invulnerableSecondsRemaining <= 0f)
            return;

        invulnerableSecondsRemaining -= dt;
        if (naturalInvulnerableActive)
            naturalInvulnerableElapsed += dt;
        if (invulnerableSecondsRemaining > 0f)
            return;

        invulnerableSecondsRemaining = 0f;
        EndInvulnerable();
    }

    void TickRemoteHeal(float dt)
    {
        if (remoteHealSecondsRemaining <= 0f)
            return;

        remoteHealSecondsRemaining -= dt;
        if (remoteHealSecondsRemaining > 0f)
            return;

        remoteHealSecondsRemaining = 0f;
        if (!IsAlive)
            return;

        float amount = GetCap(RobotStat.Hp) * RobotLevelRules.RemoteHealFraction;
        float healed = Heal(amount);
        Debug.Log("[买血] " + ActorLabel(this) + " 远程兑换到账 +" + healed.ToString("0.#"), this);
    }

    void CancelRemoteHeal()
    {
        remoteHealSecondsRemaining = 0f;
    }

    void EndWeak()
    {
        weakUntilBuffCard = false;
        weakSecondsRemaining = 0f;
        RemoveModifier(RobotStat.CanShoot, WeakSourceId, true);
    }

    void EndInvulnerable()
    {
        invulnerableSecondsRemaining = 0f;
        naturalInvulnerableActive = false;
        SetBase(RobotStat.IsInvulnerable, baseIsInvulnerable ? 1f : 0f);
    }

    void ClearReviveAftermath()
    {
        EndWeak();
        EndInvulnerable();
        naturalInvulnerableElapsed = 0f;
        RemoveModifiersFromSource(ImmediatePowerSourceId, true);
    }

    void TickLevelPerformance()
    {
        if (!levelBaselineReady)
        {
            level1MaxHp = GetBase(RobotStat.MaxHp);
            level1Power = GetBase(RobotStat.ChassisPowerLimit);
            level1Heat = GetBase(RobotStat.BarrelHeatLimit);
            level1Cool = GetBase(RobotStat.CoolingRate);
            levelBaselineReady = true;
        }

        int level = Level;
        if (level == appliedPerformanceLevel)
            return;
        if (!RobotLevelRules.TryGetPerformanceDelta(robotType, level, out float hpAdd, out float powerAdd, out float heatAdd, out float coolAdd))
        {
            appliedPerformanceLevel = level;
            return;
        }

        float oldMax = GetCurrent(RobotStat.MaxHp);
        SetBase(RobotStat.MaxHp, level1MaxHp + hpAdd);
        SetBase(RobotStat.ChassisPowerLimit, level1Power + powerAdd);
        SetBase(RobotStat.BarrelHeatLimit, level1Heat + heatAdd);
        SetBase(RobotStat.CoolingRate, level1Cool + coolAdd);
        appliedPerformanceLevel = level;

        float gainedHp = GetCurrent(RobotStat.MaxHp) - oldMax;
        if (gainedHp > 0f && IsAlive)
            Heal(gainedHp);
    }

    void RememberResourceInspector(RobotStat stat, float value)
    {
        if (stat == RobotStat.Level)
            baseLevel = Mathf.RoundToInt(value);
        else if (stat == RobotStat.Exp)
            baseExp = value;

        if (inspectorSynced == null)
            return;
        inspectorSynced[(int)stat] = stat == RobotStat.Level ? baseLevel : baseExp;
    }

    public LanRobotState CaptureLanState()
    {
        EnsureInit();
        var state = new LanRobotState
        {
            stats = new float[StatCount],
            heatLocked = barrelLockedUntilZero, permanentlyLocked = barrelLockedForMatch,
            weakUntilCard = weakUntilBuffCard, naturalInvulnerable = naturalInvulnerableActive,
            revive = reviveSecondsRemaining, invulnerable = invulnerableSecondsRemaining,
            weak = weakSecondsRemaining, remoteHeal = remoteHealSecondsRemaining,
            combatAge = secondsSinceCombat, immediateRevives = immediateReviveCount
        };
        var modifiers = new List<LanModifier>();
        for (int stat = 0; stat < StatCount; stat++)
        {
            state.stats[stat] = GetCurrent((RobotStat)stat);
            foreach (StatModifier modifier in mods[stat])
                modifiers.Add(new LanModifier { stat = stat, modifier = modifier, remaining = modifier.remaining });
        }
        state.modifiers = modifiers.ToArray();
        return state;
    }

    public void ApplyLanState(LanRobotState state)
    {
        if (!LanSession.IsClient || state.stats == null || state.stats.Length != StatCount) return;
        EnsureInit();
        lanValues = state.stats;
        barrelLockedUntilZero = state.heatLocked;
        barrelLockedForMatch = state.permanentlyLocked;
        weakUntilBuffCard = state.weakUntilCard;
        naturalInvulnerableActive = state.naturalInvulnerable;
        reviveSecondsRemaining = state.revive;
        invulnerableSecondsRemaining = state.invulnerable;
        weakSecondsRemaining = state.weak;
        remoteHealSecondsRemaining = state.remoteHeal;
        secondsSinceCombat = state.combatAge;
        immediateReviveCount = state.immediateRevives;
        foreach (var list in mods) list.Clear();
        if (state.modifiers != null)
            foreach (LanModifier entry in state.modifiers)
                if (entry.stat >= 0 && entry.stat < StatCount && entry.modifier != null)
                {
                    entry.modifier.remaining = entry.remaining;
                    mods[entry.stat].Add(entry.modifier);
                }
        RefreshView();
    }

    static string ActorLabel(RobotAttributeManager robot)
    {
        if (robot == null)
            return "未知";

        string side;
        switch (robot.team)
        {
            case RobotTeam.Red: side = "红方"; break;
            case RobotTeam.Blue: side = "蓝方"; break;
            default: side = "中立"; break;
        }

        string kind;
        switch (robot.robotType)
        {
            case RobotType.Hero: kind = "英雄"; break;
            case RobotType.Infantry: kind = "步兵"; break;
            case RobotType.Engineer: kind = "工程"; break;
            case RobotType.Sentry: kind = "哨兵"; break;
            case RobotType.Aerial: kind = "空中"; break;
            default: kind = robot.robotType.ToString(); break;
        }

        return side + kind;
    }

    void RefreshView()
    {
        viewHp = GetCurrent(RobotStat.Hp);
        viewMaxHp = GetCurrent(RobotStat.MaxHp);
        viewMoveSpeed = GetCurrent(RobotStat.MoveSpeed);
        viewDamage = GetCurrent(RobotStat.Damage);
        viewBarrelHeat = GetCurrent(RobotStat.BarrelHeat);
        viewAmmo17mm = GetCurrent(RobotStat.Ammo17mm);
        viewAmmo42mm = GetCurrent(RobotStat.Ammo42mm);
        viewAlive = IsAlive;
        viewCanMove = CanMove;
        viewCanShoot = CanShoot;
    }
}
