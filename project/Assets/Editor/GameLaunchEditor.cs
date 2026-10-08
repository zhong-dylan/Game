using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameLaunch))]
public class GameLaunchEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        var config = serializedObject.FindProperty("launchConfig");
        var configs = FindConfigs();
        var current = config.objectReferenceValue as LaunchConfig;
        if (current != null && !configs.Contains(current))
            configs.Add(current);

        if (configs.Count == 0)
        {
            EditorGUILayout.HelpBox("请先创建 LaunchConfig 资源。", MessageType.Warning);
            return;
        }

        var selectedIndex = configs.IndexOf(current);
        if (selectedIndex < 0)
        {
            selectedIndex = configs.FindIndex(item =>
                string.Equals(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(item)),
                    "Debug", StringComparison.OrdinalIgnoreCase));
            if (selectedIndex < 0)
                selectedIndex = 0;
            config.objectReferenceValue = configs[selectedIndex];
        }

        var options = new string[configs.Count];
        for (var i = 0; i < configs.Count; i++)
            options[i] = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(configs[i]));

        var newIndex = EditorGUILayout.Popup("Launch Config", selectedIndex, options);
        config.objectReferenceValue = configs[newIndex];
        serializedObject.ApplyModifiedProperties();
    }

    private static List<LaunchConfig> FindConfigs()
    {
        var paths = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:LaunchConfig"))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(StringComparer.Ordinal);

        var configs = new List<LaunchConfig>();
        foreach (var path in paths)
        {
            var config = AssetDatabase.LoadAssetAtPath<LaunchConfig>(path);
            if (config != null)
                configs.Add(config);
        }
        return configs;
    }
}
