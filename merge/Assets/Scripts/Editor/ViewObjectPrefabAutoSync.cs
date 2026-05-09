using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ViewObjectPrefabAutoSync : AssetPostprocessor
{
    private const string PrefabFolder = "Assets/Addressables/Prefabs/";
    private const string PrefabFolderTrimmed = "Assets/Addressables/Prefabs";
    private static readonly Vector2 CanvasEnvironmentSize = Const.UIReferenceResolution;
    private static bool s_IsSyncing;

    [InitializeOnLoadMethod]
    private static void InitializeOnLoad()
    {
        SyncAllPrefabs();
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (s_IsSyncing)
        {
            return;
        }

        bool changed = false;

        for (int i = 0; i < importedAssets.Length; i++)
        {
            changed |= SyncPrefab(importedAssets[i]);
        }

        for (int i = 0; i < movedAssets.Length; i++)
        {
            changed |= SyncPrefab(movedAssets[i]);
        }

        if (changed)
        {
            AssetDatabase.SaveAssets();
        }
    }

    [MenuItem("Tools/ViewObject/Sync Addressables Prefabs")]
    private static void SyncAllPrefabs()
    {
        if (s_IsSyncing)
        {
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolderTrimmed });
        bool changed = false;

        s_IsSyncing = true;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                changed |= SyncPrefabInternal(assetPath);
            }
        }
        finally
        {
            s_IsSyncing = false;
        }

        if (changed)
        {
            AssetDatabase.SaveAssets();
        }
    }

    private static bool SyncPrefab(string assetPath)
    {
        if (s_IsSyncing || !IsTargetPrefab(assetPath))
        {
            return false;
        }

        s_IsSyncing = true;
        try
        {
            return SyncPrefabInternal(assetPath);
        }
        finally
        {
            s_IsSyncing = false;
        }
    }

    private static bool SyncPrefabInternal(string assetPath)
    {
        if (!IsTargetPrefab(assetPath))
        {
            return false;
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);
        if (prefabRoot == null)
        {
            return false;
        }

        try
        {
            bool changed = false;
            ViewObject viewObject = prefabRoot.GetComponent<ViewObject>();
            if (viewObject == null)
            {
                viewObject = prefabRoot.AddComponent<ViewObject>();
                changed = true;
            }

            changed |= SyncCanvasEnvironment(prefabRoot.transform);
            changed |= SyncGameObjects(viewObject);
            if (!changed)
            {
                return false;
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool SyncGameObjects(ViewObject viewObject)
    {
        SerializedObject serializedObject = new SerializedObject(viewObject);
        SerializedProperty gameObjectsProperty = serializedObject.FindProperty("m_GameObjects");
        if (gameObjectsProperty == null)
        {
            return false;
        }

        List<GameObject> targets = CollectTargets(viewObject.transform);
        if (IsSameList(gameObjectsProperty, targets))
        {
            return false;
        }

        gameObjectsProperty.arraySize = targets.Count;
        for (int i = 0; i < targets.Count; i++)
        {
            gameObjectsProperty.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(viewObject);
        return true;
    }

    private static bool SyncCanvasEnvironment(Transform root)
    {
        RectTransform canvasEnvironment = FindCanvasEnvironment(root);
        if (canvasEnvironment == null)
        {
            return false;
        }

        bool changed = false;
        if (canvasEnvironment.anchoredPosition != Vector2.zero)
        {
            canvasEnvironment.anchoredPosition = Vector2.zero;
            changed = true;
        }

        if (canvasEnvironment.localPosition != Vector3.zero)
        {
            canvasEnvironment.localPosition = Vector3.zero;
            changed = true;
        }

        if (canvasEnvironment.sizeDelta != CanvasEnvironmentSize)
        {
            canvasEnvironment.sizeDelta = CanvasEnvironmentSize;
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(canvasEnvironment);
        }

        return changed;
    }

    private static List<GameObject> CollectTargets(Transform root)
    {
        List<GameObject> targets = new List<GameObject>();
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == null || current == root)
            {
                continue;
            }

            if (current.name.Contains("_"))
            {
                targets.Add(current.gameObject);
            }
        }

        return targets;
    }

    private static RectTransform FindCanvasEnvironment(Transform root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == null)
            {
                continue;
            }

            if (string.Equals(current.name, "Canvas(Environment)", System.StringComparison.OrdinalIgnoreCase))
            {
                return current as RectTransform;
            }
        }

        return null;
    }

    private static bool IsSameList(SerializedProperty gameObjectsProperty, List<GameObject> targets)
    {
        if (gameObjectsProperty.arraySize != targets.Count)
        {
            return false;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            if (gameObjectsProperty.GetArrayElementAtIndex(i).objectReferenceValue != targets[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTargetPrefab(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        if (!assetPath.StartsWith(PrefabFolder))
        {
            return false;
        }

        return assetPath.EndsWith(".prefab");
    }
}
