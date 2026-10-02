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
    int selected = -1;

    private void Awake()
    {
        Canvas canvas = MenuUi.CreateCanvas("VehicleSelectCanvas", 0);

        Text title = MenuUi.CreateText(canvas.transform, "Title", "选择阵营与车辆", 52, TextAnchor.MiddleCenter, Color.white);
        MenuUi.Place(title.rectTransform, new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.5f));
        title.rectTransform.sizeDelta = new Vector2(900f, 80f);

        Text hint = MenuUi.CreateText(canvas.transform, "Hint", "可以选择红方或蓝方的英雄、步兵 1、2、3。工程暂不可用", 26, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f, 1f));
        MenuUi.Place(hint.rectTransform, new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.5f));
        hint.rectTransform.sizeDelta = new Vector2(1100f, 40f);

        BuildColumn(canvas.transform, "红方", new Color(0.9f, 0.35f, 0.35f, 1f), 0.28f, RobotTeam.Red);
        BuildColumn(canvas.transform, "蓝方", new Color(0.4f, 0.6f, 1f, 1f), 0.72f, RobotTeam.Blue);

        startButton = MenuUi.CreateButton(canvas.transform, "StartButton", "开始", OnStart, false);
        MenuUi.Place(startButton.GetComponent<RectTransform>(), new Vector2(0.62f, 0.1f), new Vector2(0.62f, 0.1f), new Vector2(0.5f, 0.5f));
        startButton.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 68f);

        Button back = MenuUi.CreateButton(canvas.transform, "BackButton", "返回", OnBack, true);
        MenuUi.Place(back.GetComponent<RectTransform>(), new Vector2(0.38f, 0.1f), new Vector2(0.38f, 0.1f), new Vector2(0.5f, 0.5f));
        back.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 68f);
    }

    private void BuildColumn(Transform parent, string teamLabel, Color titleColor, float x, RobotTeam team)
    {
        Text title = MenuUi.CreateText(parent, teamLabel + "Title", teamLabel, 36, TextAnchor.MiddleCenter, titleColor);
        MenuUi.Place(title.rectTransform, new Vector2(x, 0.74f), new Vector2(x, 0.74f), new Vector2(0.5f, 0.5f));
        title.rectTransform.sizeDelta = new Vector2(360f, 50f);

        string[] labels = { "英雄", "步兵 1", "步兵 2", "步兵 3", "工程" };
        for (int i = 0; i < labels.Length; i++)
        {
            bool engineer = i == 4;
            bool usable = !engineer;
            string text = usable ? labels[i] : labels[i] + "（暂不可用）";
            float y = 0.64f - i * 0.09f;
            int captured = choices.Count;
            UnityEngine.Events.UnityAction click = null;
            if (usable)
                click = () => OnChoose(captured);
            Button button = MenuUi.CreateButton(parent, teamLabel + labels[i], text, click, usable);
            MenuUi.Place(button.GetComponent<RectTransform>(), new Vector2(x, y), new Vector2(x, y), new Vector2(0.5f, 0.5f));
            button.GetComponent<RectTransform>().sizeDelta = new Vector2(360f, 64f);
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
            if (on && choices[i].team == RobotTeam.Blue)
            {
                MenuUi.SetButtonSelected(button, false);
                Image image = button.GetComponent<Image>();
                if (image != null)
                    image.color = new Color(0.15f, 0.35f, 0.75f, 1f);
            }
            else
            {
                MenuUi.SetButtonSelected(button, on);
            }
        }

        startButton.interactable = true;
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
