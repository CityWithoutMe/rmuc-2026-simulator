using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 选车：红蓝各五台。英雄和步兵 1、2、3 可以选中并开始对局，工程仍不可用。
public class VehicleSelectUI : MonoBehaviour
{
    public const string SceneName = "VehicleSelect";
    public const string MatchSceneName = "SampleScene";

    struct Choice
    {
        public Button button;
        public RobotTeam team;
        public RobotType type;
        public int infantryIndex;
    }

    readonly List<Choice> choices = new List<Choice>();
    Button startButton;
    Text selectionText;
    int selected = -1;

    private void Awake()
    {
        Canvas canvas = MenuUi.CreateCanvas("VehicleSelectCanvas", 0);
        MenuUi.CreateBackdrop(canvas.transform, "部署中心  /  车辆配置");

        Text title = MenuUi.CreateText(canvas.transform, "Title", "选择阵营与车辆", 46, TextAnchor.MiddleCenter, MenuUi.TextPrimary);
        title.fontStyle = FontStyle.Bold;
        MenuUi.Place(title.rectTransform, new Vector2(0.5f, 0.885f), new Vector2(0.5f, 0.885f), new Vector2(0.5f, 0.5f));
        title.rectTransform.sizeDelta = new Vector2(900f, 64f);

        Text hint = MenuUi.CreateText(canvas.transform, "Hint", "选择一台可用车辆完成部署  ·  工程机器人仍在整备中", 20, TextAnchor.MiddleCenter, MenuUi.TextSecondary);
        MenuUi.Place(hint.rectTransform, new Vector2(0.5f, 0.825f), new Vector2(0.5f, 0.825f), new Vector2(0.5f, 0.5f));
        hint.rectTransform.sizeDelta = new Vector2(1100f, 38f);

        Image redPanel = CreateTeamPanel(canvas.transform, "RedPanel", 0.285f, new Color(0.16f, 0.055f, 0.075f, 0.95f));
        Image bluePanel = CreateTeamPanel(canvas.transform, "BluePanel", 0.715f, new Color(0.045f, 0.09f, 0.2f, 0.95f));
        BuildColumn(redPanel.transform, "红方", MenuUi.Red, 0.5f, RobotTeam.Red);
        BuildColumn(bluePanel.transform, "蓝方", MenuUi.Blue, 0.5f, RobotTeam.Blue);

        selectionText = MenuUi.CreateText(canvas.transform, "Selection", "尚未选择车辆", 18, TextAnchor.MiddleCenter, MenuUi.TextSecondary);
        MenuUi.Place(selectionText.rectTransform, new Vector2(0.5f, 0.165f), new Vector2(0.5f, 0.165f), new Vector2(0.5f, 0.5f));
        selectionText.rectTransform.sizeDelta = new Vector2(620f, 34f);

        startButton = MenuUi.CreateButton(canvas.transform, "StartButton", "确认部署", OnStart, false, MenuUi.ButtonTone.Primary);
        MenuUi.Place(startButton.GetComponent<RectTransform>(), new Vector2(0.59f, 0.09f), new Vector2(0.59f, 0.09f), new Vector2(0.5f, 0.5f));
        startButton.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 66f);

