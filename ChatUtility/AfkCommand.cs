namespace ChatUtility;

internal static class AfkCommand
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        new Terminal.ConsoleCommand(
            "afk",
            "enable or disable ChatUtility AFK relay notifications. Usage: /afk enable, /afk disable, /afk status",
            HandleAfkCommand);
    }

    private static void HandleAfkCommand(Terminal.ConsoleEventArgs args)
    {
        if (args.Length < 2 || args[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
        {
            AddFeedback(args, $"AFK notifications are {(ChatUtilityPlugin.AfkEnabled.Value ? "enabled" : "disabled")}.");
            AddFeedback(args, "Usage: /afk enable or /afk disable");
            return;
        }

        if (args[1].Equals("enable", System.StringComparison.OrdinalIgnoreCase) || args[1].Equals("on", System.StringComparison.OrdinalIgnoreCase))
        {
            SetAfkEnabled(args, true);
            return;
        }

        if (args[1].Equals("disable", System.StringComparison.OrdinalIgnoreCase) || args[1].Equals("off", System.StringComparison.OrdinalIgnoreCase))
        {
            SetAfkEnabled(args, false);
            return;
        }

        AddFeedback(args, "Unknown AFK command. Usage: /afk enable, /afk disable, or /afk status");
    }

    private static void SetAfkEnabled(Terminal.ConsoleEventArgs args, bool enabled)
    {
        ChatUtilityPlugin.AfkEnabled.Value = enabled;
        ChatUtilityPlugin.SaveConfig();
        AfkStatusTracker.ResetState();
        AddFeedback(args, $"AFK notifications {(enabled ? "enabled" : "disabled")}.");
    }

    private static void AddFeedback(Terminal.ConsoleEventArgs args, string message)
    {
        if (args.Context != null)
        {
            args.Context.AddString("ChatUtility", message, Talker.Type.Whisper);
        }
        else
        {
            ChatUtilityPlugin.Log.LogInfo(message);
        }
    }
}
