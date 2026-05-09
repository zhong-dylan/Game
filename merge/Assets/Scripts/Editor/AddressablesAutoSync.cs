using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public class AddressablesAutoSync : AssetPostprocessor
{
    private const string RootFolder = "Assets/Addressables/";
    private const string RootFolderTrimmed = "Assets/Addressables";

    [InitializeOnLoadMethod]
    private static void InitializeOnLoad()
    {
        SyncAllAssets();
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        bool changed = false;

        for (int i = 0; i < importedAssets.Length; i++)
        {
            changed |= SyncAsset(importedAssets[i]);
        }

        for (int i = 0; i < movedAssets.Length; i++)
        {
            if (IsValidAddressableAsset(movedAssets[i]))
            {
                changed |= SyncAsset(movedAssets[i]);
            }
            else
            {
                changed |= RemoveAssetEntryByGuid(AssetDatabase.AssetPathToGUID(movedAssets[i]));
            }
        }

        for (int i = 0; i < deletedAssets.Length; i++)
        {
            changed |= RemoveAssetEntry(deletedAssets[i]);
        }

        for (int i = 0; i < movedFromAssetPaths.Length; i++)
        {
            changed |= RemoveAssetEntry(movedFromAssetPaths[i]);
        }

        if (changed)
        {
            SaveSettings();
        }
    }

    [MenuItem("Tools/Addressables/Sync Folder")]
    private static void SyncAllAssets()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            return;
        }

        bool changed = false;
        string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { RootFolderTrimmed });
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            changed |= SyncAsset(assetPath);
        }

        if (changed)
        {
            SaveSettings();
        }
    }

    private static bool SyncAsset(string assetPath)
    {
        if (!IsValidAddressableAsset(assetPath))
        {
            return false;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            return false;
        }

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
        {
            return false;
        }

        AddressableAssetGroup group = settings.DefaultGroup;
        if (group == null)
        {
            Log.Error("Addressables default group is missing.");
            return false;
        }

        string address = BuildAddress(assetPath);
        AddressableAssetEntry entry = settings.FindAssetEntry(guid);
        if (entry == null)
        {
            entry = settings.CreateOrMoveEntry(guid, group, false, false);
        }
        else if (entry.parentGroup != group)
        {
            settings.MoveEntry(entry, group, false, false);
        }

        if (entry == null)
        {
            return false;
        }

        if (entry.address == address)
        {
            return false;
        }

        entry.SetAddress(address, false);
        return true;
    }

    private static bool RemoveAssetEntry(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".meta"))
        {
            return false;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            return false;
        }

        return RemoveAssetEntryByGuid(AssetDatabase.AssetPathToGUID(assetPath));
    }

    private static bool RemoveAssetEntryByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return false;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            return false;
        }

        AddressableAssetEntry entry = settings.FindAssetEntry(guid);
        if (entry == null)
        {
            return false;
        }

        settings.RemoveAssetEntry(guid, false);
        return true;
    }

    private static bool IsValidAddressableAsset(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        if (!assetPath.StartsWith(RootFolder))
        {
            return false;
        }

        if (assetPath.EndsWith(".meta") || assetPath.EndsWith(".DS_Store"))
        {
            return false;
        }

        return File.Exists(assetPath);
    }

    private static string BuildAddress(string assetPath)
    {
        string relativePath = assetPath.Substring(RootFolder.Length);
        string extension = Path.GetExtension(relativePath);
        if (!string.IsNullOrEmpty(extension))
        {
            relativePath = relativePath.Substring(0, relativePath.Length - extension.Length);
        }

        return relativePath.Replace("\\", "/");
    }

    private static void SaveSettings()
    {
        EditorUtility.SetDirty(AddressableAssetSettingsDefaultObject.Settings);
        AssetDatabase.SaveAssets();
    }
}
