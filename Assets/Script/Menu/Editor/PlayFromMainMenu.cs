using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 编辑器点 Play 时从主菜单开跑。playModeStartScene 只决定这次运行进哪个场景，停止后仍回到进 Play 之前打开的场景。
[InitializeOnLoad]
public static class PlayFromMainMenu
{
    const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

    static PlayFromMainMenu()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
            return;

        SceneAsset mainMenu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath);
        if (mainMenu == null)
        {
            Debug.LogWarning("未找到主菜单场景，Play 将打开当前场景：" + MainMenuScenePath);
            return;
        }

        EditorSceneManager.playModeStartScene = mainMenu;
    }
}