        Button back = MenuUi.CreateButton(canvas.transform, "BackButton", "返回主菜单", OnBack, true, MenuUi.ButtonTone.Subtle);
        MenuUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.41f, 0.09f), new Vector2(0.41f, 0.09f), new Vector2(0.5f, 0.5f));
        back.GetComponent<RectTransform>().sizeDelta = new Vector2(320f, 66f);
    }

    private static Image CreateTeamPanel(Transform parent, string name, float x, Color color)
    {
        Image panel = MenuUi.CreatePanel(parent, name, color);
        MenuUi.Place(panel.rectTransform, new Vector2(x, 0.49f), new Vector2(x, 0.49f), new Vector2(0.5f, 0.5f));
        panel.rectTransform.sizeDelta = new Vector2(700f, 610f);
        return panel;
    }

    private void BuildColumn(Transform parent, string teamLabel, Color titleColor, float x, RobotTeam team)
    {
        Text title = MenuUi.CreateText(parent, teamLabel + "Title", teamLabel, 36, TextAnchor.MiddleCenter, titleColor);
        title.fontStyle = FontStyle.Bold;
        MenuUi.Place(title.rectTransform, new Vector2(x, 0.88f), new Vector2(x, 0.88f), new Vector2(0.5f, 0.5f));
        title.rectTransform.sizeDelta = new Vector2(540f, 52f);

        Text caption = MenuUi.CreateText(parent, teamLabel + "Caption", "TEAM  /  " + (team == RobotTeam.Red ? "RED" : "BLUE"), 15,
            TextAnchor.MiddleCenter, MenuUi.TextSecondary);
        MenuUi.Place(caption.rectTransform, new Vector2(x, 0.81f), new Vector2(x, 0.81f), new Vector2(0.5f, 0.5f));
        caption.rectTransform.sizeDelta = new Vector2(540f, 28f);

        string[] labels = { "英雄", "步兵 1", "步兵 2", "步兵 3", "工程" };
        for (int i = 0; i < labels.Length; i++)
        {
            bool engineer = i == 4;
            bool usable = !engineer;
            string text = usable ? labels[i] : labels[i] + "（暂不可用）";
            float y = 0.68f - i * 0.13f;
            int captured = choices.Count;
            UnityEngine.Events.UnityAction click = null;
            if (usable)
                click = () => OnChoose(captured);
            MenuUi.ButtonTone tone = team == RobotTeam.Red ? MenuUi.ButtonTone.RedTeam : MenuUi.ButtonTone.BlueTeam;
            Button button = MenuUi.CreateButton(parent, teamLabel + labels[i], text, click, usable, tone);
            MenuUi.Place(button.GetComponent<RectTransform>(), new Vector2(x, y), new Vector2(x, y), new Vector2(0.5f, 0.5f));
            button.GetComponent<RectTransform>().sizeDelta = new Vector2(560f, 60f);
            if (!usable)
                continue;

            choices.Add(new Choice
            {
                button = button,
                team = team,
                type = i == 0 ? RobotType.Hero : RobotType.Infantry,
                infantryIndex = i == 0 ? 0 : i
            });
        }
    }

    private void OnChoose(int index)
    {
        if (index < 0 || index >= choices.Count)
            return;

        selected = index;
        for (int i = 0; i < choices.Count; i++)
        {
            Button button = choices[i].button;
            bool on = i == index;
            Color accent = choices[i].team == RobotTeam.Red ? MenuUi.Red : MenuUi.Blue;
            if (on)
                MenuUi.SetButtonSelected(button, true, accent);
            else
                MenuUi.ApplyButtonTone(button, choices[i].team == RobotTeam.Red
                    ? MenuUi.ButtonTone.RedTeam : MenuUi.ButtonTone.BlueTeam);
        }

        startButton.interactable = true;
        Text startLabel = startButton.GetComponentInChildren<Text>();
        if (startLabel != null)
            startLabel.color = MenuUi.TextPrimary;
        Choice choice = choices[index];
        selectionText.text = "已选择  ·  " + (choice.team == RobotTeam.Red ? "红方" : "蓝方") + "  /  "
            + (choice.type == RobotType.Hero ? "英雄" : "步兵 " + choice.infantryIndex);
        selectionText.color = choice.team == RobotTeam.Red ? MenuUi.Red : MenuUi.Blue;
    }

    private void OnStart()
    {
        if (selected < 0 || selected >= choices.Count)
            return;

        Choice choice = choices[selected];
        MatchLaunchSelection.Select(choice.team, choice.type, choice.infantryIndex);
        SceneManager.LoadScene(MatchSceneName);
    }

    private static void OnBack()
    {
        SceneManager.LoadScene(MainMenuUI.SceneName);
    }
}
