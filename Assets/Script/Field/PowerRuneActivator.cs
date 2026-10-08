using System;
using System.Collections.Generic;
using UnityEngine;

// 2026 规则手册 5.5.2。红蓝各自独立。
// 小符：开局与 1 分 30 秒各 1 次机会。亮 1 个未完成灯臂，2.5 秒内打中，5 臂打完给 25% 防御、45 秒。
// 大符：3 分、4 分 15 秒、5 分 30 秒各 1 次机会。同时亮 2 臂。命中一律记 6 环，
// 平均环数 6 落在 (3,7]：攻击 150%、防御 25%、热量冷却 2 倍。持续时间看激活次数（5 臂 30 秒到 10 臂 60 秒）。
// 没有裁判端按钮：有剩余机会且未激活时自动进入正在激活。不改场景。
[DefaultExecutionOrder(-50)]
public class PowerRuneActivator : MonoBehaviour
{
    const float SmallPhaseEnd = 180f;
    const float ActivateWindow = 20f;
    const float HitWindow = 2.5f;
    const float SecondHitWindow = 1f;
    const float SmallDefense = 0.25f;
    const float SmallBuffSeconds = 45f;
    // 5.5.2：一次小符增益期间，本方通过「额外 100%」合计最多 1200，全队共享。
    public const float SmallExtraExpCap = 1200f;
    public const float LargeActivationExperience = 750f;
    const float Ring = 6f;

    static readonly float[] SmallGrantAt = { 0f, 90f };
    static readonly float[] LargeGrantAt = { 180f, 255f, 330f };

    enum Phase
    {
        Idle,
        Activating,
        Buffed
    }

    enum RuneKind
    {
        Small,
        Large
    }

    class Arm
    {
        public string fanName;
        public Transform fan;
        public Transform target;
        public GameObject redLight;
        public GameObject blueLight;
        public bool struck;
    }

    class Side
    {
        public RobotTeam team;
        public string label;
        public readonly List<Arm> arms = new List<Arm>();
        public int smallChances;
        public int largeChances;
        public Phase phase;
        public RuneKind kind;
        public float activateLeft;
        public float shotLeft;
        public float buffLeft;
        public float smallExtraLeft;
        public bool waitingSecond;
        public int litA = -1;
        public int litB = -1;
        public int totalHits;
    }

    // 该方小符已激活且增益时间还没走完。大符增益不算。
    public static bool IsSmallRuneBuffActive(RobotTeam team)
    {
        Side side = FindSide(team);
        return side != null
            && side.phase == Phase.Buffed
            && side.kind == RuneKind.Small
            && side.buffLeft > 0f;
    }

    // 从本方这一次小符的共享 1200 里扣掉实际能加上的额外经验。没有小符增益时返回 0。
    public static float ConsumeSmallRuneExtra(RobotTeam team, float amount)
    {
        if (amount <= 0f)
            return 0f;
        Side side = FindSide(team);
        if (side == null
            || side.phase != Phase.Buffed
            || side.kind != RuneKind.Small
            || side.buffLeft <= 0f
            || side.smallExtraLeft <= 0f)
            return 0f;

        float take = Mathf.Min(amount, side.smallExtraLeft);
        side.smallExtraLeft -= take;
        return take;
    }

    static Side FindSide(RobotTeam team)
    {
        if (instance == null)
            return null;
        for (int i = 0; i < instance.sides.Count; i++)
        {
            Side side = instance.sides[i];
            if (side != null && side.team == team)
                return side;
        }

        return null;
    }

    // 建筑直接读取队伍剩余增益时间；无需在建筑上挂机器人属性组件。
    public static float StructureDefense(RobotTeam team)
    {
        Side side = FindSide(team);
        return side != null && side.phase == Phase.Buffed && side.buffLeft > 0f ? 0.25f : 0f;
    }

    public static bool LargeActivatingSpin { get; private set; }
    public static float LargeA { get; private set; } = 0.785f;
    public static float LargeOmega { get; private set; } = 1.884f;
    public static float LargeB { get; private set; } = 1.305f;
    public static float LargeTime { get; private set; }
    public static float SpinDeltaRadians { get; private set; }

    static PowerRuneActivator instance;

