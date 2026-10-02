using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// 运行时把机器人属性接到场景里已有的「属性管理器」上。
// 上面有十车总控时，按车位读，不在根物体或车上再挂一份。
// 没有总控时保持原行为：红方仍用根上那一份（没有组件就补上）。不新建物体，不改场景文件。
public static class PlayerAttributeBinding
{
    public const string SceneObjectName = "属性管理器";

    static bool ready;
    static bool warnedMissing;
    static RobotAttributeManager playerStats;
    static TenRobotAttributeBoard roster;

    // 关掉 Domain Reload 再进 Play 时，静态标记不能留着上一局已经拆掉的组件。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ready = false;
        warnedMissing = false;
        playerStats = null;
        roster = null;
    }

    // 进 SampleScene 后再挂。主菜单里没有「属性管理器」，不能在那时把 ready 置上。
    public static void BindForLoadedMatch()
    {
        ready = false;
        warnedMissing = false;
        playerStats = null;
        roster = null;
        EnsureReady();
    }

    // 有十车总控时，红英雄是 red_hero，蓝英雄是 blue_hero。没有总控、但有「属性管理器」时，红方仍是根上那一份。
    public static bool TryGetPlayerStats(out RobotAttributeManager stats)
    {
        EnsureReady();
        stats = playerStats;
        return stats != null;
    }

    public static bool HasRoster
    {
        get
        {
            EnsureReady();
            return roster != null;
        }
    }

    // 总控对得上这辆车时返回 true（引用没接上则 stats 为 null）。对不上返回 false，调用方走原来的逻辑。
    public static bool TryResolveRoster(GameObject go, out RobotAttributeManager stats)
    {
        stats = null;
        EnsureReady();
        if (roster == null || go == null)
            return false;
        return roster.TryResolveVehicle(go, out stats);
    }

    // 有十车总控时按车名读车位。没有总控时，红方玩家用场景上的那一份，其他物体仍用自己或父级上的组件。
    public static RobotAttributeManager Resolve(GameObject go)
    {
        if (TryResolveRoster(go, out RobotAttributeManager rosterStats))
            return rosterStats;

        if (go != null && IsPlayerHierarchy(go.transform))
        {
            if (TryGetPlayerStats(out RobotAttributeManager sceneStats) && sceneStats != null)
                return sceneStats;
        }

        if (go == null)
            return null;

        RobotAttributeManager attr = go.GetComponent<RobotAttributeManager>();
        if (attr == null)
            attr = go.GetComponentInParent<RobotAttributeManager>();
        return attr;
    }

    // 当前玩家那一台：选了步兵看对应步兵，选了蓝方英雄看 hero_blue，否则看 hero_red。
    public static bool IsPlayerHierarchy(Transform start)
    {
        Transform t = start;
        while (t != null)
        {
            if (PlayerMovement.IsSelectedPlayerVehicle(t.gameObject))
                return true;
            t = t.parent;
        }

        return false;
    }

    public static bool IsRedPlayerHierarchy(Transform start)
    {
        Transform t = start;
        while (t != null)
        {
            if (string.Equals(t.name, "hero_red", StringComparison.OrdinalIgnoreCase))
                return true;
            if (PlayerMovement.IsRedPlayerVehicle(t.gameObject))
                return true;
            t = t.parent;
        }

        return false;
    }

    static void EnsureReady()
    {
        // 本局只挂一次。没找到物体也停在这里，避免每帧再找并用默认值盖掉车上的数。
        // 不在对局场景里调用时不置 ready，否则主菜单会把这次查找吃掉。
        if (ready)
            return;
        if (SceneManager.GetActiveScene().name != VehicleSelectUI.MatchSceneName)
            return;
        ready = true;

        GameObject host = FindManagerObject();
        if (host == null)
        {
            if (!warnedMissing)
            {
                warnedMissing = true;
                Debug.LogWarning("未找到名为「属性管理器」的物体，红方玩家仍使用车上的属性组件。");
            }

            return;
        }

        TenRobotAttributeBoard board = host.GetComponent<TenRobotAttributeBoard>();
        if (board != null)
        {
            // 十车总控在根物体上。按选中车位读：英雄 red_hero / blue_hero，步兵 red_infantry_1 等。
            // 没选过时 SlotHeader 仍是 red_hero。不在根上再挂一份，也不重刷默认值。
            roster = board;
            string header = string.IsNullOrEmpty(MatchLaunchSelection.SlotHeader)
                ? TenRobotAttributeBoard.HeaderRedHero
                : MatchLaunchSelection.SlotHeader;
            playerStats = board.GetByHeader(header);
            if (playerStats == null)
                playerStats = board.redHero;
            return;
        }

        // 没有总控时，蓝方和步兵用车上自己的组件，不占用红方英雄那一份场景「属性管理器」。
        if (MatchLaunchSelection.PlaysBlueHero || MatchLaunchSelection.PlaysInfantry)
            return;

        RobotAttributeManager stats = host.GetComponent<RobotAttributeManager>();
        bool added = false;
        if (stats == null)
        {
            stats = host.AddComponent<RobotAttributeManager>();
            added = true;
        }

        GameObject vehicle = FindRedPlayerObject();
        RobotAttributeManager onVehicle = FindVehicleCopy(vehicle, stats);

        // 刚加上、还是脚本默认值时，才把车上已经写入的存量迁过来。车上没有组件则按红方英雄填，和原先挂在车上一致。
        if (added && onVehicle != null && stats.IsFreshInspectorDefault())
            stats.CopyLiveStateFrom(onVehicle);
        else if (added && onVehicle == null)
        {
            stats.robotType = RobotType.Hero;
            stats.team = RobotTeam.Red;
            if (vehicle != null)
                CombatDamage.ApplyTeamFromTags(stats, vehicle);
            stats.ApplyRobotTypeDefaults();
        }

        if (vehicle != null)
            RemoveVehicleCopies(vehicle, stats);

        playerStats = stats;
    }

    static GameObject FindManagerObject()
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform exact = null;
        Transform fuzzy = null;

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null)
                continue;

            if (string.Equals(t.name, SceneObjectName, StringComparison.Ordinal))
            {
                if (exact == null || (!exact.gameObject.activeInHierarchy && t.gameObject.activeInHierarchy))
                    exact = t;
                continue;
            }

            if (fuzzy == null && t.name.IndexOf(SceneObjectName, StringComparison.Ordinal) >= 0)
                fuzzy = t;
        }

        Transform found = exact != null ? exact : fuzzy;
        return found != null ? found.gameObject : null;
    }

    // 先精确找 hero_red。没有时再用正在操作的红方玩家车（跳过蓝方和能量机关）。
    static GameObject FindRedPlayerObject()
    {
        Transform[] all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform exactRoot = null;
        Transform exactAny = null;

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || IsUnderRune(t) || PlayerMovement.IsBlueSideVehicle(t.gameObject))
                continue;
            if (!string.Equals(t.name, "hero_red", StringComparison.OrdinalIgnoreCase))
                continue;

            if (exactAny == null)
                exactAny = t;
            if (t.parent == null && exactRoot == null)
                exactRoot = t;
        }

        if (exactRoot != null)
            return exactRoot.gameObject;
        if (exactAny != null)
            return exactAny.gameObject;

        PlayerMovement[] moves = UnityEngine.Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        PlayerMovement best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < moves.Length; i++)
        {
            PlayerMovement move = moves[i];
            if (move == null || IsUnderRune(move.transform))
                continue;
            if (PlayerMovement.IsBlueSideVehicle(move.gameObject))
                continue;

            int score = 0;
            if (move.enabled && move.gameObject.activeInHierarchy)
                score += 2;
            if (PlayerMovement.IsRedPlayerVehicle(move.gameObject))
                score += 16;
            if (score > bestScore)
            {
                bestScore = score;
                best = move;
            }
        }

        return best != null ? best.gameObject : null;
    }

    static RobotAttributeManager FindVehicleCopy(GameObject vehicle, RobotAttributeManager scene)
    {
        if (vehicle == null)
            return null;

        RobotAttributeManager[] list = vehicle.GetComponentsInChildren<RobotAttributeManager>(true);
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] != null && list[i] != scene)
                return list[i];
        }

        return null;
    }

    // 车上那份卸掉，避免和场景上的组件同时降温、过热扣血。没有脚本用 RequireComponent 依赖它。
    static void RemoveVehicleCopies(GameObject vehicle, RobotAttributeManager keep)
    {
        RobotAttributeManager[] list = vehicle.GetComponentsInChildren<RobotAttributeManager>(true);
        for (int i = 0; i < list.Length; i++)
        {
            RobotAttributeManager attr = list[i];
            if (attr == null || attr == keep)
                continue;
            // 先停掉 Update，避免和场景上那份同一帧各扣一次过热血。
            attr.enabled = false;
            UnityEngine.Object.DestroyImmediate(attr);
        }
    }

    static bool IsUnderRune(Transform t)
    {
        while (t != null)
        {
            if (t.GetComponent<RotationCenterSpin>() != null)
                return true;
            t = t.parent;
        }

        return false;
    }
}
