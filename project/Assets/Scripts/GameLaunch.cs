using System;
using UnityEngine;
using UnityEngine.Networking;

public class GameLaunch : MonoBehaviour
{
    [SerializeField] private LaunchConfig launchConfig;

    public LaunchConfig CurrentLaunchConfig => launchConfig;
    public string ServerUrl => CurrentLaunchConfig != null ? CurrentLaunchConfig.ServerUrl : null;
    public string GameVersion => GameConfig.GameVersion;

    private void Start()
    {
        GameConfig.Apply();

        _ = AddressablesMgr.Instance;
    }

    /// <summary>创建发往网关的请求，调用方负责 SendWebRequest 和 Dispose。</summary>
    public UnityWebRequest CreateServerRequest(string path, string method = UnityWebRequest.kHttpVerbGET)
    {
        if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttp && serverUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("启动配置的 Server URL 未配置或无效。");
        if (string.IsNullOrEmpty(path) || !path.StartsWith("/") || path.StartsWith("//") || path.Contains("\\"))
            throw new ArgumentException("请求路径必须以单个 / 开头。", nameof(path));

        if (string.IsNullOrWhiteSpace(GameVersion))
            throw new InvalidOperationException("GameConfig 未配置游戏版本号。");

        var request = new UnityWebRequest(ServerUrl.TrimEnd('/') + path, method);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("X-Game-Version", GameVersion);
        return request;
    }
}
