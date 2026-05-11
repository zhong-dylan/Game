using UnityEngine;

public class AppEntry : MonoBehaviour
{
    [SerializeField] private AppStartupConfig m_StartupConfig;
    [SerializeField] private Camera m_MainCamera;
    [SerializeField] private GameObject m_LoginPrefab;

    private AssetsLoader m_AssetsLoader;
    public static AppEntry I { get; private set; }
    public AppStartupConfig StartupConfig => m_StartupConfig;
    public Camera MainCamera => m_MainCamera;

    private void Awake()
    {
        I = this;
        DontDestroyOnLoad(gameObject);
        m_AssetsLoader = gameObject.GetOrAddComponent<AssetsLoader>();
        Init();
    }

    private void Init()
    {
        AssetsMgr.ConfigureRemoteResourceVersion(m_StartupConfig);
        _ = PlatformMgr.I; // 初始化平台管理器
        _ = AssetsMgr.I; // 初始化资源管理器
        _ = UIMgr.I; // 初始化UI管理器
        _ = SceneMgr.I; // 初始化场景管理器
        _ = AudioMgr.I; // 初始化音频管理器
        EnterGame();
    }

    private void EnterGame()
    {
        if (m_LoginPrefab == null)
        {
            Log.Error("AppEntry.EnterGame failed: m_LoginPrefab is null.");
            return;
        }

        UIMgr.I.OpenView<UILogin>(new OpenParam
        {
            GameAsset = m_LoginPrefab
        });
    }
}
