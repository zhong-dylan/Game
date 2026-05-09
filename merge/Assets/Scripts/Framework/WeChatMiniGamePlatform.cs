public class WeChatMiniGamePlatform : IPlatform
{
    public PlatformType PlatformType => PlatformType.WeChatMiniGame;
    public bool IsEditor => false;
    public bool IsWeChatMiniGame => true;
}
