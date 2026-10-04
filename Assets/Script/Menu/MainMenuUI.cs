using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 主菜单：局域网房间、本地跑图、退出。
public class MainMenuUI : MonoBehaviour
{
    public const string SceneName = "MainMenu";

    private Text hintText;

    private void Awake()
    {
        Canvas canvas = MenuUi.CreateCanvas("MainMenuCanvas", 0);
        MenuUi.CreateBackdrop(canvas.transform, "RoboMaster 竞技模拟器");

        Image panel = MenuUi.CreatePanel(canvas.transform, "MenuPanel", MenuUi.Surface);
        MenuUi.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        panel.rectTransform.sizeDelta = new Vector2(620f, 700f);

        Text label = MenuUi.CreateText(panel.transform, "ModeLabel", "CONTROL CENTER", 17, TextAnchor.MiddleCenter, MenuUi.PrimaryBright);
        label.fontStyle = FontStyle.Bold;
        MenuUi.Place(label.rectTransform, new Vector2(0.5f, 0.87f), new Vector2(0.5f, 0.87f), new Vector2(0.5f, 0.5f));
        label.rectTransform.sizeDelta = new Vector2(500f, 30f);

        Text title = MenuUi.CreateText(panel.transform, "Title", "选择作战模式", 48, TextAnchor.MiddleCenter, MenuUi.TextPrimary);
        title.fontStyle = FontStyle.Bold;
        MenuUi.Place(title.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.5f));
        title.rectTransform.sizeDelta = new Vector2(520f, 72f);

        Text subTitle = MenuUi.CreateText(panel.transform, "Subtitle", "进入局域网对战，或独自熟悉赛场", 21, TextAnchor.MiddleCenter, MenuUi.TextSecondary);
        MenuUi.Place(subTitle.rectTransform, new Vector2(0.5f, 0.69f), new Vector2(0.5f, 0.69f), new Vector2(0.5f, 0.5f));
        subTitle.rectTransform.sizeDelta = new Vector2(500f, 42f);

        CreateMenuButton(panel.transform, "OnlineButton", "联机对战", new Vector2(0.5f, 0.53f), OnOnline, MenuUi.ButtonTone.Primary);
        CreateMenuButton(panel.transform, "LocalButton", "本地训练", new Vector2(0.5f, 0.39f), OnLocal, MenuUi.ButtonTone.Neutral);
        CreateMenuButton(panel.transform, "QuitButton", "退出模拟器", new Vector2(0.5f, 0.25f), OnQuit, MenuUi.ButtonTone.Subtle);

        hintText = MenuUi.CreateText(panel.transform, "Hint", "●  系统就绪", 17, TextAnchor.MiddleCenter, MenuUi.TextSecondary);
        MenuUi.Place(hintText.rectTransform, new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.5f));
        hintText.rectTransform.sizeDelta = new Vector2(500f, 30f);
    }

    private static void CreateMenuButton(Transform parent, string name, string label, Vector2 anchor,
        UnityEngine.Events.UnityAction onClick, MenuUi.ButtonTone tone)
    {
        Button button = MenuUi.CreateButton(parent, name, label, onClick, true, tone);
        MenuUi.Place(button.GetComponent<RectTransform>(), anchor, anchor, new Vector2(0.5f, 0.5f));
        button.GetComponent<RectTransform>().sizeDelta = new Vector2(470f, 76f);
    }

    private void OnOnline()
    {
        LanLobbyUI.Show();
    }

    private static void OnLocal()
    {
        SceneManager.LoadScene(VehicleSelectUI.SceneName);
    }

    private static void OnQuit()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
