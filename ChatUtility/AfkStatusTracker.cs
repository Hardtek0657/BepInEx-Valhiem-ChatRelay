using UnityEngine;

namespace ChatUtility;

internal static class AfkStatusTracker
{
    private static bool _initialized;
    private static bool _isAfk;
    private static float _lastActivityTime;
    private static Vector3 _lastPlayerPosition;
    private static Quaternion _lastPlayerRotation;
    private static float _cachedAfkIdleSeconds;
    private static float _cachedMovementDistanceSqr;
    private static float _cachedRotationDegrees;
    private static string _cachedPlayerName = string.Empty;

    public static void Update()
    {
        if (!ChatUtilityPlugin.ModEnabled.Value)
        {
            ResetState();
            return;
        }

        if (!ChatUtilityPlugin.RelayEnabled.Value || !ChatUtilityPlugin.AfkEnabled.Value)
        {
            ResetState();
            return;
        }

        Player player = Player.m_localPlayer;
        if (!player)
        {
            ResetState();
            return;
        }

        if (!_initialized)
        {
            Initialize(player);
            return;
        }

        if (HasActivity(player))
        {
            MarkActivity(player);
            return;
        }

        if (!_isAfk && Time.realtimeSinceStartup - _lastActivityTime >= _cachedAfkIdleSeconds)
        {
            _isAfk = true;
            RelayChatClient.SendNotice($"{_cachedPlayerName} has gone AFK.");
        }
    }

    private static void Initialize(Player player)
    {
        _initialized = true;
        _isAfk = false;
        _lastActivityTime = Time.realtimeSinceStartup;
        _lastPlayerPosition = player.transform.position;
        _lastPlayerRotation = player.transform.rotation;
        
        _cachedAfkIdleSeconds = Mathf.Max(10f, ChatUtilityPlugin.AfkIdleSeconds.Value);
        float movementDistance = Mathf.Max(0.25f, ChatUtilityPlugin.AfkMovementDistance.Value);
        _cachedMovementDistanceSqr = movementDistance * movementDistance;
        _cachedRotationDegrees = Mathf.Max(5f, ChatUtilityPlugin.AfkRotationDegrees.Value);
        _cachedPlayerName = GetPlayerName(player);
    }

    private static bool HasActivity(Player player)
    {
        if (Input.anyKey)
        {
            return true;
        }

        if ((player.transform.position - _lastPlayerPosition).sqrMagnitude >= _cachedMovementDistanceSqr)
        {
            return true;
        }

        return Quaternion.Angle(player.transform.rotation, _lastPlayerRotation) >= _cachedRotationDegrees;
    }

    private static void MarkActivity(Player player)
    {
        bool wasAfk = _isAfk;
        _isAfk = false;
        _lastActivityTime = Time.realtimeSinceStartup;
        _lastPlayerPosition = player.transform.position;
        _lastPlayerRotation = player.transform.rotation;
        
        _cachedAfkIdleSeconds = Mathf.Max(10f, ChatUtilityPlugin.AfkIdleSeconds.Value);
        float movementDistance = Mathf.Max(0.25f, ChatUtilityPlugin.AfkMovementDistance.Value);
        _cachedMovementDistanceSqr = movementDistance * movementDistance;
        _cachedRotationDegrees = Mathf.Max(5f, ChatUtilityPlugin.AfkRotationDegrees.Value);
        _cachedPlayerName = GetPlayerName(player);

        if (wasAfk)
        {
            RelayChatClient.SendNotice($"{_cachedPlayerName} is no longer AFK.");
        }
    }

    public static void ResetState()
    {
        _initialized = false;
        _isAfk = false;
        _lastActivityTime = 0f;
    }

    private static string GetPlayerName(Player player)
    {
        string name = player.GetPlayerName();
        return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
    }
}
