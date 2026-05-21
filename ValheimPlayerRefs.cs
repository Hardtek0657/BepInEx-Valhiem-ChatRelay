using System.Reflection;
using HarmonyLib;

namespace ChatUtility;

internal static class ValheimPlayerRefs
{
    private static FieldInfo? _localPlayerField;

    public static object? LocalPlayer
    {
        get
        {
            _localPlayerField ??= AccessTools.Field(AccessTools.TypeByName("Player"), "m_localPlayer");
            return _localPlayerField?.GetValue(null);
        }
    }

    public static bool IsLocalPlayer(object player)
    {
        object? localPlayer = LocalPlayer;
        return localPlayer != null && ReferenceEquals(player, localPlayer);
    }
}
