using System;
using UnityEngine;

public sealed class LanWorld : MonoBehaviour
{
    public LanVehicle[] Vehicles { get; private set; }

    public static LanWorld Bind()
    {
        var world = new GameObject("LanWorld").AddComponent<LanWorld>();
        world.Configure();
        return world;
    }

    void Configure()
    {
        gameObject.AddComponent<LanMatchHud>();
        var board = FindAnyObjectByType<TenRobotAttributeBoard>();
        if (board == null) throw new InvalidOperationException("联机需要场景中的十车属性管理器");
        Transform[] roots = new Transform[4];
        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (PlayerMovement.IsUnderAttributeBoard(candidate.gameObject)) continue;
            string n = candidate.name.ToLowerInvariant();
            int slot = n == "hero_red" ? 0 : n == "hero_blue" ? 2
                : n == "步兵_red" || n == "red_infantry_1" ? 1
                : n == "步兵_blue" || n == "blue_infantry_1" ? 3 : -1;
            if (slot >= 0)
            {
                if (roots[slot] != null) throw new InvalidOperationException("场景中车辆重名：" + candidate.name);
                roots[slot] = candidate;
            }
        }
        for (int i = 0; i < roots.Length; i++)
            if (roots[i] == null) throw new InvalidOperationException("场景缺少 " + LanSession.SlotLabels[i] + " 实体");

