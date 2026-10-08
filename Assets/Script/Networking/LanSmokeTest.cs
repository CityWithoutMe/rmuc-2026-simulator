using System;
using System.IO;
using System.Linq;
using UnityEngine;

// 仅开发构建的显式命令行测试。普通启动不创建该组件、不修改场景。
// 构建后用四个独立进程验证真正的 Unity 物理、所有权、射击与复活/购买 RPC。
[DefaultExecutionOrder(500)]
public sealed class LanSmokeTest : MonoBehaviour
{
    public static bool Automated => instance != null;
    static LanSmokeTest instance;
    int requestedSlot, port, expectedPlayers = 4;
    int stallSeconds;
    int testDuration = 11;
    bool stalled;
    bool feedbackTest;
    bool host, selected, readied, started, arenaBuilt, movementChecked, reviveSent, ammoSent, finished;
    float bootTime, runningTime = -1, quitAt;
    float infantryAmmoBeforeRemote = -1;
    bool shieldTriggered, shieldRestored, shieldSeen;
    float heroAmmoBeforeShield;
    Vector3[] initialPositions;
    string reportPath, error;

    [Serializable] class Report
    {
        public bool success;
        public string error;
        public bool host;
        public int localSlot, members, enabledCameras, dynamicVehicles, sequence;
        public float redHeroExp, redHeroAmmo, blueHeroHp, redInfantryAmmo;
        public int blueHeroImmediateRevives;
        public bool progressSeen, progressCleared;
        public int confirmedHits, killNotices, blockedNotices;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        instance = null;
        if (!Debug.isDebugBuild && !Application.isEditor) return;
        string[] args = Environment.GetCommandLineArgs();
        string role = Arg(args, "-lanTestRole");
        if (role != "host" && role != "client") return;
        var test = new GameObject("LanSmokeTest").AddComponent<LanSmokeTest>();
        instance = test;
        DontDestroyOnLoad(test.gameObject);
        test.host = role == "host";
        test.requestedSlot = int.Parse(Arg(args, "-lanTestSlot"));
        test.port = int.Parse(Arg(args, "-lanTestPort"));
        if (int.TryParse(Arg(args, "-lanTestPlayers"), out int playerCount)) test.expectedPlayers = playerCount;
        int.TryParse(Arg(args, "-lanTestStallSeconds"), out test.stallSeconds);
        if (int.TryParse(Arg(args, "-lanTestDuration"), out int duration)) test.testDuration = duration;
        test.reportPath = Path.GetFullPath(Arg(args, "-lanTestReport"));
        test.feedbackTest = Array.IndexOf(args, "-lanTestFeedback") >= 0;
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "LanSmoke")) + Path.DirectorySeparatorChar;
        if (!test.reportPath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("测试报告必须位于构建目录同级的 LanSmoke 文件夹");
        test.bootTime = Time.realtimeSinceStartup;
        var session = LanSession.Ensure();
        if (test.host) session.CreateRoom(test.port);
        else session.JoinRoom("127.0.0.1", test.port);
        LanLobbyUI.Show();
    }

    static string Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
    }

    void Update()
    {
        if (finished) { if (Time.realtimeSinceStartup >= quitAt) Application.Quit(); return; }
        var session = LanSession.Instance;
        if (Time.realtimeSinceStartup - bootTime > Mathf.Max(70f, testDuration + 120f))
        { Finish(null, "自动联机测试超时：" + session?.Status); return; }
        if (session == null || !LanSession.Active) return;
        if (session.LocalPeer >= 0 && !selected) { selected = true; ClickLobby("Seat" + requestedSlot); }
        if (session.LocalSlot == requestedSlot && !readied) { readied = true; ClickLobby("Ready"); }
        if (host && session.Members.Length == expectedPlayers && session.CanStart && !started)
        { started = true; ClickLobby("Start"); }
        if (!session.Running) return;
        if (runningTime < 0) runningTime = Time.realtimeSinceStartup;
        float age = Age;
        if (stallSeconds > 0 && age >= 6f && !stalled)
        {
            stalled = true;
            Debug.Log("[LAN_TEST] 模拟" + (host ? "主机" : "客户端") + "主线程暂停 " + stallSeconds + " 秒");
            System.Threading.Thread.Sleep(stallSeconds * 1000);
        }
        if (host && !arenaBuilt)
        {
            arenaBuilt = true;
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "LanSmokeFloor";
            floor.transform.position = new Vector3(1002.5f, 10, 1002.5f);
            floor.transform.localScale = new Vector3(30, 1, 30);
            ResetArena();
            SeedEconomyAndAmmo();
        }
        if (host && age >= 3f && initialPositions == null)
            initialPositions = Vehicles().Select(v => v.transform.position).ToArray();
        if (host && age >= 4f && !movementChecked)
        {
            movementChecked = true;
            var vehicles = Vehicles();
            for (int i = 0; i < 4; i++)
                if (session.Members.Any(m => m.slot == i)
                    && Mathf.Abs(vehicles[i].transform.position.x - initialPositions[i].x) < 0.2f)
                    error = "车辆 " + i + " 未响应所属玩家的移动输入";
            ResetArena();
        }
        var mine = Vehicles().FirstOrDefault(v => v.IsLocal);
        if (host && age >= 1.5f && !shieldTriggered)
        {
            shieldTriggered = true;
            var hero = Vehicles()[0].Stats;
            heroAmmoBeforeShield = hero.GetCurrent(RobotStat.Ammo42mm);
            hero.SetCurrent(RobotStat.Ammo42mm, 0);
            Hero42mmShield.Notify42mmFired(hero, 0);
        }
        if (host && age >= 2.5f && shieldTriggered && !shieldRestored)
        {
            shieldRestored = true;
            Vehicles()[0].Stats.SetCurrent(RobotStat.Ammo42mm, heroAmmoBeforeShield);
        }
        if (host && age >= 6f && infantryAmmoBeforeRemote < 0)
            infantryAmmoBeforeRemote = Vehicles().First(v => v.Slot == 1).Stats.GetCurrent(RobotStat.Ammo17mm);
        if (mine != null && requestedSlot == 2 && age > 6f && !mine.Stats.IsAlive && !reviveSent)
        { reviveSent = true; session.Purchase("revive"); }
        if (mine != null && requestedSlot == 1 && age > 11.4f && !ammoSent && mine.Stats.GetCurrent(RobotStat.Ammo17mm) < 600)
        { ammoSent = true; session.Purchase("remote17"); }
    }

    float Age => runningTime < 0 ? -1f : Time.realtimeSinceStartup - runningTime;
    static void ClickLobby(string name)
    {
        var lobby = FindAnyObjectByType<LanLobbyUI>();
        var button = lobby != null ? lobby.GetComponentsInChildren<UnityEngine.UI.Button>()
            .FirstOrDefault(b => b.name == name) : null;
        if (button == null) throw new InvalidOperationException("找不到房间按钮 " + name);
        button.onClick.Invoke();
    }
    static LanVehicle[] Vehicles() => FindObjectsByType<LanVehicle>(FindObjectsSortMode.None).OrderBy(v => v.Slot).ToArray();

    void ResetArena()
    {
        foreach (LanVehicle vehicle in Vehicles())
        {
            int i = vehicle.Slot;
            vehicle.Body.position = new Vector3(1000 + (i % 2) * 5, 12, 1000 + (i / 2) * 5);
            vehicle.Body.rotation = Quaternion.Euler(0, i < 2 ? 0 : 180, 0);
            vehicle.Body.linearVelocity = Vector3.zero;
            vehicle.Body.angularVelocity = Vector3.zero;
        }
    }

    // 在真实场景 Trigger 上验证；仅显式开发测试启动，结束后恢复车辆与计时。
    void CheckSupportZones()
    {
        var support = FindAnyObjectByType<FieldSupportZoneBuff>();
        var manager = GameObject.Find("场地buff管理器");
        var red = Vehicles().First(v => v.Slot == 0);
        var blue = Vehicles().First(v => v.Slot == 2);
        var timer = FindAnyObjectByType<MatchTimer>();
        var remaining = typeof(MatchTimer).GetField("remainingSeconds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float savedTime = timer.RemainingSeconds;
        float savedHp = red.Stats.baseHp;
        try
        {
            if (support == null || manager == null) throw new InvalidOperationException("区域绑定缺失");
            var points = manager.GetComponentsInChildren<Transform>();
            Func<string, BoxCollider> redBox = name => points.First(t => t.name.Contains(name)
                && t.GetComponentsInParent<Transform>().Any(p => p.name == "red")).GetComponentInChildren<BoxCollider>();
            var supply = redBox("补给区增益点");
            var outpost = redBox("前哨站增益点");
            var central = points.First(t => t.name == "目标点" && t.parent.name.Contains("高地")
                && !t.parent.name.Contains("梯形")).GetComponentInChildren<BoxCollider>();
            Action<LanVehicle, BoxCollider> enter = (v, box) =>
            {
                v.Body.position = box.transform.TransformPoint(box.center);
                v.transform.position = v.Body.position;
                Physics.SyncTransforms();
                support.SendMessage("Update");
            };
            enter(red, supply);
            if (!FieldSupportZoneBuff.InOwnSupply(red.Stats)
                || Mathf.Abs(red.Stats.GetCurrent(RobotStat.RecoveryRate) - red.Stats.MaxHp * .1f) > .01f)
                throw new InvalidOperationException("己方补给区基础回血错误");
            remaining.SetValue(timer, timer.DurationSeconds - 240f);
            support.SendMessage("Update");
            if (Mathf.Abs(red.Stats.GetCurrent(RobotStat.RecoveryRate) - red.Stats.MaxHp * .25f) > .01f)
                throw new InvalidOperationException("四分钟后脱战回血错误");
            red.Stats.NotifyRoundFired(); support.SendMessage("Update");
            if (Mathf.Abs(red.Stats.GetCurrent(RobotStat.RecoveryRate) - red.Stats.MaxHp * .1f) > .01f)
                throw new InvalidOperationException("战斗状态没有立即回落到基础回血");
            red.Stats.SendMessage("BeginNaturalAftermath"); support.SendMessage("Update");
            if (red.Stats.IsWeak || red.Stats.InvulnerableSecondsRemaining > 10.01f)
                throw new InvalidOperationException("补给区未解除虚弱或自然复活无敌时长错误");
            enter(red, outpost);
            if (red.Stats.GetCurrent(RobotStat.DefenseBuffPercent) < .249f)
                throw new InvalidOperationException("前哨站防御增益缺失");
            ResetArena(); ExpireSupportHolders(support); support.SendMessage("Update");
            red.Stats.baseHp = savedHp; red.Stats.ResetToBase();
            enter(red, supply); red.Stats.Die();
            float before = red.Stats.ReviveSecondsRemaining;
            red.Stats.SendMessage("TickNaturalRevive", 1f);
            if (Mathf.Abs(before - red.Stats.ReviveSecondsRemaining - 4f) > .01f)
                throw new InvalidOperationException("己方补给区复活读条未加速四倍");
            red.Stats.baseHp = savedHp; red.Stats.ResetToBase();
            ResetArena(); ExpireSupportHolders(support); support.SendMessage("Update");
            enter(red, central); enter(blue, central);
            if (red.Stats.GetCurrent(RobotStat.DefenseBuffPercent) < .249f
                || blue.Stats.GetCurrent(RobotStat.DefenseBuffPercent) > .001f)
                throw new InvalidOperationException("中央高地占领或双方互斥错误");
            red.Stats.Die(); support.SendMessage("Update");
            if (red.Stats.GetCurrent(RobotStat.DefenseBuffPercent) > .001f
                || blue.Stats.GetCurrent(RobotStat.DefenseBuffPercent) < .249f)
                throw new InvalidOperationException("中央高地战亡未立即释放给对方");
            Debug.Log("[LAN_TEST] PASS 补给区回血/战斗降级/虚弱/四倍复活、前哨防御、中央高地互斥和战亡释放");
        }
        catch (Exception e) { error = "区域规则测试失败：" + e.Message; }
        finally
        {
            remaining.SetValue(timer, savedTime);
            red.Stats.baseHp = savedHp; red.Stats.ResetToBase();
            ResetArena(); ExpireSupportHolders(support); support.SendMessage("Update");
        }
    }

    static void ExpireSupportHolders(FieldSupportZoneBuff support)
    {
        var zones = (System.Collections.IEnumerable)typeof(FieldSupportZoneBuff)
            .GetField("zones", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(support);
        foreach (object zone in zones)
        {
            var holders = (System.Collections.IDictionary)zone.GetType().GetField("Holders").GetValue(zone);
            var keys = new object[holders.Count]; holders.Keys.CopyTo(keys, 0);
            foreach (object key in keys) holders[key] = Time.time - 2.01f;
        }
    }

    void EnterSupply(LanVehicle vehicle)
    {
        string team = vehicle.Stats.team == RobotTeam.Red ? "red" : "blue";
        var box = GameObject.Find("场地buff管理器").GetComponentsInChildren<Transform>()
            .First(t => t.name.Contains("补给区增益点") && t.GetComponentsInParent<Transform>().Any(p => p.name == team))
            .GetComponentInChildren<BoxCollider>();
        vehicle.Body.position = box.transform.TransformPoint(box.center);
        vehicle.transform.position = vehicle.Body.position;
        Physics.SyncTransforms();
    }

    void CheckRebuildAndRemote()
    {
        var red = Vehicles().First(v => v.Slot == 0);
        var infantry = Vehicles().First(v => v.Slot == 1);
        var timer = FindAnyObjectByType<MatchTimer>();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var remaining = typeof(MatchTimer).GetField("remainingSeconds", flags);
        float savedTime = timer.RemainingSeconds;
        try
        {
            foreach (var v in Vehicles().Where(v => v.Slot % 2 == 1))
            { v.Stats.baseMaxAmmo17mm = 600; v.Stats.SetBase(RobotStat.MaxAmmo17mm, 600); }
            // 真正的基地命中：护盾不计入累计血量损失。
            typeof(OutpostHealth).GetMethod("ApplyDamage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { RobotTeam.Red, 1500f });
            var spin = FindAnyObjectByType<OutpostTargetSpin>(); spin.SendMessage("Update");
            var baseCollider = FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .First(t => t.name.Contains("基地") && t.name.ToLowerInvariant().Contains("red") && !t.name.Contains("增益"))
                .GetComponentInChildren<Collider>();
            BaseHealth.TryAbsorbBullet(baseCollider, RobotTeam.Blue, true);
            if (BaseHealth.RedHp != 4950 || OutpostHealth.RebuildChances(RobotTeam.Red) != 0)
                throw new InvalidOperationException("护盾损失被错误计入重建机会");
            for (int i = 0; i < 5; i++) BaseHealth.TryAbsorbBullet(baseCollider, RobotTeam.Blue, true);
            if (OutpostHealth.RebuildChances(RobotTeam.Red) != 1) throw new InvalidOperationException("累计损血未生成重建机会");
            var box = GameObject.Find("场地buff管理器").GetComponentsInChildren<Transform>()
                .First(t => t.name.Contains("前哨站增益点") && t.GetComponentsInParent<Transform>().Any(p => p.name == "red"))
                .GetComponentInChildren<BoxCollider>();
            foreach (var v in new[] { red, infantry })
            { v.Body.position = box.transform.TransformPoint(box.center); v.transform.position = v.Body.position; }
            Physics.SyncTransforms();
            var outpost = FindAnyObjectByType<OutpostHealth>();
            for (int i = 0; i < 60; i++) outpost.SendMessage("TickRebuild", .1f);
            if (OutpostHealth.RedHp != 0) throw new InvalidOperationException("队友读条被合并，重建提前完成");
            red.Body.position = new Vector3(1000, 12, 1000); red.transform.position = red.Body.position;
            Physics.SyncTransforms(); outpost.SendMessage("TickRebuild", .1f);
            if (!string.IsNullOrEmpty(OutpostHealth.RebuildProgress(red.Stats))) throw new InvalidOperationException("离开前哨区读条未重置");
            for (int i = 0; i < 50; i++) outpost.SendMessage("TickRebuild", .1f);
            if (OutpostHealth.RedHp != 750 || !OutpostHealth.RedEverDestroyed || OutpostHealth.RebuildChances(RobotTeam.Red) != 0)
                throw new InvalidOperationException("重建血量、首次击毁标记或机会扣除错误");
            float hp = BaseHealth.RedHp; BaseHealth.TryAbsorbBullet(baseCollider, RobotTeam.Blue, true);
            if (BaseHealth.RedHp != hp) throw new InvalidOperationException("重建后基地未恢复无敌");
            spin.SendMessage("Update");
            var slots = (System.Collections.IEnumerable)typeof(OutpostTargetSpin).GetField("slots", flags).GetValue(spin);
            foreach (object slot in slots)
                if (slot.GetType().GetField("side").GetValue(slot).ToString() == "Red"
                    && !(bool)slot.GetType().GetField("stopped").GetValue(slot))
                    throw new InvalidOperationException("重建后前哨旋转重新启动");
            OutpostHealth.RecordBaseDamage(RobotTeam.Red, 1000);
            typeof(OutpostHealth).GetMethod("ApplyDamage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { RobotTeam.Red, 750f });
            remaining.SetValue(timer, timer.DurationSeconds - 300);
            for (int i = 0; i < 110; i++) outpost.SendMessage("TickRebuild", .1f);
            if (OutpostHealth.RedHp != 0 || OutpostHealth.RebuildChances(RobotTeam.Red) != 1)
                throw new InvalidOperationException("五分钟后仍允许重建");
            ResetArena(); ExpireSupportHolders(FindAnyObjectByType<FieldSupportZoneBuff>());
            var attr = infantry.Stats;
            int coins = MatchTeamEconomy.RedCoins;
            if (AmmoShop.ExecuteLanPurchase(attr, "ammo17") == null) throw new InvalidOperationException("区域外现场购弹仍成功");
            attr.NotifyRoundFired();
            if (AmmoShop.ExecuteLanPurchase(attr, "remote17") == null || MatchTeamEconomy.RedCoins != coins)
                throw new InvalidOperationException("未脱战远程兑换仍扣款");
            attr.ResetToBase();
            if (AmmoShop.ExecuteLanPurchase(attr, "remote17") != null || AmmoExchange.Pending(attr, false) != 100
                || attr.GetCurrent(RobotStat.Ammo17mm) != 0 || MatchTeamEconomy.RedCoins != coins - 150)
                throw new InvalidOperationException("远程兑换单位、扣款、在途弹量错误");
            FindAnyObjectByType<AmmoExchange>().SendMessage("Update");
            if (attr.GetCurrent(RobotStat.Ammo17mm) != 0) throw new InvalidOperationException("远程弹药提前到账");
            attr.SetBase(RobotStat.MaxAmmo17mm, 100);
            if (AmmoShop.ExecuteLanPurchase(attr, "remote17") == null) throw new InvalidOperationException("在途弹量没有占用容量");
            attr.SetBase(RobotStat.MaxAmmo17mm, 600);
            var exchange = FindAnyObjectByType<AmmoExchange>();
            var orders = (System.Collections.IEnumerable)typeof(AmmoExchange).GetField("orders", flags).GetValue(exchange);
            foreach (object order in orders) order.GetType().GetField("Due").SetValue(order, Time.time - 1);
            exchange.SendMessage("Update");
            if (attr.GetCurrent(RobotStat.Ammo17mm) != 100 || AmmoExchange.Pending(attr, false) != 0)
                throw new InvalidOperationException("到期远程弹量没有发放");
            attr.SetCurrent(RobotStat.Ammo17mm, 0);
            var blueInfantry = Vehicles().First(v => v.Slot == 3);
            EnterSupply(blueInfantry);
            for (int i = 0; i < 100; i++)
            {
                if (AmmoShop.ExecuteLanPurchase(blueInfantry.Stats, "ammo17") != null
                    || !blueInfantry.Stats.TryConsumeAmmo17mm(10)) throw new InvalidOperationException("全队额度测试购弹失败");
            }
            int cappedBalance = MatchTeamEconomy.BlueCoins;
            if (AmmoShop.ExecuteLanPurchase(blueInfantry.Stats, "ammo17") == null || MatchTeamEconomy.BlueCoins != cappedBalance)
                throw new InvalidOperationException("整局队伍上限未拦截或错误扣款");
            // 仅恢复本测试消耗的额度和余额，正常游戏不会清空整局额度。
            ((int[,])typeof(AmmoExchange).GetField("used", flags).GetValue(exchange))[1, 0] = 0;
            MatchTeamEconomy.Grant(0, 1000);
            Debug.Log("[LAN_TEST] PASS 前哨重建/独立读条/截止时间/永久停转、现场位置限制/远程脱战/扣款/在途预留/延迟到账");
        }
        catch (Exception e) { error += " 重建与交易测试失败：" + e.Message; Debug.LogError(error); }
        finally
        {
            remaining.SetValue(timer, savedTime); BaseHealth.BindForLoadedMatch(); OutpostHealth.BindForLoadedMatch();
            ResetArena(); ExpireSupportHolders(FindAnyObjectByType<FieldSupportZoneBuff>());
        }
    }

    void SeedEconomyAndAmmo()
    {
        foreach (var vehicle in Vehicles())
            if (vehicle.Stats.GetCurrent(RobotStat.Ammo17mm) != 0 || vehicle.Stats.GetCurrent(RobotStat.Ammo42mm) != 0)
                error = "开局弹量没有归零";
        var economy = FindAnyObjectByType<MatchTeamEconomy>();
        economy.redGrantAmount = economy.blueGrantAmount = 5000;
        if (MatchTeamEconomy.RedCoins != 0 || MatchTeamEconomy.BlueCoins != 0
            || !economy.GrantConfiguredAmounts()) error = "主机分配金币失败";
        economy.redGrantAmount = economy.blueGrantAmount = 400;
        try { if (!feedbackTest) LanRuneShieldSmoke.Exercise(Vehicles()); }
        catch (Exception e) { error += " 能量奖励/伤害屏蔽测试失败：" + e; Debug.LogError(error); }
        CheckSupportZones();
        CheckRebuildAndRemote();
        // 测试明确购弹后再射击，普通开局始终无弹。
        foreach (var vehicle in Vehicles())
        {
            EnterSupply(vehicle);
            for (int i = 0; i < (vehicle.Slot % 2 == 0 ? 16 : 50); i++)
            {
                string failure = AmmoShop.ExecuteLanPurchase(vehicle.Stats, vehicle.Slot % 2 == 0 ? "ammo42" : "ammo17");
                if (failure != null) error = "测试购弹失败：" + failure;
            }
        }
        if (MatchTeamEconomy.RedCoins != 4190 || MatchTeamEconomy.BlueCoins != 4340)
            error = "两队共享购弹扣费不正确";
        var redVehicle = Vehicles().First(v => v.Slot == 1);
        EnterSupply(redVehicle);
        var red = redVehicle.Stats;
        int before = MatchTeamEconomy.RedCoins;
        if (!MatchTeamEconomy.TrySpend(RobotTeam.Red, before)) error = "队伍余额精确扣除失败";
        float ammo = red.GetCurrent(RobotStat.Ammo17mm);
        if (AmmoShop.ExecuteLanPurchase(red, "ammo17") != "队伍金币不足" || red.GetCurrent(RobotStat.Ammo17mm) != ammo
            || MatchTeamEconomy.RedCoins != 0) error = "金币不足时仍然获得弹药";
        MatchTeamEconomy.Grant(before, 0);
        ResetArena();
    }

    public static LanInput OverrideInput(LanInput input, int slot)
    {
        if (instance == null) return input;
        float age = instance.Age;
        return new LanInput
        {
            x = age >= 3.1f && age < 3.7f ? 1f : 0f,
            yaw = slot < 2 ? 0 : 180,
            pitch = slot == 1 ? -60 : 0,
            use42 = slot % 2 == 0,
            fire = (slot == 0 || slot == 1) && age >= 5f && age < 5.22f
        };
    }

    public static void InspectHostSnapshot(LanSnapshot snapshot)
    {
        CheckShieldSnapshot(snapshot);
        if (instance != null && instance.host && instance.feedbackTest)
            LanFeedbackSmoke.Exercise(snapshot, instance.Age, instance.requestedSlot);
        if (instance == null || !instance.host || instance.Age < instance.testDuration) return;
        snapshot.testComplete = true;
        snapshot.testError = instance.error;
        if (!instance.finished) instance.Finish(snapshot, snapshot.testError);
    }

    public static void InspectClientSnapshot(LanSnapshot snapshot)
    {
        CheckShieldSnapshot(snapshot);
        if (instance != null && !instance.host && instance.feedbackTest)
            LanFeedbackSmoke.Check(snapshot, instance.requestedSlot);
        if (instance == null || instance.host || !snapshot.testComplete || instance.finished) return;
        instance.Finish(snapshot, snapshot.testError);
    }

    static void CheckShieldSnapshot(LanSnapshot snapshot)
    {
        if (instance == null) return;
        if (snapshot.red42mmBlocked && !snapshot.blue42mmBlocked) instance.shieldSeen = true;
        if (snapshot.red42mmBlocked != Hero42mmShield.IsBlocked(RobotTeam.Red)
            || snapshot.blue42mmBlocked != Hero42mmShield.IsBlocked(RobotTeam.Blue)) instance.error += " 屏蔽快照与本地状态不一致";
    }

    void Finish(LanSnapshot snapshot, string failure)
    {
        var session = LanSession.Instance;
        var vehicles = Vehicles();
        var report = new Report
        {
            host = host, localSlot = session != null ? session.LocalSlot : -1,
            members = session != null ? session.Members.Length : 0,
            enabledCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None).Count(c => c.enabled),
            dynamicVehicles = vehicles.Count(v => !v.Body.isKinematic),
            sequence = snapshot != null ? snapshot.sequence : 0,
            error = failure
        };
        if (snapshot != null && vehicles.Length == 4)
        {
            if (!shieldSeen) report.error += " 未收到真实网络中的屏蔽状态";
            report.redHeroExp = vehicles[0].Stats.Experience;
            report.redHeroAmmo = vehicles[0].Stats.GetCurrent(RobotStat.Ammo42mm);
            report.blueHeroHp = vehicles[2].Stats.Hp;
            report.redInfantryAmmo = vehicles[1].Stats.GetCurrent(RobotStat.Ammo17mm);
            report.blueHeroImmediateRevives = vehicles[2].Stats.ImmediateReviveCount;
            if (snapshot.redCoins != MatchTeamEconomy.RedCoins || snapshot.blueCoins != MatchTeamEconomy.BlueCoins)
                report.error += " 队伍金币同步失败";
            if (snapshot.red42mmBlocked != Hero42mmShield.IsBlocked(RobotTeam.Red)
                || snapshot.blue42mmBlocked != Hero42mmShield.IsBlocked(RobotTeam.Blue))
                report.error += " 42mm屏蔽状态同步失败";
            if (report.localSlot != requestedSlot || report.members != expectedPlayers || report.enabledCameras != 1
                || report.dynamicVehicles != (host ? 4 : 0)) report.error += " 所有权/相机/物理权威检查失败";
            bool blueHeroOccupied = session.Members.Any(m => m.slot == 2);
            if (!Application.runInBackground) report.error += " 联机未启用后台运行";
            if (report.redHeroExp < 810 || report.redHeroAmmo != 15
                || (blueHeroOccupied && (report.blueHeroHp < 200 || report.blueHeroImmediateRevives != 1))
                || (!blueHeroOccupied && report.blueHeroHp > 0)
                || (host && (infantryAmmoBeforeRemote <= 0 || infantryAmmoBeforeRemote >= 500
                    || report.redInfantryAmmo != infantryAmmoBeforeRemote + 100))
                || snapshot.robots[1].pendingAmmo17 != 0)
                report.error += " 射击、伤害、升级、复活或购买同步检查失败";
            if (!host)
                foreach (LanRobotState robot in snapshot.robots)
                    for (int stat = 0; stat < robot.stats.Length; stat++)
                        if (Mathf.Abs(vehicles[robot.slot].Stats.GetCurrent((RobotStat)stat) - robot.stats[stat]) > 0.001f)
                            report.error += " 客户端属性偏离主机快照";
        }
        else report.error += " 未取得完整四车快照";
        if (feedbackTest)
        {
            var hud = FindAnyObjectByType<CombatFeedbackHud>();
            report.progressSeen = LanFeedbackSmoke.Seen; report.progressCleared = LanFeedbackSmoke.Cleared;
            report.confirmedHits = hud != null ? hud.ReceivedHits : 0;
            report.killNotices = hud != null ? hud.ReceivedKills : 0;
            report.blockedNotices = hud != null ? hud.ReceivedBlocks : 0;
            if (!report.progressSeen || !report.progressCleared || report.confirmedHits < 1 || report.killNotices < 1
                || report.blockedNotices != 1 || !string.IsNullOrEmpty(LanFeedbackSmoke.Error))
                report.error += " 场地进度/清空、命中、击杀或保护提示去重检查失败 " + LanFeedbackSmoke.Error;
        }
        report.success = string.IsNullOrEmpty(report.error);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
        File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
        Debug.Log("LAN_SMOKE " + JsonUtility.ToJson(report));
        finished = true;
        quitAt = Time.realtimeSinceStartup + 5f;
    }
}