    readonly List<Side> sides = new List<Side>();
    readonly bool[] smallGranted = new bool[SmallGrantAt.Length];
    readonly bool[] largeGranted = new bool[LargeGrantAt.Length];
    bool warnedNoTimer;
    bool spinWasOn;
    bool resetSpinParameters;

    public static void BindForLoadedMatch()
    {
        if (FindAnyObjectByType<PowerRuneActivator>() != null)
            return;

        GameObject host = new GameObject("PowerRuneActivator");
        host.AddComponent<PowerRuneActivator>();
    }

    void OnEnable()
    {
        instance = this;
        LargeTime = SpinDeltaRadians = 0f;
        LargeActivatingSpin = false;
    }

    void OnDisable()
    {
        foreach (Side side in sides) ClearRuneBuffs(side);
        if (instance == this)
            instance = null;
        LargeActivatingSpin = false;
    }

    void Start()
    {
        BindSides();
        ApplyAllLights();
    }

    void Update()
    {
        if (!LanSession.CanSimulate || MatchOutcome.Decided) { SpinDeltaRadians = 0f; return; }
        float elapsed = ReadElapsed();
        float dt = Time.deltaTime;
        GrantChances(elapsed);

        bool largePhase = elapsed >= SmallPhaseEnd;
        for (int i = 0; i < sides.Count; i++)
        {
            Side side = sides[i];
            if (largePhase && side.phase == Phase.Activating && side.kind == RuneKind.Small)
                Fail(side, "小能量机关阶段结束");

            TickSide(side, dt);
            TryBegin(side, largePhase);
        }

        UpdateSpin(dt);
        ApplyAllLights();
    }

    // 打在已登记的靶上就吃掉这发，不走机器人扣血。只有己方、且正在激活时才计六环。
    public static bool TryHit(Collider hit, RobotTeam attackerTeam)
    {
        if (instance == null || hit == null)
            return false;

        for (int s = 0; s < instance.sides.Count; s++)
        {
            Side side = instance.sides[s];
            int index = FindArmIndex(side, hit.transform);
            if (index < 0)
                continue;

            bool sameTeam = attackerTeam == side.team;
            bool activating = side.phase == Phase.Activating;
            // #region agent log
            AgentLog(sameTeam ? "C" : "A", "rune-hit",
                "{\"attacker\":\"" + attackerTeam
                + "\",\"side\":\"" + side.team
                + "\",\"label\":\"" + side.label
                + "\",\"fan\":\"" + side.arms[index].fanName
                + "\",\"phase\":\"" + side.phase
                + "\",\"sameTeam\":" + (sameTeam ? "true" : "false")
                + ",\"activating\":" + (activating ? "true" : "false")
                + ",\"scored\":" + ((sameTeam && activating) ? "true" : "false")
                + ",\"hit\":\"" + hit.name + "\"}");
            // #endregion
            if (sameTeam && activating)
                instance.OnArmHit(side, index);
            return true;
        }

        return false;
    }

    void OnArmHit(Side side, int index)
    {
        if (!IsLit(side, index))
        {
            Fail(side, "击中未点亮的灯臂");
            return;
        }

        if (side.kind == RuneKind.Small)
        {
            RegisterHit(side, index);
            if (side.phase != Phase.Activating)
                return;
            if (AllStruck(side))
            {
                SucceedSmall(side);
                return;
            }

            RollSmall(side);
            side.shotLeft = HitWindow;
            return;
        }

        if (side.waitingSecond && index == side.litA)
            return;

        RegisterHit(side, index);
        if (side.phase != Phase.Activating)
            return;

        if (AllStruck(side))
        {
            SucceedLarge(side);
            return;
        }

        if (!side.waitingSecond)
        {
            int other = index == side.litA ? side.litB : side.litA;
            if (other < 0)
            {
                RollLarge(side);
                side.shotLeft = HitWindow;
                return;
            }

            side.litA = index;
            side.litB = other;
            side.waitingSecond = true;
            side.shotLeft = SecondHitWindow;
            return;
        }

        RollLarge(side);
        side.shotLeft = HitWindow;
    }

