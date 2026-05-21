using HarmonyLib;

namespace ChatUtility;

[HarmonyPatch(typeof(Chat), nameof(Chat.SendText))]
internal static class ChatSendTextRelayPatch
{
    private static bool Prefix(Talker.Type type, string text)
    {
        if (RelayChatClient.TryHandleOutgoing(type, text))
        {
            return false;
        }

        return true;
    }
}
