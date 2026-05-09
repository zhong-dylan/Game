using UnityEngine;

public static class Log
{
    public static void Debug(string message, Color color = default)
    {
        if (CanLog())
        {
            if (color == default)
            {
                UnityEngine.Debug.Log(message);
                return;
            }

            string colorHex = ColorUtility.ToHtmlStringRGB(color);
            UnityEngine.Debug.Log($"<color=#{colorHex}>{message}</color>");
        }
    }

    public static void Warning(string message)
    {
        if (CanLog())
        {
            UnityEngine.Debug.LogWarning(message);
        }
    }

    public static void Error(string message)
    {
        if (CanLog())
        {
            UnityEngine.Debug.LogError(message);
        }
    }

    private static bool CanLog()
    {
        if (AppEntry.I == null)
        {
            return true;
        }

        if (AppEntry.I.StartupConfig == null)
        {
            return true;
        }

        return AppEntry.I.StartupConfig.OpenLog;
    }
}
