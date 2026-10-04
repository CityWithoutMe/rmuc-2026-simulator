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
        if (mine != null && requestedSlot == 2 && age > 6f && !mine.Stats.IsAlive && !reviveSent)
        { reviveSent = true; session.Purchase("revive"); }
        if (mine != null && requestedSlot == 1 && age > 6f && !ammoSent && mine.Stats.GetCurrent(RobotStat.Ammo17mm) < 500)
        { ammoSent = true; session.Purchase("ammo17"); }
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
        if (instance != null && instance.host && instance.feedbackTest)
            LanFeedbackSmoke.Exercise(snapshot, instance.Age, instance.requestedSlot);
        if (instance == null || !instance.host || instance.Age < instance.testDuration) return;
        snapshot.testComplete = true;
        snapshot.testError = instance.error;
        if (!instance.finished) instance.Finish(snapshot, snapshot.testError);
    }

    public static void InspectClientSnapshot(LanSnapshot snapshot)
    {
        if (instance != null && !instance.host && instance.feedbackTest)
            LanFeedbackSmoke.Check(snapshot, instance.requestedSlot);
        if (instance == null || instance.host || !snapshot.testComplete || instance.finished) return;
        instance.Finish(snapshot, snapshot.testError);
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
            report.redHeroExp = vehicles[0].Stats.Experience;
            report.redHeroAmmo = vehicles[0].Stats.GetCurrent(RobotStat.Ammo42mm);
            report.blueHeroHp = vehicles[2].Stats.Hp;
            report.redInfantryAmmo = vehicles[1].Stats.GetCurrent(RobotStat.Ammo17mm);
            report.blueHeroImmediateRevives = vehicles[2].Stats.ImmediateReviveCount;
            if (report.localSlot != requestedSlot || report.members != expectedPlayers || report.enabledCameras != 1
                || report.dynamicVehicles != (host ? 4 : 0)) report.error += " 所有权/相机/物理权威检查失败";
            bool blueHeroOccupied = session.Members.Any(m => m.slot == 2);
            if (!Application.runInBackground) report.error += " 联机未启用后台运行";
            if (report.redHeroExp < 810 || report.redHeroAmmo != 15
                || (blueHeroOccupied && (report.blueHeroHp < 200 || report.blueHeroImmediateRevives != 1))
                || (!blueHeroOccupied && report.blueHeroHp > 0)
                || report.redInfantryAmmo != 500)
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
