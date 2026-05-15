using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor;
using UnityEngine;

public static class WeChatMiniGameExportTool
{
    private enum ExportOrientation
    {
        Portrait = 0,
        Landscape = 1,
        LandscapeLeft = 2,
        LandscapeRight = 3
    }

    private const string ConvertCoreTypeFullName = "WeChatWASM.WXConvertCore";
    private const string EditorWindowTypeFullName = "WeChatWASM.WXEditorWin";
    private const string ExportMenuPath = "Tools/WeChat Mini Game/Export";
    private const string ConvertOnlyMenuPath = "Tools/WeChat Mini Game/Convert Only";
    private const string RevealExportDirMenuPath = "Tools/WeChat Mini Game/Open Export Folder";
    private const string OpenOfficialWindowMenuPath = "Tools/WeChat Mini Game/Open Official Convert Window";
    private const string OpenUploaderMenuPath = "Tools/WeChat Mini Game/Open Upload Panel";
    private const string OpenLocalConfigMenuPath = "Tools/WeChat Mini Game/Open Local Upload Config";
    private const string SetTimestampVersionMenuPath = "Tools/WeChat Mini Game/Set Timestamp Resource Version";
    private const string LocalConfigPath = "UserSettings/WeChatMiniGameExportLocalConfig.json";
    private const string AddressablesRemoteOutputDirectory = "Build/AddressablesRemote/webgl";
    private const int TosConnectionTimeoutMs = 15000;
    private const int TosRequestTimeoutMs = 600000;
    private const int TosSocketTimeoutMs = 600000;
    private const int TosMaxConnections = 8;
    private const int TosUploadIntervalMs = 300;
    private const int TosUploadRetryCount = 3;
    private const int TosUploadRetryDelayMs = 2000;
    private static AddressablesPlayerBuildResult s_LastAddressablesBuildResult;
    private static readonly ExportConfig Config = new ExportConfig
    {
        // Root export directory consumed by the WeChat SDK converter.
        ExportDirectory = "Build/WeChatMiniGame",
        // Relative export directory written into the SDK config when supported.
        RelativeExportDirectory = "Build/WeChatMiniGame",
        // Project name written into the generated mini game metadata.
        ProjectName = "甜甜水果消",
        // WeChat mini game AppID. Keep empty if you want to fill it in the official panel.
        AppId = "wx6bcf506f2daece1e",
        // Public custom domain used for runtime resource access. TOS endpoint is still used for upload.
        CdnBaseUrl = "https://qzz2d.qzzres.com",
        // Screen orientation for the generated mini game.
        Orientation = ExportOrientation.Portrait,
        // Development build affects debug symbols and converter behavior.
        DevelopBuild = false,
        // Clean build clears Unity build cache before export.
        CleanBuild = true,
        // Uses the WeChat iOS high-performance+ rendering path.
        EnableIOSPerformancePlus = true,
        // Clears the previously exported StreamingAssets directory before export.
        DeleteStreamingAssets = true,
        // WebGL2/OpenGLES3 is required when project color space is Linear.
        Webgl2 = true,
        // Brotli multi-thread compression improves build speed but may reduce compression ratio.
        BrotliMultiThread = false,
        // Compresses the generated data package when supported by the SDK.
        CompressDataPackage = false,
        // Total memory passed into the WeChat/WebGL build pipeline.
        MemorySizeMB = 512,
        // Hides the launch cover after main is called.
        HideAfterCallMain = true,
        // Enables built-in update checking plugin in the exported package.
        NeedCheckUpdate = false,
        // Upload exported webgl folder to TOS after a successful export.
        AutoUploadWebGL = true,
        // Relative object prefix inside the bucket.
        UploadObjectPrefix = AppStartupConfig.DefaultRemoteResourcePrefix
    };

    [MenuItem(ExportMenuPath)]
    private static void Export()
    {
        RunExport(true);
    }

    [MenuItem(ConvertOnlyMenuPath)]
    private static void ConvertOnly()
    {
        RunExport(false);
    }

    [MenuItem(RevealExportDirMenuPath)]
    private static void RevealExportDirectory()
    {
        string exportDirectory = GetExportDirectory();
        if (string.IsNullOrEmpty(exportDirectory))
        {
            EditorUtility.DisplayDialog("WeChat Mini Game", "Export directory is not configured.", "OK");
            return;
        }

        if (!Directory.Exists(exportDirectory))
        {
            EditorUtility.DisplayDialog("WeChat Mini Game", $"Export directory does not exist:\n{exportDirectory}", "OK");
            return;
        }

        EditorUtility.RevealInFinder(exportDirectory);
    }

