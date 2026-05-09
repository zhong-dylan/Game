using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AppStartupConfig", menuName = "Framework/App Startup Config")]
public class AppStartupConfig : ScriptableObject
{
    [Header("打开日志")]
    public bool OpenLog = true;

    [Header("CDN配置")]
    public string CdnBaseUrl = "https://qzz2d.qzzres.com/M5_BUILD_TEST";
}
