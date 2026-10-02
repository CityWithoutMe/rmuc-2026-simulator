using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 主菜单：联网只提示，本地跑图进入选车，退出结束运行。
public class MainMenuUI : MonoBehaviour
{
    public const string SceneName = "MainMenu";

    private Text hintText;

    private void Awake()
    {
        Canvas canvas = MenuUi.CreateCanvas("MainMenuCanvas", 0);

        Text title = MenuUi.CreateText(canvas.transform, "Title", "主菜单", 64, TextAnchor.MiddleCenter, Color.white);
        MenuUi.Place(title.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.78f), new Vector2(0.5f, 0.5f));
        title.rectTransform.sizeDelta = new Vector2(800f, 100f);

        CreateMenuButton(canvas.transform, "OnlineButton", "联网游戏", new Vector2(0.5f, 0.56f), OnOnline);
        CreateMenuButton(canvas.transform, "LocalButton", "本地跑图", new Vector2(0.5f, 0.44f), OnLocal);
        CreateMenuButton(canvas.transform, "QuitButton", "退出", new Vector2(0.5f, 0.32f), OnQuit);

        hintText = MenuUi.CreateText(canvas.transform, "Hint", "", 28, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f, 1f));
        MenuUi.Place(hintText.rectTransform, new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.5f));
        hintText.rectTransform.sizeDelta = new Vector2(800f, 60f);
    }

    private static void CreateMenuButton(Transform parent, string name, string label, Vector2 anchor, UnityEngine.Events.UnityAction onClick)
    {
        Button button = MenuUi.CreateButton(parent, name, label, onClick, true);
        MenuUi.Place(button.GetComponent<RectTransform>(), anchor, anchor, new Vector2(0.5f, 0.5f));
        button.GetComponent<RectTransform>().sizeDelta = new Vector2(420f, 72f);
    }

    private void OnOnline()
    {
        hintText.text = "暂未开放";
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
