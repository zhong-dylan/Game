public enum PlatformType
{
    Unknown = 0,
    Editor = 1,
    WeChatMiniGame = 2
}

public interface IPlatform
{
    PlatformType PlatformType { get; }
    bool IsEditor { get; }
    bool IsWeChatMiniGame { get; }
}

public class PlatformMgr : MonoSingle<PlatformMgr>
{
    public IPlatform Platform { get; private set; }
    public PlatformType CurrentPlatform => Platform == null ? PlatformType.Unknown : Platform.PlatformType;
    public bool IsEditor => Platform != null && Platform.IsEditor;
    public bool IsWeChatMiniGame => Platform != null && Platform.IsWeChatMiniGame;

    protected override void OnInit()
    {
        base.OnInit();
        Platform = CreatePlatform();
    }

    private static IPlatform CreatePlatform()
    {
#if UNITY_EDITOR
        return new EditorPlatform();
#else
        return new WeChatMiniGamePlatform();
#endif
    }
}
