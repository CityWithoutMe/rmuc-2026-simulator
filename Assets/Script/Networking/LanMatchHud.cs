using UnityEngine;
using UnityEngine.UI;

public sealed class LanMatchHud : MonoBehaviour
{
    Text status;
    void Awake()
    {
        Canvas canvas = MenuUi.CreateCanvas("LanMatchCanvas", 250);
        canvas.transform.SetParent(transform, false);
        status = MenuUi.CreateText(canvas.transform, "LanStatus", "", 20, TextAnchor.MiddleLeft, Color.white);
        MenuUi.Place(status.rectTransform, new Vector2(0.02f, 0.04f), new Vector2(0.02f, 0.04f), new Vector2(0, 0.5f));
        status.rectTransform.sizeDelta = new Vector2(1000, 50);
        Button leave = MenuUi.CreateButton(canvas.transform, "LeaveMatch", "离开房间", () =>
            LanSession.Instance.Fail("已离开房间，可重新创建或加入"), true);
        MenuUi.Place(leave.GetComponent<RectTransform>(), new Vector2(0.94f, 0.04f), new Vector2(0.94f, 0.04f), new Vector2(0.5f, 0.5f));
        leave.GetComponent<RectTransform>().sizeDelta = new Vector2(190, 55);
    }
    void Update()
    {
        if (!LanSession.Active) return;
        var session = LanSession.Instance;
        status.text = (LanSession.IsHost ? "主机" : "客户端") + " | " + LanSession.SlotLabels[session.LocalSlot]
            + " | " + (session.Loading ? "等待本局玩家完成加载…" : session.Status) + " | Esc 解锁鼠标";
    }
}
