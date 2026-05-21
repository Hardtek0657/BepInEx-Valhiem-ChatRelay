namespace ChatUtility;

internal static class RetryWebSocketCommand
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
            "retryws",
            "force reconnect ChatUtility's relay WebSocket connection",
            HandleRetryCommand);
    }

    private static void HandleRetryCommand(Terminal.ConsoleEventArgs args)
    {
        RelayChatClient.ForceReconnect();
        AddFeedback(args, "Relay WebSocket reconnect requested.");
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
