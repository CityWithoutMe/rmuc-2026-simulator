using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 只呈现裁定结果，不预测命中、不改变伤害、资源或胜负。
public sealed class CombatFeedbackHud : MonoBehaviour
{
    static CombatFeedbackHud instance;
    readonly Dictionary<string, float> blockedAt = new Dictionary<string, float>();
    readonly List<string> kills = new List<string>();
    readonly List<float> killExpires = new List<float>();
    Text hit, notice, feed, structures;
    Canvas canvas;
    AudioSource sound;
    AudioClip tick;
    float hitUntil, noticeUntil, nextLabels;
    LanVehicle[] vehicles;
    Text[] plates;
    RobotAttributeManager offlinePlayer;
    public int ReceivedHits { get; private set; }
    public int ReceivedKills { get; private set; }
    public int ReceivedBlocks { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; }

    public static void BindForLoadedMatch()
    {
        if (instance == null) new GameObject("CombatFeedbackHud").AddComponent<CombatFeedbackHud>();
    }

    void Awake()
    {
        instance = this;
        canvas = MenuUi.CreateCanvas("CombatFeedbackCanvas", 150);
        canvas.transform.SetParent(transform, false);
        hit = Label("HitMarker", 34, new Vector2(0.5f, 0.5f), new Vector2(100, 70));
        notice = Label("CombatNotice", 24, new Vector2(0.5f, 0.42f), new Vector2(850, 80));
        feed = Label("KillFeed", 22, new Vector2(0.77f, 0.36f), new Vector2(680, 190));
        structures = Label("TargetStatus", 20, new Vector2(0.5f, 0.77f), new Vector2(850, 90));
        sound = gameObject.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        sound.spatialBlend = 0f;
        sound.volume = 0.12f;
        var samples = new float[2205];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = Mathf.Sin(i * 2f * Mathf.PI * 880f / 44100f) * (1f - i / (float)samples.Length);
        tick = AudioClip.Create("ConfirmedHit", samples.Length, 1, 44100, false);
        tick.SetData(samples, 0);
    }

    Text Label(string name, int size, Vector2 anchor, Vector2 dimensions)
    {
        Text text = MenuUi.CreateText(canvas.transform, name, "", size, TextAnchor.MiddleCenter, Color.white);
        text.supportRichText = false;
        MenuUi.Place(text.rectTransform, anchor, anchor, new Vector2(0.5f, 0.5f));
        text.rectTransform.sizeDelta = dimensions;
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = Color.black;
        shadow.effectDistance = new Vector2(1, -1);
        return text;
    }

    public static void Publish(RobotAttributeManager attacker, RobotAttributeManager victim,
        string target, float damage, bool killed = false, string reason = null)
    {
        if (!LanSession.CanSimulate || attacker == null || (damage <= 0f && string.IsNullOrEmpty(reason))) return;
        if (instance == null) return;
        var source = LanVehicle.ForStats(attacker);
        var defender = LanVehicle.ForStats(victim);
        var feedback = new LanCombatFeedback
        {
            attackerSlot = source != null ? source.Slot : -1,
            victimSlot = defender != null ? defender.Slot : -1,
            attacker = Actor(attacker), target = target, damage = Mathf.Max(0f, damage),
            killed = killed, reason = reason
        };
        if (!string.IsNullOrEmpty(reason))
        {
            string key = attacker.GetInstanceID() + ":" + target + ":" + reason;
            if (instance.blockedAt.TryGetValue(key, out float last) && Time.unscaledTime - last < 1f) return;
            instance.blockedAt[key] = Time.unscaledTime;
        }
        if (LanSession.IsHost) LanSession.Instance.NotifyFeedback(feedback);
        else if (!LanSession.Active)
        {
            instance.BindOfflinePlayer();
            instance.Show(feedback, attacker == instance.offlinePlayer, victim == instance.offlinePlayer);
        }
    }

    public static string Actor(RobotAttributeManager stats)
    {
        return stats == null ? "未知车辆" : (stats.team == RobotTeam.Red ? "红方" : "蓝方")
            + (stats.robotType == RobotType.Hero ? "英雄" : stats.robotType == RobotType.Infantry ? "步兵" : stats.robotType.ToString());
    }

    public static void Receive(LanCombatFeedback feedback)
    {
        if (instance == null || feedback == null || !LanSession.Active) return;
        int local = LanSession.Instance.LocalSlot;
        instance.Show(feedback, local >= 0 && feedback.attackerSlot == local,
            local >= 0 && feedback.victimSlot == local);
    }

