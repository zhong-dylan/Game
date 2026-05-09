public class EditorPlatform : IPlatform
{
    public PlatformType PlatformType => PlatformType.Editor;
    public bool IsEditor => true;
    public bool IsWeChatMiniGame => false;
}
