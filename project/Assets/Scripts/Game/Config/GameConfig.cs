using UnityEngine;
using UnityEngine.UI;

/// <summary>游戏公共配置，在此设置版本号和设计分辨率。</summary>
public static class GameConfig
{
    public const string GameVersion = "1.0";
    public static readonly Vector2Int Resolution = new Vector2Int(1080, 1920);

    /// <summary>设置游戏分辨率及当前场景 Canvas 的 UI 参考分辨率。</summary>
    public static void Apply()
    {
        Screen.SetResolution(Resolution.x, Resolution.y, Screen.fullScreen);
        foreach (var scaler in Object.FindObjectsOfType<CanvasScaler>(true))
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Resolution.x, Resolution.y);
        }
    }
}