    public static LanRuneState[] CaptureLanState()
    {
        if (instance == null) return null;
        var states = new LanRuneState[2];
        foreach (Side side in instance.sides)
        {
            var state = new LanRuneState
            {
                phase = (int)side.phase, kind = (int)side.kind, litA = side.litA, litB = side.litB,
                hits = side.totalHits, activateLeft = side.activateLeft, shotLeft = side.shotLeft,
                buffLeft = side.buffLeft, extraLeft = side.smallExtraLeft,
                waitingSecond = side.waitingSecond, struck = new bool[side.arms.Count]
            };
            for (int i = 0; i < side.arms.Count; i++) state.struck[i] = side.arms[i].struck;
            states[side.team == RobotTeam.Red ? 0 : 1] = state;
        }
        return states;
    }

    public static void ApplyLanState(LanRuneState[] states)
    {
        if (!LanSession.IsClient || instance == null || states == null || states.Length != 2) return;
        foreach (Side side in instance.sides)
        {
            LanRuneState state = states[side.team == RobotTeam.Red ? 0 : 1];
            if (state == null) continue;
            side.phase = (Phase)state.phase; side.kind = (RuneKind)state.kind;
            side.litA = state.litA; side.litB = state.litB; side.totalHits = state.hits;
            side.activateLeft = state.activateLeft; side.shotLeft = state.shotLeft;
            side.buffLeft = state.buffLeft; side.smallExtraLeft = state.extraLeft;
            side.waitingSecond = state.waitingSecond;
            if (state.struck != null)
                for (int i = 0; i < side.arms.Count && i < state.struck.Length; i++) side.arms[i].struck = state.struck[i];
        }
        instance.ApplyAllLights();
    }

    void RegisterHit(Side side, int index)
    {
        Arm arm = side.arms[index];
        arm.struck = true;
        side.totalHits++;
        Debug.Log("[能量机关] " + side.label + " 六环命中 " + arm.fanName
            + " 环数" + Ring.ToString("0")
            + " 累计" + side.totalHits
            + " 已点亮灯臂" + StruckCount(side) + "/" + side.arms.Count);
    }

    void TickSide(Side side, float dt)
    {
        if (side.phase == Phase.Buffed)
        {
            side.buffLeft -= dt;
            if (side.buffLeft <= 0f)
            {
                if (side.kind == RuneKind.Small)
                    side.smallExtraLeft = 0f;
                side.phase = Phase.Idle;
                ClearRuneBuffs(side);
                Debug.Log("[能量机关] " + side.label + " 增益结束");
            }
            else
            {
                // 战亡立即失去增益；增益期内复活者获得剩余时长，不重复发经验。
                ApplyRuneBuffs(side);
            }

            return;
        }

        if (side.phase != Phase.Activating)
            return;

        side.activateLeft -= dt;
        side.shotLeft -= dt;

        if (side.activateLeft <= 0f)
        {
            Fail(side, "20 秒内未完成");
            return;
        }

        if (side.shotLeft > 0f)
            return;

        if (side.kind == RuneKind.Large && side.waitingSecond)
        {
            RollLarge(side);
            side.shotLeft = HitWindow;
            return;
        }

        Fail(side, "2.5 秒内未击中点亮的灯臂");
    }

    void TryBegin(Side side, bool largePhase)
    {
        if (side.phase != Phase.Idle || side.arms.Count == 0)
            return;

        if (largePhase)
        {
            if (side.largeChances <= 0)
                return;
            side.largeChances--;
            Begin(side, RuneKind.Large);
            return;
        }

        if (side.smallChances <= 0)
            return;
        side.smallChances--;
        Begin(side, RuneKind.Small);
    }

    void Begin(Side side, RuneKind kind)
    {
        if (kind == RuneKind.Large) resetSpinParameters = true;
        side.phase = Phase.Activating;
        side.kind = kind;
        side.activateLeft = ActivateWindow;
        side.shotLeft = HitWindow;
        side.waitingSecond = false;
        side.totalHits = 0;
        for (int i = 0; i < side.arms.Count; i++)
            side.arms[i].struck = false;

        if (kind == RuneKind.Small)
            RollSmall(side);
        else
            RollLarge(side);

        Debug.Log("[能量机关] " + side.label
            + (kind == RuneKind.Small ? "小能量机关" : "大能量机关")
            + "进入正在激活，剩余机会 "
            + (kind == RuneKind.Small ? side.smallChances : side.largeChances)
            + "，点亮 " + LitNames(side));
    }