    [MenuItem(OpenOfficialWindowMenuPath)]
    private static void OpenOfficialConvertWindow()
    {
        Type editorWindowType = FindType(EditorWindowTypeFullName);
        MethodInfo openMethod = editorWindowType == null
            ? null
            : editorWindowType.GetMethod("Open", BindingFlags.Public | BindingFlags.Static);

        if (openMethod == null)
        {
            EditorUtility.DisplayDialog("WeChat Mini Game", "Official WeChat converter window was not found.", "OK");
            return;
        }

        openMethod.Invoke(null, null);
    }

    [MenuItem(OpenUploaderMenuPath)]
    private static void OpenUploaderPanel()
    {
        if (EditorApplication.ExecuteMenuItem("微信小游戏/宿主小游戏上传面板"))
        {
            return;
        }

        EditorUtility.DisplayDialog("WeChat Mini Game", "WeChat upload panel was not found.", "OK");
    }

    [MenuItem(OpenLocalConfigMenuPath)]
    private static void OpenLocalConfig()
    {
        EnsureLocalConfigFile();
        EditorUtility.RevealInFinder(Path.GetFullPath(LocalConfigPath));
    }

    [MenuItem(SetTimestampVersionMenuPath)]
    private static void SetTimestampResourceVersion()
    {
        AppStartupConfig startupConfig = FindStartupConfig();
        if (startupConfig == null)
        {
            EditorUtility.DisplayDialog("WeChat Mini Game", "AppStartupConfig was not found.", "OK");
            return;
        }

        string timestampVersion = GenerateTimestampResourceVersion();
        startupConfig.ResourceVersion = timestampVersion;
        EditorUtility.SetDirty(startupConfig);
        SyncAddressablesVersionSettings(timestampVersion);
        AssetDatabase.SaveAssets();
        Debug.Log($"[WeChatMiniGameExportTool] ResourceVersion updated: {timestampVersion}");
    }

    private static void RunExport(bool buildWebGL)
    {
        MethodInfo exportMethod = GetExportMethod();
        if (exportMethod == null)
        {
            EditorUtility.DisplayDialog("WeChat Mini Game", "WeChat mini game export package was not found.", "OK");
            return;
        }

        SetTimestampResourceVersion();
        if (!BuildAddressablesContent())
        {
            EditorUtility.DisplayDialog(
                "WeChat Mini Game",
                "Addressables build failed. Check Unity Console for details.",
                "OK");
            return;
        }

        ApplyExportConfig();
        string exportDirectory = GetExportDirectory();
        if (string.IsNullOrEmpty(exportDirectory))
        {
            EditorUtility.DisplayDialog(
                "WeChat Mini Game",
                "Export directory is empty after applying code config.",
                "OK");
            return;
        }

        Debug.Log($"[WeChatMiniGameExportTool] Start export. buildWebGL={buildWebGL}, dst={exportDirectory}");

        try
        {
            object result = exportMethod.Invoke(null, new object[] { buildWebGL });
            string resultText = result == null ? "Unknown" : result.ToString();
            if (string.Equals(resultText, "SUCCEED", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[WeChatMiniGameExportTool] Export succeeded. Output: {exportDirectory}");
                UploadWebGLIfNeeded(exportDirectory);
                return;
            }

            string errorDetail = BuildExportFailureMessage(resultText, exportDirectory, buildWebGL);
            Debug.LogError(errorDetail);
            EditorUtility.DisplayDialog("WeChat Mini Game", errorDetail, "OK");
        }
        catch (TargetInvocationException e)
        {
            Exception inner = e.InnerException ?? e;
            string errorDetail = $"[WeChatMiniGameExportTool] Export exception: {inner}";
            Debug.LogError(errorDetail);
            EditorUtility.DisplayDialog("WeChat Mini Game", errorDetail, "OK");
        }
    }

    private static bool BuildAddressablesContent()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[WeChatMiniGameExportTool] Addressables build failed: settings not found.");
            return false;
        }

        Debug.Log(
            $"[WeChatMiniGameExportTool] Building Addressables content. PlayerVersion={settings.PlayerBuildVersion}, RemoteBuildPath={GetActiveProfileValue(settings, AddressableAssetSettings.kRemoteBuildPath)}");

        AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
        if (!string.IsNullOrEmpty(result.Error))
        {
            Debug.LogError($"[WeChatMiniGameExportTool] Addressables build failed: {result.Error}");
            s_LastAddressablesBuildResult = null;
            return false;
        }

        s_LastAddressablesBuildResult = result;
        Debug.Log($"[WeChatMiniGameExportTool] Addressables build succeeded. OutputPath={result.OutputPath}");
        LogAddressablesArtifacts(result.OutputPath);
        LogAddressablesBuildResult(result);
        return true;
    }

    private static MethodInfo GetExportMethod()
    {
        Type convertCoreType = FindType(ConvertCoreTypeFullName);
        if (convertCoreType == null)
        {
            return null;
        }

        return convertCoreType.GetMethod("DoExport", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
    }

    private static void ApplyExportConfig()
    {
        object configObject = GetConfigObject();
        if (configObject == null)
        {
            return;
        }

        object projectConf = GetMemberValue(configObject, "ProjectConf");
        object compileOptions = GetMemberValue(configObject, "CompileOptions");

        SetMemberValue(projectConf, "DST", Path.GetFullPath(Config.ExportDirectory));
        SetMemberValue(projectConf, "relativeDST", Config.RelativeExportDirectory);
        SetMemberValue(projectConf, "projectName", Config.ProjectName);
        SetMemberValue(projectConf, "Appid", Config.AppId);
        SetMemberValue(projectConf, "CDN", BuildCdnUrl());
        SetMemberValue(projectConf, "compressDataPackage", Config.CompressDataPackage);
        SetEnumMemberValue(projectConf, "Orientation", (int)Config.Orientation);
        SetMemberValue(projectConf, "MemorySize", Config.MemorySizeMB);
        SetMemberValue(projectConf, "HideAfterCallMain", Config.HideAfterCallMain);
        SetMemberValue(projectConf, "needCheckUpdate", Config.NeedCheckUpdate);

        SetMemberValue(compileOptions, "DevelopBuild", Config.DevelopBuild);
        SetMemberValue(compileOptions, "CleanBuild", Config.CleanBuild);
        SetMemberValue(compileOptions, "enableIOSPerformancePlus", Config.EnableIOSPerformancePlus);
        SetMemberValue(compileOptions, "DeleteStreamingAssets", Config.DeleteStreamingAssets);
        SetMemberValue(compileOptions, "Webgl2", Config.Webgl2 || PlayerSettings.colorSpace == ColorSpace.Linear);
        SetMemberValue(compileOptions, "brotliMT", Config.BrotliMultiThread);

        if (configObject is UnityEngine.Object unityObject)
        {
            EditorUtility.SetDirty(unityObject);
            AssetDatabase.SaveAssets();
        }
    }

    private static string GetExportDirectory()
    {
        object configObject = GetConfigObject();
        if (configObject == null)
        {
            return null;
        }

        object projectConf = GetMemberValue(configObject, "ProjectConf");
        if (projectConf == null)
        {
            return null;
        }

        return GetMemberValue(projectConf, "DST") as string;
    }

    private static string BuildCdnUrl()
    {
        string configuredBaseUrl = GetConfiguredCdnBaseUrl();
        if (string.IsNullOrEmpty(configuredBaseUrl))
        {
            return string.Empty;
        }

        string baseUrl = configuredBaseUrl.TrimEnd('/');
        string objectPrefix = NormalizePath(Config.UploadObjectPrefix).Trim('/');
        if (string.IsNullOrEmpty(objectPrefix))
        {
            return baseUrl;
        }

        return string.IsNullOrEmpty(objectPrefix) ? baseUrl : $"{baseUrl}/{objectPrefix}";
    }

    private static string GetActiveProfileValue(AddressableAssetSettings settings, string variableName)
    {
        if (settings == null || string.IsNullOrEmpty(settings.activeProfileId))
        {
            return string.Empty;
        }

        return settings.profileSettings.GetValueByName(settings.activeProfileId, variableName);
    }

    private static void SyncAddressablesVersionSettings(string timestampVersion)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            return;
        }

        string profileId = settings.activeProfileId;
        if (string.IsNullOrEmpty(profileId))
        {
            return;
        }

        string remoteLoadPath = BuildCdnUrl();
        string remoteBuildPath = Path.GetFullPath(AddressablesRemoteOutputDirectory);
        string currentValue = settings.profileSettings.GetValueByName(profileId, AddressableAssetSettings.kRemoteLoadPath);
        string currentBuildPath = settings.profileSettings.GetValueByName(profileId, AddressableAssetSettings.kRemoteBuildPath);
        bool changed = false;

        if (!string.Equals(settings.OverridePlayerVersion, timestampVersion, StringComparison.Ordinal))
        {
            settings.OverridePlayerVersion = timestampVersion;
            changed = true;
            Debug.Log($"[WeChatMiniGameExportTool] Addressables OverridePlayerVersion updated: {timestampVersion}");
        }

        if (!string.Equals(currentValue, remoteLoadPath, StringComparison.Ordinal))
        {
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteLoadPath, remoteLoadPath);
            changed = true;
            Debug.Log($"[WeChatMiniGameExportTool] Addressables Remote.LoadPath updated: {remoteLoadPath}");
        }

        if (!string.Equals(currentBuildPath, remoteBuildPath, StringComparison.Ordinal))
        {
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteBuildPath, remoteBuildPath);
            changed = true;
            Debug.Log($"[WeChatMiniGameExportTool] Addressables Remote.BuildPath updated: {remoteBuildPath}");
        }

        if (changed)
        {
            EditorUtility.SetDirty(settings);
        }
    }

    private static string GetConfiguredCdnBaseUrl()
    {
        AppStartupConfig startupConfig = FindStartupConfig();
        return startupConfig != null && !string.IsNullOrEmpty(startupConfig.CdnBaseUrl)
            ? startupConfig.CdnBaseUrl
            : Config.CdnBaseUrl;
    }

    private static string BuildVersionedUploadObjectPrefix()
    {
        return NormalizePath(Config.UploadObjectPrefix).Trim('/');
    }

    private static AppStartupConfig FindStartupConfig()
    {
        string[] guids = AssetDatabase.FindAssets("t:AppStartupConfig");
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            AppStartupConfig config = AssetDatabase.LoadAssetAtPath<AppStartupConfig>(assetPath);
            if (config != null)
            {
                return config;
            }
        }

        return null;
    }

    private static string GenerateTimestampResourceVersion()
    {
        return DateTime.Now.ToString("yyyyMMddHHmmss");
    }

    private static Type FindType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(fullName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static object GetConfigObject()
    {
        Type convertCoreType = FindType(ConvertCoreTypeFullName);
        PropertyInfo configProperty = convertCoreType == null
            ? null
            : convertCoreType.GetProperty("config", BindingFlags.Public | BindingFlags.Static);
        return configProperty == null ? null : configProperty.GetValue(null, null);
    }

    private static string BuildExportFailureMessage(string resultText, string exportDirectory, bool buildWebGL)
    {
        string detail = $"[WeChatMiniGameExportTool] Export failed. Result={resultText}\n";
        detail += $"buildWebGL={buildWebGL}\n";
        detail += $"dst={exportDirectory}\n";
        detail += "Check Unity Console lines before this error. WXConvertCore usually fails for one of:\n";
        detail += "1. CheckSDK failed: remove old WX SDK folders and keep only current package version.\n";
        detail += "2. CheckBuildTemplate failed: custom WeChat/WebGL template conflicts with current SDK.\n";
        detail += "3. Build target switch/build failed: WeixinMiniGame/WebGL module or build environment is broken.\n";
        detail += "4. Export path/config invalid: DST exists but upstream config still contains bad values.\n";
        detail += "5. Memory/build options invalid: for example MemorySize too large or build cache issues.\n";
        return detail;
    }

    private static void UploadWebGLIfNeeded(string exportDirectory)
    {
        if (!Config.AutoUploadWebGL)
        {
            return;
        }

        string webglDirectory = Path.Combine(exportDirectory, "webgl");
        if (!Directory.Exists(webglDirectory))
        {
            Debug.LogError($"[WeChatMiniGameExportTool] Upload skipped: webgl folder not found: {webglDirectory}");
            return;
        }

        LocalUploadConfig localConfig = LoadLocalUploadConfig();
        if (localConfig == null ||
            string.IsNullOrEmpty(localConfig.TosAccessKey) ||
            string.IsNullOrEmpty(localConfig.TosSecretKey) ||
            string.IsNullOrEmpty(localConfig.TosEndpoint) ||
            string.IsNullOrEmpty(localConfig.TosRegion) ||
            string.IsNullOrEmpty(localConfig.TosBucket))
        {
            Debug.LogError(
                $"[WeChatMiniGameExportTool] Upload skipped: missing local upload config values. Open and fill: {LocalConfigPath}");
            return;
        }

        try
        {
            UploadAddressablesArtifactsIfNeeded(localConfig);
            LogAddressablesArtifacts(webglDirectory);
            UploadDirectoryToTos(
                webglDirectory,
                localConfig.TosBucket,
                NormalizePath(Config.UploadObjectPrefix).Trim('/'),
                BuildVersionedUploadObjectPrefix(),
                localConfig.TosEndpoint,
                localConfig.TosRegion,
                localConfig.TosAccessKey,
                localConfig.TosSecretKey);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WeChatMiniGameExportTool] Upload failed: {e}");
        }
    }

    private static void UploadAddressablesArtifactsIfNeeded(LocalUploadConfig localConfig)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        string remoteBuildRoot = GetActiveProfileValue(settings, AddressableAssetSettings.kRemoteBuildPath);
        if (string.IsNullOrEmpty(remoteBuildRoot) || !Directory.Exists(remoteBuildRoot))
        {
            Debug.LogWarning($"[WeChatMiniGameExportTool] Addressables upload skipped: remote build path not found: {remoteBuildRoot}");
            return;
        }

        string[] files = Directory.GetFiles(remoteBuildRoot, "*", SearchOption.AllDirectories);
        List<string> filesToUpload = new List<string>();
        for (int i = 0; i < files.Length; i++)
        {
            if (!ShouldUploadFile(files[i]))
            {
                continue;
            }

            filesToUpload.Add(NormalizePath(files[i]));
        }

        if (filesToUpload.Count == 0)
        {
            Debug.LogWarning("[WeChatMiniGameExportTool] Addressables upload skipped: no remote catalog or bundle files were found.");
            return;
        }

        Debug.Log($"[WeChatMiniGameExportTool] Addressables upload root: {remoteBuildRoot}");
        LogAddressablesArtifacts(remoteBuildRoot);

        UploadFileListToTos(
            filesToUpload,
            remoteBuildRoot,
            localConfig.TosBucket,
            BuildVersionedUploadObjectPrefix(),
            localConfig.TosEndpoint,
            localConfig.TosRegion,
            localConfig.TosAccessKey,
            localConfig.TosSecretKey,
            "Upload Addressables");
    }

    private static void LogAddressablesArtifacts(string webglDirectory)
    {
        if (string.IsNullOrEmpty(webglDirectory) || !Directory.Exists(webglDirectory))
        {
            return;
        }

        string[] artifactFiles = Directory.GetFiles(webglDirectory, "*", SearchOption.AllDirectories);
        List<string> matchedFiles = new List<string>();
        string normalizedRoot = NormalizePath(webglDirectory).TrimEnd('/');

        for (int i = 0; i < artifactFiles.Length; i++)
        {
            string normalizedPath = NormalizePath(artifactFiles[i]);
            string fileName = Path.GetFileName(normalizedPath);
            if (fileName.StartsWith("catalog", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
            {
                matchedFiles.Add(normalizedPath.Substring(normalizedRoot.Length).TrimStart('/'));
            }
        }

        if (matchedFiles.Count == 0)
        {
            Debug.Log("[WeChatMiniGameExportTool] No local Addressables catalog/hash files were found under webgl output.");
            return;
        }

        matchedFiles.Sort(StringComparer.OrdinalIgnoreCase);
        Debug.Log(
            $"[WeChatMiniGameExportTool] Local Addressables catalog/hash files:\n- {string.Join("\n- ", matchedFiles)}");
    }

    private static void LogAddressablesBuildResult(AddressablesPlayerBuildResult result)
    {
        if (result == null)
        {
            return;
        }

        List<string> details = new List<string>();
        if (!string.IsNullOrEmpty(result.RemoteCatalogHashFilePath))
        {
            details.Add($"hash: {NormalizePath(result.RemoteCatalogHashFilePath)}");
        }

        if (!string.IsNullOrEmpty(result.RemoteCatalogJsonFilePath))
        {
            details.Add($"json: {NormalizePath(result.RemoteCatalogJsonFilePath)}");
        }

        if (result.AssetBundleBuildResults != null)
        {
            for (int i = 0; i < result.AssetBundleBuildResults.Count; i++)
            {
                string bundlePath = result.AssetBundleBuildResults[i].FilePath;
                if (!string.IsNullOrEmpty(bundlePath))
                {
                    details.Add($"bundle: {NormalizePath(bundlePath)}");
                }
            }
        }

        if (details.Count > 0)
        {
            Debug.Log($"[WeChatMiniGameExportTool] Addressables build artifacts:\n- {string.Join("\n- ", details)}");
        }
    }

    private static void UploadDirectoryToTos(string localDirectory, string bucket, string objectPrefix, string versionedObjectPrefix, string endpoint, string region, string accessKey, string secretKey)
    {
        string[] files = Directory.GetFiles(localDirectory, "*", SearchOption.AllDirectories);
        List<string> uploadFiles = new List<string>();
        for (int i = 0; i < files.Length; i++)
        {
            if (!ShouldUploadFile(files[i]))
            {
                continue;
            }

            uploadFiles.Add(NormalizePath(files[i]));
        }

        UploadFileListToTos(
            uploadFiles,
            localDirectory,
            bucket,
            versionedObjectPrefix,
            endpoint,
            region,
            accessKey,
            secretKey,
            "Upload To TOS",
            objectPrefix,
            versionedObjectPrefix);
    }

    private static void UploadFileListToTos(
        List<string> uploadFiles,
        string localRootDirectory,
        string bucket,
        string uploadPrefix,
        string endpoint,
        string region,
        string accessKey,
        string secretKey,
        string progressTitle,
        string objectPrefix = null,
        string versionedObjectPrefix = null)
    {
        Type builderType = FindTypeByName("TosClientBuilder");
        Type putInputType = FindTypeByName("PutObjectFromFileInput");
        if (builderType == null || putInputType == null)
        {
            Debug.LogError("[WeChatMiniGameExportTool] Upload skipped: TOS SDK types were not found. Install the Volcengine TOS C# SDK into the Unity project first.");
            return;
        }

        object builder = GetStaticPropertyValue(builderType, "Builder") ?? InvokeStaticMethod(builderType, "Builder");
        if (builder == null)
        {
            Debug.LogError("[WeChatMiniGameExportTool] Upload skipped: failed to create TosClientBuilder.");
            return;
        }

        builder = InvokeFluentInstanceMethod(builder, "SetAk", accessKey);
        builder = InvokeFluentInstanceMethod(builder, "SetSk", secretKey);
        builder = InvokeFluentInstanceMethod(builder, "SetRegion", region);
        builder = InvokeFluentInstanceMethod(builder, "SetEndpoint", endpoint);
        builder = InvokeFluentInstanceMethod(builder, "SetConnectionTimeout", TosConnectionTimeoutMs);
        builder = InvokeFluentInstanceMethod(builder, "SetRequestTimeout", TosRequestTimeoutMs);
        builder = InvokeFluentInstanceMethod(builder, "SetSocketTimeout", TosSocketTimeoutMs);
        builder = InvokeFluentInstanceMethod(builder, "SetMaxConnections", TosMaxConnections);
        object client = InvokeInstanceMethod(builder, "Build");
        if (client == null)
        {
            Debug.LogError("[WeChatMiniGameExportTool] Upload skipped: failed to build TOS client.");
            return;
        }

        if (uploadFiles.Count == 0)
        {
            DisposeIfNeeded(client);
            Debug.Log("[WeChatMiniGameExportTool] Upload skipped: no files matched the upload rules.");
            return;
        }

        string normalizedLocalDirectory = NormalizePath(localRootDirectory).TrimEnd('/');
        string resolvedObjectPrefix = string.IsNullOrEmpty(objectPrefix) ? uploadPrefix : objectPrefix;
        string resolvedVersionedObjectPrefix = string.IsNullOrEmpty(versionedObjectPrefix) ? uploadPrefix : versionedObjectPrefix;

        try
        {
            for (int i = 0; i < uploadFiles.Count; i++)
            {
                string filePath = uploadFiles[i];
                string relativePath = NormalizePath(filePath.Substring(normalizedLocalDirectory.Length)).TrimStart('/');
                string objectKey = ResolveUploadObjectKey(relativePath, resolvedObjectPrefix, resolvedVersionedObjectPrefix);
                if (IsAddressablesCatalogArtifact(relativePath))
                {
                    Debug.Log($"[WeChatMiniGameExportTool] Queue catalog upload: {relativePath} -> {objectKey}");
                }

                EditorUtility.DisplayProgressBar(
                    progressTitle,
                    $"Uploading {i + 1}/{uploadFiles.Count}\n{relativePath}",
                    (float)(i + 1) / uploadFiles.Count);

                object putInput = Activator.CreateInstance(putInputType);
                SetMemberValue(putInput, "Bucket", bucket);
                SetMemberValue(putInput, "Key", objectKey);
                SetMemberValue(putInput, "FilePath", filePath);
                try
                {
                    UploadFileWithRetry(client, putInput, filePath, objectKey, i + 1, uploadFiles.Count);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        $"Upload failed for file '{filePath}' -> '{objectKey}' ({i + 1}/{uploadFiles.Count}).",
                        e);
                }

                Debug.Log($"[WeChatMiniGameExportTool] Uploaded ({i + 1}/{uploadFiles.Count}): {objectKey}");

                if (i < uploadFiles.Count - 1 && TosUploadIntervalMs > 0)
                {
                    Thread.Sleep(TosUploadIntervalMs);
                }
            }

            Log.Debug("[WeChatMiniGameExportTool] Upload completed.", Color.green);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"TOS upload failed while uploading directory '{localRootDirectory}'. See inner exception for the failing request.",
                e);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            DisposeIfNeeded(client);
        }
    }

    private static string ResolveUploadObjectKey(string relativePath, string objectPrefix, string versionedObjectPrefix)
    {
        string normalizedRelativePath = NormalizePath(relativePath).TrimStart('/');
        string targetPrefix = IsVersionedRemoteResource(normalizedRelativePath)
            ? versionedObjectPrefix
            : objectPrefix;

        return NormalizePath($"{targetPrefix}/{normalizedRelativePath}");
    }

    private static bool IsVersionedRemoteResource(string relativePath)
    {
        return NormalizePath(relativePath).StartsWith("StreamingAssets/aa/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAddressablesCatalogArtifact(string relativePath)
    {
        string fileName = Path.GetFileName(NormalizePath(relativePath));
        return fileName.StartsWith("catalog", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".hash", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUploadFile(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return !string.Equals(extension, ".manifest", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(extension, ".meta", StringComparison.OrdinalIgnoreCase);
    }

    private static void UploadFileWithRetry(object client, object putInput, string filePath, string objectKey, int index, int totalCount)
    {
        Exception lastException = null;
        for (int attempt = 1; attempt <= TosUploadRetryCount; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    Debug.Log($"[WeChatMiniGameExportTool] Retry upload ({attempt}/{TosUploadRetryCount}): {objectKey}");
                }

                InvokeInstanceMethod(client, "PutObjectFromFile", putInput);
                return;
            }
            catch (Exception e)
            {
                lastException = e;
                bool isLastAttempt = attempt >= TosUploadRetryCount;
                Debug.LogWarning(
                    $"[WeChatMiniGameExportTool] Upload attempt failed ({attempt}/{TosUploadRetryCount}) for {objectKey} ({index}/{totalCount}): {GetInnermostExceptionMessage(e)}");
                if (isLastAttempt)
                {
                    break;
                }

                Thread.Sleep(TosUploadRetryDelayMs * attempt);
            }
        }

        throw lastException ?? new InvalidOperationException($"Upload failed for {filePath} -> {objectKey}");
    }

    private static string GetInnermostExceptionMessage(Exception exception)
    {
        Exception current = exception;
        while (current != null && current.InnerException != null)
        {
            current = current.InnerException;
        }

        return current == null ? "unknown error" : current.Message;
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }

    private static LocalUploadConfig LoadLocalUploadConfig()
    {
        EnsureLocalConfigFile();
        if (!File.Exists(LocalConfigPath))
        {
            return null;
        }

        string json = File.ReadAllText(LocalConfigPath);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return JsonUtility.FromJson<LocalUploadConfig>(json);
    }

    private static void EnsureLocalConfigFile()
    {
        string fullPath = Path.GetFullPath(LocalConfigPath);
        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(fullPath))
        {
            return;
        }

        LocalUploadConfig template = new LocalUploadConfig
        {
            TosAccessKey = "",
            TosSecretKey = "",
            TosEndpoint = "https://tos-cn-beijing.volces.com",
            TosRegion = "cn-beijing",
            TosBucket = ""
        };
        string json = JsonUtility.ToJson(template, true);
        File.WriteAllText(fullPath, json);
    }

    private static object GetMemberValue(object target, string memberName)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
        {
            return null;
        }

        PropertyInfo property = target.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null)
        {
            return property.GetValue(target, null);
        }

        FieldInfo field = target.GetType().GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field == null ? null : field.GetValue(target);
    }

    private static void SetMemberValue(object target, string memberName, object value)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
        {
            return;
        }

        PropertyInfo property = target.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value, null);
            return;
        }

        FieldInfo field = target.GetType().GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(target, value);
        }
    }

    private static object GetStaticPropertyValue(Type type, string propertyName)
    {
        if (type == null || string.IsNullOrEmpty(propertyName))
        {
            return null;
        }

        PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        return property == null ? null : property.GetValue(null, null);
    }

    private static object InvokeStaticMethod(Type type, string methodName, params object[] args)
    {
        if (type == null || string.IsNullOrEmpty(methodName))
        {
            return null;
        }

        MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        return method == null ? null : method.Invoke(null, args);
    }

    private static object InvokeInstanceMethod(object target, string methodName, params object[] args)
    {
        MethodInfo method = FindCompatibleMethod(target, methodName, args);
        return method == null ? null : method.Invoke(target, args);
    }

    private static object InvokeFluentInstanceMethod(object target, string methodName, params object[] args)
    {
        MethodInfo method = FindCompatibleMethod(target, methodName, args);
        if (method == null)
        {
            return target;
        }

        object result = method.Invoke(target, args);
        return method.ReturnType == typeof(void) ? target : result;
    }

    private static MethodInfo FindCompatibleMethod(object target, string methodName, object[] args)
    {
        if (target == null || string.IsNullOrEmpty(methodName))
        {
            return null;
        }

        MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != methodName)
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != args.Length)
            {
                continue;
            }

            bool matched = true;
            for (int j = 0; j < parameters.Length; j++)
            {
                object arg = args[j];
                Type parameterType = parameters[j].ParameterType;
                if (arg == null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                    {
                        matched = false;
                        break;
                    }

                    continue;
                }

                if (!parameterType.IsInstanceOfType(arg))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return method;
            }
        }

        return null;
    }

    private static void DisposeIfNeeded(object target)
    {
        if (target is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static Type FindTypeByName(string typeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }

            if (types == null)
            {
                continue;
            }

            for (int j = 0; j < types.Length; j++)
            {
                Type type = types[j];
                if (type != null && type.Name == typeName)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static void SetEnumMemberValue(object target, string memberName, int enumValue)
    {
        if (target == null || string.IsNullOrEmpty(memberName))
        {
            return;
        }

        PropertyInfo property = target.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null)
        {
            object value = Enum.ToObject(property.PropertyType, enumValue);
            if (property.CanWrite)
            {
                property.SetValue(target, value, null);
            }

            return;
        }

        FieldInfo field = target.GetType().GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            field.SetValue(target, Enum.ToObject(field.FieldType, enumValue));
        }
    }

    private class ExportConfig
    {
        // Export output directory.
        public string ExportDirectory;
        // Relative export output directory.
        public string RelativeExportDirectory;
        // Display name / project name for generated config.
        public string ProjectName;
        // WeChat mini game AppID.
        public string AppId;
        // Public CDN base url used to compose the final SDK CDN value.
        public string CdnBaseUrl;
        // Desired screen orientation.
        public ExportOrientation Orientation;
        // Whether to generate a development build.
        public bool DevelopBuild;
        // Whether to clean Unity build cache before export.
        public bool CleanBuild;
        // Whether to enable WeChat iOS Performance Plus.
        public bool EnableIOSPerformancePlus;
        // Whether to clear exported StreamingAssets before export.
        public bool DeleteStreamingAssets;
        // Whether to export with WebGL2/OpenGLES3.
        public bool Webgl2;
        // Whether to enable brotli multi-thread compression.
        public bool BrotliMultiThread;
        // Whether to compress the data package.
        public bool CompressDataPackage;
        // Memory size in MB.
        public int MemorySizeMB;
        // Whether to hide splash/cover after main startup.
        public bool HideAfterCallMain;
        // Whether to keep the SDK update-check plugin enabled.
        public bool NeedCheckUpdate;
        // Whether to upload the exported webgl folder after export succeeds.
        public bool AutoUploadWebGL;
        // Object key prefix inside the configured bucket.
        public string UploadObjectPrefix;
    }

    [Serializable]
    private class LocalUploadConfig
    {
        public string TosAccessKey;
        public string TosSecretKey;
        public string TosEndpoint;
        public string TosRegion;
        public string TosBucket;
    }
}
