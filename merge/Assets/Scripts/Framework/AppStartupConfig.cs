using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AppStartupConfig", menuName = "Framework/App Startup Config")]
public class AppStartupConfig : ScriptableObject
{
    public const string DefaultRemoteResourcePrefix = "merge_farm/wx/webgl";

    [Header("打开日志")]
    public bool OpenLog = true;

    [Header("CDN配置")]
    public string CdnBaseUrl = "https://qzz2d.qzzres.com";

    [Header("资源版本")]
    public string ResourceVersion = "";

    public string GetRemoteResourceBaseUrl(string objectPrefix = DefaultRemoteResourcePrefix)
    {
        return CombineUrl(CdnBaseUrl, NormalizePath(objectPrefix).Trim('/'));
    }

    public string GetVersionedRemoteResourceBaseUrl(string objectPrefix = DefaultRemoteResourcePrefix)
    {
        string baseUrl = GetRemoteResourceBaseUrl(objectPrefix);
        string resourceVersion = GetNormalizedResourceVersion();
        return string.IsNullOrEmpty(resourceVersion) ? baseUrl : CombineUrl(baseUrl, resourceVersion);
    }

    public string GetVersionedObjectPrefix(string objectPrefix = DefaultRemoteResourcePrefix)
    {
        string normalizedPrefix = NormalizePath(objectPrefix).Trim('/');
        string resourceVersion = GetNormalizedResourceVersion();
        return string.IsNullOrEmpty(resourceVersion) ? normalizedPrefix : $"{normalizedPrefix}/{resourceVersion}";
    }

    public string GetNormalizedResourceVersion()
    {
        return NormalizePath(ResourceVersion).Trim('/');
    }

    private static string CombineUrl(string left, string right)
    {
        string normalizedLeft = NormalizePath(left).TrimEnd('/');
        string normalizedRight = NormalizePath(right).Trim('/');
        if (string.IsNullOrEmpty(normalizedLeft))
        {
            return normalizedRight;
        }

        if (string.IsNullOrEmpty(normalizedRight))
        {
            return normalizedLeft;
        }

        return $"{normalizedLeft}/{normalizedRight}";
    }

    private static string NormalizePath(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
    }
}