        PlayerShooting template = FindAnyObjectByType<PlayerShooting>();
        GameObject fallbackBullet = template != null ? template.bulletPrefab : null;
        foreach (PlayerMovement move in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            move.enabled = false;
            // 旧玩家 Cube 只是控制占位，不参与四车物理。
            if (move.name == "Cube" && Array.IndexOf(roots, move.transform) < 0)
            {
                var body = move.GetComponent<Rigidbody>();
                if (body != null) body.isKinematic = true;
                var col = move.GetComponent<Collider>();
                if (col != null) col.enabled = false;
            }
        }
        foreach (PlayerShooting shoot in FindObjectsByType<PlayerShooting>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            shoot.enabled = false;
        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            camera.enabled = false;
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            listener.enabled = false;

        // 非参赛车位不参与增益、经验、回血或胜负统计。
        foreach (string header in TenRobotAttributeBoard.SlotHeaders)
        {
            RobotAttributeManager stats = board.GetByHeader(header);
            if (stats != null) stats.gameObject.SetActive(Array.IndexOf(LanSession.SlotHeaders, header) >= 0);
        }

        Vehicles = new LanVehicle[4];
        for (int slot = 0; slot < 4; slot++)
        {
            GameObject vehicle = roots[slot].gameObject;
            vehicle.tag = slot < 2 ? CombatDamage.TagRed : CombatDamage.TagBlue;
            CombatDamage.EnsureBlockingBody(vehicle);
            RobotAttributeManager stats = board.GetByHeader(LanSession.SlotHeaders[slot]);
            if (stats == null) throw new InvalidOperationException("缺少属性车位 " + LanSession.SlotHeaders[slot]);
            var marker = vehicle.AddComponent<LanVehicle>();
            marker.Configure(slot, stats);
            Vehicles[slot] = marker;
            marker.Body.isKinematic = true;

            Camera camera = vehicle.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                camera = new GameObject("LanPlayerCamera").AddComponent<Camera>();
                camera.transform.SetParent(vehicle.transform, false);
                camera.transform.localPosition = new Vector3(-0.15f, 0.37f, -0.97f);
                camera.nearClipPlane = 0.03f;
            }
            camera.enabled = marker.IsLocal;
            var listener = camera.GetComponent<AudioListener>();
            if (listener == null) listener = camera.gameObject.AddComponent<AudioListener>();
            listener.enabled = marker.IsLocal;

            var move = vehicle.GetComponent<PlayerMovement>();
            if (move == null) move = vehicle.AddComponent<PlayerMovement>();
            move.enabled = LanSession.IsHost || marker.IsLocal;
            move.InitializeLanCamera(camera);
            var shoot = vehicle.GetComponent<PlayerShooting>();
            if (shoot == null) shoot = vehicle.AddComponent<PlayerShooting>();
            if (shoot.bulletPrefab == null) shoot.bulletPrefab = fallbackBullet;
            shoot.enabled = LanSession.IsHost || marker.IsLocal;
            marker.Shooting = shoot;
            if (marker.IsLocal) VehicleCameraShake.Attach(move, camera.transform);
        }
        if (LanSession.IsClient)
            foreach (Rigidbody body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) body.isKinematic = true;
    }

    public void Begin()
    {
        if (LanSession.IsHost)
        {
            foreach (LanVehicle vehicle in Vehicles) vehicle.Body.isKinematic = false;
            MatchTimer timer = FindAnyObjectByType<MatchTimer>();
            timer?.ResetTimer();
            timer?.StartTimer();
        }
    }

    public LanSnapshot Capture(int sequence)
    {
        var timer = FindAnyObjectByType<MatchTimer>();
        var state = new LanSnapshot
        {
            sequence = sequence,
            remaining = timer != null ? timer.RemainingSeconds : 420f,
            timeFinished = timer != null && timer.IsFinished,
            decided = MatchOutcome.Decided, winner = (int)MatchOutcome.Winner, result = MatchOutcome.ResultReason,
            redDamage = MatchOutcome.RedAttackDamage, blueDamage = MatchOutcome.BlueAttackDamage,
            redBase = BaseHealth.RedHp, blueBase = BaseHealth.BlueHp,
            redShield = BaseHealth.RedShield, blueShield = BaseHealth.BlueShield,
            redLowest = BaseHealth.RedLowestHp, blueLowest = BaseHealth.BlueLowestHp,
            redArmor = BaseHealth.RedArmorOpen, blueArmor = BaseHealth.BlueArmorOpen,
            redOutpost = OutpostHealth.RedHp, blueOutpost = OutpostHealth.BlueHp,
            redOutpostDestroyed = OutpostHealth.EverDestroyed(RobotTeam.Red),
            blueOutpostDestroyed = OutpostHealth.EverDestroyed(RobotTeam.Blue),
            robots = new LanRobotState[4],
            runes = PowerRuneActivator.CaptureLanState(),
            outpostPoses = OutpostTargetSpin.CaptureLanPoses()
        };
        RotationCenterSpin spin = FindAnyObjectByType<RotationCenterSpin>();
        if (spin != null) state.runeAngle = spin.LanAngle;
        for (int slot = 0; slot < 4; slot++)
        {
            LanVehicle vehicle = Vehicles[slot];
            LanRobotState robot = vehicle.Stats.CaptureLanState();
            robot.slot = slot;
            robot.position = vehicle.transform.position;
            robot.rotation = vehicle.transform.rotation;
            robot.velocity = vehicle.Body.linearVelocity;
            robot.fieldProgress = BuffGainHud.LocalProgress(vehicle.Stats);
            state.robots[slot] = robot;
        }
        return state;
    }

    public void Apply(LanSnapshot state)
    {
        if (state.robots == null || state.robots.Length != 4) return;
        foreach (LanRobotState robot in state.robots)
        {
            if (robot == null || robot.slot < 0 || robot.slot >= 4) continue;
            Vehicles[robot.slot].Stats.ApplyLanState(robot);
            Vehicles[robot.slot].ApplyPose(robot);
            Vehicles[robot.slot].FieldProgress = robot.fieldProgress ?? "";
        }
        BaseHealth.ApplyLanState(state);
        OutpostHealth.ApplyLanState(state);
        FindAnyObjectByType<MatchTimer>()?.ApplyLanTime(state.remaining, state.timeFinished);
        FindAnyObjectByType<MatchOutcome>()?.ApplyLanResult(state);
        PowerRuneActivator.ApplyLanState(state.runes);
        RotationCenterSpin spin = FindAnyObjectByType<RotationCenterSpin>();
        if (spin != null) spin.LanAngle = state.runeAngle;
        OutpostTargetSpin.ApplyLanPoses(state.outpostPoses);
    }
}
