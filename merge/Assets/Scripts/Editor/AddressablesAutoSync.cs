using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public class AddressablesAutoSync : AssetPostprocessor
{
    private const string RootFolder = "Assets/Addressables/";
    private const string RootFolderTrimmed = "Assets/Addressables";
    private const string RemoteGroupName = "Remote Group";
    private static bool s_InitializationQueued;

    [InitializeOnLoadMethod]
    private static void InitializeOnLoad()
    {
        QueueInitialSync();
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
        AddressableAssetGroup group = EnsureRemoteGroup(settings, out changed);
        if (group == null)
        {
            QueueInitialSync();
            return;
        }

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

        bool groupChanged;
        AddressableAssetGroup group = EnsureRemoteGroup(settings, out groupChanged);
        if (group == null)
        {
            Log.Error("Addressables remote group is missing.");
            return false;
        }

        string address = BuildAddress(assetPath);
        AddressableAssetEntry entry = settings.FindAssetEntry(guid);
        bool entryChanged = groupChanged;
        if (entry == null)
        {
            entry = settings.CreateOrMoveEntry(guid, group, false, false);
            entryChanged = entry != null;
        }
        else if (entry.parentGroup != group)
        {
            settings.MoveEntry(entry, group, false, false);
            entryChanged = true;
        }

        if (entry == null)
        {
            return false;
        }

        if (entry.address == address)
        {
            return entryChanged;
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

    private static AddressableAssetGroup EnsureRemoteGroup(AddressableAssetSettings settings, out bool changed)
    {
        changed = false;
        if (settings == null)
        {
            return null;
        }

        AddressableAssetGroup group = settings.FindGroup(RemoteGroupName);
        if (group == null)
        {
            group = settings.CreateGroup(
                RemoteGroupName,
                false,
                false,
                false,
                null,
                typeof(ContentUpdateGroupSchema),
                typeof(BundledAssetGroupSchema));
            changed = group != null;
        }

        if (group == null)
        {
            return null;
        }

        bool schemaChanged;
        try
        {
            schemaChanged = ConfigureRemoteGroup(group, settings);
        }
        catch (System.NullReferenceException)
        {
            QueueInitialSync();
            return null;
        }

        if (schemaChanged)
        {
            EditorUtility.SetDirty(group);
            changed = true;
        }

        return group;
    }

    private static bool ConfigureRemoteGroup(AddressableAssetGroup group, AddressableAssetSettings settings)
    {
        bool changed = false;

        BundledAssetGroupSchema bundledSchema = group.GetSchema<BundledAssetGroupSchema>();
        if (bundledSchema == null)
        {
            bundledSchema = group.AddSchema<BundledAssetGroupSchema>();
            changed = bundledSchema != null;
        }

        ContentUpdateGroupSchema contentUpdateSchema = group.GetSchema<ContentUpdateGroupSchema>();
        if (contentUpdateSchema == null)
        {
            contentUpdateSchema = group.AddSchema<ContentUpdateGroupSchema>();
            changed = contentUpdateSchema != null || changed;
        }

        if (bundledSchema != null)
        {
            if (bundledSchema.BuildPath.GetName(settings) != AddressableAssetSettings.kRemoteBuildPath)
            {
                bundledSchema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
                changed = true;
            }

            if (bundledSchema.LoadPath.GetName(settings) != AddressableAssetSettings.kRemoteLoadPath)
            {
                bundledSchema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
                changed = true;
            }

            if (bundledSchema.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackSeparately)
            {
                bundledSchema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
                changed = true;
            }

            if (!bundledSchema.IncludeInBuild)
            {
                bundledSchema.IncludeInBuild = true;
                changed = true;
            }

            if (!bundledSchema.UseAssetBundleCache)
            {
                bundledSchema.UseAssetBundleCache = true;
                changed = true;
            }

            if (!bundledSchema.UseAssetBundleCrc)
            {
                bundledSchema.UseAssetBundleCrc = true;
                changed = true;
            }

            if (!bundledSchema.UseAssetBundleCrcForCachedBundles)
            {
                bundledSchema.UseAssetBundleCrcForCachedBundles = true;
                changed = true;
            }

            if (bundledSchema.AssetBundledCacheClearBehavior !=
                BundledAssetGroupSchema.CacheClearBehavior.ClearWhenWhenNewVersionLoaded)
            {
                bundledSchema.AssetBundledCacheClearBehavior =
                    BundledAssetGroupSchema.CacheClearBehavior.ClearWhenWhenNewVersionLoaded;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(bundledSchema);
            }
        }

        if (contentUpdateSchema != null)
        {
            if (contentUpdateSchema.StaticContent)
            {
                contentUpdateSchema.StaticContent = false;
                EditorUtility.SetDirty(contentUpdateSchema);
                changed = true;
            }
        }

        return changed;
    }

    private static void SaveSettings()
    {
        EditorUtility.SetDirty(AddressableAssetSettingsDefaultObject.Settings);
        AssetDatabase.SaveAssets();
    }

    private static void QueueInitialSync()
    {
        if (s_InitializationQueued)
        {
            return;
        }

        s_InitializationQueued = true;
        EditorApplication.delayCall += RunDelayedInitialSync;
    }

    private static void RunDelayedInitialSync()
    {
        s_InitializationQueued = false;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            QueueInitialSync();
            return;
        }

        SyncAllAssets();
    }
}
