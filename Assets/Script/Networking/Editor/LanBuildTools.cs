#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 只在显式菜单操作或项目 Temp 下存在构建请求时构建，不保存用户当前场景。
[InitializeOnLoad]
public static class LanBuildTools
{
    static readonly string Project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    static readonly string Request = Path.Combine(Project, "Temp", "LanBuild.request");
    static readonly string Result = Path.Combine(Project, "Logs", "LanBuild.result.json");
    static bool building;
    static LanBuildTools() { EditorApplication.update += Poll; }

    [Serializable] class BuildResult { public bool success; public string path; public string message; }

    static void Poll()
    {
        if (building || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        File.Delete(Request);
        Build();
    }

    [MenuItem("联机/构建 Windows 局域网客户端")]
    public static void Build()
    {
        building = true;
        // Unity 退出时会清空 Temp；交付构建必须放在持久的 Build 目录。
        string output = Path.Combine(Project, "Build", "LanBuild", "RMNetwork.exe");
        var result = new BuildResult { path = output };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/VehicleSelect.unity", "Assets/Scenes/SampleScene.unity" },
                locationPathName = output, target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            result.success = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            result.message = report.summary.result + "; errors=" + report.summary.totalErrors;
        }
        catch (Exception e) { result.message = e.ToString(); }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Result));
            File.WriteAllText(Result, JsonUtility.ToJson(result, true));
            building = false;
        }
    }
}
#endif
