using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-500)]
public sealed class LanSession : MonoBehaviour
{
    public const int DefaultPort = 7777;
    public static readonly string[] SlotLabels = { "红方英雄", "红方步兵", "蓝方英雄", "蓝方步兵" };
    public static readonly string[] SlotHeaders = { "red_hero", "red_infantry_1", "blue_hero", "blue_infantry_1" };
    public static LanSession Instance { get; private set; }
    public static bool Active => Instance != null && Instance.transport != null;
    public static bool IsHost => Active && Instance.host;
    public static bool IsClient => Active && !Instance.host;
    public static bool CanSimulate => !Active || (IsHost && Instance.Running);

    public bool Running { get; private set; }
    public bool Loading { get; private set; }
    public int LocalPeer { get; private set; } = -1;
    public int LocalSlot => Members.FirstOrDefault(m => m.peer == LocalPeer)?.slot ?? -1;
    public LanMember[] Members { get; private set; } = Array.Empty<LanMember>();
    public bool CanStart => host && room != null && room.CanStart;
    public string Status { get; private set; } = "选择创建房间或加入房间";
    public int Port { get; private set; }

    LanTransport transport;
    LanRoom room;
    bool host;
    LanWorld world;
    readonly Dictionary<int, LanInput> inputs = new Dictionary<int, LanInput>();
    readonly Dictionary<int, float> inputTimes = new Dictionary<int, float>();
    readonly Dictionary<int, float> heard = new Dictionary<int, float>();
    readonly Dictionary<int, (double time, int count)> limits = new Dictionary<int, (double, int)>();
    float nextInput, nextSnapshot, nextHeartbeat, loadingDeadline;
    float heartbeatGrace;
    bool previousBackground;
    Coroutine loadRoutine, readyRoutine;
    public bool CancelledLoad { get; private set; }
    int sequence, shotId, receivedSequence;
    readonly Dictionary<int, GameObject> visualShots = new Dictionary<int, GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Instance = null; }

    public static LanSession Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("LanSession").AddComponent<LanSession>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        previousBackground = Application.runInBackground;
    }

    public void CreateRoom(int port)
    {
        Close();
        try
        {
            transport = new LanTransport();
            transport.Host(port);
            Application.runInBackground = true;
            host = true;
            Port = port;
            LocalPeer = 0;
            room = new LanRoom();
            room.Add(0);
            PublishRoom();
            Status = "房间已创建，等待其他玩家通过本机局域网 IP 加入";
        }
        catch (Exception e) { Close(); Status = "创建失败：" + e.Message; }
    }

    public void JoinRoom(string address, int port)
    {
        Close();
        host = false;
        Port = port;
        transport = new LanTransport();
        Application.runInBackground = true;
        Status = "正在连接 " + address + ":" + port;
        transport.Join(address, port);
    }

    public void Select(int slot) { Request(new LanMessage { kind = "select", slot = slot }); }
    public void Ready(bool ready) { Request(new LanMessage { kind = "ready", ready = ready }); }
    public void StartMatch() { Request(new LanMessage { kind = "start" }); }

    void Request(LanMessage message)
    {
        if (!Active) return;
        if (host) Handle(0, message);
        else transport.Send(0, JsonUtility.ToJson(message));
    }

    void Update()
    {
        if (transport == null) return;
        for (int i = 0; i < 256 && transport.Poll(out LanTransport.Event e); i++)
        {
            heard[e.peer] = Time.unscaledTime;
            if (e.kind == "connected")
            {
                if (host)
                {
                    if (!room.Add(e.peer)) { transport.Disconnect(e.peer); continue; }
                    Send(e.peer, new LanMessage { kind = "welcome", peer = e.peer });
                    PublishRoom();
                }
            }
            else if (e.kind == "data")
            {
                try
                {
                    // 按网络线程实际收包时间限速，而非加载结束后同一帧处理积压的时间。
                    // 客户端不对可信主机的快照回放套客户端请求限速。
                    if (host && !RateAllowed(e.peer, e.receivedAt))
                    { Debug.LogWarning("[LAN] 请求限速断开 peer=" + e.peer); transport.Disconnect(e.peer); continue; }
                    LanMessage message = JsonUtility.FromJson<LanMessage>(e.data);
                    if (message == null || message.version != LanMessage.Protocol)
                        throw new InvalidOperationException("联机版本不一致，请使用同一版本客户端");
                    Handle(e.peer, message);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[LAN] 数据处理失败 peer=" + e.peer + ": " + ex);
                    if (host) transport.Disconnect(e.peer);
                    else Fail("联机数据错误：" + ex.Message);
                    if (transport == null) return;
                }
            }
            else if (e.kind == "disconnected")
            {
                if (host && !room.Members.Any(m => m.peer == e.peer)) continue;
                if (!host || Loading || Running)
                { Fail("玩家 " + e.peer + " 断开，本局已终止：" + e.data); return; }
                room.Remove(e.peer);
                inputs.Remove(e.peer);
                limits.Remove(e.peer);
                PublishRoom();
                Status = "玩家离开，车位已释放";
            }
            else if (e.kind == "error") { Fail("连接失败：" + e.data); return; }
        }
        if (Running && Time.unscaledTime >= heartbeatGrace)
        {
            foreach (int peer in host ? Members.Where(m => m.peer != 0).Select(m => m.peer) : new[] { 0 })
                if (heard.TryGetValue(peer, out float at) && Time.unscaledTime - at > 30f)
                { Fail("联机心跳超时，本局已终止"); return; }
        }
        if (Loading && Time.realtimeSinceStartup > loadingDeadline)
        { Fail("等待场景加载超时，本局已终止"); return; }
        if (Time.unscaledTime >= nextHeartbeat)
        {
            nextHeartbeat = Time.unscaledTime + 1f;
            if (host) Broadcast(new LanMessage { kind = "ping" });
            else Send(0, new LanMessage { kind = "ping" });
        }
    }

    bool RateAllowed(int peer, double now)
    {
        if (!limits.TryGetValue(peer, out var stamp) || now - stamp.time >= 1f) stamp = (now, 0);
        stamp.count++;
        limits[peer] = stamp;
        return stamp.count <= 180;
    }

    void LateUpdate()
    {
        if (!IsHost || !Running || world == null || Time.unscaledTime < nextSnapshot) return;
        nextSnapshot = Time.unscaledTime + 0.05f;
        LanSnapshot snapshot = world.Capture(++sequence);
        LanSmokeTest.InspectHostSnapshot(snapshot);
        Broadcast(new LanMessage { kind = "snapshot", snapshot = snapshot });
    }

    void Handle(int peer, LanMessage message)
    {
        if (message.kind == "ping") return;
        if (host)
        {
            // peer 来自连接，不使用客户端传入的 peer/slot 作为操作身份。
            if (message.kind == "select")
            {
                if (!room.Select(peer, message.slot)) { Error(peer, "这台车已被占用，或对局已经开始"); return; }
                PublishRoom();
            }
            else if (message.kind == "ready")
            {
                if (!room.Ready(peer, message.ready)) { Error(peer, "请先选择车辆"); return; }
                PublishRoom();
            }
            else if (message.kind == "start")
            {
                if (!room.Start(peer)) { Error(peer, "所有已加入玩家需选好不同车辆并准备，由主机开始"); return; }
                var start = new LanMessage { kind = "load", members = Members };
                Broadcast(start);
                LoadMatch();
            }
            else if (message.kind == "loaded")
            {
                room.Loaded(peer);
                if (room.AllLoaded && Loading)
                {
                    Loading = false;
                    Running = true;
                    heartbeatGrace = Time.unscaledTime + 60f;
                    world.Begin();
                    Broadcast(new LanMessage { kind = "begin" });
                    Status = Members.Length + " 人对局进行中";
                    Debug.Log("[LAN] 开局：人数=" + Members.Length);
                }
            }
            else if (message.kind == "input" && Running && room.Slot(peer) >= 0)
            {
                if (!ValidInput(message.input)) { transport.Disconnect(peer); return; }
                int slot = room.Slot(peer);
                inputs[slot] = message.input;
                inputTimes[slot] = Time.unscaledTime;
            }
            else if (message.kind == "purchase" && Running && !MatchOutcome.Decided)
            {
                int slot = room.Slot(peer);
                if (slot < 0 || world == null) return;
                string result = AmmoShop.ExecuteLanPurchase(world.Vehicles[slot].Stats, message.action);
                if (!string.IsNullOrEmpty(result)) Error(peer, result);
            }
            return;
        }

        // 客户端只接受来自主机的房间、场景、快照和视觉事件。
        switch (message.kind)
        {
            case "welcome": LocalPeer = message.peer; Status = "已加入，请选择车辆并准备"; break;
            case "room": Members = message.members ?? Array.Empty<LanMember>(); ApplyLocalSelection(); break;
            case "error": Status = message.text; break;
            case "load": Members = message.members; LoadMatch(); break;
            case "begin":
                Loading = false; Running = true; heartbeatGrace = Time.unscaledTime + 60f;
                world?.Begin(); Status = Members.Length + " 人对局进行中"; break;
            case "snapshot":
                if (world != null && message.snapshot != null && message.snapshot.sequence > receivedSequence)
                { receivedSequence = message.snapshot.sequence; world.Apply(message.snapshot); LanSmokeTest.InspectClientSnapshot(message.snapshot); }
                break;
            case "shot": if (world != null) ShowShot(message.shot); break;
            case "feedback":
                if (Running && world != null && message.feedback != null)
                    CombatFeedbackHud.Receive(message.feedback);
                break;
            case "impact":
                if (visualShots.TryGetValue(message.peer, out GameObject bullet)) Destroy(bullet);
                visualShots.Remove(message.peer);
                break;
        }
    }

    static bool ValidInput(LanInput input)
    {
        return input != null && Finite(input.x) && Finite(input.z) && Finite(input.yaw) && Finite(input.pitch)
            && Mathf.Abs(input.x) <= 1.01f && Mathf.Abs(input.z) <= 1.01f && Mathf.Abs(input.pitch) <= 80.1f;
    }

    static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n) && Mathf.Abs(n) < 100000f;

    void ApplyLocalSelection()
    {
        int slot = LocalSlot;
        if (slot < 0) return;
        MatchLaunchSelection.Select(slot < 2 ? RobotTeam.Red : RobotTeam.Blue,
            slot % 2 == 0 ? RobotType.Hero : RobotType.Infantry, slot % 2);
    }

    void PublishRoom()
    {
        Members = room.Members.Select(m => new LanMember { peer = m.peer, slot = m.slot, ready = m.ready }).ToArray();
        ApplyLocalSelection();
        Broadcast(new LanMessage { kind = "room", members = Members });
    }

    void LoadMatch()
    {
        if (Loading || Running) return;
        ApplyLocalSelection();
        if (LocalSlot < 0) { Fail("未分配车辆，无法进入对局"); return; }
        Loading = true;
        loadingDeadline = Time.realtimeSinceStartup + 180f;
        Status = "正在加载场景，等待本局 " + Members.Length + " 位玩家";
        Debug.Log("[LAN] 开始异步加载，peer=" + LocalPeer + " slot=" + LocalSlot);
        loadRoutine = StartCoroutine(LoadMatchAsync());
    }

    IEnumerator LoadMatchAsync()
    {
        yield return SceneManager.LoadSceneAsync(VehicleSelectUI.MatchSceneName);
        loadRoutine = null;
        if (CancelledLoad)
        {
            CancelledLoad = false;
            SceneManager.LoadScene(MainMenuUI.SceneName);
            LanLobbyUI.Show();
            Application.runInBackground = previousBackground;
        }
    }

    public void AttachWorld(LanWorld value)
    {
        world = value;
        readyRoutine = StartCoroutine(ReportSceneReady());
    }

    IEnumerator ReportSceneReady()
    {
        // sceneLoaded 早于 Start 和首次渲染；等初始化及首帧完成后才通知主机。
        yield return null;
        yield return null;
        readyRoutine = null;
        if (!Active || !Loading || world == null) yield break;
        Debug.Log("[LAN] 场景准备完成，peer=" + LocalPeer);
        Request(new LanMessage { kind = "loaded" });
    }

    public void SubmitInput(LanInput input)
    {
        if (!Running) return;
        if (host) { inputs[LocalSlot] = input; inputTimes[LocalSlot] = Time.unscaledTime; return; }
        if (Time.unscaledTime < nextInput) return;
        nextInput = Time.unscaledTime + 1f / 30f;
        Send(0, new LanMessage { kind = "input", input = input });
    }

    public LanInput InputFor(int slot)
    {
        if (Running && inputTimes.TryGetValue(slot, out float at) && Time.unscaledTime - at < 0.3f
            && inputs.TryGetValue(slot, out LanInput input)) return input;
        return new LanInput { yaw = world != null ? world.Vehicles[slot].transform.eulerAngles.y : 0f };
    }

    public void Purchase(string action) { Request(new LanMessage { kind = "purchase", action = action }); }

    // 网络中断尚未触发整局终止时，裁判逻辑也能识别失联英雄。
    public bool IsRobotOnline(RobotAttributeManager stats)
    {
        if (!Running || world == null) return true;
        foreach (var member in Members)
        {
            if (member.slot < 0 || world.Vehicles[member.slot].Stats != stats) continue;
            if (member.peer == LocalPeer) return true;
            return heard.TryGetValue(member.peer, out float last) && Time.unscaledTime - last <= 3f;
        }
        return true;
    }

    public void NotifyFeedback(LanCombatFeedback feedback)
    {
        if (!IsHost || !Running || feedback == null) return;
        CombatFeedbackHud.Receive(feedback);
        var message = new LanMessage { kind = "feedback", feedback = feedback };
        foreach (var member in Members)
            if (member.peer != LocalPeer && (feedback.killed || member.slot == feedback.attackerSlot
                || member.slot == feedback.victimSlot)) Send(member.peer, message);
    }

    public int NotifyShot(LanVehicle vehicle, bool use42, Vector3 position, Vector3 direction, float speed)
    {
        if (!IsHost) return 0;
        int id = ++shotId;
        Broadcast(new LanMessage { kind = "shot", shot = new LanShot
        { id = id, slot = vehicle.Slot, use42 = use42, position = position, direction = direction, speed = speed } });
        return id;
    }

    public void NotifyImpact(int id)
    {
        if (IsHost && id > 0) Broadcast(new LanMessage { kind = "impact", peer = id });
    }

    void ShowShot(LanShot shot)
    {
        if (shot == null || shot.slot < 0 || shot.slot >= 4) return;
        GameObject visual = world.Vehicles[shot.slot].Shooting.CreateLanVisual(shot);
        if (visual != null) visualShots[shot.id] = visual;
    }

    void Error(int peer, string text)
    {
        if (peer == 0) Status = text;
        else Send(peer, new LanMessage { kind = "error", text = text });
    }

    void Send(int peer, LanMessage message) { transport?.Send(peer, JsonUtility.ToJson(message)); }
    void Broadcast(LanMessage message) { transport?.Broadcast(JsonUtility.ToJson(message)); }

    public void Fail(string reason)
    {
        Debug.LogWarning("[LAN] 返回房间：" + reason + "；host=" + host + " loading=" + Loading + " running=" + Running);
        Close();
        Status = reason;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // Unity 异步加载不能取消；先让它完成，再返回，避免随后又跳入已断开的比赛。
        if (loadRoutine != null) return;
        if (SceneManager.GetActiveScene().name != MainMenuUI.SceneName)
            SceneManager.LoadScene(MainMenuUI.SceneName);
        LanLobbyUI.Show();
    }

    public void Close()
    {
        if (loadRoutine != null) CancelledLoad = true;
        if (readyRoutine != null) StopCoroutine(readyRoutine);
        readyRoutine = null;
        transport?.Dispose();
        transport = null;
        room = null;
        world = null;
        Running = Loading = false;
        host = false;
        LocalPeer = -1;
        Members = Array.Empty<LanMember>();
        inputs.Clear(); inputTimes.Clear(); limits.Clear(); heard.Clear(); visualShots.Clear();
        sequence = receivedSequence = shotId = 0;
        nextInput = nextSnapshot = nextHeartbeat = 0f;
        Application.runInBackground = loadRoutine != null || previousBackground;
    }

    void OnApplicationQuit() { Close(); }
    void OnDestroy() { if (Instance == this) { Close(); Instance = null; } }
}