    void SucceedSmall(Side side)
    {
        side.phase = Phase.Buffed;
        side.buffLeft = SmallBuffSeconds;
        side.smallExtraLeft = SmallExtraExpCap;
        ClearLit(side);
        ApplySmallBuff(side);
        Debug.Log("[能量机关] " + side.label + " 小能量机关激活成功，防御 25%，持续 45 秒");
    }

    void SucceedLarge(Side side)
    {
        float seconds = LargeDuration(side.totalHits);
        side.phase = Phase.Buffed;
        side.buffLeft = seconds;
        ClearLit(side);
        ApplyLargeBuff(side, seconds);
        GrantLargeActivationExperience(side.team);
        Debug.Log("[能量机关] " + side.label
            + " 大能量机关激活成功，六环平均 6，攻击 150% 防御 25% 冷却 2 倍，灯臂 "
            + Mathf.Clamp(side.totalHits, 5, 10)
            + "，持续 " + seconds.ToString("0") + " 秒");
    }

    void Fail(Side side, string reason)
    {
        if (side.phase != Phase.Activating)
            return;

        side.phase = Phase.Idle;
        ClearLit(side);
        Debug.Log("[能量机关] " + side.label + " 激活失败：" + reason);
    }

    static void ApplySmallBuff(Side side)
    {
        ClearRuneBuffs(side);
        ApplyRuneBuffs(side);
    }

    static void ApplyLargeBuff(Side side, float seconds)
    {
        ClearRuneBuffs(side);
        ApplyRuneBuffs(side);
    }

    static void ApplyRuneBuffs(Side side)
    {
        string id = BuffId(side, side.kind);
        ForEachTeam(side.team, attr =>
        {
            if (!attr.IsAlive)
            {
                attr.RemoveModifiersFromSource(id, true);
                return;
            }
            AddRuneModifier(attr, RobotStat.DefenseBuffPercent, StatModifier.Additive(id, SmallDefense, side.buffLeft));
            if (side.kind != RuneKind.Large) return;
            // 仍沿用现有六环结算：攻击 150%、冷却两倍。
            AddRuneModifier(attr, RobotStat.AttackBuffPercent, StatModifier.Additive(id, 0.5f, side.buffLeft));
            AddRuneModifier(attr, RobotStat.CoolingRate, StatModifier.PercentBonus(id, 1f, side.buffLeft));
        });
    }

    static void AddRuneModifier(RobotAttributeManager attr, RobotStat stat, StatModifier modifier)
    {
        if (!attr.HasModifier(stat, modifier.sourceId)) attr.AddModifier(stat, modifier);
    }

    static void GrantLargeActivationExperience(RobotTeam team)
    {
        var eligible = new List<RobotAttributeManager>();
        ForEachAlive(team, attr =>
        {
            if (RobotLevelRules.CanGainExperience(attr.robotType)) eligible.Add(attr);
        });
        if (eligible.Count == 0) return;
        float share = LargeActivationExperience / eligible.Count;
        foreach (var attr in eligible) attr.GrantFlatExperience(share, "大能量机关激活奖励");
        Debug.Log("[能量机关] " + team + " 大符经验奖励 750，存活人数 " + eligible.Count + "，每台 " + share);
    }

    static void ClearRuneBuffs(Side side)
    {
        ForEachTeam(side.team, attr =>
        {
            attr.RemoveModifiersFromSource(BuffId(side, RuneKind.Small), true);
            attr.RemoveModifiersFromSource(BuffId(side, RuneKind.Large), true);
        });
    }

    static string BuffId(Side side, RuneKind kind)
    {
        string who = side.team == RobotTeam.Blue ? "blue" : "red";
        return kind == RuneKind.Small ? "rune_small_" + who : "rune_large_" + who;
    }

    static void ForEachAlive(RobotTeam team, Action<RobotAttributeManager> action)
    {
        ForEachTeam(team, attr =>
        {
            if (attr.IsAlive)
                action(attr);
        });
    }

