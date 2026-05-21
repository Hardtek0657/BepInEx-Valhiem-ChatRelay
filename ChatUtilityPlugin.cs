using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ChatUtility;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ChatUtilityPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "local.valheim.chatutility";
    public const string PluginName = "Chat Utility";
    public const string PluginVersion = "0.1.0";
    public const string HardcodedRelayServerUrl = "wss://127.0.0.1/chat";

    internal static ConfigEntry<bool> ModEnabled = null!;
    internal static ConfigEntry<bool> RelayEnabled = null!;
    internal static ConfigEntry<string> RelayServerUrl = null!;
    internal static ConfigEntry<float> RelayReconnectSeconds = null!;
    internal static ConfigEntry<bool> AfkEnabled = null!;
    internal static ConfigEntry<float> AfkIdleSeconds = null!;
    internal static ConfigEntry<float> AfkMovementDistance = null!;
    internal static ConfigEntry<float> AfkRotationDegrees = null!;
    internal static ManualLogSource Log = null!;
    private static ChatUtilityPlugin? _instance;

    private Harmony? _harmony;

    private void Awake()
    {
        _instance = this;
        Log = Logger;
        ModEnabled = Config.Bind("General", "Enabled", true, "Enable Chat Utility.");
        RelayEnabled = Config.Bind("RelayChat", "Enabled", true, "Relay all normal chat messages through the WebSocket server. Whispers are left as vanilla Valheim chat.");
        RelayServerUrl = Config.Bind("RelayChat", "ServerUrl", HardcodedRelayServerUrl, "WebSocket relay URL. This value is forced by the plugin at startup.");
        RelayReconnectSeconds = Config.Bind("RelayChat", "ReconnectSeconds", 5f, "Seconds to wait before reconnecting after the relay disconnects.");
        AfkEnabled = Config.Bind("AFK", "Enabled", true, "Announce AFK and return status through relay chat.");
        AfkIdleSeconds = Config.Bind("AFK", "IdleSeconds", 300f, "Seconds without input, movement, or rotation before announcing AFK.");
        AfkMovementDistance = Config.Bind("AFK", "MovementDistance", 0.25f, "Player movement distance that counts as activity.");
        AfkRotationDegrees = Config.Bind("AFK", "RotationDegrees", 10f, "Player rotation change that counts as activity.");
        RelayServerUrl.Value = HardcodedRelayServerUrl;

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(ChatUtilityPlugin).Assembly);
        AfkCommand.Register();
        RetryWebSocketCommand.Register();
        RelayChatClient.Start();

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        if (!ModEnabled.Value)
        {
            return;
        }

        RelayChatClient.Update(Time.deltaTime);
        AfkStatusTracker.Update();
    }

    private void OnDestroy()
    {
        RelayChatClient.Stop();
        _harmony?.UnpatchSelf();
        _harmony = null;
        _instance = null;
    }

    internal static void SaveConfig()
    {
        _instance?.Config.Save();
    }
}