    void Show(LanCombatFeedback feedback, bool mine, bool hurt)
    {
        if (feedback.killed)
        {
            ReceivedKills++;
            if (kills.Count == 4) { kills.RemoveAt(0); killExpires.RemoveAt(0); }
            kills.Add(feedback.attacker + " 击毁 " + feedback.target);
            killExpires.Add(Time.unscaledTime + 6f);
        }
        if (mine)
        {
            noticeUntil = Time.unscaledTime + (string.IsNullOrEmpty(feedback.reason) ? 1.2f : 2f);
            notice.color = feedback.killed ? new Color(1f, 0.5f, 0.3f) : Color.white;
            if (!string.IsNullOrEmpty(feedback.reason))
            {
                ReceivedBlocks++;
                notice.text = feedback.target + "：" + feedback.reason;
            }
            else
            {
                ReceivedHits++;
                hitUntil = Time.unscaledTime + (feedback.killed ? 0.55f : 0.18f);
                hit.text = "×";
                hit.color = feedback.killed ? new Color(1f, 0.35f, 0.25f) : Color.white;
                notice.text = (feedback.killed ? "击毁 " : "命中 ") + feedback.target + "  -" + feedback.damage.ToString("0.#");
                sound.pitch = feedback.killed ? 1.5f : 1f;
                sound.PlayOneShot(tick);
            }
        }
        else if (hurt && feedback.damage > 0f)
        {
            noticeUntil = Time.unscaledTime + 1.2f;
            notice.color = new Color(1f, 0.45f, 0.4f);
            notice.text = feedback.killed ? "已战亡：被 " + feedback.attacker + " 击毁"
                : "受到 " + feedback.attacker + " 攻击  -" + feedback.damage.ToString("0.#");
        }
    }

    void BindOfflinePlayer()
    {
        if (offlinePlayer == null) PlayerAttributeBinding.TryGetPlayerStats(out offlinePlayer);
    }

    void Update()
    {
        if (Time.unscaledTime > hitUntil) hit.text = "";
        if (Time.unscaledTime > noticeUntil) notice.text = "";
        for (int i = kills.Count - 1; i >= 0; i--)
            if (Time.unscaledTime > killExpires[i]) { kills.RemoveAt(i); killExpires.RemoveAt(i); }
        feed.text = string.Join("\n", kills);
        if (Time.unscaledTime < nextLabels) return;
        nextLabels = Time.unscaledTime + 0.1f;
        RobotTeam mine;
        if (LanSession.Active && LanSession.Instance.LocalSlot >= 0)
            mine = LanSession.Instance.LocalSlot < 2 ? RobotTeam.Red : RobotTeam.Blue;
        else { BindOfflinePlayer(); if (offlinePlayer == null) return; mine = offlinePlayer.team; }
        RobotTeam enemy = mine == RobotTeam.Red ? RobotTeam.Blue : RobotTeam.Red;
        structures.text = "敌方前哨 " + OutpostHealth.HpOf(enemy).ToString("0")
            + "  |  敌方基地 " + BaseHealth.HpOf(enemy).ToString("0") + "  护盾 " + BaseHealth.ShieldOf(enemy).ToString("0")
            + "\n" + (OutpostHealth.HpOf(enemy) > 0f ? "基地受保护：先击毁敌方前哨" : "基地可攻击");
        RefreshPlates(mine);
    }

    void RefreshPlates(RobotTeam mine)
    {
        if (!LanSession.Active) return;
        if (vehicles == null || vehicles.Length == 0)
        {
            vehicles = FindObjectsByType<LanVehicle>(FindObjectsSortMode.None);
            plates = new Text[vehicles.Length];
            for (int i = 0; i < vehicles.Length; i++) plates[i] = Label("VehicleIdentity" + vehicles[i].Slot, 20, Vector2.zero, new Vector2(340, 40));
        }
        Camera camera = null;
        Transform local = null;
        foreach (var vehicle in vehicles)
            if (vehicle != null && vehicle.IsLocal)
            { local = vehicle.transform; camera = vehicle.GetComponentInChildren<Camera>(); break; }
        for (int i = 0; i < vehicles.Length; i++)
        {
            var vehicle = vehicles[i];
            var plate = plates[i];
            plate.text = "";
            if (vehicle == null || vehicle.IsLocal || camera == null) continue;
            var col = vehicle.GetComponent<Collider>();
            Vector3 center = col != null ? col.bounds.center : vehicle.transform.position;
            Vector3 point = center + Vector3.up * ((col != null ? col.bounds.extents.y : 1f) + 0.3f);
            Vector3 screen = camera.WorldToViewportPoint(point);
            if (screen.z <= 0f || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1) continue;
            bool blocked = false;
            Vector3 direction = center - camera.transform.position;
            foreach (var obstacle in Physics.RaycastAll(camera.transform.position, direction.normalized,
                direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!obstacle.transform.IsChildOf(vehicle.transform) && !obstacle.transform.IsChildOf(local))
                { blocked = true; break; }
            if (blocked) continue; // 不提供穿墙敌情。
            bool friendly = vehicle.Stats.team == mine;
            plate.text = (friendly ? "友军 " : "敌军 ") + LanSession.SlotLabels[vehicle.Slot]
                + (vehicle.Stats.IsAlive ? "" : "（战亡）");
            plate.color = friendly ? new Color(0.45f, 1f, 0.6f) : new Color(1f, 0.45f, 0.4f);
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(screen.x, screen.y);
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (tick != null) Destroy(tick);
    }
}