    static void ForEachTeam(RobotTeam team, Action<RobotAttributeManager> action)
    {
        RobotAttributeManager[] all = FindObjectsByType<RobotAttributeManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RobotAttributeManager attr = all[i];
            if (attr != null && attr.team == team)
                action(attr);
        }
    }

    static float LargeDuration(int hits)
    {
        switch (Mathf.Clamp(hits, 5, 10))
        {
            case 5: return 30f;
            case 6: return 35f;
            case 7: return 40f;
            case 8: return 45f;
            case 9: return 50f;
            default: return 60f;
        }
    }

    void RollSmall(Side side)
    {
        side.waitingSecond = false;
        side.litB = -1;
        side.litA = PickUnstruck(side, -1);
    }

    void RollLarge(Side side)
    {
        side.waitingSecond = false;
        side.litA = UnityEngine.Random.Range(0, side.arms.Count);
        side.litB = -1;
        if (side.arms.Count < 2)
            return;

        side.litB = (side.litA + 1 + UnityEngine.Random.Range(0, side.arms.Count - 1)) % side.arms.Count;
    }

    static int PickUnstruck(Side side, int except)
    {
        int count = 0;
        for (int i = 0; i < side.arms.Count; i++)
        {
            if (i == except || side.arms[i].struck)
                continue;
            count++;
        }

        if (count == 0)
            return -1;

        int pick = UnityEngine.Random.Range(0, count);
        for (int i = 0; i < side.arms.Count; i++)
        {
            if (i == except || side.arms[i].struck)
                continue;
            if (pick == 0)
                return i;
            pick--;
        }

        return -1;
    }

    static bool AllStruck(Side side)
    {
        if (side.arms.Count == 0)
            return false;
        for (int i = 0; i < side.arms.Count; i++)
        {
            if (!side.arms[i].struck)
                return false;
        }

        return true;
    }

    static int StruckCount(Side side)
    {
        int n = 0;
        for (int i = 0; i < side.arms.Count; i++)
        {
            if (side.arms[i].struck)
                n++;
        }

        return n;
    }

    static bool IsLit(Side side, int index)
    {
        return index >= 0 && (index == side.litA || index == side.litB);
    }

    void ClearLit(Side side)
    {
        side.litA = -1;
        side.litB = -1;
        side.waitingSecond = false;
    }

    string LitNames(Side side)
    {
        string a = ArmName(side, side.litA);
        string b = ArmName(side, side.litB);
        if (string.IsNullOrEmpty(b))
            return a;
        return a + "、" + b;
    }

    static string ArmName(Side side, int index)
    {
        if (index < 0 || index >= side.arms.Count)
            return "";
        return side.arms[index].fanName;
    }

    void ApplyAllLights()
    {
        for (int s = 0; s < sides.Count; s++)
        {
            Side side = sides[s];
            bool show = side.phase == Phase.Activating;
            for (int i = 0; i < side.arms.Count; i++)
            {
                bool on = show && IsLit(side, i);
                SetLight(side.arms[i].redLight, on && side.team == RobotTeam.Red);
                SetLight(side.arms[i].blueLight, on && side.team == RobotTeam.Blue);
            }
        }
    }

    static void SetLight(GameObject lamp, bool on)
    {
        if (lamp != null && lamp.activeSelf != on)
            lamp.SetActive(on);
    }

    void UpdateSpin(float dt)
    {
        bool want = false;
        for (int i = 0; i < sides.Count; i++)
        {
            Side side = sides[i];
            if (side.phase == Phase.Activating && side.kind == RuneKind.Large)
            {
                want = true;
                break;
            }
        }

        if (want && (!spinWasOn || resetSpinParameters))
        {
            LargeA = UnityEngine.Random.Range(0.780f, 1.045f);
            LargeOmega = UnityEngine.Random.Range(1.884f, 2.000f);
            LargeB = 2.090f - LargeA;
            LargeTime = 0f;
            Debug.Log("[能量机关] 大符正在激活，转速 spd = "
                + LargeA.ToString("0.000") + " * sin("
                + LargeOmega.ToString("0.000") + " * t) + "
                + LargeB.ToString("0.000"));
        }

        spinWasOn = want;
        resetSpinParameters = false;
        LargeActivatingSpin = want;
        SpinDeltaRadians = (float)RuneRotationRules.Delta(want, LargeTime, dt, LargeA, LargeOmega);
        if (want)
            LargeTime += dt;
    }

    void GrantChances(float elapsed)
    {
        Grant(elapsed, SmallGrantAt, smallGranted, true);
        Grant(elapsed, LargeGrantAt, largeGranted, false);
    }

    void Grant(float elapsed, float[] at, bool[] granted, bool small)
    {
        for (int i = 0; i < at.Length; i++)
        {
            if (granted[i] || elapsed + 0.0001f < at[i])
                continue;
            granted[i] = true;
            for (int s = 0; s < sides.Count; s++)
            {
                if (small)
                    sides[s].smallChances++;
                else
                    sides[s].largeChances++;
            }
        }
    }

    float ReadElapsed()
    {
        MatchTimer timer = FindAnyObjectByType<MatchTimer>();
        if (timer != null)
            return timer.ElapsedSeconds;

        if (!warnedNoTimer)
        {
            warnedNoTimer = true;
            Debug.LogWarning("[能量机关] 场景里没有 MatchTimer，已进行时间按 0 秒。");
        }

        return 0f;
    }

    void BindSides()
    {
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int rotations = 0;
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || !string.Equals(t.name, "rotation", StringComparison.OrdinalIgnoreCase))
                continue;
            rotations++;
            BindColor(t, "red", RobotTeam.Red, "红方");
            BindColor(t, "blue", RobotTeam.Blue, "蓝方");
        }

        if (rotations == 0)
            Debug.LogWarning("[能量机关] 没有找到名为 rotation 的物体。");
    }

    void BindColor(Transform rotation, string colorName, RobotTeam team, string label)
    {
        Transform color = FindDirectChild(rotation, colorName);
        if (color == null)
            color = FindNamedChild(rotation, colorName);
        if (color == null)
        {
            Debug.LogWarning("[能量机关] " + rotation.name + " 下没有 " + colorName);
            return;
        }

        Side side = new Side();
        side.team = team;
        side.label = label;

        int madeTargets = 0;
        int madeLights = 0;
        for (int i = 0; i < color.childCount; i++)
        {
            Transform node = color.GetChild(i);
            if (node == null || !node.name.StartsWith("fan", StringComparison.OrdinalIgnoreCase))
                continue;

            bool madeTarget;
            Transform target = FindOrCreateTarget(node, out madeTarget);
            if (madeTarget)
                madeTargets++;

            Arm arm = new Arm();
            arm.fanName = node.name;
            arm.fan = node;
            arm.target = target;
            arm.redLight = FindOrCreateLight(node, "Tip_light_red", new Vector3(0.318f, 0.079f, 0.76f),
                new Color(0.91823894f, 0.20516539f, 0.04908818f), ref madeLights);
            arm.blueLight = FindOrCreateLight(node, "Tip_light_blue", new Vector3(-0.353f, -0.134f, 0.76f),
                new Color(0f, 0.050593495f, 0.99215686f), ref madeLights);
            // 运行时补出来的 target 在转轴中心。扇叶网格已有碰撞体时不再叠盒子，否则五片会叠在中心，挡在弹道前面。
            // 场景里本来就有的 target 没有碰撞体时，仍然补盒子。
            if (!madeTarget || !FanHasColliderOutside(node, target))
                EnsureTargetCollider(target);

            side.arms.Add(arm);
        }

        if (side.arms.Count == 0)
        {
            Debug.LogWarning("[能量机关] " + label + " 没有可用的 fan/target");
            return;
        }

        sides.Add(side);
        Debug.Log("[能量机关] " + label + " 登记 " + side.arms.Count + " 个灯臂，命中靶是各 fan 下的 target");
        // #region agent log
        AgentLog("B", "rune-bind",
            "{\"label\":\"" + label + "\",\"team\":\"" + team
            + "\",\"parent\":\"" + color.name + "\",\"arms\":" + side.arms.Count
            + ",\"fans\":\"" + FanList(side) + "\"}");
        // #endregion
        if (madeTargets > 0)
            Debug.LogWarning("[能量机关] " + label + " 有 " + madeTargets
                + " 个 fan 下没有 target，已在运行时补上。打中该扇叶的碰撞体算打中这个 target。");
        if (madeLights > 0)
            Debug.LogWarning("[能量机关] " + label + " 有 " + madeLights
                + " 盏 Tip_light 不在扇叶下，已在运行时补上。只有当前亮靶会打开。");
    }

    static Transform FindOrCreateTarget(Transform fan, out bool created)
    {
        Transform target = FindDirectChild(fan, "target");
        if (target == null)
            target = FindNamedChild(fan, "target");
        created = false;
        if (target != null)
            return target;

        created = true;
        GameObject go = new GameObject("target");
        go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        go.transform.SetParent(fan, false);
        return go.transform;
    }

    static GameObject FindOrCreateLight(Transform fan, string exactName, Vector3 localPos, Color color, ref int madeLights)
    {
        Transform found = FindDirectChild(fan, exactName);
        if (found == null)
            found = FindNamedChild(fan, exactName);
        if (found != null)
            return found.gameObject;

        madeLights++;
        GameObject lamp = new GameObject(exactName);
        lamp.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        lamp.transform.SetParent(fan, false);
        lamp.transform.localPosition = localPos;
        lamp.transform.localRotation = Quaternion.identity;
        lamp.transform.localScale = Vector3.one;
        Light light = lamp.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = color;
        light.intensity = 10f;
        light.range = 10f;
        light.spotAngle = 30f;
        light.innerSpotAngle = 21.8f;
        lamp.SetActive(false);
        return lamp;
    }

    // #region agent log
    static void AgentLog(string hypothesisId, string message, string dataJson)
    {
        try
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string line = "{\"sessionId\":\"a1c594\",\"hypothesisId\":\"" + hypothesisId
                + "\",\"location\":\"PowerRuneActivator\",\"message\":\"" + message
                + "\",\"data\":" + dataJson + ",\"timestamp\":" + ts + "}\n";
            System.IO.File.AppendAllText(@"E:\模拟器开发\debug-a1c594.log", line);
        }
        catch
        {
        }
    }

    static string FanList(Side side)
    {
        string names = "";
        for (int i = 0; i < side.arms.Count; i++)
        {
            if (i > 0)
                names += ",";
            names += side.arms[i].fanName;
        }

        return names;
    }
    // #endregion

    static bool FanHasColliderOutside(Transform fan, Transform target)
    {
        Collider[] cols = fan.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            Collider col = cols[i];
            if (col == null)
                continue;
            Transform t = col.transform;
            if (target != null && (t == target || t.IsChildOf(target)))
                continue;
            return true;
        }

        return false;
    }

    static int FindArmIndex(Side side, Transform hit)
    {
        for (int i = 0; i < side.arms.Count; i++)
        {
            Transform target = side.arms[i].target;
            if (target != null && (hit == target || hit.IsChildOf(target)))
                return i;
        }

        // 场景里扇叶网格碰撞体和 target 是同级。打在这块扇叶上，算打中它的 target。
        for (int i = 0; i < side.arms.Count; i++)
        {
            Transform fan = side.arms[i].fan;
            if (fan != null && (hit == fan || hit.IsChildOf(fan)))
                return i;
        }

        return -1;
    }

    static Transform FindDirectChild(Transform root, string exactName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && string.Equals(child.name, exactName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    static Transform FindNamedChild(Transform root, string exactName)
    {
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            Transform node = nodes[i];
            if (node != null && node != root && string.Equals(node.name, exactName, StringComparison.OrdinalIgnoreCase))
                return node;
        }

        return null;
    }

    static void EnsureTargetCollider(Transform target)
    {
        if (target.GetComponentInChildren<Collider>(true) != null)
            return;

        Bounds bounds = new Bounds(target.position, Vector3.one * 0.3f);
        bool has = false;
        Renderer[] rends = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null)
                continue;
            if (!has)
            {
                bounds = rends[i].bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(rends[i].bounds);
            }
        }

        BoxCollider box = target.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.center = target.InverseTransformPoint(bounds.center);
        Vector3 scale = target.lossyScale;
        float sx = Mathf.Max(Mathf.Abs(scale.x), 0.0001f);
        float sy = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float sz = Mathf.Max(Mathf.Abs(scale.z), 0.0001f);
        box.size = new Vector3(
            Mathf.Max(0.05f, bounds.size.x / sx),
            Mathf.Max(0.05f, bounds.size.y / sy),
            Mathf.Max(0.05f, bounds.size.z / sz));
    }
}
