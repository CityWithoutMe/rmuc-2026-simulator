using System.Linq;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;

public sealed class LanLobbyUI : MonoBehaviour
{
    LanSession session;
    InputField address, port;
    Text status, roster;
    Button create, join, ready, start;
    readonly Button[] seats = new Button[4];

    public static void Show()
    {
        if (Object.FindAnyObjectByType<LanLobbyUI>() != null) return;
        MenuUi.CreateCanvas("LanLobbyCanvas", 400).gameObject.AddComponent<LanLobbyUI>();
    }

    void Awake()
    {
        session = LanSession.Ensure();
        // 全屏挡住底层单机菜单，避免联网时另开单机对局。
        MenuUi.CreateBackdrop(transform, "联机作战  /  局域网大厅");
        Image panel = MenuUi.CreatePanel(transform, "LobbyPanel", new Color(0.04f, 0.065f, 0.11f, 0.97f));
        MenuUi.Place(panel.rectTransform, new Vector2(0.5f, 0.49f), new Vector2(0.5f, 0.49f), new Vector2(0.5f, 0.5f));
        panel.rectTransform.sizeDelta = new Vector2(1540f, 850f);

        Text title = PlaceText("Title", "局域网  2v2  房间", 42, 0.86f);
        title.fontStyle = FontStyle.Bold;
        title.color = MenuUi.TextPrimary;
        Text help = PlaceText("Help", "创建或加入房间，选择空闲席位并准备", 20, 0.8f);
        help.color = MenuUi.TextSecondary;

        Label("IpLabel", "主机 IPv4 地址", 0.35f, 0.735f, 400f);
        Label("PortLabel", "端口", 0.65f, 0.735f, 200f);
        address = Field("HostIp", "127.0.0.1", 0.35f, 0.68f, 400f);
        port = Field("Port", LanSession.DefaultPort.ToString(), 0.65f, 0.68f, 200f);
        port.contentType = InputField.ContentType.IntegerNumber;
        create = Button("Create", "创建房间", 0.35f, 0.59f, () =>
        { if (TryPort(out int p)) session.CreateRoom(p); }, MenuUi.ButtonTone.Primary);
        join = Button("Join", "通过 IP 加入", 0.65f, 0.59f, () =>
        { if (TryPort(out int p) && !string.IsNullOrWhiteSpace(address.text)) session.JoinRoom(address.text.Trim(), p); }, MenuUi.ButtonTone.Neutral);

        Text seatLabel = PlaceText("SeatLabel", "选择车辆席位", 17, 0.505f);
        seatLabel.color = MenuUi.TextSecondary;
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            MenuUi.ButtonTone tone = i < 2 ? MenuUi.ButtonTone.RedTeam : MenuUi.ButtonTone.BlueTeam;
            seats[i] = Button("Seat" + i, LanSession.SlotLabels[i], 0.2f + i * 0.2f, 0.45f, () => session.Select(slot), tone);
            seats[i].GetComponent<RectTransform>().sizeDelta = new Vector2(290, 62);
        }
        roster = PlaceText("Roster", "", 21, 0.35f);
        roster.color = MenuUi.TextSecondary;
        roster.rectTransform.sizeDelta = new Vector2(1320, 75);
        ready = Button("Ready", "准备", 0.35f, 0.245f, () =>
        { var me = session.Members.FirstOrDefault(m => m.peer == session.LocalPeer); session.Ready(me == null || !me.ready); }, MenuUi.ButtonTone.Neutral);
        start = Button("Start", "主机开始本局", 0.65f, 0.245f, session.StartMatch, MenuUi.ButtonTone.Primary);
        status = PlaceText("Status", "", 19, 0.15f);
        status.color = MenuUi.Warning;
        Button("Leave", "离开房间  /  返回", 0.5f, 0.065f, () => { session.Close(); Destroy(gameObject); }, MenuUi.ButtonTone.Subtle);
    }

    void Update()
    {
        create.interactable = join.interactable = !LanSession.Active;
        address.interactable = port.interactable = !LanSession.Active;
        var me = session.Members.FirstOrDefault(m => m.peer == session.LocalPeer);
        ready.interactable = LanSession.Active && !session.Loading && !session.Running && session.LocalSlot >= 0;
        ready.GetComponentInChildren<Text>().text = me != null && me.ready ? "取消准备" : "准备";
        start.interactable = session.CanStart;
        for (int slot = 0; slot < 4; slot++)
        {
            int s = slot;
            var owner = session.Members.FirstOrDefault(m => m.slot == s);
            seats[slot].interactable = LanSession.Active && !session.Loading && !session.Running
                && (owner == null || owner.peer == session.LocalPeer);
            seats[slot].GetComponentInChildren<Text>().text = LanSession.SlotLabels[slot]
                + (owner == null ? "（空闲）" : owner.peer == session.LocalPeer ? "（你）" : "（已占用）");
            if (slot == session.LocalSlot)
                MenuUi.SetButtonSelected(seats[slot], true, slot < 2 ? MenuUi.Red : MenuUi.Blue);
            else
                MenuUi.ApplyButtonTone(seats[slot], slot < 2 ? MenuUi.ButtonTone.RedTeam : MenuUi.ButtonTone.BlueTeam);
        }
        roster.text = "玩家 " + session.Members.Length + "/4\n" + string.Join("    ", session.Members.Select(m =>
            (m.peer == 0 ? "主机" : "玩家" + m.peer) + "："
            + (m.slot < 0 ? "未选车" : LanSession.SlotLabels[m.slot]) + (m.ready ? " 已准备" : " 未准备")));
        status.text = session.Status + (LanSession.IsHost ? "\n本机 IPv4：" + LocalAddresses() + "  端口：" + session.Port : "");
    }

    bool TryPort(out int value)
    {
        if (int.TryParse(port.text, out value) && value >= 1024 && value <= 65535) return true;
        status.text = "端口需为 1024–65535";
        return false;
    }

    string cachedAddresses;
    string LocalAddresses()
    {
        if (cachedAddresses != null) return cachedAddresses;
        try { cachedAddresses = string.Join(" / ", Dns.GetHostAddresses(Dns.GetHostName())
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString())); }
        catch { cachedAddresses = "请在系统网络设置查看"; }
        return cachedAddresses;
    }

    Text PlaceText(string name, string value, int size, float y)
    {
        Text text = MenuUi.CreateText(transform, name, value, size, TextAnchor.MiddleCenter, Color.white);
        MenuUi.Place(text.rectTransform, new Vector2(0.5f, y), new Vector2(0.5f, y), new Vector2(0.5f, 0.5f));
        text.rectTransform.sizeDelta = new Vector2(1600, 65);
        return text;
    }

    Button Button(string name, string label, float x, float y, UnityEngine.Events.UnityAction action,
        MenuUi.ButtonTone tone = MenuUi.ButtonTone.Neutral)
    {
        Button button = MenuUi.CreateButton(transform, name, label, action, true, tone);
        MenuUi.Place(button.GetComponent<RectTransform>(), new Vector2(x, y), new Vector2(x, y), new Vector2(0.5f, 0.5f));
        button.GetComponent<RectTransform>().sizeDelta = new Vector2(340, 60);
        return button;
    }

    InputField Field(string name, string value, float x, float y, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var image = go.AddComponent<Image>();
        image.color = Color.white;
        var field = go.AddComponent<InputField>();
        Text text = MenuUi.CreateText(go.transform, "Text", "", 24, TextAnchor.MiddleCenter, MenuUi.TextPrimary);
        MenuUi.Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(18f, 0f);
        text.rectTransform.offsetMax = new Vector2(-18f, 0f);
        field.textComponent = text;
        field.targetGraphic = image;
        field.text = value;
        field.characterLimit = 64;
        MenuUi.Place(image.rectTransform, new Vector2(x, y), new Vector2(x, y), new Vector2(0.5f, 0.5f));
        image.rectTransform.sizeDelta = new Vector2(width, 60);
        MenuUi.StyleInputField(field);
        return field;
    }

    void Label(string name, string value, float x, float y, float width)
    {
        Text label = MenuUi.CreateText(transform, name, value, 16, TextAnchor.MiddleLeft, MenuUi.TextSecondary);
        MenuUi.Place(label.rectTransform, new Vector2(x, y), new Vector2(x, y), new Vector2(0.5f, 0.5f));
        label.rectTransform.sizeDelta = new Vector2(width, 28f);
    }
}
