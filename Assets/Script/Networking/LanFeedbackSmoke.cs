using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 只由 -lanTestFeedback 显式测试调用，普通对局不注入状态。
public static class LanFeedbackSmoke
{
    public static bool Seen { get; private set; }
    public static bool Cleared { get; private set; }
    public static string Error { get; private set; }
    static bool exercised;
    static int visibleSnapshots;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Seen = Cleared = exercised = false; visibleSnapshots = 0; Error = null; }
    static FieldInfo Field(object obj, string name) => obj.GetType().GetField(name,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    static object Nested(Type owner, string name) => Activator.CreateInstance(owner.GetNestedType(name, BindingFlags.NonPublic), true);
    static void Set(object obj, string name, object value) { Field(obj, name).SetValue(obj, value); }
    static object Data(object obj, string name) => Field(obj, name).GetValue(obj);

    public static void Exercise(LanSnapshot snapshot, float age, int slot)
    {
        if (age < 7f) return;
        var highland = UnityEngine.Object.FindAnyObjectByType<HighlandZoneContest>();
        var side = UnityEngine.Object.FindAnyObjectByType<SideZoneBuff>();
        var terrain = UnityEngine.Object.FindAnyObjectByType<TerrainCrossBuff>();
        var vehicles = UnityEngine.Object.FindObjectsByType<LanVehicle>(FindObjectsSortMode.None);
        if (!exercised)
        {
            exercised = true;
            highland.enabled = side.enabled = terrain.enabled = false;
            ((IList)Data(highland, "zones")).Clear(); ((IList)Data(side, "zones")).Clear();
            ((IDictionary)Data(terrain, "attempts")).Clear();
            foreach (var vehicle in vehicles)
            {
                var attr = vehicle.Stats;
                var presence = new GameObject("FeedbackTestPresence").AddComponent<HighlandZonePresence>();
                ((IList)Data(presence, "inside")).Add(attr);
                var zone = Nested(typeof(HighlandZoneContest), "Zone");
                Set(zone, "Root", presence.transform); Set(zone, "ChannelTeam", attr.team);
                Set(zone, "Channeling", true); Set(zone, "Progress", 0.5f);
                ((IList)Data(highland, "zones")).Add(zone);
                var fort = Nested(typeof(SideZoneBuff), "Zone");
                Set(fort, "Kind", Enum.Parse(Field(fort, "Kind").FieldType, "Fort"));
                var person = Nested(typeof(SideZoneBuff), "Person");
                Set(person, "Holding", true); Set(person, "Progress", 10f);
                ((IDictionary)Data(fort, "People")).Add(attr, person);
                ((IList)Data(side, "zones")).Add(fort);
                var route = Nested(typeof(TerrainCrossBuff), "Route"); Set(route, "Name", "测试隧道");
                var attempt = Nested(typeof(TerrainCrossBuff), "Attempt");
                Set(attempt, "Route", route); Set(attempt, "Deadline", Time.time + 1f);
                ((IDictionary)Data(terrain, "attempts")).Add(attr, attempt);
                CombatFeedbackHud.Publish(attr, null, "反馈测试靶", 7f);
                CombatFeedbackHud.Publish(attr, null, "保护测试靶", 0f, reason: "测试保护状态");
                CombatFeedbackHud.Publish(attr, null, "保护测试靶", 0f, reason: "测试保护状态");
            }
            CombatFeedbackHud.Publish(vehicles.First(v => v.Slot == 0).Stats, null, "全局击杀测试靶", 1f, true);
        }
        if (age >= 8f)
        {
            ((IList)Data(highland, "zones")).Clear(); ((IList)Data(side, "zones")).Clear();
            ((IDictionary)Data(terrain, "attempts")).Clear();
        }
        foreach (var robot in snapshot.robots)
            robot.fieldProgress = BuffGainHud.LocalProgress(vehicles.First(v => v.Slot == robot.slot).Stats);
        Check(snapshot, slot);
    }

    public static void Check(LanSnapshot snapshot, int slot)
    {
        string text = snapshot.robots.First(r => r.slot == slot).fieldProgress ?? "";
        if (text.Contains("梯形高地 占领中 50%") && text.Contains("堡垒增益点 占领中 50%") && text.Contains("测试隧道"))
        {
            Seen = true;
            if (++visibleSnapshots == 3 && !Application.isBatchMode)
            {
                string[] args = Environment.GetCommandLineArgs();
                int reportArg = Array.IndexOf(args, "-lanTestReport");
                if (reportArg >= 0 && reportArg + 1 < args.Length)
                    CaptureHud(System.IO.Path.ChangeExtension(args[reportArg + 1], ".png"));
            }
        }
        if (Seen && text.Length == 0) Cleared = true;
        var vehicle = UnityEngine.Object.FindObjectsByType<LanVehicle>(FindObjectsSortMode.None).First(v => v.Slot == slot);
        if (LanSession.IsClient && BuffGainHud.ProgressFor(vehicle.Stats) != text) Error = "客户端提示偏离主机快照";
    }

    // 隐藏的自动测试窗口没有可读的系统后缓冲，使用离屏渲染检查 UI 布局。
    static void CaptureHud(string path)
    {
        var local = UnityEngine.Object.FindObjectsByType<LanVehicle>(FindObjectsSortMode.None).First(v => v.IsLocal);
        var camera = local.GetComponentInChildren<Camera>();
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var previousCameras = canvases.Select(c => c.worldCamera).ToArray();
        var previousPlanes = canvases.Select(c => c.planeDistance).ToArray();
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        var target = new RenderTexture(960, 540, 24);
        var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f; }
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = RenderMode.ScreenSpaceOverlay;
                canvases[i].worldCamera = previousCameras[i]; canvases[i].planeDistance = previousPlanes[i];
            }
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(texture);
        }
    }
}
