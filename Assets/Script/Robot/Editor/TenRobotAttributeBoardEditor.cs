using UnityEditor;
using UnityEngine;

// 在「属性管理器」上把 10 份已有的 RobotAttributeManager 检视器展开。
// 不另做一套可序列化字段，改这里就是改子物体上的那一份。
[CustomEditor(typeof(TenRobotAttributeBoard))]
public class TenRobotAttributeBoardEditor : Editor
{
    readonly bool[] open = { true, true, true, true, true, true, true, true, true, true };

    Editor[] nested = new Editor[TenRobotAttributeBoard.SlotCount];

    void OnDisable()
    {
        for (int i = 0; i < nested.Length; i++)
        {
            if (nested[i] == null)
                continue;
            DestroyImmediate(nested[i]);
            nested[i] = null;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "挂在场景物体「属性管理器」上。添加组件时会在下面生成 10 个子物体；已有同名的会接着用，不覆盖已经改过的数。每个英文标题下展开的就是那辆车的属性，改完请 Ctrl+S 保存场景。红、蓝第 5 辆是工程。",
            MessageType.Info);

        SerializedProperty script = serializedObject.FindProperty("m_Script");
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(script);

        for (int i = 0; i < TenRobotAttributeBoard.SlotCount; i++)
        {
            SerializedProperty prop = serializedObject.FindProperty(TenRobotAttributeBoard.SlotPropertyNames[i]);
            if (prop == null)
                continue;

            EditorGUILayout.Space(4f);
            // 画出字段上的 [Header("red_hero")] 等原文，下面再展开那一份 RobotAttributeManager。
            EditorGUILayout.PropertyField(prop, new GUIContent("属性"));
            serializedObject.ApplyModifiedProperties();

            DrawNested(prop.objectReferenceValue as RobotAttributeManager, i);
        }
    }

    void DrawNested(RobotAttributeManager ram, int index)
    {
        if (ram == null)
        {
            if (nested[index] != null)
            {
                DestroyImmediate(nested[index]);
                nested[index] = null;
            }

            return;
        }

        open[index] = EditorGUILayout.Foldout(open[index], "本车属性", true);
        if (!open[index])
            return;

        Editor.CreateCachedEditor(ram, null, ref nested[index]);
        if (nested[index] == null)
            return;

        EditorGUI.indentLevel++;
        nested[index].OnInspectorGUI();
        EditorGUI.indentLevel--;
    }
}
